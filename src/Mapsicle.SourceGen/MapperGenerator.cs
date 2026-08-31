using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Mapsicle.SourceGen
{
    /// <summary>
    /// Emits a compile-time mapper for every pair declared with <c>[assembly: MapsicleGenerate]</c>.
    /// </summary>
    /// <remarks>
    /// This is a cache pre-loader, not a second engine. The runtime engine already separates how a
    /// mapper is made from how it is used: the first map of a pair builds a delegate, a cache holds
    /// it, and every later call just invokes it. So the generated code replaces the factory and
    /// nothing else, by handing a delegate to <c>Mapper.RegisterGenerated</c> from a module
    /// initializer. A pair nobody declares is untouched and still maps through the engine.
    ///
    /// The conversion rules it emits have to agree with the runtime cascade in
    /// <c>PropertyConversion</c>, which is a second implementation of the one thing CONTRIBUTING
    /// says must exist once. That is a deliberate and uncomfortable exception, so it is paid for
    /// with a conformance suite that runs one table of cases through both lanes and asserts they
    /// produce identical output. Any rule added here without a row in that table is the drift this
    /// project has already shipped twice.
    ///
    /// Only the rules the conformance suite covers are emitted. Anything else is refused with a
    /// diagnostic rather than guessed at, because a generated mapper that silently disagrees with
    /// the engine is worse than no generated mapper.
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed class MapperGenerator : IIncrementalGenerator
    {
        private const string AttributeName = "Mapsicle.MapsicleGenerateAttribute";

        private static readonly DiagnosticDescriptor CannotGenerate = new(
            id: "MSG001",
            title: "Mapsicle cannot generate this pair",
            messageFormat: "Cannot generate a mapper from '{0}' to '{1}': {2}. The pair still maps through the runtime engine.",
            category: "Mapsicle",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "A pair the generator refuses falls back to the runtime engine, so mapping still works. " +
                         "The warning exists because the declaration asked for something it did not get.");

        /// <summary>Wires the generator to the assembly's declared pairs.</summary>
        /// <param name="context">Supplied by Roslyn.</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var pairs = context.CompilationProvider.Select(static (compilation, _) => ReadPairs(compilation));

            context.RegisterSourceOutput(pairs, static (spc, requested) =>
            {
                foreach (var diagnostic in requested.Diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic);
                }

                if (requested.Plans.Count == 0) return;

                spc.AddSource("MapsicleGenerated.g.cs", SourceText.From(Emit(requested.Plans), Encoding.UTF8));
                spc.AddSource("MapsicleGeneratedExtensions.g.cs", SourceText.From(EmitExtensions(requested.Plans), Encoding.UTF8));
            });
        }

        // ---- reading the declarations ---------------------------------------------------------

        private readonly struct Requested
        {
            internal Requested(List<MapPlan> plans, List<Diagnostic> diagnostics)
            {
                Plans = plans;
                Diagnostics = diagnostics;
            }

            internal List<MapPlan> Plans { get; }
            internal List<Diagnostic> Diagnostics { get; }
        }

        private static Requested ReadPairs(Compilation compilation)
        {
            var plans = new List<MapPlan>();
            var diagnostics = new List<Diagnostic>();

            var marker = compilation.GetTypeByMetadataName(AttributeName);
            if (marker is null) return new Requested(plans, diagnostics);

            foreach (var attribute in compilation.Assembly.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker)) continue;
                if (attribute.ConstructorArguments.Length != 2) continue;

                if (attribute.ConstructorArguments[0].Value is not INamedTypeSymbol source) continue;
                if (attribute.ConstructorArguments[1].Value is not INamedTypeSymbol destination) continue;

                var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;

                if (Refusal(source, destination) is { } reason)
                {
                    diagnostics.Add(Diagnostic.Create(
                        CannotGenerate, location, source.ToDisplayString(), destination.ToDisplayString(), reason));
                    continue;
                }

                plans.Add(Plan(source, destination));
            }

            return new Requested(plans, diagnostics);
        }

        /// <summary>
        /// Why this pair cannot be generated, or null when it can.
        /// </summary>
        /// <remarks>
        /// Narrow on purpose. Every shape allowed here has to be a shape the conformance suite runs
        /// through both lanes, so the list grows only when the suite grows with it.
        /// </remarks>
        private static string? Refusal(INamedTypeSymbol source, INamedTypeSymbol destination)
        {
            if (destination.IsAbstract) return "the destination is abstract";
            if (destination.TypeKind != TypeKind.Class) return "the destination is not a class";
            if (source.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return "the source is not a class or struct";
            if (destination.IsGenericType || source.IsGenericType) return "generic types are not generated yet";

            var hasParameterlessCtor = destination.InstanceConstructors
                .Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

            if (!hasParameterlessCtor) return "the destination has no public parameterless constructor";

            if (destination.DeclaredAccessibility != Accessibility.Public) return "the destination is not public";
            if (source.DeclaredAccessibility != Accessibility.Public) return "the source is not public";

            var plan = Plan(source, destination);

            if (plan.Assignments.Count == 0)
            {
                return "no destination member could be matched to a readable source member";
            }

            // Every member the engine would map has to be a member this can emit, or the generated
            // mapper silently returns less than the engine would and the declaration makes the
            // mapping worse. Accepting a pair because *some* member matched produced exactly that:
            // an Order with a widening id, an enum to string, a nested reference and a collection
            // matched on one nullable DateTime, so the generated mapper filled that and left the
            // other five at their defaults. It returned an almost empty object and raised nothing.
            var unmapped = Unmappable(source, destination).FirstOrDefault();
            if (unmapped != null)
            {
                return $"'{unmapped}' is a member the engine maps and this generator cannot emit, "
                     + "either because the conversion is not supported yet or because its setter is "
                     + "not public; dropping it would return less than the engine does";
            }

            // A member marked [Obsolete(error: true)] cannot be touched by generated code. The
            // compiler reports CS0619 as an error, and #pragma warning disable does not suppress
            // errors, so emitting the assignment breaks the consumer's build inside a file they did
            // not write. Refusing the pair keeps the engine mapping it, which is what would have
            // happened without the declaration, so the two lanes still agree.
            var blocked = plan.Assignments
                .Select(a => ObsoleteAsError(source, a.SourceName) ?? ObsoleteAsError(destination, a.DestinationName))
                .FirstOrDefault(m => m != null);

            if (blocked != null)
            {
                return $"'{blocked}' is marked [Obsolete] as an error, which generated code cannot reference";
            }

            return null;
        }

        /// <summary>
        /// Destination members the engine would map that this generator cannot emit.
        /// </summary>
        /// <remarks>
        /// The engine matches on name and then converts: widening, enum to string, nullable, nested
        /// references and collections. This emitter only assigns identical types. A member the
        /// engine would fill and this would not is the difference between the two lanes, and the
        /// pair has to be refused rather than generated short.
        /// </remarks>
        private static IEnumerable<string> Unmappable(INamedTypeSymbol source, INamedTypeSymbol destination)
        {
            var readable = Properties(source)
                .Where(p => p.GetMethod is { DeclaredAccessibility: Accessibility.Public })
                .ToList();

            foreach (var destProp in Properties(destination))
            {
                // Any setter, not just a public one. Reflection writes a private setter and
                // generated code cannot, so a member with one is a member the engine fills and this
                // would silently leave at its default.
                if (destProp.SetMethod is null) continue;
                if (destProp.IsIndexer) continue;

                var match = readable.FirstOrDefault(
                    p => string.Equals(p.Name, destProp.Name, StringComparison.OrdinalIgnoreCase));

                if (match is null) continue;

                if (destProp.SetMethod.DeclaredAccessibility != Accessibility.Public)
                {
                    yield return destProp.Name;
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(match.Type, destProp.Type)) continue;

                yield return destProp.Name;
            }
        }

        /// <summary>The member's name when it is obsolete as an error, otherwise null.</summary>
        private static string? ObsoleteAsError(INamedTypeSymbol type, string memberName)
        {
            var member = Properties(type).FirstOrDefault(
                p => string.Equals(p.Name, memberName, StringComparison.OrdinalIgnoreCase));

            if (member is null) return null;

            foreach (var attribute in member.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != "System.ObsoleteAttribute") continue;

                var isError = attribute.ConstructorArguments.Length > 1
                              && attribute.ConstructorArguments[1].Value is true;

                if (isError) return member.Name;
            }

            return null;
        }

        // ---- deciding what to emit ------------------------------------------------------------

        private sealed class MapPlan
        {
            internal MapPlan(INamedTypeSymbol source, INamedTypeSymbol destination, List<Assignment> assignments)
            {
                Source = source;
                Destination = destination;
                Assignments = assignments;
            }

            internal INamedTypeSymbol Source { get; }
            internal INamedTypeSymbol Destination { get; }
            internal List<Assignment> Assignments { get; }
        }

        private readonly struct Assignment
        {
            internal Assignment(string destinationName, string sourceName)
            {
                DestinationName = destinationName;
                SourceName = sourceName;
            }

            internal string DestinationName { get; }
            internal string SourceName { get; }
        }

        /// <summary>
        /// Matches destination members to source members by the same rule the engine uses.
        /// </summary>
        /// <remarks>
        /// Name equality ignoring case, a readable public source property, a writable public
        /// destination property, and identical types. Identical types only, for now: every widening,
        /// enum, nullable and nested conversion the engine performs is a rule that would have to be
        /// restated here and proven identical, so they are refused until the conformance suite
        /// covers them one at a time.
        /// </remarks>
        private static MapPlan Plan(INamedTypeSymbol source, INamedTypeSymbol destination)
        {
            var readable = Properties(source)
                .Where(p => p.GetMethod is { DeclaredAccessibility: Accessibility.Public })
                .ToList();

            var assignments = new List<Assignment>();

            foreach (var destProp in Properties(destination))
            {
                if (destProp.SetMethod is not { DeclaredAccessibility: Accessibility.Public }) continue;
                if (destProp.IsIndexer) continue;

                var match = readable.FirstOrDefault(
                    p => string.Equals(p.Name, destProp.Name, StringComparison.OrdinalIgnoreCase));

                if (match is null) continue;
                if (!SymbolEqualityComparer.Default.Equals(match.Type, destProp.Type)) continue;

                assignments.Add(new Assignment(destProp.Name, match.Name));
            }

            return new MapPlan(source, destination, assignments);
        }

        private static IEnumerable<IPropertySymbol> Properties(INamedTypeSymbol type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (current.SpecialType == SpecialType.System_Object) yield break;

                foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
                {
                    if (member.IsStatic) continue;
                    if (member.DeclaredAccessibility != Accessibility.Public) continue;
                    yield return member;
                }
            }
        }

        // ---- emitting -------------------------------------------------------------------------

        /// <summary>
        /// Emits a <c>MapTo</c> extension per declared source type, so a declared pair skips the lookup.
        /// </summary>
        /// <remarks>
        /// Registering a generated mapper removes the compile, not the route to it. Measured on an
        /// idle machine, the generated code runs in 23 ns and <c>MapTo&lt;TDest&gt;(object)</c> takes
        /// 86.5, because reaching it costs a GetType, a tuple key, a dictionary probe, a delegate
        /// cast and an invoke. That is 73 percent of the call spent finding the mapper.
        ///
        /// An extension whose receiver is the declared source type is more specific than the one
        /// taking <c>object</c>, so the compiler binds to it and the pair is resolved when you
        /// compile rather than when you run. The destination is still a type argument, so it is
        /// checked here, which is a type handle comparison rather than a hash lookup. Anything this
        /// method was not generated for falls through to the engine unchanged.
        ///
        /// It is emitted into the source type's own namespace, which is the only placement that
        /// reliably wins. C# searches enclosing namespaces from the innermost outward and stops at
        /// the first one holding an applicable candidate. Putting it in the global namespace failed
        /// exactly that way: a call site inside <c>Mapsicle.SourceGen.Tests</c> has <c>Mapsicle</c>
        /// as an ancestor namespace, so the search found <c>Mapper.MapTo(object)</c> there and never
        /// reached the global namespace. It bound in a benchmark whose types happened to sit in the
        /// global namespace, and a timing that improved was the only evidence either way, which is
        /// the worst outcome available here: no error, no speedup, and a number that says it worked.
        /// </remarks>
        private static string EmitExtensions(List<MapPlan> plans)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Generated by Mapsicle.SourceGen. Do not edit.");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("// A member the consumer marked obsolete is still mapped, because the runtime engine maps it");
            sb.AppendLine("// and the two lanes have to agree. Without this, declaring a pair whose source or");
            sb.AppendLine("// destination carries [Obsolete(error: true)] fails the build inside a generated file the");
            sb.AppendLine("// consumer did not write and cannot edit.");
            sb.AppendLine("#pragma warning disable CS0612, CS0618, CS0619");
            sb.AppendLine();

            var byNamespace = plans.GroupBy(p => p.Source.ContainingNamespace.IsGlobalNamespace
                ? ""
                : p.Source.ContainingNamespace.ToDisplayString());

            foreach (var namespaceGroup in byNamespace)
            {
                var indent = "";
                if (namespaceGroup.Key.Length > 0)
                {
                    sb.AppendLine($"namespace {namespaceGroup.Key}");
                    sb.AppendLine("{");
                    indent = "    ";
                }

                sb.AppendLine($"{indent}/// <summary>Compile-time bound MapTo for the pairs this assembly declared.</summary>");
                sb.AppendLine($"{indent}internal static class MapsicleGeneratedExtensions");
                sb.AppendLine($"{indent}{{");
                EmitGroup(sb, namespaceGroup, indent);
                sb.AppendLine($"{indent}}}");

                if (namespaceGroup.Key.Length > 0) sb.AppendLine("}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void EmitGroup(StringBuilder sb, IEnumerable<MapPlan> plans, string indent)
        {
            var bySource = plans
                .GroupBy(p => p.Source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .ToList();

            foreach (var group in bySource)
            {
                var src = group.Key;
                sb.AppendLine($"{indent}    /// <summary>Maps a <see cref=\"{Escape(src)}\"/> into <typeparamref name=\"TDest\"/>.</summary>");
                sb.AppendLine($"{indent}    public static TDest? MapTo<TDest>(this {src}? source)");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        if (source is null) return default;");
                sb.AppendLine();

                foreach (var plan in group)
                {
                    var dst = plan.Destination.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    sb.AppendLine($"{indent}        if (typeof(TDest) == typeof({dst}))");
                    sb.AppendLine($"{indent}        {{");
                    sb.AppendLine($"{indent}            return (TDest)(object)new {dst}");
                    sb.AppendLine($"{indent}            {{");
                    for (var i = 0; i < plan.Assignments.Count; i++)
                    {
                        var a = plan.Assignments[i];
                        var comma = i == plan.Assignments.Count - 1 ? "" : ",";
                        sb.AppendLine($"{indent}                @{a.DestinationName} = source.@{a.SourceName}{comma}");
                    }
                    sb.AppendLine($"{indent}            }};");
                    sb.AppendLine($"{indent}        }}");
                    sb.AppendLine();
                }

                sb.AppendLine($"{indent}        // Not a pair this assembly declared, so the engine resolves it as before.");
                sb.AppendLine($"{indent}        return global::Mapsicle.Mapper.MapTo<TDest>((object)source);");
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine();
            }
        }

        private static string Escape(string value) => value.Replace("<", "{").Replace(">", "}");

        /// <summary>
        /// Every emitted member name carries an <c>@</c>, including the ones that do not need it.
        /// </summary>
        /// <remarks>
        /// A member called <c>class</c> or <c>event</c> is legal C# written as <c>@class</c>, and a
        /// generator emitting the bare name produces source that does not compile. That is the worst
        /// failure available to a generator: the consumer's build breaks inside a file they did not
        /// write and cannot edit. Prefixing unconditionally is valid for ordinary identifiers too,
        /// so there is no keyword table to keep current.
        /// </remarks>
        private static string Emit(List<MapPlan> plans)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Generated by Mapsicle.SourceGen. Do not edit.");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("// A member the consumer marked obsolete is still mapped, because the runtime engine maps it");
            sb.AppendLine("// and the two lanes have to agree. Without this, declaring a pair whose source or");
            sb.AppendLine("// destination carries [Obsolete(error: true)] fails the build inside a generated file the");
            sb.AppendLine("// consumer did not write and cannot edit.");
            sb.AppendLine("#pragma warning disable CS0612, CS0618, CS0619");
            sb.AppendLine();
            sb.AppendLine("namespace Mapsicle.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Compile-time mappers, registered into the engine at startup.</summary>");
            sb.AppendLine("    internal static class MapsicleGeneratedMappers");
            sb.AppendLine("    {");
            sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
            sb.AppendLine("        internal static void Register()");
            sb.AppendLine("        {");

            foreach (var plan in plans)
            {
                var src = plan.Source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var dst = plan.Destination.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                sb.AppendLine($"            global::Mapsicle.Mapper.RegisterGenerated<{src}, {dst}>(");
                sb.AppendLine($"                static source => new {dst}");
                sb.AppendLine("                {");

                for (var i = 0; i < plan.Assignments.Count; i++)
                {
                    var a = plan.Assignments[i];
                    var comma = i == plan.Assignments.Count - 1 ? "" : ",";
                    sb.AppendLine($"                    @{a.DestinationName} = source.@{a.SourceName}{comma}");
                }

                sb.AppendLine("                },");
                sb.AppendLine("                requiresDepthTracking: false);");
                sb.AppendLine();
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
