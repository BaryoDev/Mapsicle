using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>The name of the source property to read this member from.</summary>
        public string SourcePropertyName { get; }

        /// <summary>Maps this member from a source property with a different name.</summary>
        /// <param name="sourcePropertyName">The source property name.</param>
        public MapFromAttribute(string sourcePropertyName) => SourcePropertyName = sourcePropertyName;
    }

    #endregion

    #region Cache Info

    /// <summary>
    /// Contains information about the mapper cache state.
    /// </summary>
    public readonly struct MapperCacheInfo
    {
        /// <summary>Cache sizes with no statistics, used when counting is not active.</summary>
        /// <param name="mapToEntries">Number of cached MapTo delegates.</param>
        /// <param name="mapEntries">Number of cached in-place Map delegates.</param>
        public MapperCacheInfo(int mapToEntries, int mapEntries)
        {
            MapToEntries = mapToEntries;
            MapEntries = mapEntries;
            Hits = 0;
            Misses = 0;
        }

        /// <summary>Cache sizes together with the hit and miss counts.</summary>
        /// <param name="mapToEntries">Number of cached MapTo delegates.</param>
        /// <param name="mapEntries">Number of cached in-place Map delegates.</param>
        /// <param name="hits">Reads served from the cache.</param>
        /// <param name="misses">Reads that had to build a delegate.</param>
        public MapperCacheInfo(int mapToEntries, int mapEntries, long hits, long misses)
        {
            MapToEntries = mapToEntries;
            MapEntries = mapEntries;
            Hits = hits;
            Misses = misses;
        }

        /// <summary>Number of cached MapTo delegates.</summary>
        public int MapToEntries { get; }

        /// <summary>Number of cached in-place Map delegates.</summary>
        public int MapEntries { get; }

        /// <summary>Total cached delegates across both caches.</summary>
        public int Total => MapToEntries + MapEntries;

        /// <summary>
        /// Number of cache hits. Zero unless <see cref="Mapper.UseLruCache"/> is enabled.
        /// </summary>
        /// <remarks>
        /// Only the bounded cache counts, because it is the only one with a capacity worth tuning
        /// against a hit ratio, and because an atomic increment on every call of the default warm
        /// path would cost more than the number is worth.
        /// </remarks>
        public long Hits { get; }

        /// <summary>
        /// Number of cache misses. Zero unless <see cref="Mapper.UseLruCache"/> is enabled.
        /// </summary>
        public long Misses { get; }

        /// <summary>
        /// Cache hit ratio, from 0.0 to 1.0. Zero when nothing has been counted, which includes
        /// every case where <see cref="Mapper.UseLruCache"/> is off.
        /// </summary>
        public double HitRatio => Hits + Misses > 0 ? (double)Hits / (Hits + Misses) : 0.0;
    }

    #endregion

    /// <summary>
    /// Maps objects by convention, with no configuration and no registration.
    /// </summary>
    /// <remarks>
    /// Every entry point compiles a delegate on the first map of a given type pair and caches it, so
    /// the cost of the conversion rules is paid once rather than per call. The rules themselves are
    /// stated in one place, so the answer does not depend on which entry point was used.
    /// </remarks>
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

        // Bumped whenever the delegate caches are cleared. Call-site holders compare against it, so
        // ClearCache still means what it says: a holder that resolved before the clear is stale and
        // resolves again. Without it a test calling ClearCache would keep using the very delegate it
        // was trying to discard, which is the coincidental pass CLAUDE.md section 4 warns about.
        private static int _cacheGeneration;

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

        /// <summary>
        /// Maximum mapping depth for cycle detection. Default: 32. A value below 1 is refused and
        /// the default is kept.
        /// </summary>
        public static int MaxDepth
        {
            get => _maxDepth;
            set => _maxDepth = value > 0 ? value : 32;
        }

        /// <summary>
        /// Logger for diagnostic output. Null disables logging.
        /// </summary>
        public static Action<string>? Logger { get; set; }

        /// <summary>
        /// Whether the dictionary entry point parses values whose runtime type does not match the
        /// destination property, for example the string "123" into an <c>int</c>. Default: false.
        /// </summary>
        /// <remarks>
        /// The documented rule is that a value of the wrong type is dropped rather than coerced. The
        /// object and property entry points always honoured it; the dictionary one did not, and ran
        /// <c>Convert.ChangeType</c> on anything <c>IConvertible</c>. The two doors therefore
        /// disagreed about what "wrong type" meant, and an attacker controlling a dictionary (a
        /// parsed form post, a document, a header bag) could push string payloads through
        /// conversions the object path rejects.
        ///
        /// Parsing is still available for callers who genuinely want it, because a dictionary of
        /// strings is the normal shape of a form post, but it is now a decision rather than a
        /// default. When enabled, parsing uses <see cref="CultureInfo.InvariantCulture"/>, so the
        /// same input maps to the same value in every region.
        ///
        /// Widening, enum and nullable conversions are not affected: those are lossless and apply on
        /// every entry point regardless of this setting.
        /// </remarks>
        public static bool CoerceDictionaryValues { get; set; }

        private static void ReinitializeCaches()
        {
            System.Threading.Interlocked.Increment(ref _cacheGeneration);
            _mapToCache.Clear();
            _mapCache.Clear();
            _excludingMapCache.Clear();
            _listLoopCache.Clear();
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

            // Last, because the caches a registration writes into are the ones replaced above.
            // Re-applying before that wrote into the caches this method was about to discard.
            ReapplyGenerated();
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
                System.Threading.Interlocked.Increment(ref _cacheGeneration);
                _mapToCache.Clear();
                _mapCache.Clear();
                _excludingMapCache.Clear();
                _listLoopCache.Clear();
                _lruMapToCache?.Clear();
                _lruMapCache?.Clear();
                _propertyCache.Clear();
                _readablePropertyCache.Clear();
                _writablePropertyCache.Clear();
                _needsDepthTrackingCache.Clear();
                ResetTypedCaches();
                ReapplyGenerated();
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
            // Compiled list loops count here too. Mapping a collection used to cache the element
            // delegate as a side effect of mapping its first item, and this counter reported it.
            // The compiled loop does not call that path, so without this a collection map would
            // build and keep a delegate while the public counter said nothing had been cached.
            var typed = _typedCacheResetters.Count + CompiledListLoopCount();

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
            var destProps = typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite);

            var readableSourceProps = GetCachedReadableProperties(typeof(TSource));

            foreach (var destProp in destProps)
            {
                // Resolved by exactly the helper the mapper uses, rather than by re-deciding here.
                // The two used to differ over [MapFrom] naming a property that does not exist: the
                // mapper falls back to the destination member's own name and fills it, while this
                // checked only the named property and reported the member unmapped. So
                // AssertMappingValid threw for a mapping that demonstrably works, which is the same
                // class of defect as certifying one that does not.
                if (!MemberResolution.TryResolveSource(destProp, readableSourceProps, out var resolved)) continue;

                if (resolved != null) continue;

                // Flattening, decided by exactly the rule the mapper uses. Asking the same helper
                // is the point: this check answering "mapped" where TryCreateFlattenedBinding
                // answers "skip" is a validator that certifies a property the mapper will leave at
                // its default.
                // The same readable-property set the mapper flattens over. An unfiltered
                // GetProperties() also returns write-only and indexed properties, which the mapper
                // never considers, so the validator could call a destination mapped from a source
                // property the mapper will not read.
                bool hasFlattening = GetCachedReadableProperties(typeof(TSource))
                    .Any(sp => PropertyConversion.TryFindFlattenedSource(
                        destProp, sp, GetCachedReadableProperties(sp.PropertyType), out _));
                if (hasFlattening) continue;

                unmapped.Add(destProp.Name);
            }
            return unmapped;
        }

        #endregion

        #region Depth Tracking (Cycle Detection)

        /// <summary>Source instances on the current mapping path, tracked only past the ceiling.</summary>
        /// <remarks>
        /// Allocated lazily and reused for the lifetime of the thread, so a map that never exceeds
        /// <see cref="MaxDepth"/> never touches it. That keeps the warm path allocating nothing
        /// beyond the destination, which section 5 of the working agreement treats as a correctness
        /// property rather than a preference.
        /// </remarks>
        [ThreadStatic]
        private static HashSet<object>? _onPath;

        /// <summary>The depth at which recursion stops even when no repeat has been seen.</summary>
        /// <remarks>
        /// A guard against exhausting the stack, not a cycle detector. A graph this deep would
        /// overflow hand written recursion too, so stopping is the only useful thing left.
        /// </remarks>
        private const int StackGuardDepth = 10_000;

        /// <summary>
        /// Enters one level of mapping, or refuses when the graph genuinely loops back on itself.
        /// </summary>
        /// <remarks>
        /// A counter alone cannot tell a cycle from a deep graph, and treating them the same lost
        /// real data: a two hundred element chain with no cycle in it came back holding thirty two
        /// elements, silently, because the counter reached <see cref="MaxDepth"/> and gave up. Both
        /// source generators return all two hundred.
        ///
        /// So the counter now decides only when to start checking. Below the ceiling nothing is
        /// tracked and nothing is allocated, which is the common case and the measured one. At the
        /// ceiling the source instances on the current path start being recorded, and mapping stops
        /// on a genuine repeat rather than on an arbitrary number. A cycle repeats within one loop of
        /// itself so it still terminates promptly, and a long chain never repeats so it completes.
        /// </remarks>
        private static bool IncrementDepth(object? source = null)
        {
            // OPTIMIZATION: ThreadStatic access is much faster than AsyncLocal
            if (_mappingDepth < MaxDepth)
            {
                _mappingDepth++;
                return true;
            }

            if (_mappingDepth >= StackGuardDepth)
            {
                Logger?.Invoke($"[Mapsicle] Depth {StackGuardDepth} reached, stopping to protect the stack");
                return false;
            }

            // Nothing to check against, so the old behaviour stands: stop. The collection loops take
            // one level for the whole loop rather than one per element, so they have no single
            // instance to offer. Continuing without an instance would mean nothing could ever stop
            // that path, and a list holding itself went from truncating to overflowing the stack.
            if (source is null)
            {
                Logger?.Invoke($"[Mapsicle] Max depth {MaxDepth} reached with no instance to check, stopping");
                return false;
            }

            var path = _onPath ??= new HashSet<object>(ReferenceIdentity.Instance);

            if (!path.Add(source))
            {
                Logger?.Invoke("[Mapsicle] Circular reference reached, the same instance is already being mapped");
                return false;
            }

            _mappingDepth++;
            return true;
        }

        private static void DecrementDepth(object? source = null)
        {
            if (_mappingDepth > 0) _mappingDepth--;

            if (source is not null && _mappingDepth >= MaxDepth)
            {
                _onPath?.Remove(source);
            }

            // Back at the top, so nothing is on the path. Cleared rather than freed, because the next
            // deep map on this thread would otherwise allocate it again.
            if (_mappingDepth == 0) _onPath?.Clear();
        }

        /// <summary>Compares by instance, not by whatever Equals the type happens to define.</summary>
        /// <remarks>
        /// Supplied rather than taken from the framework because netstandard2.0 is a target and
        /// <c>ReferenceEqualityComparer</c> arrived in .NET 5. A record, or anything else overriding
        /// Equals, would otherwise make two distinct instances look like a cycle.
        /// </remarks>
        private sealed class ReferenceIdentity : IEqualityComparer<object>
        {
            internal static readonly ReferenceIdentity Instance = new();

            bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);

            int IEqualityComparer<object>.GetHashCode(object obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        #endregion

        #region Cache Helpers

        /// <summary>
        /// Reads the MapTo delegate cache, counting the hit or the miss when statistics are live.
        /// </summary>
        /// <remarks>
        /// The counters used to be written by a method nothing called. Both real read paths reached
        /// the caches inline, so <c>CacheInfo().Hits</c> and <c>Misses</c> reported zero under any
        /// load, and <c>HitRatio</c> divided zero by zero into a constant zero. A metric that always
        /// reads zero while looking live is worse than an absent one, because it invites someone to
        /// tune <c>MaxCacheSize</c> against it.
        ///
        /// Counting happens on the LRU path only, which is what the properties have always
        /// documented and is also the only path where the number means anything: the unbounded cache
        /// has no capacity to tune. Keeping the atomic off the default warm path matters, because an
        /// interlocked increment on one shared cache line, taken on every single map call, is a
        /// throughput regression on a path measured in tens of nanoseconds.
        /// </remarks>
        /// <summary>The delegate for this pair, rebuilding it only when nothing has claimed it.</summary>
        /// <remarks>
        /// A miss consults the registry before compiling anything. Under the bounded cache a
        /// registered pair could be evicted like any other entry, and the rebuild then compiled an
        /// expression tree and answered with it, so the untyped door returned the engine's result
        /// while the typed door still returned the registration: the two doors disagreed for the
        /// rest of the process. The typed cache pins registrations during its trim and the untyped
        /// one cannot, since eviction there is the whole point of the bound, so the fix belongs on
        /// the rebuild. Eviction now costs a re-resolve rather than a wrong answer.
        /// </remarks>
        private static Func<object, T> GetOrAddMapToDelegate<T>((Type, Type) key, Func<(Type, Type), Delegate> factory)
        {
            if (_useLruCache && _lruMapToCache != null)
            {
                if (_lruMapToCache.TryGetValue(key, out var cached))
                {
                    System.Threading.Interlocked.Increment(ref _cacheHits);
                    return (Func<object, T>)cached;
                }

                System.Threading.Interlocked.Increment(ref _cacheMisses);

                if (_generatedPairs.TryGetValue(key, out var reapply))
                {
                    reapply();

                    if (_lruMapToCache.TryGetValue(key, out var restored))
                    {
                        return (Func<object, T>)restored;
                    }
                }

                return (Func<object, T>)_lruMapToCache.GetOrAdd(key, factory);
            }

            return (Func<object, T>)_mapToCache.GetOrAdd(key, factory);
        }

        private static Action<object, object> GetOrAddMapDelegate((Type, Type) key, Func<(Type, Type), Action<object, object>> factory)
        {
            if (_useLruCache && _lruMapCache != null)
            {
                if (_lruMapCache.TryGetValue(key, out var cached))
                {
                    System.Threading.Interlocked.Increment(ref _cacheHits);
                    return cached;
                }

                System.Threading.Interlocked.Increment(ref _cacheMisses);
                return _lruMapCache.GetOrAdd(key, factory);
            }

            return _mapCache.GetOrAdd(key, factory);
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
            // A generated registration is never evicted. The order is first in, and module
            // initializers run first, so registrations were always the oldest and went first: under
            // the bounded cache a declared pair degraded to the engine lane permanently once enough
            // other pairs had been mapped, because the rebuild path does not consult the registry.
            // The untyped cache already treats registrations this way, since Set never enqueues into
            // the access order. This makes the typed side agree.
            var scanned = 0;
            var capacity = _typedCacheOrder.Count;

            while (_typedCacheResetters.Count > _maxCacheSize
                   && scanned++ < capacity
                   && _typedCacheOrder.TryDequeue(out var oldest))
            {
                if (_generatedPairs.ContainsKey(oldest))
                {
                    _typedCacheOrder.Enqueue(oldest);
                    continue;
                }

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

            /// <summary>Stores an entry, unconditionally. Only a registration may call this.</summary>
            public static void Initialize(Func<TSource, TDest> mapper, bool requiresDepthTracking) =>
                Store(new TypedMapperCacheEntry<TSource, TDest>(mapper, requiresDepthTracking));

            /// <summary>Stores an entry the engine compiled, and yields to anything already there.</summary>
            /// <remarks>
            /// An unconditional write here lost completed registrations. Thread A takes the cold
            /// path, spends a slow Expression.Compile, and writes; thread B calls RegisterGenerated
            /// and writes; A finishes last and its engine-built entry wins. Both calls returned
            /// successfully and the registration was gone for the rest of the process, because the
            /// cold path never runs again and nothing rechecks the registry. Measured at roughly two
            /// thirds of three thousand interleavings, and it left the typed and untyped doors
            /// answering differently for the same pair.
            ///
            /// A registration is a statement about the pair; an engine build is a cache fill. The
            /// cache fill is the one that gives way.
            /// </remarks>
            public static void InitializeFromEngine(Func<TSource, TDest> mapper, bool requiresDepthTracking)
            {
                var candidate = new TypedMapperCacheEntry<TSource, TDest>(mapper, requiresDepthTracking);
                if (System.Threading.Interlocked.CompareExchange(ref _entry, candidate, null) != null) return;

                Register();
            }

            private static void Store(TypedMapperCacheEntry<TSource, TDest> entry)
            {
                _entry = entry;
                Register();
            }

            private static void Register()
            {

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
        /// Registers a mapper built at compile time, so the engine invokes it instead of compiling one.
        /// </summary>
        /// <remarks>
        /// The seam the source generator writes through. The engine already separates how a mapper is
        /// made from how it is used: the first map of a pair builds a delegate, a cache holds it, and
        /// every later call just invokes it. So a generated mapper replaces the factory, not the
        /// engine, and everything the engine does around it is unchanged.
        ///
        /// It fills every cache the entry points read, which is three rather than one. Registering
        /// only the typed cache would have made a generated mapping apply to
        /// <c>MapTo&lt;TSource, TDest&gt;()</c> and not to <c>MapTo&lt;TDest&gt;(object)</c>, which
        /// is the call every example in the README uses, nor to nested members, nor to collections.
        /// The compiled list loop is dropped for the pair rather than filled, so the next collection
        /// map rebuilds a loop that calls the generated element mapper.
        ///
        /// Calling this for a pair that has already been mapped replaces what the engine compiled.
        /// That is deliberate: a module initializer runs before user code, but a library loaded later
        /// should still win for its own pairs rather than lose to whatever ran first.
        /// </remarks>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The compile-time mapper for this pair.</param>
        /// <param name="requiresDepthTracking">
        /// Whether the source type can form a cycle. Pass what <c>HasNestedComplexTypes</c> would
        /// answer for <typeparamref name="TSource"/>; the generator knows this statically.
        /// </param>
        public static void RegisterGenerated<TSource, TDest>(
            Func<TSource, TDest> mapper, bool requiresDepthTracking)
        {
            if (mapper is null) throw new ArgumentNullException(nameof(mapper));

            // Recorded as well as applied. A generated mapper is a registration, not something the
            // engine compiled, so clearing the caches must not lose it: the module initializer that
            // registered it has already run and will not run again, and the pair would quietly fall
            // back to the expression builder for the rest of the process.
            _generatedPairs[(typeof(TSource), typeof(TDest))] = () => ApplyGenerated(mapper, requiresDepthTracking);
            ApplyGenerated(mapper, requiresDepthTracking);

            // A loop compiled earlier inlined the expression tree for this pair. Dropping it makes
            // the next collection map rebuild against the generated mapper rather than keep using
            // the one this call just replaced.
            foreach (var entry in _listLoopCache)
            {
                if (entry.Key.Item2 == typeof(TDest)) _listLoopCache.TryRemove(entry.Key, out _);
            }

            // A nested-member holder caches what it resolved last time and keeps it while the source
            // type still matches and the generation still matches. Dropping the list loop alone left
            // those holders untouched, so a parent mapped before this call kept invoking the
            // delegate this call replaced: map a parent, register, map the parent again, and the
            // nested member still came back from the expression builder. Bumping the generation is
            // what makes them re-resolve.
            System.Threading.Interlocked.Increment(ref _cacheGeneration);
        }

        private static void ApplyGenerated<TSource, TDest>(Func<TSource, TDest> mapper, bool requiresDepthTracking)
        {
            TypedMapperCache<TSource, TDest>.Initialize(mapper, requiresDepthTracking);

            // The untyped door keys on the runtime type of the source, so a generated mapper for
            // TSource answers for an instance of exactly TSource. A derived instance still resolves
            // its own pair, generated or compiled.
            var key = (typeof(TSource), typeof(TDest));
            Func<object, TDest> untyped = source => mapper((TSource)source);

            if (_useLruCache && _lruMapToCache != null)
            {
                // A replacing write, not GetOrAdd. Under the bounded cache a pair mapped before this
                // registration already had a compiled delegate stored, and GetOrAdd keeps whichever
                // arrived first, so the generated mapper never applied for the rest of the process.
                _lruMapToCache.Set(key, untyped);
            }
            else
            {
                _mapToCache[key] = untyped;
            }
        }

        /// <summary>
        /// Forgets every generated registration, so a clear no longer puts them back.
        /// </summary>
        /// <remarks>
        /// Internal because nothing in a running application should want it. A generator registers
        /// from a module initializer and the registration is meant to last the process. Tests are
        /// the exception: registrations outlive <c>ClearCache</c> by design now, so without this a
        /// pair registered by one test would answer for the next one.
        /// </remarks>
        internal static void ResetGeneratedRegistrations()
        {
            _generatedPairs.Clear();
            ClearCache();
        }

        /// <summary>
        /// Re-applies every generated registration after the caches have been emptied.
        /// </summary>
        private static void ReapplyGenerated()
        {
            foreach (var apply in _generatedPairs.Values)
            {
                apply();
            }
        }

        /// <summary>
        /// The generated element mapper for a pair, or null when the pair was not generated.
        /// </summary>
        private static Func<object, TDest>? GeneratedElementMapper<TDest>(Type elementType)
        {
            if (!_generatedPairs.ContainsKey((elementType, typeof(TDest)))) return null;
            return _mapToCache.TryGetValue((elementType, typeof(TDest)), out var cached)
                ? (Func<object, TDest>)cached
                : null;
        }

        /// <summary>
        /// Pairs whose mapper came from a generator rather than from the expression builder.
        /// </summary>
        /// <remarks>
        /// Both kinds live in the same delegate cache, because every entry point should invoke them
        /// the same way. The one place the difference matters is the compiled list loop, which earns
        /// its speed by inlining the expression tree for the element type. There is no expression to
        /// inline for a generated pair, and inlining what the builder would have produced would
        /// quietly ignore the generated mapper, so the loop stands aside and the element delegate is
        /// invoked per item instead. That is the slower loop and the faster mapper.
        /// </remarks>
        private static readonly ConcurrentDictionary<(Type, Type), Action> _generatedPairs = new();

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
                    if (!IncrementDepth(source)) return default;
                    try
                    {
                        return entry.CompiledMapper(source);
                    }
                    finally
                    {
                        DecrementDepth(source);
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

            // The yielding write, because a registration may have landed while this was compiling.
            TypedMapperCache<TSource, TDest>.InitializeFromEngine(mapper, requiresDepthTracking);

            // Execute with depth tracking if needed
            if (requiresDepthTracking)
            {
                if (!IncrementDepth(source)) return default;
                try
                {
                    return mapper(source);
                }
                finally
                {
                    DecrementDepth(source);
                }
            }
            return mapper(source);
        }

        private static bool NeedsDepthTracking(Type type)
        {
            return _needsDepthTrackingCache.GetOrAdd(type, HasNestedComplexTypes);
        }

        /// <summary>
        /// Whether a type can take part in a reference cycle, and so needs depth tracking.
        /// </summary>
        /// <remarks>
        /// This used to treat any <c>IEnumerable</c> property as harmless, so a type whose only
        /// recursion ran through a collection of itself was judged acyclic. Depth tracking was then
        /// skipped and the collection path recursed with no ceiling: a tree node holding a
        /// <c>List</c> of its own type with a back edge overflowed the stack and took the process
        /// down with an uncatchable <c>StackOverflowException</c>. The ASP.NET Core helpers map on
        /// this path, so a self-referential request body was a remote crash.
        ///
        /// A collection is now judged by what it holds, not by being a collection.
        /// </remarks>
        private static bool HasNestedComplexTypes(Type type)
        {
            var props = GetCachedReadableProperties(type);
            for (int i = 0; i < props.Length; i++)
            {
                if (CanHoldMappableReference(props[i].PropertyType))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool CanHoldMappableReference(Type propType) =>
            CanHoldMappableReference(propType, null);

        /// <summary>
        /// Whether a member of this type could hold a reference worth following, and therefore
        /// could take part in a cycle.
        /// </summary>
        /// <param name="propType">The declared member type.</param>
        /// <param name="seen">
        /// Element types already being examined on this walk. Null until the walk descends, so the
        /// common case of a member that is not a collection allocates nothing.
        /// </param>
        /// <remarks>
        /// The walk has to be cycle-aware itself. A type declared as <c>IEnumerable&lt;Self&gt;</c>
        /// has itself as its own element type, so asking the element the same question recurses
        /// forever and overflows the stack, which is the exact failure this predicate exists to
        /// prevent. Revisiting a type means it can reach itself, so the answer is yes and the walk
        /// stops there.
        ///
        /// Value types are examined rather than dismissed. A struct is not a reference, but a
        /// generic one can hold references in its arguments, and a
        /// <c>Dictionary&lt;string, Node&gt;</c> enumerates as
        /// <c>KeyValuePair&lt;string, Node&gt;</c>. Treating that struct as inert would judge a
        /// dictionary of nodes acyclic and put the crash back.
        /// </remarks>
        private static bool CanHoldMappableReference(Type propType, HashSet<Type>? seen)
        {
            if (propType == typeof(string) || propType.IsPrimitive || propType.IsEnum)
            {
                return false;
            }

            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(propType))
            {
                var element = GetEnumerableElementType(propType);

                // A non-generic IEnumerable advertises nothing about what it holds, so it has to be
                // assumed capable of holding a cycle. Guessing the other way is how this crashed.
                if (element is null)
                {
                    return true;
                }

                seen ??= new HashSet<Type>();
                if (!seen.Add(propType))
                {
                    return true;
                }

                return CanHoldMappableReference(element, seen);
            }

            if (propType.IsValueType)
            {
                if (!propType.IsGenericType)
                {
                    return false;
                }

                var arguments = propType.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++)
                {
                    seen ??= new HashSet<Type>();
                    if (!seen.Add(propType))
                    {
                        return true;
                    }

                    if (CanHoldMappableReference(arguments[i], seen))
                    {
                        return true;
                    }
                }

                return false;
            }

            return propType.IsClass || propType.IsInterface;
        }

        private static Type? GetEnumerableElementType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return type.GetGenericArguments()[0];
            }

            var interfaces = type.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                if (interfaces[i].IsGenericType && interfaces[i].GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return interfaces[i].GetGenericArguments()[0];
                }
            }

            return null;
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
                    if (!MemberResolution.TryResolveSource(destProp, sourceProps, out var sourceProp)) continue;

                    if (sourceProp != null)
                    {
                        var binding = CreateTypedPropertyBinding<TSource>(destProp, sourceProp, sourceParam);
                        if (binding != null) bindings.Add(binding);
                    }
                    else
                    {
                        // Try flattening
                        var flattenedBinding = TryBindFlattenedPath(destProp, sourceProps, sourceParam);
                        if (flattenedBinding != null) bindings.Add(flattenedBinding);
                    }
                }

                var init = Expression.MemberInit(Expression.New(destType), bindings);
                var body = WithFilledCollections(init, sourceType, destType, sourceParam);
                return Expression.Lambda<Func<TSource, TDest>>(body, sourceParam).Compile();
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

        /// <summary>
        /// Completes a constructor-built destination by mapping the members the constructor did not.
        /// </summary>
        /// <remarks>
        /// The constructor parameters were matched and filled and the mapping stopped there, so a
        /// destination with a parameterized constructor came back with every other writable member
        /// still at its initialiser and nothing raised. That is the shape of most immutable DTOs
        /// and every positional record.
        ///
        /// A member whose name matches a constructor parameter is left alone: it already carries
        /// the value the constructor was given, and re-binding it would either duplicate that work
        /// or, for a get-only property, fail to build at all.
        /// </remarks>
        internal static Expression CompleteConstructedDestination(
            ConstructorInfo ctor,
            NewExpression newExp,
            PropertyInfo[] destProps,
            Expression typedSource,
            PropertyInfo[] sourceProps,
            Func<Expression, Type, Expression> nestedMapCall)
        {
            var fromConstructor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in ctor.GetParameters())
            {
                if (parameter.Name != null) fromConstructor.Add(parameter.Name);
            }

            List<MemberBinding>? bindings = null;

            for (int i = 0; i < destProps.Length; i++)
            {
                var destProp = destProps[i];
                if (!destProp.CanWrite) continue;
                if (fromConstructor.Contains(destProp.Name)) continue;
                if (!MemberResolution.TryResolveSource(destProp, sourceProps, out var sourceProp)) continue;
                if (sourceProp is null) continue;

                var value = PropertyConversion.TryBuild(
                    Expression.Property(typedSource, sourceProp),
                    sourceProp.PropertyType,
                    destProp.PropertyType,
                    nestedMapCall);

                if (value != null)
                {
                    (bindings ??= new List<MemberBinding>()).Add(Expression.Bind(destProp, value));
                }
            }

            return bindings is null ? newExp : Expression.MemberInit(newExp, bindings);
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
                var body = CompleteConstructedDestination(
                    ctor, newExp, GetCachedWritableProperties(destType), sourceParam, sourceProps, BuildNestedMapCall);
                return Expression.Lambda<Func<TSource, TDest>>(body, sourceParam).Compile();
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
                if (!IncrementDepth(source)) return default;
                try
                {
                    return ((Func<object, T>)cached)(source);
                }
                finally
                {
                    DecrementDepth(source);
                }
            }

            // Cold path with depth tracking
            if (!IncrementDepth(source))
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
                // Asks the shared cascade rather than deciding here. This block used to carry its
                // own reduced copy covering assignable types, ToString and the nullable underlying
                // type, and nothing else, so a value mapped on its own rather than as a property
                // silently lost every conversion the cascade performs: (object)42 into a long came
                // back as 0, and so did an enum into an int. It is the same defect as in-place Map,
                // one level up, and it also reached anything mapping dictionary values.
                if (sourceType.IsValueType || sourceType == typeof(string))
                {
                    var direct = PropertyConversion.TryBuild(typedSource, sourceType, destType, BuildNestedMapCall);
                    if (direct is not null)
                    {
                        return Expression.Lambda<Func<object, T>>(direct, sourceParam).Compile();
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

                    // A dictionary destination is built by mapping keys and values separately. It
                    // enumerates as KeyValuePair<K, V>, a struct with read-only properties, so
                    // mapping the pair as an object yields a default pair with a null key and the
                    // IEnumerable constructor below then throws on the first one.
                    if (targetItemType.IsGenericType
                        && targetItemType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
                        && typeof(System.Collections.IDictionary).IsAssignableFrom(sourceType))
                    {
                        var pairArgs = targetItemType.GetGenericArguments();
                        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(pairArgs);

                        if (destType.IsAssignableFrom(dictionaryType))
                        {
                            var buildDictionary = typeof(Mapper)
                                .GetMethod(nameof(BuildMappedDictionary), BindingFlags.NonPublic | BindingFlags.Static)!
                                .MakeGenericMethod(pairArgs);

                            var built = Expression.Call(buildDictionary, Expression.Convert(sourceParam, typeof(object)));
                            return Expression.Lambda<Func<object, T>>(Expression.Convert(built, destType), sourceParam).Compile();
                        }
                    }

                    // A destination that is neither an array nor assignable from List<T> used to
                    // fall through to the member-init path below, which constructed the collection
                    // and populated nothing. A HashSet<string> came back non-null and empty, so the
                    // destination looked mapped while every item had been dropped.
                    //
                    // Most collections take IEnumerable<T>: HashSet, SortedSet, Queue, Stack,
                    // Collection and ObservableCollection.
                    var fromEnumerable = destType.GetConstructor(
                        new[] { typeof(IEnumerable<>).MakeGenericType(targetItemType) });

                    if (fromEnumerable != null)
                    {
                        // Guarded, because a collection constructor can reject the items it is
                        // handed: SortedSet<T> throws when T has no ordering, and this branch is
                        // reached for any destination that is not assignable from List<T>. Before
                        // this branch existed that case produced an empty collection, so letting it
                        // throw would turn silent data loss into a map-time exception, which
                        // PropertyConversion's own rule says is the worse of the two. It degrades
                        // to the destination default and says so through the logger.
                        var exception = Expression.Parameter(typeof(Exception), "ex");
                        var built = Expression.Convert(Expression.New(fromEnumerable, call), destType);

                        var guarded = Expression.TryCatch(
                            built,
                            Expression.Catch(
                                exception,
                                Expression.Call(
                                    typeof(Mapper)
                                        .GetMethod(nameof(LogCollectionFallback), BindingFlags.NonPublic | BindingFlags.Static)!
                                        .MakeGenericMethod(destType),
                                    exception,
                                    Expression.Constant(destType, typeof(Type)))));

                        return Expression.Lambda<Func<object, T>>(guarded, sourceParam).Compile();
                    }
                }

                // OPTIMIZATION: Use cached property arrays instead of reflection + LINQ
                var sourceProps = GetCachedReadableProperties(sourceType);

                // --- 1. Parameterless Constructor Path ---
                var memberInit = TryBuildMemberInit(sourceType, destType, typedSource, sourceParam, isSourceVisible);
                if (memberInit != null)
                {
                    var body = WithFilledCollections(memberInit, sourceType, destType, typedSource);
                    return Expression.Lambda<Func<object, T>>(body, sourceParam).Compile();
                }

                var destProps = GetCachedWritableProperties(destType);

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
                    var body = CompleteConstructedDestination(
                        ctor, newExp, GetCachedWritableProperties(destType), typedSource, sourceProps, BuildNestedMapCall);
                    return Expression.Lambda<Func<object, T>>(body, sourceParam).Compile();
                }

                return Expression.Lambda<Func<object, T>>(Expression.Default(destType), sourceParam).Compile();
            });

                return mapFunction(source);
            }
            finally
            {
                DecrementDepth(source);
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
            GetOrAddMapDelegate(key, k => BuildInPlaceMapper(k.Item1, k.Item2, null))(source, destination);
            return destination;
        }

        /// <summary>
        /// The in-place mapping delegate for one pair, optionally skipping named members.
        /// </summary>
        /// <remarks>
        /// Deliberately internal and deliberately returning the delegate rather than doing the
        /// map. The first attempt at this was a public <c>Map(source, destination, excluded)</c>
        /// overload, which had to turn the member names into a cache key on every call: an array,
        /// a lowercased string per name and a joined string, per map. It made the caller it was
        /// written for three times slower and quadrupled its allocation. Handing back the delegate
        /// lets the caller resolve once and keep it, which is the only way to use this well, so it
        /// is the only way it is offered.
        ///
        /// <c>Mapsicle.Fluent</c> is the caller: it writes some members itself and would rather
        /// the convention pass did not compute them first.
        /// </remarks>
        internal static Action<object, object> GetInPlaceMapper(
            Type sourceType, Type destType, IReadOnlyCollection<string>? excludedMembers)
        {
            if (excludedMembers is null || excludedMembers.Count == 0)
            {
                return GetOrAddMapDelegate((sourceType, destType), k => BuildInPlaceMapper(k.Item1, k.Item2, null));
            }

            var key = (sourceType, destType, ExclusionKey(excludedMembers));
            return _excludingMapCache.GetOrAdd(key, k => BuildInPlaceMapper(k.Item1, k.Item2, excludedMembers));
        }

        private static readonly ConcurrentDictionary<(Type, Type, string), Action<object, object>> _excludingMapCache = new();

        /// <summary>
        /// A stable key for a set of member names, independent of the order they arrived in.
        /// </summary>
        private static string ExclusionKey(IReadOnlyCollection<string> excludedMembers)
        {
            var names = new string[excludedMembers.Count];
            var i = 0;
            foreach (var name in excludedMembers) names[i++] = name.ToLowerInvariant();
            Array.Sort(names, StringComparer.Ordinal);
            return string.Join("\u001f", names);
        }

        /// <summary>
        /// Builds the in-place mapping delegate for one pair, optionally skipping named members.
        /// </summary>
        private static Action<object, object> BuildInPlaceMapper(
            Type sourceType, Type destType, IReadOnlyCollection<string>? excludedMembers)
        {
            var sourceParam = Expression.Parameter(typeof(object), "source");
            var destParam = Expression.Parameter(typeof(object), "destination");

            var typedSource = Expression.Convert(sourceParam, sourceType);
            var typedDest = Expression.Convert(destParam, destType);

            var sourceProps = GetCachedReadableProperties(sourceType);
            var destProps = GetCachedWritableProperties(destType);
            var assignments = new List<Expression>(destProps.Length);

            HashSet<string>? excluded = null;
            if (excludedMembers is { Count: > 0 })
            {
                excluded = new HashSet<string>(excludedMembers, StringComparer.OrdinalIgnoreCase);
            }

            for (int i = 0; i < destProps.Length; i++)
            {
                var destProp = destProps[i];
                if (excluded != null && excluded.Contains(destProp.Name)) continue;
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

            // A setterless collection cannot be assigned, so the in-place map fills it the same way
            // the constructing maps do, or mapping onto an existing object would silently skip it.
            foreach (var (dest, source) in FindFieldMembers(sourceType, destType))
            {
                if (excluded != null && excluded.Contains(dest.Name)) continue;

                assignments.Add(Expression.Assign(
                    Expression.MakeMemberAccess(typedDest, dest),
                    Expression.MakeMemberAccess(typedSource, source)));
            }

            foreach (var (destProp, sourceProp, sourceItem, destItem) in FindFillableCollections(sourceType, destType))
            {
                if (excluded != null && excluded.Contains(destProp.Name)) continue;

                var copy = typeof(PropertyConversion)
                    .GetMethod(nameof(PropertyConversion.CopyInto), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)!
                    .MakeGenericMethod(sourceItem, destItem);

                // TypeAs for the same reason as the other call site: the declared type is all the
                // eligibility test can see, and a hard cast on a value that is not an ICollection<T>
                // throws from inside the compiled delegate.
                assignments.Add(Expression.Call(
                    copy,
                    Expression.Property(typedSource, sourceProp),
                    Expression.TypeAs(Expression.Property(typedDest, destProp), typeof(ICollection<>).MakeGenericType(destItem))));
            }

            if (assignments.Count == 0)
            {
                return (s, d) => { };
            }

            var block = Expression.Block(assignments);
            return Expression.Lambda<Action<object, object>>(block, sourceParam, destParam).Compile();
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

            // Reference arrays and List<T> are the overwhelmingly common shapes, and enumerating
            // either through the non-generic IEnumerable boxes its struct enumerator and pays two
            // interface calls per item. That was measurable against AutoMapper, which compiles a
            // typed loop. Both fast paths below run the exact same MapItems loop, only the cursor
            // feeding it changes. The gates are deliberately narrow: an arbitrary IList may index
            // in O(n), a multi-dimension or non-zero-based array throws from the IList indexer,
            // and both still enumerate fine on the general path.
            if (source is object[] array)
            {
                var result = new List<T>(array.Length);
                var cursor = new ArrayCursor(array);
                MapItems(ref cursor, result);
                return result;
            }

            if (source is System.Collections.IList list && IsGenericList(source.GetType()))
            {
                var compiled = GetCompiledListLoop<T>(source.GetType());
                if (compiled?.Loop is Func<object, List<T>> loop)
                {
                    if (!compiled.NeedsDepth)
                    {
                        return loop(source);
                    }

                    // Mapping a collection is one level of nesting however many items it holds, so
                    // depth is taken once here rather than per element. When it cannot be taken the
                    // existing loop below runs instead, because it already produces the right thing
                    // at the ceiling and that edge is not worth a second implementation.
                    if (IncrementDepth())
                    {
                        try
                        {
                            return loop(source);
                        }
                        finally
                        {
                            DecrementDepth();
                        }
                    }
                }

                var result = new List<T>(list.Count);
                var cursor = new ListCursor(list);
                MapItems(ref cursor, result);
                return result;
            }

            return MapEnumerated<T>(source);
        }

        /// <summary>
        /// A compiled list loop and whether its element type needs the collection to hold depth.
        /// </summary>
        private sealed class ListLoop
        {
            internal readonly Delegate? Loop;
            internal readonly bool NeedsDepth;

            internal ListLoop(Delegate? loop, bool needsDepth)
            {
                Loop = loop;
                NeedsDepth = needsDepth;
            }
        }

        private static readonly ConcurrentDictionary<(Type, Type), ListLoop> _listLoopCache = new();

        /// <summary>
        /// A loop over a <see cref="List{T}"/> with the element mapping compiled into it, or null
        /// when this pair is not a shape that can be inlined.
        /// </summary>
        /// <remarks>
        /// Mapping a hundred element list spent about a fifth of its time on the loop rather than
        /// on the mapping. Seven loop shapes were measured in one process on both architectures
        /// before this one was written: indexing the typed list instead of the non-generic IList
        /// won nothing, writing through CollectionsMarshal instead of Add won nothing, and the
        /// library's own typed collection API was slower than the untyped one. What won was
        /// compiling the loop, on x64 by 20 percent and on arm64 by 21.
        ///
        /// The per element runtime type check stays. A list declared List&lt;Animal&gt; may hold a
        /// Dog and then a Cat, and applying Dog's mapping to the Cat throws from inside a compiled
        /// lambda. An element whose type does not match goes back through the single object entry
        /// point, which is also what happens for a shape this cannot inline at all.
        /// </remarks>
        private static ListLoop? GetCompiledListLoop<TDest>(Type listType)
        {
            // The bound cache exists to limit retained delegates, and a second unbounded cache
            // beside it would defeat that.
            if (_useLruCache) return null;

            // Keyed on the concrete List<T> rather than its element, because reaching the element
            // means GetGenericArguments, which allocates a Type[] every call. That is 16 bytes per
            // map on a path whose whole budget is the destinations and the list.
            return _listLoopCache.GetOrAdd(
                (listType, typeof(TDest)), key => BuildCompiledListLoop<TDest>(key.Item1));
        }

        private static ListLoop BuildCompiledListLoop<TDest>(Type listType)
        {
            var elementType = listType.GetGenericArguments()[0];

            // A generated mapper is a delegate, not an expression, so there is nothing to inline and
            // inlining what the builder would have produced would ignore it. Standing aside entirely
            // was the first answer and it cost more than it saved: the fallback loop is generic over
            // its cursor and checks the runtime type per element, which measured a hundred element
            // collection at 5,338 ns against 4,311 for the same shape ungenerated. The loop is still
            // built, and its body calls the generated delegate instead of inlining an expression.
            var generated = GeneratedElementMapper<TDest>(elementType);

            // Depth is taken once for the whole collection rather than per element, which is what
            // the existing loop does and why this only needs to know whether to take it. Refusing
            // these outright was the first attempt and it excluded almost everything worth
            // optimising: any element type holding a nested reference answers yes, which is most
            // DTOs, including the one the performance claim is measured on.
            var needsDepth = NeedsDepthTracking(elementType);

            if (!elementType.IsVisible || elementType.IsValueType) return new ListLoop(null, needsDepth);

            // A declared element type nothing can actually be leaves every element failing the
            // runtime type check and taking the fallback, one entry point call each. List<object>
            // measured 10.9x slower that way than List<T>, and List<object> is the shape this
            // library exists for: items whose types are only known at runtime. The existing loop
            // resolves against the first element's runtime type instead, which is right for these.
            if (elementType == typeof(object) || elementType.IsAbstract || elementType.IsInterface)
            {
                return new ListLoop(null, needsDepth);
            }

            var destType = typeof(TDest);

            // The single object builder tries a direct conversion, then a collection destination,
            // then a dictionary, then a constructor taking IEnumerable, and only then a member
            // initialiser. Inlining the member initialiser without those means claiming pairs it
            // would never have reached. List<List<Src>> to List<Dst> is the one that showed it:
            // the destination element is itself a list, and a member initialiser for a list maps
            // its properties rather than its contents, so every inner list came back empty and
            // nothing was raised.
            if (generated is null
                && (destType.IsValueType
                    || destType == typeof(string)
                    || typeof(System.Collections.IEnumerable).IsAssignableFrom(destType)
                    || destType.GetConstructor(Type.EmptyTypes) is null))
            {
                return new ListLoop(null, needsDepth);
            }

            // A destination the element can simply be assigned to is handed over as-is by the
            // conversion cascade, not rebuilt member by member. Constructing a new one here would
            // return a copy where the library returns the same reference.
            if (generated is null && destType.IsAssignableFrom(elementType))
            {
                return new ListLoop(null, needsDepth);
            }

            var sourceParam = Expression.Parameter(typeof(object), "source");
            var list = Expression.Variable(listType, "list");
            var result = Expression.Variable(typeof(List<TDest>), "result");
            var index = Expression.Variable(typeof(int), "i");
            var count = Expression.Variable(typeof(int), "count");
            var item = Expression.Variable(elementType, "item");
            var done = Expression.Label("done");

            Expression mapped;
            if (generated != null)
            {
                mapped = Expression.Invoke(
                    Expression.Constant(generated, typeof(Func<object, TDest>)),
                    Expression.Convert(item, typeof(object)));
            }
            else
            {
                var memberInit = TryBuildMemberInit(elementType, destType, item, Expression.Convert(item, typeof(object)), true);
                if (memberInit is null) return new ListLoop(null, needsDepth);
                if (!destType.IsAssignableFrom(memberInit.Type)) return new ListLoop(null, needsDepth);

                mapped = Expression.Convert(memberInit, typeof(TDest));
            }

            var fallback = Expression.Call(
                typeof(Mapper).GetMethod(nameof(MapTo), new[] { typeof(object) })!.MakeGenericMethod(destType),
                Expression.Convert(item, typeof(object)));

            // A sealed element type has no derived types, so every non-null element is exactly it
            // and the check can only ever be true.
            Expression matched = elementType.IsSealed
                ? mapped
                : Expression.Condition(
                    Expression.Equal(
                        Expression.Call(item, typeof(object).GetMethod(nameof(GetType))!),
                        Expression.Constant(elementType, typeof(Type))),
                    mapped,
                    fallback);

            var perItem = Expression.Condition(
                Expression.ReferenceEqual(item, Expression.Constant(null, elementType)),
                Expression.Default(typeof(TDest)),
                matched);

            var body = Expression.Block(
                new[] { list, result, index, count },
                Expression.Assign(list, Expression.Convert(sourceParam, listType)),
                Expression.Assign(count, Expression.Property(list, listType.GetProperty("Count")!)),
                Expression.Assign(result, Expression.New(
                    typeof(List<TDest>).GetConstructor(new[] { typeof(int) })!, count)),
                Expression.Assign(index, Expression.Constant(0)),
                Expression.Loop(
                    Expression.IfThenElse(
                        Expression.LessThan(index, count),
                        Expression.Block(
                            new[] { item },
                            Expression.Assign(item,
                                Expression.MakeIndex(list, listType.GetProperty("Item"), new Expression[] { index })),
                            Expression.Call(result, typeof(List<TDest>).GetMethod(nameof(List<TDest>.Add))!, perItem),
                            Expression.PostIncrementAssign(index)),
                        Expression.Break(done)),
                    done),
                result);

            return new ListLoop(
                Expression.Lambda<Func<object, List<TDest>>>(body, sourceParam).Compile(), needsDepth);
        }

        /// <summary>
        /// Compiled list loops that were actually built, for <see cref="CacheInfo"/>.
        /// </summary>
        /// <remarks>
        /// Null entries are the pairs this cannot inline, remembered so the decision is not retaken
        /// on every map. They are not delegates and are not counted as such.
        /// </remarks>
        private static int CompiledListLoopCount()
        {
            var built = 0;
            foreach (var entry in _listLoopCache)
            {
                if (entry.Value.Loop != null) built++;
            }
            return built;
        }

        private static bool IsGenericList(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

        private static List<T> MapEnumerated<T>(System.Collections.IEnumerable source)
        {
            List<T> result;
            if (source is System.Collections.ICollection collection)
            {
                result = new List<T>(collection.Count);
            }
            else
            {
                result = new List<T>();
            }

            var enumerator = source.GetEnumerator();
            try
            {
                var cursor = new EnumeratorCursor(enumerator);
                MapItems(ref cursor, result);
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
            return result;
        }

        private interface IItemCursor
        {
            bool MoveNext();
            object? Current { get; }
        }

        private struct ListCursor : IItemCursor
        {
            private readonly System.Collections.IList _list;
            private readonly int _count;
            private int _index;

            public ListCursor(System.Collections.IList list)
            {
                _list = list;
                _count = list.Count;
                _index = -1;
            }

            public bool MoveNext() => ++_index < _count;
            public object? Current => _list[_index];
        }

        private struct ArrayCursor : IItemCursor
        {
            private readonly object?[] _items;
            private int _index;

            public ArrayCursor(object?[] items)
            {
                _items = items;
                _index = -1;
            }

            public bool MoveNext() => ++_index < _items.Length;
            public object? Current => _items[_index];
        }

        private struct EnumeratorCursor : IItemCursor
        {
            private readonly System.Collections.IEnumerator _enumerator;

            public EnumeratorCursor(System.Collections.IEnumerator enumerator) => _enumerator = enumerator;

            public bool MoveNext() => _enumerator.MoveNext();
            public object? Current => _enumerator.Current;
        }

        private static void MapItems<T, TCursor>(ref TCursor cursor, List<T> result)
            where TCursor : struct, IItemCursor
        {
            Type? itemType = null;
            Func<object, T>? cachedMapper = null;
            bool depthHeld = false;

            try
            {
                // Resolution phase: walk items until the item delegate is cached and the depth
                // decision is made, then drop into the warm loop below, whose body carries only
                // the checks that can still change the answer. The two flags this phase settles
                // used to be re-tested on every item of the warm loop. Under the LRU
                // configuration every item stays on this path, as it always has.
                while (cursor.MoveNext())
                {
                    var item = cursor.Current;
                    if (item is null)
                    {
                        result.Add(default!);
                        continue;
                    }

                    itemType = item.GetType();
                    var key = (itemType, typeof(T));
                    bool alreadyMapped = false;

                    // OPTIMIZATION: Direct cache access
                    if (!_useLruCache && _mapToCache.TryGetValue(key, out var cached))
                    {
                        cachedMapper = (Func<object, T>)cached;
                    }
                    else
                    {
                        // Fall back to single-item MapTo which will cache the delegate. This
                        // adds the item, so it must not be mapped again by the loop body below.
                        result.Add(item.MapTo<T>()!);
                        alreadyMapped = true;

                        // Now get the cached delegate for subsequent items
                        if (!_useLruCache && _mapToCache.TryGetValue(key, out cached))
                        {
                            cachedMapper = (Func<object, T>)cached;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    // Depth is taken once for the whole collection, not once per element.
                    // Mapping a collection is one level of nesting however many items it holds,
                    // and every item starts from the same depth either way, so the cycle
                    // ceiling is unchanged. Per item this used to cost an IncrementDepth, a
                    // try/finally and a DecrementDepth, on a loop body of about twenty
                    // nanoseconds.
                    if (NeedsDepthTracking(itemType))
                    {
                        if (!IncrementDepth())
                        {
                            // The ceiling was already reached, so nothing deeper can map: the
                            // rest of the collection degrades to defaults, matching what the
                            // per-item flag did before this loop was split.
                            while (cursor.MoveNext())
                            {
                                result.Add(default!);
                            }
                            return;
                        }
                        depthHeld = true;
                    }

                    if (!alreadyMapped)
                    {
                        result.Add(cachedMapper(item)!);
                    }
                    break;
                }

                // Empty, all nulls, or the LRU path mapped everything above.
                if (cachedMapper is null)
                {
                    return;
                }

                while (cursor.MoveNext())
                {
                    var item = cursor.Current;
                    if (item is null)
                    {
                        result.Add(default!);
                    }
                    // The cached delegate is compiled for exactly one runtime type, and its first
                    // instruction is a cast to that type. A collection declared List<Animal> may hold a
                    // Dog and then a Cat, in which case applying Dog's delegate to the Cat threw
                    // InvalidCastException from inside the compiled lambda. Fall back to the per-item
                    // path for the odd item out; the homogeneous case, which is nearly all of them,
                    // still pays only one reference comparison.
                    else if (item.GetType() != itemType)
                    {
                        result.Add(item.MapTo<T>()!);
                    }
                    else
                    {
                        result.Add(cachedMapper(item)!);
                    }
                }
            }
            finally
            {
                if (depthHeld)
                {
                    DecrementDepth();
                }
            }
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
                        if (TryConvertDictionaryValue(value, prop.PropertyType, out var converted))
                        {
                            prop.SetValue(dest, converted);
                        }
                        else
                        {
                            Logger?.Invoke(
                                $"[Mapsicle] Dropped '{prop.Name}': a {value.GetType().Name} is not a {prop.PropertyType.Name}. " +
                                "Set Mapper.CoerceDictionaryValues to parse values of the wrong type.");
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

        /// <summary>
        /// The dictionary entry point's conversion decision, matching what the compiled paths allow.
        /// </summary>
        /// <remarks>
        /// The compiled paths decide from declared types and emit an expression. Here the value is
        /// already boxed and only its runtime type is known, so the mechanism has to differ. Which
        /// pairs are permitted must not: the widening table is asked for rather than restated, since
        /// a second copy of that table is exactly the drift PropertyConversion exists to prevent.
        /// </remarks>
        private static bool TryConvertDictionaryValue(object value, Type targetType, out object? converted)
        {
            converted = null;
            var valueType = value.GetType();
            var targetUnderlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (targetType.IsAssignableFrom(valueType) || targetUnderlying.IsAssignableFrom(valueType))
            {
                converted = value;
                return true;
            }

            if (targetType == typeof(string))
            {
                converted = value is IFormattable formattable
                    ? formattable.ToString(null, CultureInfo.InvariantCulture)
                    : value.ToString();
                return true;
            }

            if (valueType.IsEnum && (targetUnderlying == typeof(int) || targetUnderlying == typeof(long)))
            {
                converted = Convert.ChangeType(value, targetUnderlying, CultureInfo.InvariantCulture);
                return true;
            }

            if (targetUnderlying.IsEnum && valueType == typeof(int))
            {
                converted = Enum.ToObject(targetUnderlying, value);
                return true;
            }

            if (PropertyConversion.IsLosslessNumericWidening(valueType, targetUnderlying))
            {
                converted = Convert.ChangeType(value, targetUnderlying, CultureInfo.InvariantCulture);
                return true;
            }

            if (CoerceDictionaryValues
                && value is IConvertible
                && typeof(IConvertible).IsAssignableFrom(targetUnderlying))
            {
                converted = Convert.ChangeType(value, targetUnderlying, CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }


        /// <summary>
        /// Builds a dictionary destination by mapping each key and value separately.
        /// </summary>
        /// <remarks>
        /// A dictionary enumerates as <c>KeyValuePair&lt;TKey, TValue&gt;</c>, which is a struct
        /// whose properties are read only, so mapping the pair as if it were an object produces a
        /// default pair with a null key. Feeding those to <c>Dictionary(IEnumerable&lt;...&gt;)</c>
        /// throws <c>ArgumentNullException</c> on the first one, which would turn a mapping the
        /// caller got no result from into an exception at map time. Keys and values are mapped
        /// individually instead.
        ///
        /// A key that maps to null is skipped rather than throwing, matching what the rest of the
        /// mapper does with a value it cannot produce.
        /// </remarks>
        /// <summary>
        /// Reports a collection destination that could not be constructed, and yields its default.
        /// </summary>
        private static TCollection LogCollectionFallback<TCollection>(Exception ex, Type destType)
        {
            Logger?.Invoke(
                $"[Mapsicle] Could not build {destType.Name} from the mapped items: {ex.Message}. " +
                "The destination was left at its default.");
            return default!;
        }

        internal static Dictionary<TKey, TValue> BuildMappedDictionary<TKey, TValue>(object? source)
            where TKey : notnull
        {
            var result = new Dictionary<TKey, TValue>();

            if (source is not System.Collections.IDictionary dictionary)
            {
                return result;
            }

            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                var key = entry.Key.MapTo<TKey>();
                if (key is null) continue;

                result[key] = entry.Value is null ? default! : entry.Value.MapTo<TValue>()!;
            }

            return result;
        }

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

        /// <summary>
        /// Builds the member initialisation for one pair, or null when the destination cannot be
        /// constructed without arguments.
        /// </summary>
        /// <remarks>
        /// Returned as an expression rather than a delegate so it can be used two ways: compiled on
        /// its own, which is what mapping a single object does, and inlined into a loop, which is
        /// what mapping a list does. Those two used to be the same code only by coincidence. The
        /// collection loop calling a delegate per element cost about 20 percent, and the alternative
        /// to this method was a second copy of the binding logic reachable only from the loop, which
        /// is the mistake CONTRIBUTING describes: three copies of the conversion cascade drifted and
        /// 1.2.3 shipped a mapper that dropped nested objects only when built by MapperFactory.
        /// </remarks>
        /// <summary>
        /// Assignments involving a public field on either side, which the property paths never see.
        /// </summary>
        /// <remarks>
        /// The resolution machinery is built on <c>PropertyInfo</c> throughout, so a field was
        /// invisible: a type exposing public fields mapped to nothing at all, with no diagnostic.
        /// AutoMapper and Mapperly both map them, and a caller mapping a struct or an interop type
        /// hits this immediately.
        ///
        /// Done as a separate pass rather than by widening every cache and signature to a member
        /// abstraction. That change would touch the conversion cascade, member resolution and all
        /// four delegate builders at once, for a case that is a minority of real DTOs. This pass
        /// only claims pairs the property paths did not, so it cannot disturb what already worked.
        /// </remarks>
        private static List<(MemberInfo Dest, MemberInfo Source)> FindFieldMembers(Type sourceType, Type destType)
        {
            const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;

            var sourceMembers = sourceType.GetFields(Public).Cast<MemberInfo>()
                .Concat(sourceType.GetProperties(Public).Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
                .ToList();

            var found = new List<(MemberInfo, MemberInfo)>();

            foreach (var destField in destType.GetFields(Public).Where(f => !f.IsInitOnly && !f.IsLiteral))
            {
                var match = sourceMembers.FirstOrDefault(
                    m => string.Equals(m.Name, destField.Name, StringComparison.OrdinalIgnoreCase));

                if (match != null && TypeOf(match) == destField.FieldType) found.Add((destField, match));
            }

            // A destination property whose only source is a field. The property paths matched on
            // source properties alone, so these were dropped.
            foreach (var destProp in destType.GetProperties(Public).Where(p => p.CanWrite && p.GetIndexParameters().Length == 0))
            {
                if (sourceType.GetProperties(Public).Any(
                        p => string.Equals(p.Name, destProp.Name, StringComparison.OrdinalIgnoreCase))) continue;

                var field = sourceType.GetFields(Public).FirstOrDefault(
                    f => string.Equals(f.Name, destProp.Name, StringComparison.OrdinalIgnoreCase));

                if (field != null && field.FieldType == destProp.PropertyType) found.Add((destProp, field));
            }

            return found;
        }

        private static Type TypeOf(MemberInfo member) =>
            member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;

        /// <summary>
        /// Destination collections with no setter, which a member initialiser cannot bind.
        /// </summary>
        private static List<(PropertyInfo Dest, PropertyInfo Source, Type SourceItem, Type DestItem)> FindFillableCollections(
            Type sourceType, Type destType)
        {
            var found = new List<(PropertyInfo, PropertyInfo, Type, Type)>();
            var readable = GetCachedReadableProperties(sourceType);

            foreach (var destProp in destType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (destProp.CanWrite || destProp.GetIndexParameters().Length > 0) continue;

                var destItem = ElementTypeOf(destProp.PropertyType);
                if (destItem is null) continue;
                if (!typeof(System.Collections.ICollection).IsAssignableFrom(destProp.PropertyType)
                    && !destProp.PropertyType.IsGenericType) continue;

                foreach (var sourceProp in readable)
                {
                    if (!string.Equals(sourceProp.Name, destProp.Name, StringComparison.OrdinalIgnoreCase)) continue;

                    var sourceItem = ElementTypeOf(sourceProp.PropertyType);
                    if (sourceItem is null) continue;

                    found.Add((destProp, sourceProp, sourceItem, destItem));
                    break;
                }
            }

            return found;
        }

        private static Type? ElementTypeOf(Type type)
        {
            if (type == typeof(string)) return null;
            if (type.IsArray) return type.GetElementType();

            foreach (var i in type.GetInterfaces().Concat(new[] { type }))
            {
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return i.GetGenericArguments()[0];
                }
            }

            return null;
        }

        /// <summary>
        /// Wraps a member initialiser so setterless collections are filled after construction.
        /// </summary>
        /// <remarks>
        /// A member initialiser can only bind members it can assign, so a collection with no setter
        /// was skipped entirely and came back empty. Filling it needs statements after the object
        /// exists, which is a block rather than an initialiser, so the initialiser is assigned to a
        /// variable and the copies follow it.
        /// </remarks>
        internal static Expression WithFilledCollections(
            Expression memberInit, Type sourceType, Type destType, Expression typedSource)
        {
            var fillable = FindFillableCollections(sourceType, destType);
            var fieldMembers = FindFieldMembers(sourceType, destType);
            if (fillable.Count == 0 && fieldMembers.Count == 0) return memberInit;

            var result = Expression.Variable(destType, "result");
            var body = new List<Expression> { Expression.Assign(result, memberInit) };

            foreach (var (dest, source) in fieldMembers)
            {
                body.Add(Expression.Assign(
                    Expression.MakeMemberAccess(result, dest),
                    Expression.MakeMemberAccess(typedSource, source)));
            }

            foreach (var (destProp, sourceProp, sourceItem, destItem) in fillable)
            {
                var copy = typeof(PropertyConversion)
                    .GetMethod(nameof(PropertyConversion.CopyInto), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)!
                    .MakeGenericMethod(sourceItem, destItem);

                // TypeAs, not Convert. The eligibility test can only see the declared type, and a
                // getter-only member declared as IEnumerable<T> can return anything at run time: an
                // iterator from a computed getter is not an ICollection<T>, and the hard cast threw
                // InvalidCastException from inside the compiled delegate. Section 6 of CLAUDE.md is
                // explicit that a value of the wrong shape is dropped rather than thrown, and
                // CopyInto already treats a null destination as nothing to do.
                body.Add(Expression.Call(
                    copy,
                    Expression.Property(typedSource, sourceProp),
                    Expression.TypeAs(Expression.Property(result, destProp), typeof(ICollection<>).MakeGenericType(destItem))));
            }

            body.Add(result);
            return Expression.Block(new[] { result }, body);
        }

        private static MemberInitExpression? TryBuildMemberInit(
            Type sourceType, Type destType, Expression typedSource, Expression sourceAsObject, bool isSourceVisible)
        {
            if (destType.GetConstructor(Type.EmptyTypes) is null && !destType.IsValueType) return null;

            var sourceProps = GetCachedReadableProperties(sourceType);
            var destProps = GetCachedWritableProperties(destType);
            var bindings = new List<MemberBinding>(destProps.Length);

            for (int i = 0; i < destProps.Length; i++)
            {
                var destProp = destProps[i];
                if (!MemberResolution.TryResolveSource(destProp, sourceProps, out var sourceProp)) continue;

                if (sourceProp != null)
                {
                    var binding = CreatePropertyBinding(destProp, sourceProp, typedSource, sourceAsObject, isSourceVisible);
                    if (binding != null) bindings.Add(binding);
                }
                else
                {
                    var flattenedBinding = TryBindFlattenedPath(destProp, sourceProps, typedSource);
                    if (flattenedBinding != null) bindings.Add(flattenedBinding);
                }
            }

            return Expression.MemberInit(Expression.New(destType), bindings);
        }

        private static MemberBinding? CreatePropertyBinding(PropertyInfo destProp, PropertyInfo sourceProp,
            Expression typedSource, Expression sourceAsObject, bool isSourceVisible)
        {
            Expression propExp;
            if (isSourceVisible && sourceProp.GetGetMethod()?.IsPublic == true)
            {
                propExp = Expression.Property(typedSource, sourceProp);
            }
            else
            {
                var getValue = typeof(PropertyInfo).GetMethod("GetValue", new[] { typeof(object), typeof(object[]) })!;
                var call = Expression.Call(Expression.Constant(sourceProp), getValue, sourceAsObject, Expression.Constant(null, typeof(object[])));
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
            // One holder per nested member per compiled parent, captured as a constant in the
            // expression tree. It outlives nothing the parent delegate does not.
            var holderType = typeof(NestedMemberMapper<>).MakeGenericType(targetType);
            var holder = Activator.CreateInstance(holderType)!;

            return Expression.Call(
                Expression.Constant(holder, holderType),
                holderType.GetMethod(nameof(NestedMemberMapper<object>.Map))!,
                Expression.Convert(propExp, typeof(object)));
        }

        /// <summary>
        /// Maps one nested member of one compiled parent, remembering what it resolved last time.
        /// </summary>
        /// <remarks>
        /// The nested call used to go back through the public <c>MapTo&lt;T&gt;(object)</c> entry
        /// point, which is correct and was measurably the most expensive thing in a map. Per nested
        /// member per item it paid a <c>GetType</c>, a tuple key construction, a dictionary lookup
        /// for the delegate and a second one inside <c>NeedsDepthTracking</c>. Measured on an idle
        /// machine at 100 items, one nested reference cost 65 ns per item against 39.6 ns for the
        /// whole of the rest of the mapping, and a second nested reference cost the same again.
        ///
        /// Resolution is per call site rather than global because the answer is almost always the
        /// same one: a given member of a given parent nearly always holds the same runtime type.
        /// The runtime type is still checked on every call, so a member declared as a base type and
        /// holding a derived one is handled exactly as before. It just stops paying for two hash
        /// lookups to learn what it already knew.
        ///
        /// The whole thing is skipped under <see cref="UseLruCache"/>. That mode exists to bound
        /// memory, and a per-call-site cache nothing evicts is the opposite of that.
        /// </remarks>
        internal sealed class NestedMemberMapper<TDest>
        {
            private sealed class Resolved
            {
                internal readonly Type SourceType;
                internal readonly Func<object, TDest> Map;
                internal readonly bool NeedsDepth;
                internal readonly int Generation;

                internal Resolved(Type sourceType, Func<object, TDest> map, bool needsDepth, int generation)
                {
                    SourceType = sourceType;
                    Map = map;
                    NeedsDepth = needsDepth;
                    Generation = generation;
                }
            }

            // A single reference, written and read atomically, so a torn pair is not possible. Two
            // threads racing to resolve compute the same answer, so the loser overwriting the
            // winner costs nothing.
            private volatile Resolved? _resolved;

            public TDest? Map(object? source)
            {
                if (source is null) return default;

                var resolved = _resolved;
                var sourceType = source.GetType();

                if (resolved is null
                    || !ReferenceEquals(resolved.SourceType, sourceType)
                    || resolved.Generation != System.Threading.Volatile.Read(ref _cacheGeneration))
                {
                    return ResolveAndMap(source, sourceType);
                }

                if (!resolved.NeedsDepth)
                {
                    return resolved.Map(source);
                }

                if (!IncrementDepth(source)) return default;
                try
                {
                    return resolved.Map(source);
                }
                finally
                {
                    DecrementDepth(source);
                }
            }

            private TDest? ResolveAndMap(object source, Type sourceType)
            {
                // The public path compiles and caches the delegate, and tracks depth while doing
                // it, so the first call through a holder is exactly what it always was.
                var result = source.MapTo<TDest>();

                if (!_useLruCache && _mapToCache.TryGetValue((sourceType, typeof(TDest)), out var cached))
                {
                    _resolved = new Resolved(
                        sourceType,
                        (Func<object, TDest>)cached,
                        NeedsDepthTracking(sourceType),
                        System.Threading.Volatile.Read(ref _cacheGeneration));
                }

                return result;
            }
        }

        /// <summary>
        /// Builds a null safe read along a flattened path, or null when the path does not resolve.
        /// </summary>
        /// <remarks>
        /// One implementation for all three delegate builders. There were three, differing only in
        /// how they reached the source, which is exactly the drift CONTRIBUTING describes: the typed
        /// door, the untyped door and the instance mapper each had their own flattening, so a fix
        /// applied to one left the other two behind.
        ///
        /// A null anywhere along the chain yields the destination default rather than throwing, so
        /// Customer.Address.City reads as null when Customer is null, when Address is null, and when
        /// neither is.
        /// </remarks>
        internal static MemberBinding? TryBindFlattenedPath(
            PropertyInfo destProp, PropertyInfo[] sourceProps, Expression source)
        {
            if (!PropertyConversion.TryFindFlattenedPath(
                    destProp, sourceProps, GetCachedReadableProperties, out var path))
            {
                return null;
            }

            Expression access = source;
            Expression? guard = null;

            // Every step but the last can be null, and each one needs testing before the next is
            // read. Built inside out so the guards nest in the order they must be evaluated.
            for (var i = 0; i < path.Count; i++)
            {
                access = Expression.Property(access, path[i]);

                if (i == path.Count - 1) break;
                if (!access.Type.IsValueType || Nullable.GetUnderlyingType(access.Type) != null)
                {
                    var isNull = Expression.Equal(access, Expression.Constant(null, access.Type));
                    guard = guard is null ? isNull : Expression.OrElse(guard, isNull);
                }
            }

            Expression value;
            try
            {
                value = access.Type == destProp.PropertyType
                    ? access
                    : Expression.Convert(access, destProp.PropertyType);
            }
            catch (InvalidOperationException)
            {
                // The names lined up and the types do not. Leaving the member unmapped is what the
                // engine does everywhere else for a pair it cannot convert.
                return null;
            }

            if (guard != null)
            {
                value = Expression.Condition(guard, Expression.Default(destProp.PropertyType), value);
            }

            return Expression.Bind(destProp, value);
        }

        /// <summary>
        /// Attempts to create a binding for flattened properties (e.g., AddressCity -> Address.City).
        /// </summary>
        private static MemberBinding? TryCreateFlattenedBinding(PropertyInfo destProp, PropertyInfo[] sourceProps,
            Expression typedSource, Expression sourceAsObject, bool isSourceVisible)
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
