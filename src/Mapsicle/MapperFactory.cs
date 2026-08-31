using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;

namespace Mapsicle
{
    /// <summary>
    /// Factory for creating scoped mapper instances with isolated caches.
    /// </summary>
    public static class MapperFactory
    {
        /// <summary>
        /// Creates a new mapper instance with its own isolated cache.
        /// </summary>
        public static IMapperInstance Create(MapperOptions? options = null)
        {
            return new MapperInstance(options ?? new MapperOptions());
        }
    }

    /// <summary>
    /// Configuration options for mapper instances.
    /// </summary>
    public class MapperOptions
    {
        /// <summary>
        /// Maximum number of cached mapping delegates. Default: 1000.
        /// </summary>
        public int MaxCacheSize { get; set; } = 1000;

        private int _maxDepth = 32;

        /// <summary>
        /// Maximum mapping depth to prevent stack overflow on circular references. Default: 32.
        /// A value below 1 is rejected and the default is kept.
        /// </summary>
        /// <remarks>
        /// This used to accept 0, and 0 disables the mapper completely: the first depth check fails
        /// before any property is read, so every call returns the destination default with nothing
        /// logged and nothing thrown. A zeroed or defaulted configuration field silently turned the
        /// whole mapper into a no-op that still looked like it ran.
        ///
        /// Guarding here matches <see cref="Mapper.MaxDepth"/>, whose setter has always refused a
        /// non-positive value. The two were inconsistent, and the one people configure through an
        /// options object was the unguarded one.
        /// </remarks>
        public int MaxDepth
        {
            get => _maxDepth;
            set => _maxDepth = value > 0 ? value : 32;
        }

        /// <summary>
        /// Logger for diagnostic output. Null disables logging.
        /// </summary>
        public Action<string>? Logger { get; set; }
    }

    /// <summary>
    /// Scoped mapper instance with isolated cache.
    /// </summary>
    public interface IMapperInstance : IDisposable
    {
        /// <summary>Maps source to new instance of T.</summary>
        T? MapTo<T>(object? source);

        /// <summary>Maps collection to List of T.</summary>
        List<T> MapTo<T>(System.Collections.IEnumerable? source);

        /// <summary>Maps source properties to existing destination.</summary>
        TDest Map<TDest>(object? source, TDest destination);

        /// <summary>Clears the instance cache.</summary>
        void ClearCache();

        /// <summary>Gets cache statistics.</summary>
        MapperCacheInfo CacheInfo();
    }

    internal sealed class MapperInstance : IMapperInstance
    {
        private readonly LruCache<(Type, Type), Delegate> _mapToCache;
        private readonly LruCache<(Type, Type), Action<object, object>> _mapCache;
        private readonly MapperOptions _options;
        private readonly AsyncLocal<int> _currentDepth = new();
        private bool _disposed;

        // PropertyInfo cache for this instance
        private readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

        public MapperInstance(MapperOptions options)
        {
            _options = options;
            _mapToCache = new LruCache<(Type, Type), Delegate>(options.MaxCacheSize);
            _mapCache = new LruCache<(Type, Type), Action<object, object>>(options.MaxCacheSize);
        }

        public T? MapTo<T>(object? source)
        {
            ThrowIfDisposed();
            if (source is null) return default;

            var key = (source.GetType(), typeof(T));
            var destType = typeof(T);

            // Fast path for primitives - no depth tracking needed
            if (destType.IsValueType || destType == typeof(string))
            {
                if (_mapToCache.TryGetValue(key, out var cachedMapper))
                {
                    return ((Func<object, T>)cachedMapper)(source);
                }
            }

            // Depth check for cycle detection
            var depth = _currentDepth.Value;
            if (depth >= _options.MaxDepth)
            {
                _options.Logger?.Invoke($"[Mapsicle] Max depth {_options.MaxDepth} reached, returning default for {typeof(T).Name}");
                return default;
            }

            _currentDepth.Value = depth + 1;
            try
            {
                // Use THIS instance's cache, not static Mapper
                var mapFunction = (Func<object, T>)_mapToCache.GetOrAdd(key, k => BuildMapToDelegate<T>(k.Item1, k.Item2));
                return mapFunction(source);
            }
            finally
            {
                _currentDepth.Value = depth;
            }
        }

        public List<T> MapTo<T>(System.Collections.IEnumerable? source)
        {
            ThrowIfDisposed();
            if (source is null) return new List<T>();

            // Pre-allocate if count is known
            List<T> result;
            if (source is System.Collections.ICollection collection)
            {
                result = new List<T>(collection.Count);
            }
            else
            {
                result = new List<T>();
            }

            // Get the item mapper once, then apply to all items
            Type? itemType = null;
            Func<object, T>? itemMapper = null;

            foreach (var item in source)
            {
                if (item is null)
                {
                    result.Add(default!);
                    continue;
                }

                // Same reason as the static Mapper: the cached delegate casts to one runtime type,
                // so a mixed collection threw InvalidCastException on the first item of a different
                // type. Map that item through its own delegate instead.
                if (itemMapper is not null && item.GetType() != itemType)
                {
                    result.Add(MapTo<T>(item)!);
                    continue;
                }

                // Lazily get mapper for first non-null item type
                if (itemMapper is null)
                {
                    itemType = item.GetType();
                    var key = (itemType, typeof(T));
                    itemMapper = (Func<object, T>)_mapToCache.GetOrAdd(key, k => BuildMapToDelegate<T>(k.Item1, k.Item2));
                }

                result.Add(itemMapper(item)!);
            }

            return result;
        }

        public TDest Map<TDest>(object? source, TDest destination)
        {
            ThrowIfDisposed();
            if (source is null || destination is null) return destination;

            var key = (source.GetType(), typeof(TDest));

            var mapAction = _mapCache.GetOrAdd(key, k => BuildMapAction<TDest>(k.Item1, k.Item2));
            mapAction(source, destination!);
            return destination;
        }

        public void ClearCache()
        {
            ThrowIfDisposed();
            _mapToCache.Clear();
            _mapCache.Clear();
            _propertyCache.Clear();
        }

        public MapperCacheInfo CacheInfo()
        {
            ThrowIfDisposed();
            return new MapperCacheInfo(_mapToCache.Count, _mapCache.Count);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _mapToCache.Clear();
                _mapCache.Clear();
                _propertyCache.Clear();
                _disposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MapperInstance));
        }

        #region Expression Building

        private PropertyInfo[] GetProperties(Type type)
        {
            return _propertyCache.GetOrAdd(type, t =>
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0)
                    .ToArray());
        }

        private Delegate BuildMapToDelegate<T>(Type sourceType, Type destType)
        {
            var sourceParam = Expression.Parameter(typeof(object), "source");
            bool isSourceVisible = sourceType.IsVisible;
            var typedSource = Expression.Convert(sourceParam, sourceType);

            // Direct Primitive/Value Mapping
            if (sourceType.IsValueType || sourceType == typeof(string))
            {
                if (destType.IsAssignableFrom(sourceType))
                {
                    return Expression.Lambda<Func<object, T>>(Expression.Convert(typedSource, destType), sourceParam).Compile();
                }
                if (destType == typeof(string))
                {
                    var toStringCall = PropertyConversion.BuildToString(typedSource, sourceType);
                    return Expression.Lambda<Func<object, T>>(toStringCall, sourceParam).Compile();
                }
                var underlyingDest = Nullable.GetUnderlyingType(destType) ?? destType;
                var underlyingSource = Nullable.GetUnderlyingType(sourceType) ?? sourceType;

                if (underlyingDest.IsAssignableFrom(underlyingSource))
                {
                    return Expression.Lambda<Func<object, T>>(Expression.Convert(typedSource, destType), sourceParam).Compile();
                }
            }

            // Collection Mapping
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(sourceType) &&
                typeof(System.Collections.IEnumerable).IsAssignableFrom(destType) &&
                sourceType != typeof(string) && destType != typeof(string))
            {
                return BuildCollectionMapper<T>(sourceType, destType, sourceParam);
            }

            var bindings = new List<MemberBinding>();
            var sourceProps = GetProperties(sourceType).Where(p => p.CanRead).ToArray();
            var destProps = GetProperties(destType);

            // Parameterless Constructor Path
            if (destType.GetConstructor(Type.EmptyTypes) != null || destType.IsValueType)
            {
                foreach (var destProp in destProps)
                {
                    if (!destProp.CanWrite) continue;
                    if (!MemberResolution.TryResolveSource(destProp, sourceProps, out var sourceProp)) continue;

                    if (sourceProp != null)
                    {
                        var binding = CreatePropertyBinding(destProp, sourceProp, typedSource, sourceParam, isSourceVisible);
                        if (binding != null) bindings.Add(binding);
                    }
                    else
                    {
                        var flattenedBinding = Mapper.TryBindFlattenedPath(destProp, sourceProps, typedSource);
                        if (flattenedBinding != null) bindings.Add(flattenedBinding);
                    }
                }
                var init = Expression.MemberInit(Expression.New(destType), bindings);
                var body = Mapper.WithFilledCollections(init, sourceType, destType, typedSource);
                return Expression.Lambda<Func<object, T>>(body, sourceParam).Compile();
            }

            // Constructor / Record Path
            var ctor = destType.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (ctor != null)
            {
                var args = new List<Expression>();
                foreach (var param in ctor.GetParameters())
                {
                    var sourceProp = sourceProps.FirstOrDefault(p =>
                        p.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase) && p.CanRead);

                    if (sourceProp != null)
                    {
                        var propExp = Expression.Property(typedSource, sourceProp);
                        var value = PropertyConversion.TryBuild(
                            propExp, sourceProp.PropertyType, param.ParameterType, BuildNestedMapCall);
                        args.Add(value ?? Expression.Default(param.ParameterType));
                    }
                    else
                    {
                        args.Add(Expression.Default(param.ParameterType));
                    }
                }
                var newExp = Expression.New(ctor, args);
                var body = Mapper.CompleteConstructedDestination(
                    ctor, newExp, destProps, typedSource, sourceProps, BuildNestedMapCall);
                return Expression.Lambda<Func<object, T>>(body, sourceParam).Compile();
            }

            return Expression.Lambda<Func<object, T>>(Expression.Default(destType), sourceParam).Compile();
        }

        private Delegate BuildCollectionMapper<T>(Type sourceType, Type destType, ParameterExpression sourceParam)
        {
            var destEnumerableInt = destType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            Type targetItemType = typeof(object);
            if (destEnumerableInt != null)
            {
                targetItemType = destEnumerableInt.GetGenericArguments()[0];
            }
            else if (destType.IsGenericType)
            {
                targetItemType = destType.GetGenericArguments()[0];
            }
            else if (destType.IsArray)
            {
                targetItemType = destType.GetElementType()!;
            }

            // Call this instance's MapTo<T>(IEnumerable) instead of static Mapper
            var mapCollectionMethod = typeof(MapperInstance)
                .GetMethod(nameof(MapTo), new[] { typeof(System.Collections.IEnumerable) })!
                .MakeGenericMethod(targetItemType);

            var instanceExpr = Expression.Constant(this);
            var call = Expression.Call(instanceExpr, mapCollectionMethod,
                Expression.Convert(sourceParam, typeof(System.Collections.IEnumerable)));

            if (destType.IsArray)
            {
                var toArrayMethod = typeof(Enumerable).GetMethod("ToArray")!.MakeGenericMethod(targetItemType);
                var toArrayCall = Expression.Call(toArrayMethod, call);
                return Expression.Lambda<Func<object, T>>(Expression.Convert(toArrayCall, destType), sourceParam).Compile();
            }

            if (destType.IsAssignableFrom(typeof(List<>).MakeGenericType(targetItemType)))
            {
                return Expression.Lambda<Func<object, T>>(Expression.Convert(call, destType), sourceParam).Compile();
            }

            // Same materialisation the static path uses: a collection that is not assignable from
            // List<T> is built through its IEnumerable<T> constructor rather than being left at
            // default, which is what silently emptied a HashSet destination.
            var fromEnumerable = destType.GetConstructor(
                new[] { typeof(IEnumerable<>).MakeGenericType(targetItemType) });

            if (fromEnumerable != null)
            {
                var built = Expression.New(fromEnumerable, call);
                return Expression.Lambda<Func<object, T>>(Expression.Convert(built, destType), sourceParam).Compile();
            }

            return Expression.Lambda<Func<object, T>>(Expression.Default(destType), sourceParam).Compile();
        }

        private Action<object, object> BuildMapAction<TDest>(Type sourceType, Type destType)
        {
            var sourceParam = Expression.Parameter(typeof(object), "source");
            var destParam = Expression.Parameter(typeof(object), "destination");

            var typedSource = Expression.Convert(sourceParam, sourceType);
            var typedDest = Expression.Convert(destParam, destType);

            var assignments = new List<Expression>();
            var sourceProps = GetProperties(sourceType).Where(p => p.CanRead).ToArray();
            var destProps = GetProperties(destType);

            foreach (var destProp in destProps)
            {
                if (!destProp.CanWrite) continue;
                if (!MemberResolution.TryResolveSource(destProp, sourceProps, out var sourceProp)) continue;

                if (sourceProp != null)
                {
                    var propExp = Expression.Property(typedSource, sourceProp);
                    var destPropExp = Expression.Property(typedDest, destProp);

                    var value = PropertyConversion.TryBuild(
                        propExp, sourceProp.PropertyType, destProp.PropertyType, BuildNestedMapCall);

                    if (value != null)
                    {
                        assignments.Add(Expression.Assign(destPropExp, value));
                    }
                }
            }

            if (assignments.Count == 0)
            {
                return (s, d) => { };
            }

            var block = Expression.Block(assignments);
            return Expression.Lambda<Action<object, object>>(block, sourceParam, destParam).Compile();
        }

        private static PropertyInfo? FindSourceProperty(PropertyInfo[] sourceProps, string primaryName, string fallbackName)
        {
            return sourceProps.FirstOrDefault(p => p.Name.Equals(primaryName, StringComparison.OrdinalIgnoreCase) && p.CanRead)
                ?? sourceProps.FirstOrDefault(p => p.Name.Equals(fallbackName, StringComparison.OrdinalIgnoreCase) && p.CanRead);
        }

        private MemberBinding? CreatePropertyBinding(PropertyInfo destProp, PropertyInfo sourceProp,
            Expression typedSource, ParameterExpression sourceParam, bool isSourceVisible)
        {
            Expression propExp;
            if (isSourceVisible && sourceProp.GetGetMethod()?.IsPublic == true)
            {
                propExp = Expression.Property(typedSource, sourceProp);
            }
            else
            {
                var getValue = typeof(PropertyInfo).GetMethod("GetValue", new[] { typeof(object), typeof(object[]) })!;
                var call = Expression.Call(Expression.Constant(sourceProp), getValue, sourceParam, Expression.Constant(null, typeof(object[])));
                propExp = Expression.Convert(call, sourceProp.PropertyType);
            }

            var value = PropertyConversion.TryBuild(
                propExp, sourceProp.PropertyType, destProp.PropertyType, BuildNestedMapCall);

            return value is null ? null : Expression.Bind(destProp, value);
        }

        /// <summary>
        /// Recurses through <em>this instance's</em> MapTo, so a nested object uses the instance
        /// cache and depth tracking rather than the static mapper's.
        /// </summary>
        private Expression BuildNestedMapCall(Expression propExp, Type targetType)
        {
            var mapMethod = MapToObjectOverload.MakeGenericMethod(targetType);
            return Expression.Call(Expression.Constant(this), mapMethod, Expression.Convert(propExp, typeof(object)));
        }

        private static readonly MethodInfo MapToObjectOverload =
            typeof(MapperInstance).GetMethod(nameof(MapTo), new[] { typeof(object) })
            ?? throw new InvalidOperationException(
                "MapperInstance.MapTo<T>(object) was not found. Renaming or changing that overload breaks nested mapping.");

        private MemberBinding? TryCreateFlattenedBinding(PropertyInfo destProp, PropertyInfo[] sourceProps,
            Expression typedSource, ParameterExpression sourceParam, bool isSourceVisible)
        {
            string destName = destProp.Name;

            foreach (var sourceProp in sourceProps)
            {
                if (!sourceProp.PropertyType.IsClass || sourceProp.PropertyType == typeof(string)) continue;
                if (!destName.StartsWith(sourceProp.Name, StringComparison.OrdinalIgnoreCase)) continue;

                string remainder = destName.Substring(sourceProp.Name.Length);
                if (string.IsNullOrEmpty(remainder)) continue;

                var nestedProps = sourceProp.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead);

                var nestedProp = nestedProps.FirstOrDefault(p => p.Name.Equals(remainder, StringComparison.OrdinalIgnoreCase));
                if (nestedProp != null && destProp.PropertyType.IsAssignableFrom(nestedProp.PropertyType))
                {
                    var parentAccess = Expression.Property(typedSource, sourceProp);
                    var nestedAccess = Expression.Property(parentAccess, nestedProp);

                    var nullCheck = Expression.Equal(parentAccess, Expression.Constant(null, sourceProp.PropertyType));
                    var safeAccess = Expression.Condition(
                        nullCheck,
                        Expression.Default(destProp.PropertyType),
                        Expression.Convert(nestedAccess, destProp.PropertyType)
                    );

                    return Expression.Bind(destProp, safeAccess);
                }
            }

            return null;
        }

        #endregion
    }
}
