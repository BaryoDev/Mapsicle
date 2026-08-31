using System;

namespace Mapsicle
{
    /// <summary>
    /// Asks the source generator to emit a compile-time mapper for one type pair.
    /// </summary>
    /// <remarks>
    /// Applied to the assembly, once per pair:
    ///
    /// <code>
    /// [assembly: MapsicleGenerate(typeof(User), typeof(UserDto))]
    /// </code>
    ///
    /// The attribute lives in the core because the assembly that declares the pairs has to reference
    /// it, and requiring the analyzer package at runtime for a declaration the analyzer only reads at
    /// build time would put a dependency where the core has none. Installing
    /// <c>Mapsicle.SourceGen</c> is what makes it do anything; without it the attribute is inert and
    /// the pair maps through the runtime engine exactly as before.
    ///
    /// This is the explicit door. A later release adds usage scanning, where call sites are walked
    /// for <c>.MapTo&lt;T&gt;()</c> and pairs whose source type is statically known there are
    /// generated without any annotation. The explicit form stays, because a pair reached only through
    /// reflection or configuration has no call site to scan.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class MapsicleGenerateAttribute : Attribute
    {
        /// <summary>Creates a request to generate a mapper from <paramref name="sourceType"/> to <paramref name="destinationType"/>.</summary>
        /// <param name="sourceType">The type being mapped from.</param>
        /// <param name="destinationType">The type being mapped into.</param>
        public MapsicleGenerateAttribute(Type sourceType, Type destinationType)
        {
            SourceType = sourceType;
            DestinationType = destinationType;
        }

        /// <summary>The type being mapped from.</summary>
        public Type SourceType { get; }

        /// <summary>The type being mapped into.</summary>
        public Type DestinationType { get; }
    }
}
