using System;
using System.Text;
using System.Text.Json;

namespace Mapsicle.Caching
{
    /// <summary>
    /// The bytes a distributed cache entry holds: a header naming the destination type, then JSON.
    /// </summary>
    /// <remarks>
    /// A hit used to deserialize whatever JSON was stored, so a destination member marked
    /// [JsonIgnore] came back empty on a hit while the miss that stored it returned it filled, and
    /// an entry written for one destination type deserialized silently into another. Now a value
    /// is only stored if it reads back identical, and a hit is only accepted for the same type.
    /// </remarks>
    internal static class DistributedPayload
    {
        private const string Magic = "mapsicle:1:";

        public static byte[]? TryWrite<TDest>(TDest value)
        {
            try
            {
                var json = JsonSerializer.SerializeToUtf8Bytes(value);
                var copy = JsonSerializer.Deserialize<TDest>(json);

                var original = Fingerprint.Create(value!, typeof(TDest), "");
                if (original is null || copy is null
                    || !string.Equals(original, Fingerprint.Create(copy, typeof(TDest), ""), StringComparison.Ordinal))
                {
                    return null;
                }

                var header = Header(typeof(TDest));
                var bytes = new byte[header.Length + json.Length];
                Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
                Buffer.BlockCopy(json, 0, bytes, header.Length, json.Length);
                return bytes;
            }
            catch (Exception ex) when (ex is JsonException || ex is NotSupportedException || ex is InvalidOperationException)
            {
                return null;
            }
        }

        public static bool TryRead<TDest>(byte[]? bytes, out TDest? value)
        {
            value = default;
            if (bytes is null) return false;

            var header = Header(typeof(TDest));
            if (bytes.Length < header.Length) return false;
            for (var i = 0; i < header.Length; i++)
            {
                if (bytes[i] != header[i]) return false;
            }

            try
            {
                value = JsonSerializer.Deserialize<TDest>(new ReadOnlySpan<byte>(bytes, header.Length, bytes.Length - header.Length));
                return value is not null;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static byte[] Header(Type type)
        {
            var name = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
            return Encoding.UTF8.GetBytes(Magic + name.Length + ":" + name + "\n");
        }
    }
}
