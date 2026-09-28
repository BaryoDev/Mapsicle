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
        /// The depth past which the mapper checks for circular references. Default: 32.
        /// A value below 1 is rejected and the default is kept.
        /// </summary>
        /// <remarks>
        /// Past this depth mapping stops when the same source instance repeats on the current path,
        /// or when the thread is close to running out of stack, which is the rule
        /// <see cref="Mapper.MaxDepth"/> follows. An acyclic graph deeper than this maps whole.
        ///
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
        private readonly AsyncLocal<HashSet<object>?> _onPath = new();
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

            var depth = _currentDepth.Value;
            if (!TryEnter(source, depth)) return default;

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
                Leave(source, depth);
            }
        }

        /// <summary>The static mapper's depth rule: past MaxDepth, stop on a repeat, not a number.</summary>
        /// <remarks>
        /// This used to stop at MaxDepth outright, so an acyclic chain of 40 came back holding 32
        /// while the static mapper returned all 40. The factory is the oracle the generator's
        /// conformance suite compares against, so a lane that truncates here hides a lane that
        /// does not.
        /// </remarks>
        private bool TryEnter(object source, int depth)
        {
            if (depth < _options.MaxDepth) return true;

            if (depth >= Mapper.StackGuardDepth)
            {
                _options.Logger?.Invoke($"[Mapsicle] Depth {Mapper.StackGuardDepth} reached, stopping to protect the stack");
                return false;
            }

            if (!Mapper.HasStackHeadroom())
            {
                _options.Logger?.Invoke($"[Mapsicle] Stack nearly exhausted at depth {depth}, stopping to protect the process");
                return false;
            }

            var path = _onPath.Value ??= new HashSet<object>(Mapper.ReferenceIdentity.Instance);
            if (!path.Add(source))
            {
                _options.Logger?.Invoke("[Mapsicle] Circular reference reached, the same instance is already being mapped");
                return false;
            }

            return true;
        }

        private void Leave(object source, int depth)
        {
            var path = _onPath.Value;
            if (path is null) return;

            if (depth >= _options.MaxDepth) path.Remove(source);
            if (depth == 0) path.Clear();
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

            var mapAction = _mapCache.GetOrAdd(
                key, k => Mapper.BuildInPlaceMapper(k.Item1, k.Item2, null, BuildNestedMapCall));

            if (!typeof(TDest).IsValueType)
            {
                mapAction(source, destination!);
                return destination;
            }

            // Written through a box, so the box is the result. See Mapper.Map.
            object boxed = destination!;
            mapAction(source, boxed);
            return (TDest)boxed;
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
            DynamicCodeGuard.EnsureSupported(sourceType, destType);
            var sourceParam = Expression.Parameter(typeof(object), "source");
            bool isSourceVisible = sourceType.IsVisible;
            var typedSource = Expression.Convert(sourceParam, sourceType);

            // Direct Primitive/Value Mapping, through the shared cascade. The reduced copy that was
            // here covered assignable types and ToString only, so MapTo<long>(5) returned 0.
            if ((sourceType.IsValueType || sourceType == typeof(string))
                && !PropertyConversion.IsNestedPair(sourceType, destType))
            {
                var direct = PropertyConversion.TryBuild(typedSource, sourceType, destType, BuildNestedMapCall);
                if (direct is not null)
                {
                    return Expression.Lambda<Func<object, T>>(direct, sourceParam).Compile();
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
            var sourceProps = GetProperties(sourceType).Where(p => p.GetGetMethod() != null).ToArray();
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
                        p.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase) && p.GetGetMethod() != null);

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

            // Keys and values mapped separately, as the static path does. Mapping each
            // KeyValuePair as an object gave a default pair with a null key, and the dictionary
            // constructor threw ArgumentNullException on the first one.
            if (targetItemType.IsGenericType
                && targetItemType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
                && typeof(System.Collections.IDictionary).IsAssignableFrom(sourceType))
            {
                var pairArgs = targetItemType.GetGenericArguments();

                if (destType.IsAssignableFrom(typeof(Dictionary<,>).MakeGenericType(pairArgs)))
                {
                    var buildDictionary = typeof(MapperInstance)
                        .GetMethod(nameof(BuildMappedDictionary), BindingFlags.NonPublic | BindingFlags.Instance)!
                        .MakeGenericMethod(pairArgs);

                    var dictionary = Expression.Call(Expression.Constant(this), buildDictionary, sourceParam);
                    return Expression.Lambda<Func<object, T>>(Expression.Convert(dictionary, destType), sourceParam).Compile();
                }
            }

            // Same materialisation the static path uses: a collection that is not assignable from
            // List<T> is built through its IEnumerable<T> constructor rather than being left at
            // default, which is what silently emptied a HashSet destination. Guarded the same way
            // too: a SortedSet of items with no ordering threw from the factory and not from the
            // static mapper.
            var fromEnumerable = destType.GetConstructor(
                new[] { typeof(IEnumerable<>).MakeGenericType(targetItemType) });

            if (fromEnumerable != null)
            {
                var exception = Expression.Parameter(typeof(Exception), "ex");
                var built = Expression.Convert(Expression.New(fromEnumerable, call), destType);

                var guarded = Expression.TryCatch(
                    built,
                    Expression.Catch(
                        exception,
                        Expression.Call(
                            Expression.Constant(this),
                            typeof(MapperInstance)
                                .GetMethod(nameof(LogCollectionFallback), BindingFlags.NonPublic | BindingFlags.Instance)!
                                .MakeGenericMethod(destType),
                            exception)));

                return Expression.Lambda<Func<object, T>>(guarded, sourceParam).Compile();
            }

            return Expression.Lambda<Func<object, T>>(Expression.Default(destType), sourceParam).Compile();
        }

        private TCollection LogCollectionFallback<TCollection>(Exception ex)
        {
            _options.Logger?.Invoke(
                $"[Mapsicle] Could not build {typeof(TCollection).Name} from the mapped items: {ex.Message}. " +
                "The destination was left at its default.");
            return default!;
        }

        private Dictionary<TKey, TValue> BuildMappedDictionary<TKey, TValue>(object? source)
            where TKey : notnull
        {
            var result = new Dictionary<TKey, TValue>();
            if (source is not System.Collections.IDictionary dictionary) return result;

            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                var key = MapTo<TKey>(entry.Key);
                if (key is null) continue;

                result[key] = entry.Value is null ? default! : MapTo<TValue>(entry.Value)!;
            }

            return result;
        }

        private static PropertyInfo? FindSourceProperty(PropertyInfo[] sourceProps, string primaryName, string fallbackName)
        {
            return sourceProps.FirstOrDefault(p => p.Name.Equals(primaryName, StringComparison.OrdinalIgnoreCase) && p.GetGetMethod() != null)
                ?? sourceProps.FirstOrDefault(p => p.Name.Equals(fallbackName, StringComparison.OrdinalIgnoreCase) && p.GetGetMethod() != null);
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
                    .Where(p => p.GetIndexParameters().Length == 0 && p.GetGetMethod() != null);

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
