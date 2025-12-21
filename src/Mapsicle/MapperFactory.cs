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

        /// <summary>
        /// Maximum mapping depth to prevent stack overflow on circular references. Default: 32.
        /// </summary>
        public int MaxDepth { get; set; } = 32;

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
            var typedSource = isSourceVisible ? Expression.Convert(sourceParam, sourceType) : null;

            // Direct Primitive/Value Mapping
            if (sourceType.IsValueType || sourceType == typeof(string))
            {
                if (destType.IsAssignableFrom(sourceType))
                {
                    var castSrc = isSourceVisible ? typedSource! : Expression.Convert(sourceParam, sourceType);
                    return Expression.Lambda<Func<object, T>>(Expression.Convert(castSrc, destType), sourceParam).Compile();
                }
                if (destType == typeof(string))
                {
                    var castSrc = isSourceVisible ? typedSource! : Expression.Convert(sourceParam, sourceType);
                    var toStringCall = Expression.Call(castSrc, typeof(object).GetMethod("ToString")!);
                    return Expression.Lambda<Func<object, T>>(toStringCall, sourceParam).Compile();
                }
                var underlyingDest = Nullable.GetUnderlyingType(destType) ?? destType;
                var underlyingSource = Nullable.GetUnderlyingType(sourceType) ?? sourceType;

                if (underlyingDest.IsAssignableFrom(underlyingSource))
                {
                    var castSrc = isSourceVisible ? typedSource! : Expression.Convert(sourceParam, sourceType);
                    return Expression.Lambda<Func<object, T>>(Expression.Convert(castSrc, destType), sourceParam).Compile();
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
                    if (destProp.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;

                    var mapFromAttr = destProp.GetCustomAttribute<MapFromAttribute>();
                    string sourcePropertyName = mapFromAttr?.SourcePropertyName ?? destProp.Name;

                    var sourceProp = FindSourceProperty(sourceProps, sourcePropertyName, destProp.Name);

                    if (sourceProp != null)
                    {
                        var binding = CreatePropertyBinding(destProp, sourceProp, typedSource!, sourceParam, isSourceVisible);
                        if (binding != null) bindings.Add(binding);
                    }
                    else
                    {
                        var flattenedBinding = TryCreateFlattenedBinding(destProp, sourceProps, typedSource!, sourceParam, isSourceVisible);
                        if (flattenedBinding != null) bindings.Add(flattenedBinding);
                    }
                }
                var init = Expression.MemberInit(Expression.New(destType), bindings);
                return Expression.Lambda<Func<object, T>>(init, sourceParam).Compile();
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
                        var propExp = Expression.Property(typedSource!, sourceProp);
                        if (param.ParameterType.IsAssignableFrom(sourceProp.PropertyType))
                        {
                            args.Add(Expression.Convert(propExp, param.ParameterType));
                        }
                        else if (param.ParameterType == typeof(string))
                        {
                            args.Add(Expression.Call(propExp, typeof(object).GetMethod("ToString")!));
                        }
                        else if (sourceProp.PropertyType.IsEnum && (param.ParameterType == typeof(int) || param.ParameterType == typeof(long)))
                        {
                            args.Add(Expression.Convert(propExp, param.ParameterType));
                        }
                        else
                        {
                            args.Add(Expression.Default(param.ParameterType));
                        }
                    }
                    else
                    {
                        args.Add(Expression.Default(param.ParameterType));
                    }
                }
                var newExp = Expression.New(ctor, args);
                return Expression.Lambda<Func<object, T>>(newExp, sourceParam).Compile();
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

            // Create a lambda that calls MapTo<targetItemType> on each item
            var listType = typeof(List<>).MakeGenericType(targetItemType);
            var result = Expression.Variable(listType, "result");
            var enumerator = Expression.Variable(typeof(System.Collections.IEnumerator), "enumerator");
            var item = Expression.Variable(typeof(object), "item");

            var getEnumerator = Expression.Call(Expression.Convert(sourceParam, typeof(System.Collections.IEnumerable)),
                typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!);
            var moveNext = Expression.Call(enumerator, typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!);
            var current = Expression.Property(enumerator, "Current");

            var addMethod = listType.GetMethod("Add")!;
            var breakLabel = Expression.Label();

            // For simplicity, fall back to direct conversion for collections
            var call = Expression.Call(null,
                typeof(Mapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == "MapTo" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(System.Collections.IEnumerable))
                    .MakeGenericMethod(targetItemType),
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
                if (destProp.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;

                var mapFromAttr = destProp.GetCustomAttribute<MapFromAttribute>();
                string sourcePropertyName = mapFromAttr?.SourcePropertyName ?? destProp.Name;

                var sourceProp = FindSourceProperty(sourceProps, sourcePropertyName, destProp.Name);

                if (sourceProp != null)
                {
                    var propExp = Expression.Property(typedSource, sourceProp);
                    var targetType = destProp.PropertyType;
                    var srcType = sourceProp.PropertyType;
                    var destPropExp = Expression.Property(typedDest, destProp);

                    if (targetType.IsAssignableFrom(srcType))
                    {
                        assignments.Add(Expression.Assign(destPropExp, Expression.Convert(propExp, targetType)));
                    }
                    else if (targetType == typeof(string))
                    {
                        var toStringCall = Expression.Call(propExp, typeof(object).GetMethod("ToString")!);
                        assignments.Add(Expression.Assign(destPropExp, toStringCall));
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

            var targetType = destProp.PropertyType;
            var srcType = sourceProp.PropertyType;

            if (targetType.IsAssignableFrom(srcType))
            {
                return Expression.Bind(destProp, Expression.Convert(propExp, targetType));
            }
            else if (targetType == typeof(string))
            {
                var toStringCall = Expression.Call(propExp, typeof(object).GetMethod("ToString")!);
                return Expression.Bind(destProp, toStringCall);
            }
            else if (srcType.IsEnum && (targetType == typeof(int) || targetType == typeof(long)))
            {
                return Expression.Bind(destProp, Expression.Convert(propExp, targetType));
            }
            else
            {
                var underlyingTarget = Nullable.GetUnderlyingType(targetType);
                var underlyingSource = Nullable.GetUnderlyingType(srcType);

                if (underlyingSource != null && targetType.IsAssignableFrom(underlyingSource))
                {
                    var coalesce = Expression.Coalesce(propExp, Expression.Default(targetType));
                    return Expression.Bind(destProp, coalesce);
                }
            }

            return null;
        }

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
