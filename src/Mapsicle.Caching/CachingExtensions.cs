using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Mapsicle.Fluent;

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

            if (cache.TryGetValue(cacheKey, out TDest? cached))
            {
                return cached;
            }

            var mapped = source.MapTo<TDest>();
            if (mapped is null) return default;

            cache.Set(cacheKey, mapped, options ?? new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(5)
            });

            return mapped;
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

            if (cache.TryGetValue(cacheKey, out TDest? cached))
            {
                return cached;
            }

            var mapped = mapper.Map<TDest>(source);
            if (mapped is null) return default;

            cache.Set(cacheKey, mapped, options ?? new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(5)
            });

            return mapped;
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

            var cacheKey = GenerateCacheKey(source, typeof(TDest));
            var options = new MemoryCacheEntryOptions
            {
                SlidingExpiration = expiration ?? TimeSpan.FromMinutes(5)
            };

            return source.MapToCached<TDest>(cache, cacheKey, options);
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
            if (cachedBytes is not null)
            {
                return JsonSerializer.Deserialize<TDest>(cachedBytes);
            }

            var mapped = source.MapTo<TDest>();
            if (mapped is null) return default;

            var bytes = JsonSerializer.SerializeToUtf8Bytes(mapped);
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
            if (cachedBytes is not null)
            {
                return JsonSerializer.Deserialize<TDest>(cachedBytes);
            }

            var mapped = mapper.Map<TDest>(source);
            if (mapped is null) return default;

            var bytes = JsonSerializer.SerializeToUtf8Bytes(mapped);
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
        public static string GenerateCacheKey(object source, Type destType)
        {
            var json = JsonSerializer.Serialize(source);
            var hash = ComputeHash(json);
            return $"mapsicle:{source.GetType().Name}:{destType.Name}:{hash}";
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

        private static string ComputeHash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes).Substring(0, 8);
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
        }

        /// <inheritdoc/>
        public TDest? Map<TDest>(object? source)
        {
            if (source is null) return default;

            var cacheKey = CachingExtensions.GenerateCacheKey(source, typeof(TDest));
            return _innerMapper.MapToCached<TDest>(source, _cache, cacheKey, _options.ToMemoryCacheOptions());
        }

        /// <inheritdoc/>
        public TDest? Map<TSource, TDest>(TSource? source)
        {
            if (source is null) return default;

            var cacheKey = CachingExtensions.GenerateCacheKey(source, typeof(TDest));

            if (_cache.TryGetValue(cacheKey, out TDest? cached))
            {
                return cached;
            }

            var mapped = _innerMapper.Map<TSource, TDest>(source);
            if (mapped is not null)
            {
                _cache.Set(cacheKey, mapped, _options.ToMemoryCacheOptions());
            }

            return mapped;
        }

        /// <inheritdoc/>
        public TDest Map<TSource, TDest>(TSource source, TDest destination)
        {
            // In-place mapping doesn't benefit from caching
            return _innerMapper.Map(source, destination);
        }

        /// <summary>
        /// Invalidates all cache entries.
        /// </summary>
        public void InvalidateAll()
        {
            // Memory cache doesn't support clear, so this is a no-op
            // Users should use specific key invalidation
        }
    }
}
