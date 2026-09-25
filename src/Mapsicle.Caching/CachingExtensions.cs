using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mapsicle.Fluent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace Mapsicle.Caching
{
    /// <summary>
    /// Extension methods for caching mapped results.
    /// </summary>
    public static class CachingExtensions
    {
        #region Memory Cache Extensions

        /// <summary>
        /// Maps the source object to the destination type, caching the result.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="cache">The memory cache.</param>
        /// <param name="cacheKey">The cache key.</param>
        /// <param name="options">Optional cache entry options.</param>
        /// <returns>The mapped and cached destination object.</returns>
        public static TDest? MapToCached<TDest>(
            this object? source,
            IMemoryCache cache,
            string cacheKey,
            MemoryCacheEntryOptions? options = null)
        {
            if (source is null) return default;

            return cache.GetOrCreate(cacheKey, entry =>
            {
                entry.SetOptions(options ?? new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(5),
                    Size = 1
                });
                return source.MapTo<TDest>();
            });
        }

        /// <summary>
        /// Maps the source object using IMapper, caching the result.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <param name="cache">The memory cache.</param>
        /// <param name="cacheKey">The cache key.</param>
        /// <param name="options">Optional cache entry options.</param>
        /// <returns>The mapped and cached destination object.</returns>
        public static TDest? MapToCached<TDest>(
            this IMapper mapper,
            object? source,
            IMemoryCache cache,
            string cacheKey,
            MemoryCacheEntryOptions? options = null)
        {
            if (source is null) return default;

            return cache.GetOrCreate(cacheKey, entry =>
            {
                entry.SetOptions(options ?? new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(5),
                    Size = 1
                });
                return mapper.Map<TDest>(source);
            });
        }

        /// <summary>
        /// Maps the source object, using an auto-generated cache key based on source content.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="cache">The memory cache.</param>
        /// <param name="expiration">Optional expiration time.</param>
        /// <returns>The mapped and cached destination object.</returns>
        public static TDest? MapToCachedAuto<TDest>(
            this object? source,
            IMemoryCache cache,
            TimeSpan? expiration = null)
        {
            if (source is null) return default;

            var fingerprint = Fingerprint.Create(source, typeof(TDest), StaticLane);
            if (fingerprint is null) return source.MapTo<TDest>();

            var cacheKey = KeyFor(source, typeof(TDest), fingerprint);
            if (TryGetVerified(cache, cacheKey, fingerprint, out TDest? hit)) return hit;

            var mapped = source.MapTo<TDest>();
            cache.Set(cacheKey, new VerifiedEntry(fingerprint, mapped), new MemoryCacheEntryOptions
            {
                SlidingExpiration = expiration ?? TimeSpan.FromMinutes(5),
                Size = 1
            });
            return mapped;
        }

        internal const string StaticLane = "static";

        internal static string KeyFor(object source, Type destType, string fingerprint) =>
            $"mapsicle:{source.GetType().Name}:{destType.Name}:{Fingerprint.Hash(fingerprint)}";

        /// <summary>
        /// A hit counts only when the stored fingerprint equals this source's, so a hash
        /// collision, or an entry someone else put under the same key, maps afresh instead.
        /// </summary>
        internal static bool TryGetVerified<TDest>(IMemoryCache cache, string cacheKey, string fingerprint, out TDest? value)
        {
            if (cache.TryGetValue(cacheKey, out var entry)
                && entry is VerifiedEntry verified
                && string.Equals(verified.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                value = verified.Value is TDest typed ? typed : default;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Maps a collection with caching for each item.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source collection.</param>
        /// <param name="cache">The memory cache.</param>
        /// <param name="keySelector">Function to generate cache key for each item.</param>
        /// <param name="options">Optional cache entry options.</param>
        /// <returns>List of mapped and cached destination objects.</returns>
        public static List<TDest> MapCollectionToCached<TDest>(
            this IEnumerable<object>? source,
            IMemoryCache cache,
            Func<object, string> keySelector,
            MemoryCacheEntryOptions? options = null)
        {
            if (source is null) return new List<TDest>();

            var result = new List<TDest>();
            foreach (var item in source)
            {
                var key = keySelector(item);
                var mapped = item.MapToCached<TDest>(cache, key, options);
                if (mapped is not null)
                {
                    result.Add(mapped);
                }
            }
            return result;
        }

        #endregion

        #region Distributed Cache Extensions

        /// <summary>
        /// Maps the source object to the destination type, caching in distributed cache.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="cache">The distributed cache.</param>
        /// <param name="cacheKey">The cache key.</param>
        /// <param name="options">Optional distributed cache entry options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The mapped and cached destination object.</returns>
        public static async Task<TDest?> MapToCachedAsync<TDest>(
            this object? source,
            IDistributedCache cache,
            string cacheKey,
            DistributedCacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (source is null) return default;

            var cachedBytes = await cache.GetAsync(cacheKey, cancellationToken);
            if (DistributedPayload.TryRead(cachedBytes, out TDest? hit)) return hit;

            var mapped = source.MapTo<TDest>();
            if (mapped is null) return default;

            var bytes = DistributedPayload.TryWrite(mapped);
            if (bytes is null) return mapped;

            await cache.SetAsync(cacheKey, bytes, options ?? new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(5)
            }, cancellationToken);

            return mapped;
        }

        /// <summary>
        /// Maps the source object using IMapper, caching in distributed cache.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <param name="cache">The distributed cache.</param>
        /// <param name="cacheKey">The cache key.</param>
        /// <param name="options">Optional distributed cache entry options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The mapped and cached destination object.</returns>
        public static async Task<TDest?> MapToCachedAsync<TDest>(
            this IMapper mapper,
            object? source,
            IDistributedCache cache,
            string cacheKey,
            DistributedCacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (source is null) return default;

            var cachedBytes = await cache.GetAsync(cacheKey, cancellationToken);
            if (DistributedPayload.TryRead(cachedBytes, out TDest? hit)) return hit;

            var mapped = mapper.Map<TDest>(source);
            if (mapped is null) return default;

            var bytes = DistributedPayload.TryWrite(mapped);
            if (bytes is null) return mapped;

            await cache.SetAsync(cacheKey, bytes, options ?? new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(5)
            }, cancellationToken);

            return mapped;
        }

        /// <summary>
        /// Maps a collection with distributed caching for each item.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source collection.</param>
        /// <param name="cache">The distributed cache.</param>
        /// <param name="keySelector">Function to generate cache key for each item.</param>
        /// <param name="options">Optional distributed cache entry options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of mapped and cached destination objects.</returns>
        public static async Task<List<TDest>> MapCollectionToCachedAsync<TDest>(
            this IEnumerable<object>? source,
            IDistributedCache cache,
            Func<object, string> keySelector,
            DistributedCacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (source is null) return new List<TDest>();

            var result = new List<TDest>();
            foreach (var item in source)
            {
                var key = keySelector(item);
                var mapped = await item.MapToCachedAsync<TDest>(cache, key, options, cancellationToken);
                if (mapped is not null)
                {
                    result.Add(mapped);
                }
            }
            return result;
        }

        #endregion

        #region Cache Invalidation

        /// <summary>
        /// Removes a cached mapping from memory cache.
        /// </summary>
        /// <param name="cache">The memory cache.</param>
        /// <param name="cacheKey">The cache key to remove.</param>
        public static void InvalidateMappingCache(this IMemoryCache cache, string cacheKey)
        {
            cache.Remove(cacheKey);
        }

        /// <summary>
        /// Removes a cached mapping from distributed cache.
        /// </summary>
        /// <param name="cache">The distributed cache.</param>
        /// <param name="cacheKey">The cache key to remove.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static async Task InvalidateMappingCacheAsync(
            this IDistributedCache cache,
            string cacheKey,
            CancellationToken cancellationToken = default)
        {
            await cache.RemoveAsync(cacheKey, cancellationToken);
        }

        /// <summary>
        /// Removes multiple cached mappings from distributed cache.
        /// </summary>
        /// <param name="cache">The distributed cache.</param>
        /// <param name="cacheKeys">The cache keys to remove.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static async Task InvalidateMappingCachesAsync(
            this IDistributedCache cache,
            IEnumerable<string> cacheKeys,
            CancellationToken cancellationToken = default)
        {
            foreach (var key in cacheKeys)
            {
                await cache.RemoveAsync(key, cancellationToken);
            }
        }

        #endregion

        #region Cache Key Generation

        /// <summary>
        /// Generates a cache key based on object content and destination type.
        /// </summary>
        /// <param name="source">The source object.</param>
        /// <param name="destType">The destination type.</param>
        /// <returns>A unique cache key.</returns>
        /// <remarks>
        /// The key covers every public property and field of the source graph and the full
        /// identity of both types. Cycles are allowed.
        /// </remarks>
        /// <exception cref="NotSupportedException">
        /// The source graph cannot be described completely, for example because it is deeper than
        /// 64 levels or a getter throws.
        /// </exception>
        public static string GenerateCacheKey(object source, Type destType)
        {
            var fingerprint = Fingerprint.Create(source, destType, StaticLane)
                ?? throw new NotSupportedException(
                    $"A cache key cannot be generated for {source.GetType().FullName}: its public members could not be read completely.");
            return KeyFor(source, destType, fingerprint);
        }

        /// <summary>
        /// Creates a cache key with prefix.
        /// </summary>
        /// <param name="prefix">The prefix for the key.</param>
        /// <param name="identifier">The unique identifier.</param>
        /// <returns>The cache key.</returns>
        public static string CreateCacheKey(string prefix, string identifier)
        {
            return $"mapsicle:{prefix}:{identifier}";
        }

        /// <summary>
        /// Creates a cache key for an entity by ID.
        /// </summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <typeparam name="TDto">The DTO type.</typeparam>
        /// <param name="id">The entity ID.</param>
        /// <returns>The cache key.</returns>
        public static string CreateEntityCacheKey<TEntity, TDto>(object id)
        {
            return $"mapsicle:{typeof(TEntity).Name}:{typeof(TDto).Name}:{id}";
        }

        #endregion
    }

    /// <summary>
    /// Options for cached mapping operations.
    /// </summary>
    public class CachedMappingOptions
    {
        /// <summary>
        /// Default sliding expiration time.
        /// </summary>
        public TimeSpan DefaultSlidingExpiration { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Default absolute expiration time.
        /// </summary>
        public TimeSpan? DefaultAbsoluteExpiration { get; set; }

        /// <summary>
        /// Prefix for all cache keys.
        /// </summary>
        public string KeyPrefix { get; set; } = "mapsicle";

        /// <summary>
        /// Creates MemoryCacheEntryOptions from these settings.
        /// </summary>
        public MemoryCacheEntryOptions ToMemoryCacheOptions()
        {
            var options = new MemoryCacheEntryOptions
            {
                SlidingExpiration = DefaultSlidingExpiration
            };

            if (DefaultAbsoluteExpiration.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = DefaultAbsoluteExpiration.Value;
            }

            return options;
        }

        /// <summary>
        /// Creates DistributedCacheEntryOptions from these settings.
        /// </summary>
        public DistributedCacheEntryOptions ToDistributedCacheOptions()
        {
            var options = new DistributedCacheEntryOptions
            {
                SlidingExpiration = DefaultSlidingExpiration
            };

            if (DefaultAbsoluteExpiration.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = DefaultAbsoluteExpiration.Value;
            }

            return options;
        }
    }

    /// <summary>
    /// A cached mapper that wraps IMapper with automatic caching.
    /// </summary>
    public class CachedMapper : IMapper
    {
        private readonly IMapper _innerMapper;
        private readonly IMemoryCache _cache;
        private readonly CachedMappingOptions _options;
        private readonly string _lane;

        // The keys this mapper has put into the cache. IMemoryCache cannot enumerate its own
        // contents, so invalidating what we added means remembering what we added.
        private readonly ConcurrentDictionary<string, byte> _keys =
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        /// <summary>
        /// Creates a new cached mapper.
        /// </summary>
        /// <param name="innerMapper">The inner mapper to wrap.</param>
        /// <param name="cache">The memory cache.</param>
        /// <param name="options">Optional caching options.</param>
        public CachedMapper(IMapper innerMapper, IMemoryCache cache, CachedMappingOptions? options = null)
        {
            _innerMapper = innerMapper ?? throw new ArgumentNullException(nameof(innerMapper));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _options = options ?? new CachedMappingOptions();
            _lane = LaneFor(_innerMapper);
        }

        /// <inheritdoc/>
        public TDest? Map<TDest>(object? source)
        {
            if (source is null) return default;

            return GetOrMap(source, typeof(TDest), _lane, () => _innerMapper.Map<TDest>(source));
        }

        /// <inheritdoc/>
        public TDest? Map<TSource, TDest>(TSource? source)
        {
            if (source is null) return default;

            var lane = _lane + "|" + (typeof(TSource).AssemblyQualifiedName ?? typeof(TSource).Name);
            return GetOrMap(source, typeof(TDest), lane, () => _innerMapper.Map<TSource, TDest>(source));
        }

        private TDest? GetOrMap<TDest>(object source, Type destType, string lane, Func<TDest?> map)
        {
            var fingerprint = Fingerprint.Create(source, destType, lane);
            if (fingerprint is null) return map();

            var cacheKey = CachingExtensions.KeyFor(source, destType, fingerprint);
            if (CachingExtensions.TryGetVerified(_cache, cacheKey, fingerprint, out TDest? hit)) return hit;

            var mapped = map();
            _cache.Set(cacheKey, new VerifiedEntry(fingerprint, mapped), TrackedOptions(cacheKey));
            return mapped;
        }

        // Two CachedMappers over differently configured inner mappers can share one IMemoryCache,
        // so the inner mapper's identity is part of every fingerprint this class makes.
        private static readonly ConditionalWeakTable<IMapper, string> Lanes = new ConditionalWeakTable<IMapper, string>();
        private static long _nextLane;

        private static string LaneFor(IMapper mapper) =>
            Lanes.GetValue(mapper, _ => "mapper:" + Interlocked.Increment(ref _nextLane).ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <inheritdoc/>
        public TDest Map<TSource, TDest>(TSource source, TDest destination)
        {
            // In-place mapping doesn't benefit from caching
            return _innerMapper.Map(source, destination);
        }

        /// <summary>
        /// Cache options that also record the key, so it can be invalidated later.
        /// </summary>
        /// <remarks>
        /// The eviction callback removes the key again when the entry leaves the cache, so the
        /// tracking set stays roughly the size of the cache rather than growing for the life of the
        /// process. Without it, a long-running app mapping many distinct values would accumulate
        /// keys for entries that expired long ago.
        /// </remarks>
        private MemoryCacheEntryOptions TrackedOptions(string cacheKey)
        {
            var options = _options.ToMemoryCacheOptions();
            options.Size ??= 1;
            _keys[cacheKey] = 0;

            options.RegisterPostEvictionCallback(
                (key, _, _, state) =>
                {
                    if (state is ConcurrentDictionary<string, byte> keys && key is string evicted)
                    {
                        keys.TryRemove(evicted, out _);
                    }
                },
                _keys);

            return options;
        }

        /// <summary>
        /// Invalidates every entry this mapper has cached.
        /// </summary>
        /// <remarks>
        /// This used to be an empty method with a comment saying memory caches cannot be cleared,
        /// so a caller who invoked it kept receiving stale mappings until they expired, with
        /// nothing to indicate the call had done nothing. A public method that documents a
        /// behaviour it does not perform is worse than one that is absent, because the caller has
        /// no reason to look further.
        ///
        /// It removes the keys this mapper created rather than calling <c>MemoryCache.Clear()</c>.
        /// The cache is normally resolved from the container and shared across the application, so
        /// clearing it wholesale would evict entries belonging to components that have nothing to
        /// do with mapping.
        /// </remarks>
        public void InvalidateAll()
        {
            foreach (var key in _keys.Keys)
            {
                _cache.Remove(key);
            }

            _keys.Clear();
        }
    }

    internal sealed class VerifiedEntry
    {
        public VerifiedEntry(string fingerprint, object? value)
        {
            Fingerprint = fingerprint;
            Value = value;
        }

        public string Fingerprint { get; }

        public object? Value { get; }
    }
}
