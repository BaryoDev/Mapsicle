using System;

namespace Mapsicle
{
    /// <summary>
    /// Asks the source generator to find the pairs itself, rather than being told one at a time.
    /// </summary>
    /// <remarks>
    /// Applied once to the assembly:
    ///
    /// <code>
    /// [assembly: MapsicleGenerateAll]
    /// </code>
    ///
    /// The generator then walks this assembly's call sites for <c>.MapTo&lt;TDest&gt;()</c> and emits
    /// a mapper for every pair whose source type it can read from the call site. Nothing else
    /// changes: the call sites are the ones already written, and a pair it cannot resolve or cannot
    /// emit keeps mapping through the runtime engine.
    ///
    /// It is deliberately quiet. A pair you named with <see cref="MapsicleGenerateAttribute"/> and
    /// which cannot be generated reports <c>MSG001</c>, because you asked for it by name and a
    /// silent refusal would be a broken promise. A pair found by scanning was never asked for, so a
    /// refusal is reported as information rather than a warning: turning one attribute on should not
    /// fill a build log with notices about members you never mentioned.
    ///
    /// Both doors work together. Scanning cannot see a pair reached only through reflection,
    /// configuration or a plugin, because there is no call site to read, so those still need naming.
    /// A pair named explicitly and also found by scanning is emitted once.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class MapsicleGenerateAllAttribute : Attribute
    {
    }
}
