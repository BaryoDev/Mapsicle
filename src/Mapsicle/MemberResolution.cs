using System;
using System.Reflection;

namespace Mapsicle
{
    /// <summary>
    /// The single decision about which source property fills a destination member.
    /// </summary>
    /// <remarks>
    /// PropertyConversion states how one property converts to another. This states which two
    /// properties are being asked about in the first place, and it exists for the same reason: the
    /// four lines that skip <c>[IgnoreMap]</c>, read <c>[MapFrom]</c> and look the source up were
    /// written out once per entry point, and the copies drifted.
    ///
    /// They had already drifted twice by 2.0.0. In-place <c>Map</c> resolved its own bindings and so
    /// never received the conversion cascade, which is why it silently skipped every widening, enum
    /// and nullable conversion that <c>MapTo</c> performed. And the strongly-typed path matched only
    /// the name given by <c>[MapFrom]</c>, with no fallback to the destination member's own name,
    /// so <c>[MapFrom("DoesNotExist")]</c> on a property called <c>Name</c> mapped the value through
    /// <c>MapTo&lt;T&gt;(object)</c> and returned null through <c>MapTo&lt;TSource, TDest&gt;()</c>.
    /// Same two types, two answers, decided by which overload the caller reached for.
    ///
    /// Nothing here runs per call. It runs once, while a mapper is being compiled.
    /// </remarks>
    internal static class MemberResolution
    {
        /// <summary>
        /// Finds the source property for <paramref name="destProp"/>, or reports that the member is
        /// not to be mapped at all.
        /// </summary>
        /// <returns>
        /// False when <c>[IgnoreMap]</c> means the member must be left alone. True otherwise, with
        /// <paramref name="sourceProp"/> set to the match, or null when there is none and the caller
        /// should try flattening.
        /// </returns>
        internal static bool TryResolveSource(
            PropertyInfo destProp,
            PropertyInfo[] sourceProps,
            out PropertyInfo? sourceProp)
        {
            sourceProp = null;

            if (destProp.GetCustomAttribute<IgnoreMapAttribute>() != null)
            {
                return false;
            }

            var mapFrom = destProp.GetCustomAttribute<MapFromAttribute>();
            var primaryName = mapFrom?.SourcePropertyName ?? destProp.Name;

            sourceProp = FindSourceProperty(sourceProps, primaryName, destProp.Name);
            return true;
        }

        /// <summary>
        /// The named source property, preferring <paramref name="primaryName"/> and falling back to
        /// <paramref name="fallbackName"/>.
        /// </summary>
        /// <remarks>
        /// One pass rather than two LINQ scans, and no closure, because this runs for every
        /// destination member of every type pair the process ever maps. The fallback is what makes
        /// a <c>[MapFrom]</c> naming a property that does not exist degrade to ordinary convention
        /// matching instead of silently leaving the member unmapped.
        /// </remarks>
        internal static PropertyInfo? FindSourceProperty(
            PropertyInfo[] sourceProps,
            string primaryName,
            string fallbackName)
        {
            PropertyInfo? fallbackMatch = null;

            for (int i = 0; i < sourceProps.Length; i++)
            {
                var prop = sourceProps[i];

                if (prop.Name.Equals(primaryName, StringComparison.OrdinalIgnoreCase))
                {
                    return prop;
                }

                if (fallbackMatch is null && prop.Name.Equals(fallbackName, StringComparison.OrdinalIgnoreCase))
                {
                    fallbackMatch = prop;
                }
            }

            return fallbackMatch;
        }
    }
}
