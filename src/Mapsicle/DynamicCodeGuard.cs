using System;

namespace Mapsicle
{
    /// <summary>
    /// Refuses a runtime-built mapper where the runtime cannot build one correctly.
    /// </summary>
    /// <remarks>
    /// Under NativeAOT the expression builder does not fail, it answers wrongly. The trimmer drops
    /// the property and constructor metadata it reads, so a pair nobody declared came back null
    /// from MapTo, wrote nothing through Map, and filled zeros from a dictionary, all without an
    /// exception or a log line. A declared pair is served by its generated mapper and never gets
    /// here. A value or string on both sides needs no member metadata and still converts.
    /// </remarks>
    internal static class DynamicCodeGuard
    {
        /// <summary>
        /// Stands in for the runtime's answer in tests, which cannot run under NativeAOT.
        /// </summary>
        internal static bool? SupportedOverride;

        internal static bool IsSupported =>
            SupportedOverride ??
#if NETSTANDARD2_0
            true;
#else
            System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;
#endif

        internal static void EnsureSupported(Type sourceType, Type destType)
        {
            if (IsSupported || (IsScalar(sourceType) && IsScalar(destType))) return;

            throw new NotSupportedException(
                $"Mapsicle cannot map {Describe(sourceType)} to {Describe(destType)} here: this runtime does not " +
                "support dynamic code (NativeAOT) and the pair has no generated mapper. " + Remedy(sourceType, destType));
        }

        private static string Remedy(Type sourceType, Type destType)
        {
            var element = ElementType(destType);
            if (element != null && ElementType(sourceType) != null)
            {
                return $"Declare the element pair with [MapsicleGenerate] and map the collection with " +
                    $"source.MapTo<{Describe(element)}>(), which serves each element from the generated mapper.";
            }

            return $"Declare it with [assembly: MapsicleGenerate(typeof({Describe(sourceType)}), typeof({Describe(destType)}))] " +
                "from the Mapsicle.SourceGen package and map it with MapTo; Map onto an existing object, " +
                "MapperFactory and dictionary mapping are not generated.";
        }

        private static Type? ElementType(Type type)
        {
            if (type == typeof(string)) return null;
            if (type.IsArray) return type.GetElementType();
            return type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
                ? type.GetGenericArguments()[0]
                : null;
        }

        private static string Describe(Type type)
        {
            if (type.IsArray) return Describe(type.GetElementType()!) + "[]";
            if (!type.IsGenericType) return type.Name;
            var name = type.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0) name = name.Substring(0, tick);
            var args = type.GetGenericArguments();
            var parts = new string[args.Length];
            for (var i = 0; i < args.Length; i++) parts[i] = Describe(args[i]);
            return name + "<" + string.Join(", ", parts) + ">";
        }

        private static bool IsScalar(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying.IsPrimitive || underlying.IsEnum || underlying == typeof(string)
                || underlying == typeof(decimal) || underlying == typeof(DateTime)
                || underlying == typeof(DateTimeOffset) || underlying == typeof(Guid)
                || underlying == typeof(TimeSpan);
        }
    }
}
