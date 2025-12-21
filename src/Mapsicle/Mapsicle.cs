using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Mapsicle
{
    #region Attributes

    /// <summary>
    /// Marks a property to be ignored during mapping.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class IgnoreMapAttribute : Attribute { }

    /// <summary>
    /// Specifies the source property name to map from.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class MapFromAttribute : Attribute
    {
        public string SourcePropertyName { get; }
        public MapFromAttribute(string sourcePropertyName) => SourcePropertyName = sourcePropertyName;
    }

    #endregion

    #region Cache Info

    /// <summary>
    /// Contains information about the mapper cache state.
    /// </summary>
    public readonly struct MapperCacheInfo
    {
        public MapperCacheInfo(int mapToEntries, int mapEntries)
        {
            MapToEntries = mapToEntries;
            MapEntries = mapEntries;
            Hits = 0;
            Misses = 0;
        }

        public MapperCacheInfo(int mapToEntries, int mapEntries, long hits, long misses)
        {
            MapToEntries = mapToEntries;
            MapEntries = mapEntries;
            Hits = hits;
            Misses = misses;
        }

        public int MapToEntries { get; }
        public int MapEntries { get; }
        public int Total => MapToEntries + MapEntries;

        /// <summary>
        /// Number of cache hits (only tracked when UseLruCache is enabled).
        /// </summary>
        public long Hits { get; }

        /// <summary>
        /// Number of cache misses (only tracked when UseLruCache is enabled).
        /// </summary>
        public long Misses { get; }

        /// <summary>
        /// Cache hit ratio (0.0 to 1.0). Returns 0 if no cache accesses.
        /// </summary>
        public double HitRatio => Hits + Misses > 0 ? (double)Hits / (Hits + Misses) : 0.0;
    }

    #endregion

    public static class Mapper
    {
        // Unbounded caches (default for backward compatibility)
        private static readonly ConcurrentDictionary<(Type, Type), Delegate> _mapToCache = new();
        private static readonly ConcurrentDictionary<(Type, Type), Action<object, object>> _mapCache = new();

        // LRU caches (optional, for memory-bounded operation)
        private static LruCache<(Type, Type), Delegate>? _lruMapToCache;
        private static LruCache<(Type, Type), Action<object, object>>? _lruMapCache;

        // PropertyInfo cache for performance
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

        private static readonly System.Threading.AsyncLocal<int> _mappingDepth = new();

        // Cache statistics
        private static long _cacheHits;
        private static long _cacheMisses;
        private static readonly object _configLock = new();

        #region Configuration

        private static bool _useLruCache;
        private static int _maxCacheSize = 1000;

        /// <summary>
        /// When true, uses LRU cache with bounded memory. When false (default), uses unbounded cache.
        /// Changing this setting clears all caches.
        /// </summary>
        public static bool UseLruCache
        {
            get => _useLruCache;
            set
            {
                lock (_configLock)
                {
                    if (_useLruCache != value)
                    {
                        _useLruCache = value;
                        ReinitializeCaches();
                    }
                }
            }
        }

        /// <summary>
        /// Maximum cache size when UseLruCache is enabled. Default: 1000.
        /// Changing this setting clears all caches if LRU is enabled.
        /// </summary>
        public static int MaxCacheSize
        {
            get => _maxCacheSize;
            set
            {
                lock (_configLock)
                {
                    if (_maxCacheSize != value)
                    {
                        _maxCacheSize = value > 0 ? value : 1000;
                        if (_useLruCache)
                        {
                            ReinitializeCaches();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Maximum mapping depth for cycle detection. Default: 32.
        /// </summary>
        public static int MaxDepth { get; set; } = 32;

        /// <summary>
        /// Logger for diagnostic output. Null disables logging.
        /// </summary>
        public static Action<string>? Logger { get; set; }

        private static void ReinitializeCaches()
        {
            _mapToCache.Clear();
            _mapCache.Clear();
            _propertyCache.Clear();
            System.Threading.Interlocked.Exchange(ref _cacheHits, 0);
            System.Threading.Interlocked.Exchange(ref _cacheMisses, 0);

            if (_useLruCache)
            {
                _lruMapToCache = new LruCache<(Type, Type), Delegate>(_maxCacheSize);
                _lruMapCache = new LruCache<(Type, Type), Action<object, object>>(_maxCacheSize);
            }
            else
            {
                _lruMapToCache = null;
                _lruMapCache = null;
            }
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Clears all cached mapping delegates.
        /// </summary>
        public static void ClearCache()
        {
            lock (_configLock)
            {
                _mapToCache.Clear();
                _mapCache.Clear();
                _lruMapToCache?.Clear();
                _lruMapCache?.Clear();
                _propertyCache.Clear();
                System.Threading.Interlocked.Exchange(ref _cacheHits, 0);
                System.Threading.Interlocked.Exchange(ref _cacheMisses, 0);
            }
        }

        /// <summary>
        /// Gets information about the current cache state.
        /// </summary>
        public static MapperCacheInfo CacheInfo()
        {
            if (_useLruCache && _lruMapToCache != null && _lruMapCache != null)
            {
                return new MapperCacheInfo(
                    _lruMapToCache.Count,
                    _lruMapCache.Count,
                    System.Threading.Interlocked.Read(ref _cacheHits),
                    System.Threading.Interlocked.Read(ref _cacheMisses));
            }
            return new MapperCacheInfo(_mapToCache.Count, _mapCache.Count);
        }

        /// <summary>
        /// Gets cached PropertyInfo array for a type.
        /// </summary>
        internal static PropertyInfo[] GetCachedProperties(Type type)
        {
            return _propertyCache.GetOrAdd(type, t =>
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0)
                    .ToArray());
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validates that all destination properties can be mapped from source.
        /// Throws if unmapped properties exist.
        /// </summary>
        public static void AssertMappingValid<TSource, TDest>()
        {
            var unmapped = GetUnmappedProperties<TSource, TDest>();
            if (unmapped.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Unmapped properties on {typeof(TDest).Name} from {typeof(TSource).Name}: {string.Join(", ", unmapped)}");
            }
        }

        /// <summary>
        /// Gets list of destination properties that cannot be mapped from source.
        /// </summary>
        public static List<string> GetUnmappedProperties<TSource, TDest>()
        {
            var unmapped = new List<string>();
            var sourceProps = new HashSet<string>(
                typeof(TSource).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                    .Select(p => p.Name),
                StringComparer.OrdinalIgnoreCase);
            var destProps = typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite);

            foreach (var destProp in destProps)
            {
                if (destProp.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;
                
                var mapFrom = destProp.GetCustomAttribute<MapFromAttribute>();
                var sourceName = mapFrom?.SourcePropertyName ?? destProp.Name;
                
                if (sourceProps.Contains(sourceName)) continue;
                
                // Check flattening
                bool hasFlattening = typeof(TSource).GetProperties()
                    .Any(sp => destProp.Name.StartsWith(sp.Name, StringComparison.OrdinalIgnoreCase) &&
                               destProp.Name.Length > sp.Name.Length);
                if (hasFlattening) continue;

                unmapped.Add(destProp.Name);
            }
            return unmapped;
        }

        #endregion

        #region Depth Tracking (Cycle Detection)

        private static bool IncrementDepth()
        {
            var depth = _mappingDepth.Value;
            if (depth >= MaxDepth)
            {
                Logger?.Invoke($"[Mapsicle] Max depth {MaxDepth} reached - possible circular reference");
                return false;
            }
            _mappingDepth.Value = depth + 1;
            return true;
        }

        private static void DecrementDepth()
        {
            _mappingDepth.Value = Math.Max(0, _mappingDepth.Value - 1);
        }

        #endregion

        #region Cache Helpers

        private static Func<object, T>? GetCachedMapToDelegate<T>((Type, Type) key)
        {
            if (_useLruCache && _lruMapToCache != null)
            {
                if (_lruMapToCache.TryGetValue(key, out var cached))
                {
                    System.Threading.Interlocked.Increment(ref _cacheHits);
                    return (Func<object, T>)cached;
                }
                System.Threading.Interlocked.Increment(ref _cacheMisses);
                return null;
            }
            else
            {
                if (_mapToCache.TryGetValue(key, out var cached))
                {
                    return (Func<object, T>)cached;
                }
                return null;
            }
        }

        private static Func<object, T> GetOrAddMapToDelegate<T>((Type, Type) key, Func<(Type, Type), Delegate> factory)
        {
            if (_useLruCache && _lruMapToCache != null)
            {
                return (Func<object, T>)_lruMapToCache.GetOrAdd(key, factory);
            }
            else
            {
                return (Func<object, T>)_mapToCache.GetOrAdd(key, factory);
            }
        }

        private static Action<object, object> GetOrAddMapDelegate((Type, Type) key, Func<(Type, Type), Action<object, object>> factory)
        {
            if (_useLruCache && _lruMapCache != null)
            {
                return _lruMapCache.GetOrAdd(key, factory);
            }
            else
            {
                return _mapCache.GetOrAdd(key, factory);
            }
        }

        #endregion

        #region MapTo<T> - Single Object

        /// <summary>
        /// Maps the source object to a new instance of the destination type T.
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <returns>A new instance of T mapped from source, or default(T) if source is null or max depth reached.</returns>
        /// <remarks>
        /// Supports type coercion, nested objects, collections, and flattening.
        /// Circular references are detected via depth tracking and return default.
        /// </remarks>
        public static T? MapTo<T>(this object? source)
        {
            if (source is null) return default;

            var key = (source.GetType(), typeof(T));
            var destType = typeof(T);

            // OPTIMIZATION: Fast path for primitives - no depth tracking needed
            if (destType.IsValueType || destType == typeof(string))
            {
                var cachedMapper = GetCachedMapToDelegate<T>(key);
                if (cachedMapper != null)
                {
                    return cachedMapper(source);
                }
                // Fall through to build the delegate
            }

            // Full path with cycle detection for complex objects
            if (!IncrementDepth())
            {
                return default; // Max depth reached - likely circular reference
            }

            try
            {
                var mapFunction = GetOrAddMapToDelegate<T>(key, k =>
            {
                var sourceType = k.Item1;
                var destType = k.Item2;
                var sourceParam = Expression.Parameter(typeof(object), "source");
                bool isSourceVisible = sourceType.IsVisible;
                var typedSource = isSourceVisible ? Expression.Convert(sourceParam, sourceType) : null;

                // --- 0. Direct Primitive/Value Mapping ---
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

                // --- 0.5 Collection Mapping Support ---
                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(sourceType) &&
                    typeof(System.Collections.IEnumerable).IsAssignableFrom(destType) &&
                    sourceType != typeof(string) && destType != typeof(string))
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

                    var collectionMapMethod = typeof(Mapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .First(m => m.Name == "MapTo" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(System.Collections.IEnumerable))
                        .MakeGenericMethod(targetItemType);

                    var call = Expression.Call(collectionMapMethod, Expression.Convert(sourceParam, typeof(System.Collections.IEnumerable)));

                    // Handle array destination
                    if (destType.IsArray)
                    {
                        var toArrayMethod = typeof(Enumerable).GetMethod("ToArray")!.MakeGenericMethod(targetItemType);
                        var toArrayCall = Expression.Call(toArrayMethod, call);
                        return Expression.Lambda<Func<object, T>>(Expression.Convert(toArrayCall, destType), sourceParam).Compile();
                    }

                    if (destType.IsAssignableFrom(collectionMapMethod.ReturnType))
                    {
                        return Expression.Lambda<Func<object, T>>(Expression.Convert(call, destType), sourceParam).Compile();
                    }
                }

                var bindings = new List<MemberBinding>();
                var sourceProps = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
                    .ToArray();
                var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                // --- 1. Parameterless Constructor Path ---
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
                            // Try flattening: AddressCity -> Address.City
                            var flattenedBinding = TryCreateFlattenedBinding(destProp, sourceProps, typedSource!, sourceParam, isSourceVisible);
                            if (flattenedBinding != null) bindings.Add(flattenedBinding);
                        }
                    }
                    var init = Expression.MemberInit(Expression.New(destType), bindings);
                    return Expression.Lambda<Func<object, T>>(init, sourceParam).Compile();
                }

                // --- 2. Constructor / Record Path ---
                var ctor = destType.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault();

                if (ctor != null)
                {
                    var args = new List<Expression>();
                    foreach (var param in ctor.GetParameters())
                    {
                        var sourceProp = sourceProps.FirstOrDefault(p =>
                            p.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase) &&
                            p.CanRead);

                        if (sourceProp != null)
                        {
                            var propExp = Expression.Property(typedSource!, sourceProp);
                            if (param.ParameterType.IsAssignableFrom(sourceProp.PropertyType))
                            {
                                args.Add(Expression.Convert(propExp, param.ParameterType));
                            }
                            else if (param.ParameterType == typeof(string))
                            {
                                var toStringCall = Expression.Call(propExp, typeof(object).GetMethod("ToString")!);
                                args.Add(toStringCall);
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
            });

            return mapFunction(source);
            }
            finally
            {
                DecrementDepth();
            }
        }

        #endregion

        #region Map - Update Existing

        /// <summary>
        /// Maps properties from the source object to an existing destination object.
        /// </summary>
        public static TDestination Map<TDestination>(this object? source, TDestination destination)
        {
            if (source is null || destination is null) return destination;

            var key = (source.GetType(), typeof(TDestination));

            var mapAction = GetOrAddMapDelegate(key, k =>
            {
                var sourceType = k.Item1;
                var destType = k.Item2;
                var sourceParam = Expression.Parameter(typeof(object), "source");
                var destParam = Expression.Parameter(typeof(object), "destination");

                var typedSource = Expression.Convert(sourceParam, sourceType);
                var typedDest = Expression.Convert(destParam, destType);

                var assignments = new List<Expression>();
                var sourceProps = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
                    .ToArray();
                var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

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
                        else if (srcType.IsClass && targetType.IsClass && srcType != typeof(string) && targetType != typeof(string))
                        {
                            var mapMethod = typeof(Mapper).GetMethods()
                                .First(m => m.Name == "MapTo" && m.GetParameters().Length == 1 && m.GetGenericArguments().Length == 1)
                                .MakeGenericMethod(targetType);

                            var recursiveCall = Expression.Call(null, mapMethod, propExp);
                            assignments.Add(Expression.Assign(destPropExp, recursiveCall));
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
            });

            mapAction(source, destination);
            return destination;
        }

        #endregion

        #region MapTo<T> - Collection

        /// <summary>
        /// Maps a collection of objects to a List of type T.
        /// </summary>
        public static List<T> MapTo<T>(this System.Collections.IEnumerable? source)
        {
            if (source is null) return new List<T>();

            // OPTIMIZATION: Pre-allocate list with capacity hint
            List<T> result;
            if (source is System.Collections.ICollection collection)
            {
                result = new List<T>(collection.Count);
            }
            else
            {
                result = new List<T>();
            }

            // OPTIMIZATION: Cache the mapper delegate once and reuse for all items
            // This avoids repeated cache lookups during collection iteration
            Type? itemType = null;
            Func<object, T>? cachedMapper = null;

            foreach (var item in source)
            {
                if (item is null)
                {
                    result.Add(default!);
                    continue;
                }

                // Lazily get mapper for first non-null item
                if (cachedMapper is null)
                {
                    itemType = item.GetType();
                    var key = (itemType, typeof(T));
                    // Try to get existing cached delegate
                    cachedMapper = GetCachedMapToDelegate<T>(key);
                    if (cachedMapper is null)
                    {
                        // Fall back to single-item MapTo which will cache the delegate
                        result.Add(item.MapTo<T>()!);
                        // Now get the cached delegate for subsequent items
                        cachedMapper = GetCachedMapToDelegate<T>(key);
                        continue;
                    }
                }

                result.Add(cachedMapper(item)!);
            }
            return result;
        }

        /// <summary>
        /// Maps a collection of objects to an array of type T.
        /// </summary>
        public static T[] MapToArray<T>(this System.Collections.IEnumerable? source)
        {
            return source.MapTo<T>().ToArray();
        }

        #endregion

        #region Dictionary Mapping

        /// <summary>
        /// Converts an object to a Dictionary with property names as keys.
        /// </summary>
        public static Dictionary<string, object?> ToDictionary(this object? source)
        {
            if (source is null) return new Dictionary<string, object?>();

            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead);

            foreach (var prop in props)
            {
                if (prop.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;
                dict[prop.Name] = prop.GetValue(source);
            }

            return dict;
        }

        /// <summary>
        /// Maps a dictionary to an object of type T.
        /// </summary>
        public static T? MapTo<T>(this IDictionary<string, object?>? source) where T : new()
        {
            if (source is null) return default;

            var dest = new T();
            var destProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.GetCustomAttribute<IgnoreMapAttribute>() == null);

            foreach (var prop in destProps)
            {
                var mapFromAttr = prop.GetCustomAttribute<MapFromAttribute>();
                string key = mapFromAttr?.SourcePropertyName ?? prop.Name;

                // Case-insensitive key lookup
                var matchingKey = source.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (matchingKey != null && source.TryGetValue(matchingKey, out var value) && value != null)
                {
                    try
                    {
                        if (prop.PropertyType.IsAssignableFrom(value.GetType()))
                        {
                            prop.SetValue(dest, value);
                        }
                        else if (prop.PropertyType == typeof(string))
                        {
                            prop.SetValue(dest, value.ToString());
                        }
                        else if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(prop.PropertyType))
                        {
                            var converted = Convert.ChangeType(value, prop.PropertyType);
                            prop.SetValue(dest, converted);
                        }
                    }
                    catch
                    {
                        // Skip incompatible types silently
                    }
                }
            }

            return dest;
        }

        #endregion

        #region Private Helpers

        private static PropertyInfo? FindSourceProperty(PropertyInfo[] sourceProps, string primaryName, string fallbackName)
        {
            return sourceProps.FirstOrDefault(p => p.Name.Equals(primaryName, StringComparison.OrdinalIgnoreCase) && p.CanRead)
                ?? sourceProps.FirstOrDefault(p => p.Name.Equals(fallbackName, StringComparison.OrdinalIgnoreCase) && p.CanRead);
        }

        private static MemberBinding? CreatePropertyBinding(PropertyInfo destProp, PropertyInfo sourceProp,
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
            else if (srcType.IsClass && targetType.IsClass && srcType != typeof(string) && targetType != typeof(string))
            {
                var mapMethod = typeof(Mapper).GetMethods()
                    .First(m => m.Name == "MapTo" && m.GetParameters().Length == 1 && m.GetGenericArguments().Length == 1)
                    .MakeGenericMethod(targetType);
                var recursiveCall = Expression.Call(null, mapMethod, propExp);
                return Expression.Bind(destProp, recursiveCall);
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

        /// <summary>
        /// Attempts to create a binding for flattened properties (e.g., AddressCity -> Address.City).
        /// </summary>
        private static MemberBinding? TryCreateFlattenedBinding(PropertyInfo destProp, PropertyInfo[] sourceProps,
            Expression typedSource, ParameterExpression sourceParam, bool isSourceVisible)
        {
            string destName = destProp.Name;

            // Try to find nested properties by splitting the destination name
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
                    // Build: source.Address?.City ?? default
                    var parentAccess = Expression.Property(typedSource, sourceProp);
                    var nestedAccess = Expression.Property(parentAccess, nestedProp);

                    // Handle null parent with conditional
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
