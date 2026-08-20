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
        private static volatile LruCache<(Type, Type), Delegate>? _lruMapToCache;
        private static volatile LruCache<(Type, Type), Action<object, object>>? _lruMapCache;

        // PropertyInfo caches for performance - separate readable/writable for faster lookup
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _readablePropertyCache = new();
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _writablePropertyCache = new();

        // Per-type "can this type participate in a cycle" answers so warm paths can skip depth tracking safely
        private static readonly ConcurrentDictionary<Type, bool> _needsDepthTrackingCache = new();

        // OPTIMIZATION: Use ThreadStatic instead of AsyncLocal for zero-allocation depth tracking
        [ThreadStatic]
        private static int _mappingDepth;

        // Cache statistics
        private static long _cacheHits;
        private static long _cacheMisses;
        private static readonly object _configLock = new();

        #region Configuration

        private static volatile bool _useLruCache;
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
        private static volatile int _maxDepth = 32;
        public static int MaxDepth
        {
            get => _maxDepth;
            set => _maxDepth = value > 0 ? value : 32;
        }

        /// <summary>
        /// Logger for diagnostic output. Null disables logging.
        /// </summary>
        public static Action<string>? Logger { get; set; }

        private static void ReinitializeCaches()
        {
            _mapToCache.Clear();
            _mapCache.Clear();
            _propertyCache.Clear();
            _readablePropertyCache.Clear();
            _writablePropertyCache.Clear();
            _needsDepthTrackingCache.Clear();
            ResetTypedCaches();
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
                _readablePropertyCache.Clear();
                _writablePropertyCache.Clear();
                _needsDepthTrackingCache.Clear();
                ResetTypedCaches();
                System.Threading.Interlocked.Exchange(ref _cacheHits, 0);
                System.Threading.Interlocked.Exchange(ref _cacheMisses, 0);
            }
        }

        /// <summary>
        /// Gets information about the current cache state.
        /// </summary>
        public static MapperCacheInfo CacheInfo()
        {
            // Typed mappers count toward MapToEntries. They are compiled MapTo delegates held for
            // the lifetime of the process, so reporting a total that excluded them meant CacheInfo()
            // said 0 while delegates were cached, which is exactly when someone is looking at it.
            var typed = _typedCacheResetters.Count;

            if (_useLruCache && _lruMapToCache != null && _lruMapCache != null)
            {
                return new MapperCacheInfo(
                    _lruMapToCache.Count + typed,
                    _lruMapCache.Count,
                    System.Threading.Interlocked.Read(ref _cacheHits),
                    System.Threading.Interlocked.Read(ref _cacheMisses));
            }
            return new MapperCacheInfo(_mapToCache.Count + typed, _mapCache.Count);
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

        /// <summary>
        /// Gets cached readable PropertyInfo array for a type (optimized for source types).
        /// </summary>
        internal static PropertyInfo[] GetCachedReadableProperties(Type type)
        {
            return _readablePropertyCache.GetOrAdd(type, t =>
            {
                var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                var count = 0;
                // Count first to avoid list allocation
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].GetIndexParameters().Length == 0 && props[i].CanRead)
                        count++;
                }
                var result = new PropertyInfo[count];
                var idx = 0;
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].GetIndexParameters().Length == 0 && props[i].CanRead)
                        result[idx++] = props[i];
                }
                return result;
            });
        }

        /// <summary>
        /// Gets cached writable PropertyInfo array for a type (optimized for destination types).
        /// </summary>
        internal static PropertyInfo[] GetCachedWritableProperties(Type type)
        {
            return _writablePropertyCache.GetOrAdd(type, t =>
            {
                var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                var count = 0;
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].GetIndexParameters().Length == 0 && props[i].CanWrite)
                        count++;
                }
                var result = new PropertyInfo[count];
                var idx = 0;
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].GetIndexParameters().Length == 0 && props[i].CanWrite)
                        result[idx++] = props[i];
                }
                return result;
            });
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

                // Flattening, decided by exactly the rule the mapper uses. Asking the same helper
                // is the point: this check answering "mapped" where TryCreateFlattenedBinding
                // answers "skip" is a validator that certifies a property the mapper will leave at
                // its default.
                bool hasFlattening = typeof(TSource).GetProperties()
                    .Any(sp => PropertyConversion.TryFindFlattenedSource(
                        destProp, sp, GetCachedReadableProperties(sp.PropertyType), out _));
                if (hasFlattening) continue;

                unmapped.Add(destProp.Name);
            }
            return unmapped;
        }

        #endregion

        #region Depth Tracking (Cycle Detection)

        private static bool IncrementDepth()
        {
            // OPTIMIZATION: ThreadStatic access is much faster than AsyncLocal
            if (_mappingDepth >= MaxDepth)
            {
                Logger?.Invoke($"[Mapsicle] Max depth {MaxDepth} reached - possible circular reference");
                return false;
            }
            _mappingDepth++;
            return true;
        }

        private static void DecrementDepth()
        {
            if (_mappingDepth > 0) _mappingDepth--;
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

        // Reset actions for the strongly-typed caches below, one per closed generic pair that has
        // been initialized. The cache itself stays a static field on TypedMapperCache<TSource,TDest>
        // because that is what makes the typed path fast: a static field read, no dictionary lookup
        // and no tuple key. But a per-closed-generic static is unreachable from ClearCache(), so
        // every typed mapper was invisible to CacheInfo(), survived ClearCache(), and was never
        // bounded by MaxCacheSize. In an application that closes generics over many type pairs that
        // is a permanent, unreportable retention of compiled delegates.
        //
        // Registering a reset action costs one dictionary insert per pair, once, at compile time.
        // The warm read path is untouched.
        private static readonly ConcurrentDictionary<(Type, Type), Action> _typedCacheResetters = new();
        private static readonly ConcurrentQueue<(Type, Type)> _typedCacheOrder = new();

        /// <summary>
        /// Number of compiled mappers held by the strongly-typed cache.
        /// </summary>
        internal static int TypedCacheCount => _typedCacheResetters.Count;

        private static void ResetTypedCaches()
        {
            foreach (var reset in _typedCacheResetters.Values)
            {
                reset();
            }
            _typedCacheResetters.Clear();
            while (_typedCacheOrder.TryDequeue(out _)) { }
        }

        /// <summary>
        /// Applies MaxCacheSize to the typed cache by evicting in registration order.
        /// </summary>
        /// <remarks>
        /// First-in rather than least-recently-used: a static field read leaves no access record to
        /// order by, and adding one would put a write on the path this cache exists to keep fast.
        /// The bound is what matters here; which entry goes is a secondary concern.
        /// </remarks>
        private static void TrimTypedCache()
        {
            while (_typedCacheResetters.Count > _maxCacheSize && _typedCacheOrder.TryDequeue(out var oldest))
            {
                if (_typedCacheResetters.TryRemove(oldest, out var reset))
                {
                    reset();
                }
            }
        }

        // PERFORMANCE: Strongly-typed cache avoids boxing and enables faster lookups
        // Thread-safety: Single volatile write of an immutable entry object ensures atomicity
        private static class TypedMapperCache<TSource, TDest>
        {
            private static volatile TypedMapperCacheEntry<TSource, TDest>? _entry;

            public static TypedMapperCacheEntry<TSource, TDest>? Entry => _entry;

            public static void Initialize(Func<TSource, TDest> mapper, bool requiresDepthTracking)
            {
                _entry = new TypedMapperCacheEntry<TSource, TDest>(mapper, requiresDepthTracking);

                var key = (typeof(TSource), typeof(TDest));
                if (_typedCacheResetters.TryAdd(key, static () => _entry = null))
                {
                    _typedCacheOrder.Enqueue(key);
                    if (_useLruCache)
                    {
                        TrimTypedCache();
                    }
                }
            }
        }

        private sealed class TypedMapperCacheEntry<TSource, TDest>
        {
            public readonly Func<TSource, TDest> CompiledMapper;
            public readonly bool RequiresDepthTracking;
            public TypedMapperCacheEntry(Func<TSource, TDest> mapper, bool requiresDepthTracking)
            {
                CompiledMapper = mapper;
                RequiresDepthTracking = requiresDepthTracking;
            }
        }

        /// <summary>
        /// High-performance strongly-typed mapping. Use this when source type is known at compile time.
        /// </summary>
        public static TDest? MapTo<TSource, TDest>(this TSource? source)
        {
            if (source is null) return default;

            // FAST PATH: Read entry once into local for thread-safe access
            var entry = TypedMapperCache<TSource, TDest>.Entry;
            if (entry != null)
            {
                if (entry.RequiresDepthTracking)
                {
                    if (!IncrementDepth()) return default;
                    try
                    {
                        return entry.CompiledMapper(source);
                    }
                    finally
                    {
                        DecrementDepth();
                    }
                }
                return entry.CompiledMapper(source);
            }

            // Cold path: Build and cache the typed mapper
            return BuildAndCacheTypedMapper<TSource, TDest>(source);
        }

        private static TDest? BuildAndCacheTypedMapper<TSource, TDest>(TSource source)
        {
            var sourceType = typeof(TSource);

            // Build the strongly-typed mapper and determine depth tracking
            bool requiresDepthTracking = HasNestedComplexTypes(sourceType);
            var mapper = BuildTypedMapper<TSource, TDest>();

            // Single atomic write of immutable entry
            TypedMapperCache<TSource, TDest>.Initialize(mapper, requiresDepthTracking);

            // Execute with depth tracking if needed
            if (requiresDepthTracking)
            {
                if (!IncrementDepth()) return default;
                try
                {
                    return mapper(source);
                }
                finally
                {
                    DecrementDepth();
                }
            }
            return mapper(source);
        }

        private static bool NeedsDepthTracking(Type type)
        {
            return _needsDepthTrackingCache.GetOrAdd(type, HasNestedComplexTypes);
        }

        private static bool HasNestedComplexTypes(Type type)
        {
            var props = GetCachedReadableProperties(type);
            for (int i = 0; i < props.Length; i++)
            {
                var propType = props[i].PropertyType;
                if (propType.IsClass && propType != typeof(string) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(propType))
                {
                    return true; // Has nested complex object - needs depth tracking
                }
            }
            return false;
        }

        private static Func<TSource, TDest> BuildTypedMapper<TSource, TDest>()
        {
            var sourceType = typeof(TSource);
            var destType = typeof(TDest);
            var sourceParam = Expression.Parameter(sourceType, "source");

            var sourceProps = GetCachedReadableProperties(sourceType);
            var destProps = GetCachedWritableProperties(destType);

            // For simple object-to-object mapping, build optimal expression
            if (destType.GetConstructor(Type.EmptyTypes) != null || destType.IsValueType)
            {
                var bindings = new List<MemberBinding>(destProps.Length);

                for (int i = 0; i < destProps.Length; i++)
                {
                    var destProp = destProps[i];
                    if (destProp.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;

                    var mapFromAttr = destProp.GetCustomAttribute<MapFromAttribute>();
                    string sourcePropertyName = mapFromAttr?.SourcePropertyName ?? destProp.Name;

                    PropertyInfo? sourceProp = null;
                    for (int j = 0; j < sourceProps.Length; j++)
                    {
                        if (sourceProps[j].Name.Equals(sourcePropertyName, StringComparison.OrdinalIgnoreCase))
                        {
                            sourceProp = sourceProps[j];
                            break;
                        }
                    }

                    if (sourceProp != null)
                    {
                        var binding = CreateTypedPropertyBinding<TSource>(destProp, sourceProp, sourceParam);
                        if (binding != null) bindings.Add(binding);
                    }
                    else
                    {
                        // Try flattening
                        var flattenedBinding = TryCreateTypedFlattenedBinding<TSource>(destProp, sourceProps, sourceParam);
                        if (flattenedBinding != null) bindings.Add(flattenedBinding);
                    }
                }

                var init = Expression.MemberInit(Expression.New(destType), bindings);
                return Expression.Lambda<Func<TSource, TDest>>(init, sourceParam).Compile();
            }

            // Fallback to constructor-based mapping
            return BuildConstructorBasedMapper<TSource, TDest>(sourceParam, sourceProps);
        }

        private static MemberBinding? CreateTypedPropertyBinding<TSource>(PropertyInfo destProp, PropertyInfo sourceProp, ParameterExpression sourceParam)
        {
            var propExp = Expression.Property(sourceParam, sourceProp);

            var value = PropertyConversion.TryBuild(
                propExp, sourceProp.PropertyType, destProp.PropertyType, BuildNestedMapCall);

            return value is null ? null : Expression.Bind(destProp, value);
        }

        private static MemberBinding? TryCreateTypedFlattenedBinding<TSource>(PropertyInfo destProp, PropertyInfo[] sourceProps, ParameterExpression sourceParam)
        {
            for (int i = 0; i < sourceProps.Length; i++)
            {
                var sourceProp = sourceProps[i];
                var nestedProps = GetCachedReadableProperties(sourceProp.PropertyType);

                if (PropertyConversion.TryFindFlattenedSource(destProp, sourceProp, nestedProps, out var nestedProp)
                    && nestedProp != null)
                {
                    var parentAccess = Expression.Property(sourceParam, sourceProp);
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

        private static Func<TSource, TDest> BuildConstructorBasedMapper<TSource, TDest>(ParameterExpression sourceParam, PropertyInfo[] sourceProps)
        {
            var destType = typeof(TDest);
            var ctors = destType.GetConstructors();
            ConstructorInfo? ctor = null;
            int maxParams = -1;
            for (int i = 0; i < ctors.Length; i++)
            {
                var paramCount = ctors[i].GetParameters().Length;
                if (paramCount > maxParams)
                {
                    maxParams = paramCount;
                    ctor = ctors[i];
                }
            }

            if (ctor != null)
            {
                var ctorParams = ctor.GetParameters();
                var args = new List<Expression>(ctorParams.Length);
                for (int paramIdx = 0; paramIdx < ctorParams.Length; paramIdx++)
                {
                    var param = ctorParams[paramIdx];
                    PropertyInfo? sourceProp = null;
                    for (int j = 0; j < sourceProps.Length; j++)
                    {
                        if (sourceProps[j].Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            sourceProp = sourceProps[j];
                            break;
                        }
                    }

                    if (sourceProp != null)
                    {
                        var propExp = Expression.Property(sourceParam, sourceProp);
                        if (param.ParameterType.IsAssignableFrom(sourceProp.PropertyType))
                        {
                            args.Add(Expression.Convert(propExp, param.ParameterType));
                        }
                        else if (param.ParameterType == typeof(string))
                        {
                            args.Add(PropertyConversion.BuildToString(propExp, sourceProp.PropertyType));
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
                return Expression.Lambda<Func<TSource, TDest>>(newExp, sourceParam).Compile();
            }

            return _ => default!;
        }

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

            var sourceType = source.GetType();
            var destType = typeof(T);
            var key = (sourceType, destType);

            // OPTIMIZATION: Fast path - inline cache check without method call
            if (!_useLruCache && _mapToCache.TryGetValue(key, out var cached))
            {
                // Depth tracking can only be skipped for types that cannot form cycles.
                // Skipping based on _mappingDepth == 0 would also disable tracking in recursive
                // calls (they see depth 0 too), turning circular graphs into stack overflows.
                if (!NeedsDepthTracking(sourceType))
                {
                    return ((Func<object, T>)cached)(source);
                }
                if (!IncrementDepth()) return default;
                try
                {
                    return ((Func<object, T>)cached)(source);
                }
                finally
                {
                    DecrementDepth();
                }
            }

            // Cold path with depth tracking
            if (!IncrementDepth())
            {
                return default;
            }

            try
            {
                // Build the delegate
                var mapFunction = GetOrAddMapToDelegate<T>(key, k =>
            {
                var sourceType = k.Item1;
                var destType = k.Item2;
                var sourceParam = Expression.Parameter(typeof(object), "source");
                bool isSourceVisible = sourceType.IsVisible;
                var typedSource = Expression.Convert(sourceParam, sourceType);

                // --- 0. Direct Primitive/Value Mapping ---
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

                // OPTIMIZATION: Use cached property arrays instead of reflection + LINQ
                var sourceProps = GetCachedReadableProperties(sourceType);
                var destProps = GetCachedWritableProperties(destType);
                var bindings = new List<MemberBinding>(destProps.Length);

                // --- 1. Parameterless Constructor Path ---
                if (destType.GetConstructor(Type.EmptyTypes) != null || destType.IsValueType)
                {
                    // OPTIMIZATION: destProps is already filtered to writable, use for loop instead of foreach
                    for (int i = 0; i < destProps.Length; i++)
                    {
                        var destProp = destProps[i];
                        if (destProp.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;

                        var mapFromAttr = destProp.GetCustomAttribute<MapFromAttribute>();
                        string sourcePropertyName = mapFromAttr?.SourcePropertyName ?? destProp.Name;

                        var sourceProp = FindSourceProperty(sourceProps, sourcePropertyName, destProp.Name);

                        if (sourceProp != null)
                        {
                            var binding = CreatePropertyBinding(destProp, sourceProp, typedSource, sourceParam, isSourceVisible);
                            if (binding != null) bindings.Add(binding);
                        }
                        else
                        {
                            // Try flattening: AddressCity -> Address.City
                            var flattenedBinding = TryCreateFlattenedBinding(destProp, sourceProps, typedSource, sourceParam, isSourceVisible);
                            if (flattenedBinding != null) bindings.Add(flattenedBinding);
                        }
                    }
                    var init = Expression.MemberInit(Expression.New(destType), bindings);
                    return Expression.Lambda<Func<object, T>>(init, sourceParam).Compile();
                }

                // --- 2. Constructor / Record Path ---
                // OPTIMIZATION: Find largest constructor without LINQ
                var ctors = destType.GetConstructors();
                System.Reflection.ConstructorInfo? ctor = null;
                int maxParams = -1;
                for (int i = 0; i < ctors.Length; i++)
                {
                    var paramCount = ctors[i].GetParameters().Length;
                    if (paramCount > maxParams)
                    {
                        maxParams = paramCount;
                        ctor = ctors[i];
                    }
                }

                if (ctor != null)
                {
                    var ctorParams = ctor.GetParameters();
                    var args = new List<Expression>(ctorParams.Length);
                    for (int paramIdx = 0; paramIdx < ctorParams.Length; paramIdx++)
                    {
                        var param = ctorParams[paramIdx];
                        // OPTIMIZATION: Use for loop instead of LINQ FirstOrDefault
                        PropertyInfo? sourceProp = null;
                        for (int j = 0; j < sourceProps.Length; j++)
                        {
                            // CanRead check removed - sourceProps is already filtered
                            if (sourceProps[j].Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                sourceProp = sourceProps[j];
                                break;
                            }
                        }

                        if (sourceProp != null)
                        {
                            var propExp = Expression.Property(typedSource, sourceProp);
                            if (param.ParameterType.IsAssignableFrom(sourceProp.PropertyType))
                            {
                                args.Add(Expression.Convert(propExp, param.ParameterType));
                            }
                            else if (param.ParameterType == typeof(string))
                            {
                                var toStringCall = PropertyConversion.BuildToString(propExp, sourceProp.PropertyType);
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

                // OPTIMIZATION: Use cached property arrays
                var sourceProps = GetCachedReadableProperties(sourceType);
                var destProps = GetCachedWritableProperties(destType);
                var assignments = new List<Expression>(destProps.Length);

                for (int i = 0; i < destProps.Length; i++)
                {
                    var destProp = destProps[i];
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
                            var toStringCall = PropertyConversion.BuildToString(propExp, sourceProp.PropertyType);
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
        /// High-performance strongly-typed collection mapping. Use when source type is known at compile time.
        /// </summary>
        public static List<TDest> MapTo<TSource, TDest>(this IEnumerable<TSource>? source)
        {
            if (source is null) return new List<TDest>();

            // Pre-allocate with capacity hint
            List<TDest> result;
            if (source is ICollection<TSource> collection)
            {
                result = new List<TDest>(collection.Count);
            }
            else if (source is System.Collections.ICollection legacyCollection)
            {
                result = new List<TDest>(legacyCollection.Count);
            }
            else
            {
                result = new List<TDest>();
            }

            // Read entry once for thread-safe access
            var entry = TypedMapperCache<TSource, TDest>.Entry;
            if (entry == null)
            {
                // Initialize on first non-null item
                using var enumerator = source.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var first = enumerator.Current;
                    if (first is null)
                    {
                        result.Add(default!);
                        continue;
                    }
                    // This will initialize the cache
                    result.Add(first.MapTo<TSource, TDest>()!);

                    // Re-read the now-initialized entry
                    entry = TypedMapperCache<TSource, TDest>.Entry;
                    // Now process remaining with fast path
                    // (route through MapTo when depth tracking is required so cyclic items can't overflow the stack)
                    while (enumerator.MoveNext())
                    {
                        var item = enumerator.Current;
                        if (item is null)
                        {
                            result.Add(default!);
                        }
                        else
                        {
                            result.Add(entry!.RequiresDepthTracking ? item.MapTo<TSource, TDest>()! : entry.CompiledMapper(item)!);
                        }
                    }
                    return result;
                }
                return result;
            }

            // Fast path - mapper already cached
            // (route through MapTo when depth tracking is required so cyclic items can't overflow the stack)
            var mapper = entry.CompiledMapper;
            bool trackDepth = entry.RequiresDepthTracking;
            foreach (var item in source)
            {
                if (item is null)
                {
                    result.Add(default!);
                }
                else
                {
                    result.Add(trackDepth ? item.MapTo<TSource, TDest>()! : mapper(item)!);
                }
            }
            return result;
        }

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
            Type? itemType = null;
            Func<object, T>? cachedMapper = null;
            bool trackDepth = false;

            foreach (var item in source)
            {
                if (item is null)
                {
                    result.Add(default!);
                    continue;
                }

                // The cached delegate is compiled for exactly one runtime type, and its first
                // instruction is a cast to that type. A collection declared List<Animal> may hold a
                // Dog and then a Cat, in which case applying Dog's delegate to the Cat threw
                // InvalidCastException from inside the compiled lambda. Fall back to the per-item
                // path for the odd item out; the homogeneous case, which is nearly all of them,
                // still pays only one reference comparison.
                if (cachedMapper is not null && item.GetType() != itemType)
                {
                    result.Add(item.MapTo<T>()!);
                    continue;
                }

                // Lazily get mapper for first non-null item
                if (cachedMapper is null)
                {
                    itemType = item.GetType();
                    var key = (itemType, typeof(T));
                    // OPTIMIZATION: Direct cache access
                    if (!_useLruCache && _mapToCache.TryGetValue(key, out var cached))
                    {
                        cachedMapper = (Func<object, T>)cached;
                        trackDepth = NeedsDepthTracking(itemType);
                    }
                    else
                    {
                        // Fall back to single-item MapTo which will cache the delegate
                        result.Add(item.MapTo<T>()!);
                        // Now get the cached delegate for subsequent items
                        if (!_useLruCache && _mapToCache.TryGetValue(key, out cached))
                        {
                            cachedMapper = (Func<object, T>)cached;
                            trackDepth = NeedsDepthTracking(itemType);
                        }
                        continue;
                    }
                }

                // Items with nested complex objects must map under depth tracking so a
                // cyclic graph in any item hits MaxDepth instead of overflowing the stack
                if (trackDepth)
                {
                    if (!IncrementDepth())
                    {
                        result.Add(default!);
                        continue;
                    }
                    try
                    {
                        result.Add(cachedMapper(item)!);
                    }
                    finally
                    {
                        DecrementDepth();
                    }
                }
                else
                {
                    result.Add(cachedMapper(item)!);
                }
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
            var props = GetCachedProperties(source.GetType())
                .Where(p => p.CanRead);

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
            var destProps = GetCachedWritableProperties(typeof(T));

            // Build case-insensitive lookup once
            var lookup = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in source) lookup[kvp.Key] = kvp.Value;

            foreach (var prop in destProps)
            {
                if (prop.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;

                var mapFromAttr = prop.GetCustomAttribute<MapFromAttribute>();
                string key = mapFromAttr?.SourcePropertyName ?? prop.Name;

                if (lookup.TryGetValue(key, out var value) && value != null)
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
                    catch (Exception ex)
                    {
                        Logger?.Invoke($"[Mapsicle] Type conversion failed for property '{prop.Name}': {ex.Message}");
                    }
                }
            }

            return dest;
        }

        #endregion

        #region Private Helpers

        private static PropertyInfo? FindSourceProperty(PropertyInfo[] sourceProps, string primaryName, string fallbackName)
        {
            // OPTIMIZATION: Use for loop instead of LINQ - avoids allocations
            PropertyInfo? fallbackMatch = null;
            for (int i = 0; i < sourceProps.Length; i++)
            {
                var prop = sourceProps[i];
                // CanRead check removed - sourceProps is already filtered to readable
                if (prop.Name.Equals(primaryName, StringComparison.OrdinalIgnoreCase))
                {
                    return prop;
                }
                if (fallbackMatch == null && prop.Name.Equals(fallbackName, StringComparison.OrdinalIgnoreCase))
                {
                    fallbackMatch = prop;
                }
            }
            return fallbackMatch;
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

            var value = PropertyConversion.TryBuild(
                propExp, sourceProp.PropertyType, destProp.PropertyType, BuildNestedMapCall);

            return value is null ? null : Expression.Bind(destProp, value);
        }

        /// <summary>
        /// The recursive <c>MapTo&lt;T&gt;(object)</c> call used for a nested complex object.
        /// </summary>
        /// <remarks>
        /// Selected by exact signature. This used to be
        /// <c>GetMethods().First(m =&gt; m.Name == "MapTo" &amp;&amp; ...)</c>, and three public overloads
        /// satisfy that predicate: the <c>object</c> one, the <c>IEnumerable</c> one and the
        /// <c>IDictionary</c> one. <see cref="Type.GetMethods()"/> does not guarantee order, so
        /// which overload got picked was not decided by the code. It happened to be the right one
        /// on .NET 8; a different order would have produced either a delegate-build failure or, via
        /// the <c>IDictionary</c> overload's <c>where T : new()</c> constraint, an
        /// <see cref="ArgumentException"/> from <c>MakeGenericMethod</c> for any destination
        /// without a public parameterless constructor.
        /// </remarks>
        private static Expression BuildNestedMapCall(Expression propExp, Type targetType)
        {
            var mapMethod = MapToObjectOverload.MakeGenericMethod(targetType);
            return Expression.Call(null, mapMethod, Expression.Convert(propExp, typeof(object)));
        }

        private static readonly MethodInfo MapToObjectOverload =
            typeof(Mapper).GetMethod(nameof(MapTo), new[] { typeof(object) })
            ?? throw new InvalidOperationException(
                "Mapper.MapTo<T>(object) was not found. Renaming or changing that overload breaks nested mapping.");

        /// <summary>
        /// Attempts to create a binding for flattened properties (e.g., AddressCity -> Address.City).
        /// </summary>
        private static MemberBinding? TryCreateFlattenedBinding(PropertyInfo destProp, PropertyInfo[] sourceProps,
            Expression typedSource, ParameterExpression sourceParam, bool isSourceVisible)
        {
            // Try to find nested properties by splitting the destination name
            // OPTIMIZATION: Use for loop instead of foreach
            for (int i = 0; i < sourceProps.Length; i++)
            {
                var sourceProp = sourceProps[i];

                // OPTIMIZATION: Use cached readable properties for nested type
                var nestedProps = GetCachedReadableProperties(sourceProp.PropertyType);

                if (PropertyConversion.TryFindFlattenedSource(destProp, sourceProp, nestedProps, out var nestedProp)
                    && nestedProp != null)
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
