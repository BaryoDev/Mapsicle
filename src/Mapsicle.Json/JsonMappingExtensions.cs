using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mapsicle.Fluent;

namespace Mapsicle.Json
{
    /// <summary>
    /// Extension methods for JSON serialization integration with Mapsicle mappings.
    /// </summary>
    public static class JsonMappingExtensions
    {
        private static readonly Lazy<JsonSerializerOptions> _lazyDefaultOptions = new(CreateDefaultOptions);
        private static volatile JsonSerializerOptions? _overriddenOptions;

        /// <summary>
        /// Gets or sets the default JSON serializer options used when none are specified.
        /// </summary>
        public static JsonSerializerOptions DefaultOptions
        {
            get => _overriddenOptions ?? _lazyDefaultOptions.Value;
            set => _overriddenOptions = value;
        }

        private static JsonSerializerOptions CreateDefaultOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };
        }

        #region MapToJson - Object to JSON

        /// <summary>
        /// Maps the source object to the destination type and serializes to JSON string.
        /// </summary>
        /// <typeparam name="TDest">The destination type to map to.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <returns>JSON string representation of the mapped object.</returns>
        public static string? MapToJson<TDest>(this object? source, JsonSerializerOptions? options = null)
        {
            if (source is null) return null;

            var mapped = source.MapTo<TDest>();
            if (mapped is null) return null;

            return JsonSerializer.Serialize(mapped, options ?? DefaultOptions);
        }

        /// <summary>
        /// Maps the source object to the destination type and serializes to JSON string using IMapper.
        /// </summary>
        /// <typeparam name="TDest">The destination type to map to.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <returns>JSON string representation of the mapped object.</returns>
        public static string? MapToJson<TDest>(this IMapper mapper, object? source, JsonSerializerOptions? options = null)
        {
            if (source is null) return null;

            var mapped = mapper.Map<TDest>(source);
            if (mapped is null) return null;

            return JsonSerializer.Serialize(mapped, options ?? DefaultOptions);
        }

        /// <summary>
        /// Maps the source object to the destination type and serializes to a UTF-8 byte array.
        /// </summary>
        /// <typeparam name="TDest">The destination type to map to.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <returns>UTF-8 encoded JSON bytes.</returns>
        public static byte[]? MapToJsonBytes<TDest>(this object? source, JsonSerializerOptions? options = null)
        {
            if (source is null) return null;

            var mapped = source.MapTo<TDest>();
            if (mapped is null) return null;

            return JsonSerializer.SerializeToUtf8Bytes(mapped, options ?? DefaultOptions);
        }

        /// <summary>
        /// Maps the source object to the destination type and writes to a stream asynchronously.
        /// </summary>
        /// <typeparam name="TDest">The destination type to map to.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static async Task MapToJsonAsync<TDest>(
            this object? source,
            Stream stream,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (source is null) return;

            var mapped = source.MapTo<TDest>();
            if (mapped is null) return;

            await JsonSerializer.SerializeAsync(stream, mapped, options ?? DefaultOptions, cancellationToken);
        }

        #endregion

        #region MapFromJson - JSON to Object

        /// <summary>
        /// Deserializes JSON and maps to the destination type.
        /// </summary>
        /// <typeparam name="TIntermediate">The intermediate type to deserialize to.</typeparam>
        /// <typeparam name="TDest">The final destination type to map to.</typeparam>
        /// <param name="json">The JSON string.</param>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <returns>The mapped destination object.</returns>
        public static TDest? MapFromJson<TIntermediate, TDest>(
            this string? json,
            JsonSerializerOptions? options = null)
        {
            // Explicit null check (not IsNullOrEmpty) so the netstandard2.0 compiler can narrow json to non-null
            if (json is null || json.Length == 0) return default;

            var intermediate = JsonSerializer.Deserialize<TIntermediate>(json, options ?? DefaultOptions);
            if (intermediate is null) return default;

            return intermediate.MapTo<TDest>();
        }

        /// <summary>
        /// Deserializes JSON and maps to the destination type using IMapper.
        /// </summary>
        /// <typeparam name="TIntermediate">The intermediate type to deserialize to.</typeparam>
        /// <typeparam name="TDest">The final destination type to map to.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="json">The JSON string.</param>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <returns>The mapped destination object.</returns>
        public static TDest? MapFromJson<TIntermediate, TDest>(
            this IMapper mapper,
            string? json,
            JsonSerializerOptions? options = null)
        {
            // Explicit null check (not IsNullOrEmpty) so the netstandard2.0 compiler can narrow json to non-null
            if (json is null || json.Length == 0) return default;

            var intermediate = JsonSerializer.Deserialize<TIntermediate>(json, options ?? DefaultOptions);
            if (intermediate is null) return default;

            return mapper.Map<TIntermediate, TDest>(intermediate);
        }

        /// <summary>
        /// Deserializes JSON from a stream and maps to the destination type asynchronously.
        /// </summary>
        /// <typeparam name="TIntermediate">The intermediate type to deserialize to.</typeparam>
        /// <typeparam name="TDest">The final destination type to map to.</typeparam>
        /// <param name="stream">The stream containing JSON.</param>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The mapped destination object.</returns>
        public static async Task<TDest?> MapFromJsonAsync<TIntermediate, TDest>(
            this Stream stream,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var intermediate = await JsonSerializer.DeserializeAsync<TIntermediate>(
                stream, options ?? DefaultOptions, cancellationToken);

            if (intermediate is null) return default;

            return intermediate.MapTo<TDest>();
        }

        /// <summary>
        /// Deserializes JSON from UTF-8 bytes and maps to the destination type.
        /// </summary>
        /// <typeparam name="TIntermediate">The intermediate type to deserialize to.</typeparam>
        /// <typeparam name="TDest">The final destination type to map to.</typeparam>
        /// <param name="utf8Json">The UTF-8 encoded JSON bytes.</param>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <returns>The mapped destination object.</returns>
        public static TDest? MapFromJsonBytes<TIntermediate, TDest>(
            this ReadOnlySpan<byte> utf8Json,
            JsonSerializerOptions? options = null)
        {
            if (utf8Json.IsEmpty) return default;

            var intermediate = JsonSerializer.Deserialize<TIntermediate>(utf8Json, options ?? DefaultOptions);
            if (intermediate is null) return default;

            return intermediate.MapTo<TDest>();
        }

        #endregion

        #region Collection Mapping

        /// <summary>
        /// Maps a collection of objects to JSON array string.
        /// </summary>
        /// <typeparam name="TDest">The destination type for each item.</typeparam>
        /// <param name="source">The source collection.</param>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <returns>JSON array string.</returns>
        public static string? MapCollectionToJson<TDest>(
            this IEnumerable<object>? source,
            JsonSerializerOptions? options = null)
        {
            if (source is null) return null;

            var mapped = new List<TDest>();
            foreach (var item in source)
            {
                var mappedItem = item.MapTo<TDest>();
                if (mappedItem is not null)
                {
                    mapped.Add(mappedItem);
                }
            }

            return JsonSerializer.Serialize(mapped, options ?? DefaultOptions);
        }

        /// <summary>
        /// Deserializes JSON array and maps each item to the destination type.
        /// </summary>
        /// <typeparam name="TIntermediate">The intermediate type to deserialize to.</typeparam>
        /// <typeparam name="TDest">The final destination type to map to.</typeparam>
        /// <param name="json">The JSON array string.</param>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <returns>List of mapped destination objects.</returns>
        public static List<TDest> MapCollectionFromJson<TIntermediate, TDest>(
            this string? json,
            JsonSerializerOptions? options = null)
        {
            // Explicit null check (not IsNullOrEmpty) so the netstandard2.0 compiler can narrow json to non-null
            if (json is null || json.Length == 0) return new List<TDest>();

            var intermediates = JsonSerializer.Deserialize<List<TIntermediate>>(json, options ?? DefaultOptions);
            if (intermediates is null) return new List<TDest>();

            var result = new List<TDest>(intermediates.Count);
            foreach (var item in intermediates)
            {
                if (item is not null)
                {
                    var mapped = item.MapTo<TDest>();
                    if (mapped is not null)
                    {
                        result.Add(mapped);
                    }
                }
            }

            return result;
        }

        #endregion

        #region JsonDocument Mapping

        /// <summary>
        /// Maps properties from a JsonDocument to a destination type.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="document">The JSON document.</param>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <returns>The mapped destination object.</returns>
        public static TDest? MapFromJsonDocument<TDest>(
            this JsonDocument? document,
            JsonSerializerOptions? options = null)
            where TDest : new()
        {
            if (document is null) return default;

            return JsonSerializer.Deserialize<TDest>(document.RootElement.GetRawText(), options ?? DefaultOptions);
        }

        /// <summary>
        /// Maps properties from a JsonElement to a destination type.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="element">The JSON element.</param>
        /// <param name="options">Optional JSON serializer options.</param>
        /// <returns>The mapped destination object.</returns>
        public static TDest? MapFromJsonElement<TDest>(
            this JsonElement element,
            JsonSerializerOptions? options = null)
            where TDest : new()
        {
            return JsonSerializer.Deserialize<TDest>(element.GetRawText(), options ?? DefaultOptions);
        }

        #endregion
    }

    /// <summary>
    /// JSON serializer options presets for common scenarios.
    /// </summary>
    public static class JsonMappingOptions
    {
        /// <summary>
        /// Camel case property names with null value exclusion.
        /// </summary>
        public static JsonSerializerOptions CamelCase { get; } = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Snake case property names (e.g., user_name).
        /// </summary>
        public static JsonSerializerOptions SnakeCase { get; } = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Kebab case property names (e.g., user-name).
        /// </summary>
        public static JsonSerializerOptions KebabCase { get; } = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Indented output for human readability.
        /// </summary>
        public static JsonSerializerOptions Indented { get; } = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Strict mode - case sensitive, no extra properties allowed.
        /// </summary>
        public static JsonSerializerOptions Strict { get; } = new()
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
    }
}
