using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Mapsicle.Caching
{
    /// <summary>
    /// A complete, unambiguous description of an object graph as the mapper sees it: every public
    /// instance property and field, recursively, with the runtime type of each value.
    /// </summary>
    /// <remarks>
    /// Cache keys used to be a JSON hash, which dropped [JsonIgnore] members and public fields,
    /// threw on cycles and was cut to 48 bits, so two sources the mapper would map differently
    /// could share a key and one caller got the other's result. Two equal fingerprints mean the
    /// mapper read the same values. Lengths prefix every string, so no value can imitate structure.
    /// </remarks>
    internal static class Fingerprint
    {
        private const int MaxDepth = 64;
        private const int MaxNodes = 100_000;

        private static readonly ConcurrentDictionary<Type, MemberInfo[]> Members =
            new ConcurrentDictionary<Type, MemberInfo[]>();

        private static readonly ConcurrentDictionary<Type, MemberInfo[]> CollectionMembers =
            new ConcurrentDictionary<Type, MemberInfo[]>();

        /// <summary>
        /// Returns null when the graph cannot be described completely: too deep, too large, a
        /// getter that throws, or a type with no public state to read. Callers must then skip the
        /// cache rather than guess.
        /// </summary>
        public static string? Create(object value, Type destType, string lane)
        {
            var writer = new Writer();
            writer.Text(lane);
            writer.Text(destType.AssemblyQualifiedName ?? destType.FullName ?? destType.Name);
            return writer.Value(value, 0) ? writer.ToString() : null;
        }

        public static string Hash(string fingerprint)
        {
            var bytes = Encoding.UTF8.GetBytes(fingerprint);
#if NET5_0_OR_GREATER
            var hash = SHA256.HashData(bytes);
#else
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(bytes);
#endif
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static bool IsLeaf(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
            || type == typeof(Guid);

        private static string LeafText(object value) => value switch
        {
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            Enum e => e.ToString("D"),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };

        private static MemberInfo[] MembersOf(Type type) => Members.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
                .Cast<MemberInfo>()
                .Concat(t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToArray());

        // A collection is described by its items. Members a framework collection adds (Capacity,
        // Comparer, SyncRoot) are not mapped, but members a user collection type adds might be.
        private static MemberInfo[] UserMembersOf(Type type) => CollectionMembers.GetOrAdd(type, t =>
            MembersOf(t).Where(m => m.DeclaringType?.Namespace?.StartsWith("System", StringComparison.Ordinal) != true).ToArray());

        private sealed class Writer
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private readonly Dictionary<Type, int> _types = new Dictionary<Type, int>();
            private readonly Dictionary<object, int> _seen = new Dictionary<object, int>(ByReference.Instance);
            private int _nodes;

            public override string ToString() => _sb.ToString();

            public void Text(string s) => _sb.Append(s.Length).Append(':').Append(s);

            private void TypeTag(Type type)
            {
                if (_types.TryGetValue(type, out var id))
                {
                    _sb.Append('T').Append(id).Append(';');
                    return;
                }

                _types[type] = _types.Count;
                _sb.Append('D');
                Text(type.AssemblyQualifiedName ?? type.FullName ?? type.Name);
            }

            public bool Value(object? value, int depth)
            {
                if (value is null)
                {
                    _sb.Append('n');
                    return true;
                }

                if (depth > MaxDepth || ++_nodes > MaxNodes) return false;

                var type = value.GetType();
                TypeTag(type);

                if (IsLeaf(type))
                {
                    _sb.Append('v');
                    Text(LeafText(value));
                    return true;
                }

                if (value is Type t)
                {
                    _sb.Append('v');
                    Text(t.AssemblyQualifiedName ?? t.FullName ?? t.Name);
                    return true;
                }

                if (value is Delegate || value is MemberInfo || value is Pointer) return false;

                if (!type.IsValueType)
                {
                    if (_seen.TryGetValue(value, out var id))
                    {
                        _sb.Append('r').Append(id).Append(';');
                        return true;
                    }

                    _seen[value] = _seen.Count;
                }

                if (value is IEnumerable items)
                {
                    _sb.Append('[');
                    try
                    {
                        foreach (var item in items)
                        {
                            if (!Value(item, depth + 1)) return false;
                        }
                    }
                    catch (Exception)
                    {
                        return false;
                    }

                    _sb.Append(']');
                    return Members(value, UserMembersOf(type), depth);
                }

                var members = MembersOf(type);
                return members.Length > 0 && Members(value, members, depth);
            }

            private bool Members(object value, MemberInfo[] members, int depth)
            {
                _sb.Append('{');
                foreach (var member in members)
                {
                    object? memberValue;
                    try
                    {
                        memberValue = member is PropertyInfo p ? p.GetValue(value) : ((FieldInfo)member).GetValue(value);
                    }
                    catch (Exception)
                    {
                        return false;
                    }

                    _sb.Append(member is PropertyInfo ? 'p' : 'f');
                    Text(member.Name);
                    if (!Value(memberValue, depth + 1)) return false;
                }

                _sb.Append('}');
                return true;
            }
        }

        private sealed class ByReference : IEqualityComparer<object>
        {
            public static readonly ByReference Instance = new ByReference();

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
