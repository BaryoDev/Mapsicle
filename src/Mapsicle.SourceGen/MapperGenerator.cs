using System;
using System.Collections.Generic;
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
    ///
    /// The shape of the emitted code is load bearing, not cosmetic. Collection helpers keep the
    /// concrete element type in their parameter and index it, because widening the parameter to an
    /// interface makes foreach box the struct enumerator and dispatch through it. Measured on four
    /// collections of five items, that one habit costs 38.5 ns and 120 bytes, and it is the whole of
    /// why the nearest competitor measures 1.08 to 1.11 against hand written rather than 1.00.
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

            var index = 0;

            foreach (var attribute in compilation.Assembly.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker)) continue;
                if (attribute.ConstructorArguments.Length != 2) continue;

                if (attribute.ConstructorArguments[0].Value is not INamedTypeSymbol source) continue;
                if (attribute.ConstructorArguments[1].Value is not INamedTypeSymbol destination) continue;

                var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;

                var plan = TryPlan(source, destination, index, out var refusal);

                if (plan is null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        CannotGenerate, location, source.ToDisplayString(), destination.ToDisplayString(), refusal));
                    continue;
                }

                plans.Add(plan);
                index++;
            }

            return new Requested(plans, diagnostics);
        }

        // ---- planning ---------------------------------------------------------------------------

        /// <summary>The plan for one declared pair, or null with a reason it was refused.</summary>
        /// <remarks>
        /// Planning and refusing are the same walk, deliberately. They used to be two, and the pair
        /// was planned twice: once to decide whether it could be emitted and once to emit it. Two
        /// walks over the same members is two places for the answer to differ, and it is the shape
        /// that let a pair be accepted because some member matched while other members were quietly
        /// dropped.
        /// </remarks>
        private static MapPlan? TryPlan(
            INamedTypeSymbol source, INamedTypeSymbol destination, int index, out string? refusal)
        {
            refusal = StructuralRefusal(source, destination);
            if (refusal != null) return null;

            var context = new PlanContext($"P{index}_");
            var assignments = PlanMembers(source, destination, context, out refusal);

            if (assignments is null) return null;

            if (assignments.Count == 0)
            {
                refusal = "no destination member could be matched to a readable source member";
                return null;
            }

            // A member marked [Obsolete(error: true)] cannot be touched by generated code. The
            // compiler reports CS0619 as an error, and #pragma warning disable does not suppress
            // errors, so emitting the assignment breaks the consumer's build inside a file they did
            // not write. Refusing the pair keeps the engine mapping it, which is what would have
            // happened without the declaration, so the two lanes still agree.
            var blocked = assignments
                .Select(a => ObsoleteAsError(source, a.SourceName) ?? ObsoleteAsError(destination, a.DestinationName))
                .FirstOrDefault(m => m != null);

            if (blocked != null)
            {
                refusal = $"'{blocked}' is marked [Obsolete] as an error, which generated code cannot reference";
                return null;
            }

            refusal = null;
            return new MapPlan(source, destination, assignments, context.Helpers.ToList(), $"{context.Prefix}Map");
        }

        /// <summary>Why this pair cannot be generated at all, before any member is looked at.</summary>
        private static string? StructuralRefusal(INamedTypeSymbol source, INamedTypeSymbol destination)
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

            return null;
        }

        /// <summary>
        /// Every destination member the engine would fill, with the C# that fills it.
        /// </summary>
        /// <remarks>
        /// Returns null the moment one member cannot be emitted, because the pair is refused whole.
        /// Emitting a partial mapper would return less than the engine does for the same call, and
        /// silently: an Order with a widening id, an enum to string, a nested reference and a
        /// collection once matched on one nullable DateTime, filled that, and left the rest at their
        /// defaults. It returned an almost empty object and raised nothing.
        /// </remarks>
        private static List<Assignment>? PlanMembers(
            INamedTypeSymbol source, INamedTypeSymbol destination, PlanContext context, out string? refusal)
        {
            var readable = ReadableProperties(source);
            var assignments = new List<Assignment>();

            foreach (var destProp in Properties(destination))
            {
                if (destProp.IsIndexer) continue;
                if (destProp.SetMethod is null) continue;

                // Any setter, not just a public one. Reflection writes a private setter and
                // generated code cannot, so a member with one is a member the engine fills and this
                // would silently leave at its default.
                var match = readable.FirstOrDefault(
                    p => string.Equals(p.Name, destProp.Name, StringComparison.OrdinalIgnoreCase));

                if (destProp.SetMethod.DeclaredAccessibility != Accessibility.Public)
                {
                    if (match != null || FlattenedPath(destProp, readable) != null)
                    {
                        refusal = $"'{destProp.Name}' has a setter that is not public, which reflection can "
                                + "write and generated code cannot";
                        return null;
                    }

                    continue;
                }

                if (match != null)
                {
                    var expression = Convert(match.Type, destProp.Type, $"source.@{match.Name}", context);

                    if (expression is null)
                    {
                        refusal = $"'{destProp.Name}' converts {Describe(match.Type)} into {Describe(destProp.Type)}, "
                                + "which the engine performs and this generator has no emitted rule for";
                        return null;
                    }

                    assignments.Add(new Assignment(destProp.Name, expression, match.Name));
                    continue;
                }

                // No member of that name, so the engine tries a flattened path: CustomerAddressCity
                // from Customer.Address.City. The search is the same one the cascade runs, longest
                // prefix first and four levels deep, because a different answer here is drift.
                var path = FlattenedPath(destProp, readable);
                if (path != null)
                {
                    var expression = FlattenedExpression(path, destProp.Type, context);

                    if (expression is null)
                    {
                        refusal = $"'{destProp.Name}' flattens to {string.Join(".", path.Select(p => p.Name))}, "
                                + "and the conversion at the end of that path has no emitted rule";
                        return null;
                    }

                    assignments.Add(new Assignment(destProp.Name, expression, path[0].Name));
                }
            }

            refusal = null;
            return assignments;
        }

        // ---- the conversion rules, in the cascade's order ----------------------------------------

        /// <summary>
        /// The C# that converts <paramref name="expression"/> from one type to another, or null.
        /// </summary>
        /// <remarks>
        /// The order matches <c>PropertyConversion.TryBuild</c> exactly, because the order is part of
        /// the rule: an enum into a string has to be caught by the string branch before the enum to
        /// enum branch sees it, and a nested class has to be caught before anything tries to format
        /// it. Reordering these silently changes what a member maps to.
        ///
        /// Null means refuse. It never means guess.
        /// </remarks>
        private static string? Convert(ITypeSymbol from, ITypeSymbol to, string expression, PlanContext context)
        {
            // Identical, or the destination is a base type of the source.
            if (SymbolEqualityComparer.Default.Equals(from, to)) return expression;

            var fromNullable = Underlying(from);
            var toNullable = Underlying(to);

            // int into int?, and the reference conversions the CLR already allows.
            if (fromNullable is null && toNullable != null
                && SymbolEqualityComparer.Default.Equals(from, toNullable))
            {
                return expression;
            }

            if (IsReferenceAssignable(from, to)) return expression;

            // A nested complex object. Excluded on both sides: a string is a class but mapping into
            // or out of it means formatting or parsing, not member by member mapping.
            if (IsMappable(from) && to.TypeKind == TypeKind.Class && !IsString(to))
            {
                return NestedCall(from, to, expression, context);
            }

            // A collection. Only the concrete shapes are emitted, because the loop is where the
            // speed is and a loop over an unknown shape cannot be indexed.
            if (CollectionCall(from, to, expression, context) is { } collection) return collection;

            if (IsString(to))
            {
                // Only an enum, and only because its invariant formatting and its ToString agree.
                // Everything else formats through CultureInfo.InvariantCulture in the cascade, and
                // re-deriving that here is exactly the drift this generator is on parole for.
                var sourceEnum = fromNullable ?? from;
                if (sourceEnum.TypeKind == TypeKind.Enum)
                {
                    return fromNullable is null
                        ? $"{expression}.ToString()"
                        : $"({expression}.HasValue ? {expression}.Value.ToString() : string.Empty)";
                }

                return null;
            }

            var fromEnum = fromNullable ?? from;
            var toEnum = toNullable ?? to;

            if (fromEnum.TypeKind == TypeKind.Enum && toEnum.TypeKind != TypeKind.Enum
                && IsIntOrLong(toEnum) && fromNullable is null && toNullable is null)
            {
                return $"({Full(to)}){expression}";
            }

            // One enum into a different enum, matched by member name. The switch is resolved here,
            // while the file is generated, so the emitted code is a jump rather than a lookup.
            if (fromEnum.TypeKind == TypeKind.Enum && toEnum.TypeKind == TypeKind.Enum
                && !SymbolEqualityComparer.Default.Equals(fromEnum, toEnum))
            {
                return EnumCall(fromEnum, toEnum, from, to, expression, context);
            }

            if (Widening(fromEnum, toEnum))
            {
                if (fromNullable != null && toNullable != null) return $"({Full(to)}){expression}";
                if (fromNullable != null)
                {
                    return $"({expression}.HasValue ? ({Full(to)}){expression}.Value : default({Full(to)}))";
                }

                return $"({Full(to)}){expression}";
            }

            // DateTime into DateTimeOffset. The framework defines the conversion implicitly, and the
            // cascade leans on that, so the emitted cast is the same operator.
            if (IsNamed(fromEnum, "System.DateTime") && IsNamed(toEnum, "System.DateTimeOffset"))
            {
                if (fromNullable != null && toNullable != null)
                {
                    return $"({expression}.HasValue ? (global::System.DateTimeOffset?)(global::System.DateTimeOffset){expression}.Value : null)";
                }

                if (fromNullable != null)
                {
                    return $"({expression}.HasValue ? (global::System.DateTimeOffset){expression}.Value : default(global::System.DateTimeOffset))";
                }

                if (toNullable != null) return $"(global::System.DateTimeOffset?)(global::System.DateTimeOffset){expression}";

                return $"(global::System.DateTimeOffset){expression}";
            }

            // A nullable source into its non-nullable counterpart: null becomes the destination
            // default, which is what Expression.Coalesce does in the cascade.
            if (fromNullable != null && SymbolEqualityComparer.Default.Equals(fromNullable, to))
            {
                return $"({expression} ?? default({Full(to)}))";
            }

            return null;
        }

        /// <summary>Plans a nested pair and returns the call that maps it, or null if it cannot be.</summary>
        /// <remarks>
        /// A pair already open further up the stack is a cycle, and a cycle is refused. Generated
        /// code has no depth counter, so following one recurses until the stack ends, which is what
        /// the nearest competitor does: it aborts the process. The engine stops at a ceiling and
        /// returns, so emitting a mapper that dies is a lane that disagrees in the worst possible
        /// way. Refusing keeps the pair on the engine, which terminates.
        /// </remarks>
        private static string? NestedCall(ITypeSymbol from, ITypeSymbol to, string expression, PlanContext context)
        {
            if (from is not INamedTypeSymbol source || to is not INamedTypeSymbol destination) return null;
            if (StructuralRefusal(source, destination) != null) return null;

            var key = Key(source, destination, HelperKind.Object);

            if (context.IsCyclic(key)) return null;

            if (context.TryGetHelper(key, out var existing))
            {
                return $"{existing.Name}({expression})!";
            }

            var helper = context.Begin(key, HelperKind.Object, Full(source), Full(destination));
            var assignments = PlanMembers(source, destination, context, out _);
            context.End(key);

            if (assignments is null || assignments.Count == 0)
            {
                context.Abandon(key);
                return null;
            }

            helper.Assignments.AddRange(assignments);
            return $"{helper.Name}({expression})!";
        }

        /// <summary>Plans a collection member and returns the call that maps it, or null.</summary>
        /// <remarks>
        /// Deliberately narrow: a source that is a <c>List&lt;T&gt;</c> or an array, into a
        /// destination <c>List&lt;U&gt;</c>. Those are the shapes that can be indexed, and indexing
        /// is the entire reason the emitted loop reaches hand written speed. Anything else is
        /// refused and stays on the engine rather than being emitted as a slower loop, because a
        /// generated mapper that is not faster is not worth the second implementation.
        /// </remarks>
        private static string? CollectionCall(ITypeSymbol from, ITypeSymbol to, string expression, PlanContext context)
        {
            var sourceElement = IndexableElement(from, out var sourceIsArray);
            if (sourceElement is null) return null;

            if (to is not INamedTypeSymbol { IsGenericType: true } list) return null;
            if (list.ConstructedFrom.ToDisplayString() != "System.Collections.Generic.List<T>") return null;

            var destElement = list.TypeArguments[0];
            var key = Key(sourceElement, destElement, HelperKind.List) + (sourceIsArray ? "[]" : "");

            if (context.TryGetHelper(key, out var existing))
            {
                return $"{existing.Name}({expression})";
            }

            var helper = context.Begin(key, HelperKind.List, Full(sourceElement), Full(destElement));
            helper.SourceIsArray = sourceIsArray;
            helper.ElementSourceType = Full(sourceElement);
            helper.ElementDestinationType = Full(destElement);

            var element = Convert(sourceElement, destElement, "item", context);
            context.End(key);

            if (element is null)
            {
                context.Abandon(key);
                return null;
            }

            helper.ElementExpression = element;
            return $"{helper.Name}({expression})";
        }

        /// <summary>Plans the enum switch and returns the call, resolving names now rather than later.</summary>
        private static string EnumCall(
            ITypeSymbol fromEnum, ITypeSymbol toEnum, ITypeSymbol from, ITypeSymbol to,
            string expression, PlanContext context)
        {
            var key = Key(fromEnum, toEnum, HelperKind.Enum);

            if (!context.TryGetHelper(key, out var helper))
            {
                helper = context.Begin(key, HelperKind.Enum, Full(fromEnum), Full(toEnum));
                context.End(key);

                var destNames = toEnum.GetMembers().OfType<IFieldSymbol>()
                    .Where(f => f.HasConstantValue)
                    .Select(f => f.Name)
                    .ToList();

                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var member in fromEnum.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue))
                {
                    var match = destNames.FirstOrDefault(
                        n => string.Equals(n, member.Name, StringComparison.OrdinalIgnoreCase));

                    if (match is null) continue;

                    // An alias declares two names for one value. A switch rejects a repeated label,
                    // so the first name wins, which is the one declaration order gives, matching
                    // what Enum.GetNames hands the runtime rule.
                    if (!seen.Add(member.ConstantValue?.ToString() ?? member.Name)) continue;

                    helper.Assignments.Add(new Assignment(match, member.Name, member.Name));
                }
            }

            var fromNullable = Underlying(from);
            var toNullable = Underlying(to);

            if (fromNullable is null) return toNullable is null ? $"{helper.Name}({expression})" : $"({Full(to)}){helper.Name}({expression})";

            var mapped = $"{helper.Name}({expression}.Value)";
            return toNullable is null
                ? $"({expression}.HasValue ? {mapped} : default({Full(to)}))"
                : $"({expression}.HasValue ? ({Full(to)}){mapped} : null)";
        }

        // ---- flattening -------------------------------------------------------------------------

        /// <summary>
        /// The path <c>CustomerAddressCity</c> resolves to, or null when there is not one.
        /// </summary>
        /// <remarks>
        /// A transcription of <c>PropertyConversion.Descend</c>, including the parts that look
        /// arbitrary. Longest prefix first, so <c>Address</c> beats <c>A</c> when both are properties
        /// and the name is <c>AddressCity</c>; without it the answer depends on the order members
        /// come back in. Four levels, matching <c>MaxFlattenDepth</c>. A whole name consumed at depth
        /// zero is an ordinary member match rather than flattening and is skipped here.
        /// </remarks>
        private const int MaxFlattenDepth = 4;

        private static List<IPropertySymbol>? FlattenedPath(IPropertySymbol destProp, List<IPropertySymbol> sourceProps)
        {
            var path = new List<IPropertySymbol>();
            return Descend(destProp.Name, destProp.Type, sourceProps, path, 0) ? path : null;
        }

        private static bool Descend(
            string remainingName, ITypeSymbol destType, List<IPropertySymbol> candidates,
            List<IPropertySymbol> path, int depth)
        {
            if (depth >= MaxFlattenDepth) return false;

            foreach (var candidate in candidates
                         .Where(p => remainingName.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(p => p.Name.Length))
            {
                var remainder = remainingName.Substring(candidate.Name.Length);

                if (remainder.Length == 0)
                {
                    if (depth == 0) continue;
                    if (!IsAssignableForFlattening(candidate.Type, destType)) continue;

                    path.Add(candidate);
                    return true;
                }

                if (!CanDescendInto(candidate.Type)) continue;

                path.Add(candidate);
                if (Descend(remainder, destType, ReadableProperties(candidate.Type), path, depth + 1))
                {
                    return true;
                }

                path.RemoveAt(path.Count - 1);
            }

            return false;
        }

        /// <summary>The expression for a flattened path, guarded at every hop that can be null.</summary>
        /// <remarks>
        /// Every intermediate is checked, because any of them can be null and the engine yields the
        /// destination default rather than throwing. A guard the engine has and this does not is a
        /// NullReferenceException thrown from inside generated code the consumer did not write.
        /// </remarks>
        private static string? FlattenedExpression(
            List<IPropertySymbol> path, ITypeSymbol destType, PlanContext context)
        {
            var leaf = path[path.Count - 1];
            var access = "source" + string.Concat(path.Select(p => $".@{p.Name}"));

            var converted = Convert(leaf.Type, destType, access, context);
            if (converted is null) return null;

            var guards = new List<string>();
            var walked = "source";

            for (var i = 0; i < path.Count - 1; i++)
            {
                walked += $".@{path[i].Name}";
                if (path[i].Type.IsReferenceType) guards.Add($"{walked} is null");
            }

            if (guards.Count == 0) return converted;

            return $"({string.Join(" || ", guards)} ? default({Full(destType)}) : {converted})";
        }

        private static bool CanDescendInto(ITypeSymbol type) =>
            type.TypeKind == TypeKind.Class && !IsString(type) && !IsEnumerable(type);

        private static bool IsAssignableForFlattening(ITypeSymbol from, ITypeSymbol to) =>
            SymbolEqualityComparer.Default.Equals(from, to)
            || IsReferenceAssignable(from, to)
            || Widening(Underlying(from) ?? from, Underlying(to) ?? to);

        // ---- symbols ----------------------------------------------------------------------------

        private static List<IPropertySymbol> ReadableProperties(ITypeSymbol type) =>
            Properties(type)
                .Where(p => !p.IsIndexer && p.GetMethod is { DeclaredAccessibility: Accessibility.Public })
                .ToList();

        private static IEnumerable<IPropertySymbol> Properties(ITypeSymbol type)
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

        /// <summary>The element type when the source can be indexed, otherwise null.</summary>
        private static ITypeSymbol? IndexableElement(ITypeSymbol type, out bool isArray)
        {
            isArray = false;

            if (type is IArrayTypeSymbol { IsSZArray: true } array)
            {
                isArray = true;
                return array.ElementType;
            }

            if (type is INamedTypeSymbol { IsGenericType: true } named
                && named.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.List<T>")
            {
                return named.TypeArguments[0];
            }

            return null;
        }

        private static ITypeSymbol? Underlying(ITypeSymbol type) =>
            type is INamedTypeSymbol { IsGenericType: true, OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n
                ? n.TypeArguments[0]
                : null;

        private static bool IsString(ITypeSymbol type) => type.SpecialType == SpecialType.System_String;

        private static bool IsNamed(ITypeSymbol type, string fullName) => type.ToDisplayString() == fullName;

        private static bool IsIntOrLong(ITypeSymbol type) =>
            type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64;

        private static bool IsEnumerable(ITypeSymbol type) =>
            type.SpecialType == SpecialType.System_String
            || type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable)
            || type.SpecialType == SpecialType.System_Collections_IEnumerable;

        /// <summary>A source worth mapping member by member: a class or interface that is not a string.</summary>
        private static bool IsMappable(ITypeSymbol type) =>
            type.TypeKind is TypeKind.Class or TypeKind.Interface && !IsString(type) && !IsEnumerable(type);

        private static bool IsReferenceAssignable(ITypeSymbol from, ITypeSymbol to)
        {
            if (to.TypeKind == TypeKind.Interface)
            {
                return from.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, to));
            }

            for (var current = from.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, to)) return true;
            }

            return false;
        }

        /// <summary>The widening table, transcribed from <c>PropertyConversion.WideningTargets</c>.</summary>
        private static bool Widening(ITypeSymbol from, ITypeSymbol to)
        {
            if (SymbolEqualityComparer.Default.Equals(from, to)) return false;
            if (!WideningTargets.TryGetValue(from.SpecialType, out var targets)) return false;
            return Array.IndexOf(targets, to.SpecialType) >= 0;
        }

        private static readonly Dictionary<SpecialType, SpecialType[]> WideningTargets = new()
        {
            [SpecialType.System_SByte] = new[] { SpecialType.System_Int16, SpecialType.System_Int32, SpecialType.System_Int64, SpecialType.System_Single, SpecialType.System_Double, SpecialType.System_Decimal },
            [SpecialType.System_Byte] = new[] { SpecialType.System_Int16, SpecialType.System_UInt16, SpecialType.System_Int32, SpecialType.System_UInt32, SpecialType.System_Int64, SpecialType.System_UInt64, SpecialType.System_Single, SpecialType.System_Double, SpecialType.System_Decimal },
            [SpecialType.System_Int16] = new[] { SpecialType.System_Int32, SpecialType.System_Int64, SpecialType.System_Single, SpecialType.System_Double, SpecialType.System_Decimal },
            [SpecialType.System_UInt16] = new[] { SpecialType.System_Int32, SpecialType.System_UInt32, SpecialType.System_Int64, SpecialType.System_UInt64, SpecialType.System_Single, SpecialType.System_Double, SpecialType.System_Decimal },
            [SpecialType.System_Int32] = new[] { SpecialType.System_Int64, SpecialType.System_Single, SpecialType.System_Double, SpecialType.System_Decimal },
            [SpecialType.System_UInt32] = new[] { SpecialType.System_Int64, SpecialType.System_UInt64, SpecialType.System_Single, SpecialType.System_Double, SpecialType.System_Decimal },
            [SpecialType.System_Int64] = new[] { SpecialType.System_Single, SpecialType.System_Double, SpecialType.System_Decimal },
            [SpecialType.System_UInt64] = new[] { SpecialType.System_Single, SpecialType.System_Double, SpecialType.System_Decimal },
            [SpecialType.System_Char] = new[] { SpecialType.System_UInt16, SpecialType.System_Int32, SpecialType.System_UInt32, SpecialType.System_Int64, SpecialType.System_UInt64, SpecialType.System_Single, SpecialType.System_Double, SpecialType.System_Decimal },
            [SpecialType.System_Single] = new[] { SpecialType.System_Double },
            [SpecialType.System_Decimal] = new[] { SpecialType.System_Double },
        };

        private static string Full(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static string Describe(ITypeSymbol type) => type.ToDisplayString();

        private static string Key(ITypeSymbol from, ITypeSymbol to, HelperKind kind) =>
            $"{kind}|{Full(from)}|{Full(to)}";

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

        // ---- plan data --------------------------------------------------------------------------

        /// <summary>One destination member and the C# that fills it, relative to a local called <c>source</c>.</summary>
        private readonly struct Assignment
        {
            internal Assignment(string destinationName, string expression, string sourceName)
            {
                DestinationName = destinationName;
                Expression = expression;
                SourceName = sourceName;
            }

            internal string DestinationName { get; }

            /// <summary>The expression, already carrying any conversion or helper call.</summary>
            internal string Expression { get; }

            /// <summary>The source member the value started from, for the obsolete check.</summary>
            internal string SourceName { get; }
        }

        private enum HelperKind { Object, List, Enum }

        /// <summary>A static method the emitted file needs: one per nested pair, one per element pair.</summary>
        private sealed class Helper
        {
            internal Helper(string name, HelperKind kind, string sourceType, string destinationType)
            {
                Name = name;
                Kind = kind;
                SourceType = sourceType;
                DestinationType = destinationType;
                Assignments = new List<Assignment>();
            }

            internal string Name { get; }
            internal HelperKind Kind { get; }
            internal string SourceType { get; }
            internal string DestinationType { get; }
            internal List<Assignment> Assignments { get; }

            internal string? ElementExpression { get; set; }
            internal string? ElementSourceType { get; set; }
            internal string? ElementDestinationType { get; set; }
            internal bool SourceIsArray { get; set; }
        }

        /// <summary>
        /// Helpers for one declared pair, named with that pair's prefix.
        /// </summary>
        /// <remarks>
        /// The prefix is per pair rather than per file because two declared pairs can each need a
        /// helper for a different type that happens to share a name, and a collision inside a
        /// generated file is a build break the consumer cannot fix. Sharing within a pair still
        /// happens: a Country reached through two paths is emitted once.
        /// </remarks>
        private sealed class PlanContext
        {
            private readonly Dictionary<string, Helper> _helpers = new(StringComparer.Ordinal);
            private readonly List<string> _open = new();
            private int _next;

            internal PlanContext(string prefix) => Prefix = prefix;

            internal string Prefix { get; }

            internal IEnumerable<Helper> Helpers => _helpers.Values;

            internal bool IsCyclic(string key) => _open.Contains(key);

            internal bool TryGetHelper(string key, out Helper helper) => _helpers.TryGetValue(key, out helper!);

            internal Helper Begin(string key, HelperKind kind, string sourceType, string destinationType)
            {
                var helper = new Helper($"{Prefix}{kind}{_next++}", kind, sourceType, destinationType);
                _helpers[key] = helper;
                _open.Add(key);
                return helper;
            }

            internal void End(string key) => _open.Remove(key);

            internal void Abandon(string key)
            {
                _helpers.Remove(key);
                _open.Remove(key);
            }
        }

        private sealed class MapPlan
        {
            internal MapPlan(
                INamedTypeSymbol source, INamedTypeSymbol destination,
                List<Assignment> assignments, List<Helper> helpers, string methodName)
            {
                Source = source;
                Destination = destination;
                Assignments = assignments;
                Helpers = helpers;
                MethodName = methodName;
            }

            internal INamedTypeSymbol Source { get; }
            internal INamedTypeSymbol Destination { get; }
            internal List<Assignment> Assignments { get; }
            internal List<Helper> Helpers { get; }

            /// <summary>The static method both call sites invoke, so the body is written once.</summary>
            internal string MethodName { get; }
        }

        // ---- emitting -------------------------------------------------------------------------

        private static void Preamble(StringBuilder sb)
        {
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
            sb.AppendLine("// Nullable flow analysis cannot see that a helper only returns null when its argument was");
            sb.AppendLine("// null, so it warns on assignments that are already guarded. A consumer building with");
            sb.AppendLine("// TreatWarningsAsErrors would fail inside this file, which they did not write and cannot edit.");
            sb.AppendLine("#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8619");
            sb.AppendLine();
        }

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
            Preamble(sb);

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
                var src = Full(plan.Source);
                var dst = Full(plan.Destination);

                // requiresDepthTracking is false and stays false: a pair whose graph contains a
                // cycle is refused at planning time, so nothing emitted here can recurse forever.
                sb.AppendLine($"            global::Mapsicle.Mapper.RegisterGenerated<{src}, {dst}>(");
                sb.AppendLine($"                {plan.MethodName},");
                sb.AppendLine("                requiresDepthTracking: false);");
                sb.AppendLine();
            }

            sb.AppendLine("        }");

            foreach (var plan in plans)
            {
                sb.AppendLine();
                EmitBody(sb, plan.MethodName, Full(plan.Source), Full(plan.Destination), plan.Assignments, nullable: false);

                foreach (var helper in plan.Helpers)
                {
                    sb.AppendLine();
                    EmitHelper(sb, helper);
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static void EmitBody(
            StringBuilder sb, string name, string sourceType, string destType,
            List<Assignment> assignments, bool nullable)
        {
            var question = nullable ? "?" : "";
            sb.AppendLine($"        internal static {destType}{question} {name}({sourceType}{question} source)");
            sb.AppendLine("        {");
            if (nullable) sb.AppendLine("            if (source is null) return null;");
            sb.AppendLine($"            return new {destType}");
            sb.AppendLine("            {");

            for (var i = 0; i < assignments.Count; i++)
            {
                var comma = i == assignments.Count - 1 ? "" : ",";
                sb.AppendLine($"                @{assignments[i].DestinationName} = {assignments[i].Expression}{comma}");
            }

            sb.AppendLine("            };");
            sb.AppendLine("        }");
        }

        /// <summary>
        /// A collection helper, keeping the concrete type and indexing it.
        /// </summary>
        /// <remarks>
        /// The parameter is <c>List&lt;T&gt;</c> or <c>T[]</c>, never an interface. Widening it makes
        /// foreach call <c>IEnumerable&lt;T&gt;.GetEnumerator()</c>, which boxes the struct
        /// enumerator on the heap and dispatches every MoveNext and Current through an interface.
        /// Measured on four collections holding five items, that costs 38.5 ns and 120 bytes against
        /// the indexed form, and it is the entire gap between the nearest competitor and hand
        /// written code. The destination is pre-sized for the same reason.
        /// </remarks>
        private static void EmitHelper(StringBuilder sb, Helper helper)
        {
            if (helper.Kind == HelperKind.Enum)
            {
                EmitEnumSwitch(sb, helper);
                return;
            }

            if (helper.Kind == HelperKind.Object)
            {
                EmitBody(sb, helper.Name, helper.SourceType, helper.DestinationType, helper.Assignments, nullable: true);
                return;
            }

            var element = helper.ElementSourceType!;
            var destElement = helper.ElementDestinationType!;
            var parameter = helper.SourceIsArray ? $"{element}[]" : $"global::System.Collections.Generic.List<{element}>";
            var count = helper.SourceIsArray ? "Length" : "Count";

            sb.AppendLine($"        internal static global::System.Collections.Generic.List<{destElement}> {helper.Name}({parameter}? source)");
            sb.AppendLine("        {");
            sb.AppendLine($"            if (source is null) return new global::System.Collections.Generic.List<{destElement}>();");
            sb.AppendLine();
            sb.AppendLine($"            var target = new global::System.Collections.Generic.List<{destElement}>(source.{count});");
            sb.AppendLine($"            for (var i = 0; i < source.{count}; i++)");
            sb.AppendLine("            {");
            sb.AppendLine($"                var item = source[i];");
            sb.AppendLine($"                target.Add({helper.ElementExpression});");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            return target;");
            sb.AppendLine("        }");
        }

        /// <summary>The enum switch, with every arm resolved while the file was generated.</summary>
        private static void EmitEnumSwitch(StringBuilder sb, Helper helper)
        {
            sb.AppendLine($"        internal static {helper.DestinationType} {helper.Name}({helper.SourceType} value)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (value)");
            sb.AppendLine("            {");

            foreach (var arm in helper.Assignments)
            {
                sb.AppendLine($"                case {helper.SourceType}.@{arm.SourceName}: return {helper.DestinationType}.@{arm.DestinationName};");
            }

            sb.AppendLine($"                default: return default({helper.DestinationType});");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
        }

        /// <summary>
        /// Emits a <c>MapTo</c> extension per declared source type, so a declared pair skips the lookup.
        /// </summary>
        /// <remarks>
        /// Registering a generated mapper removes the compile, not the route to it. Measured on an
        /// idle machine, the same five member copy costs 12.3 ns reached through the typed overload
        /// and 28.0 ns reached through <c>MapTo&lt;TDest&gt;(object)</c>, because that route pays a
        /// GetType, a tuple key, a dictionary probe for the delegate, a second probe to decide on
        /// depth tracking, a delegate cast and an invoke. Hand written is 9.5 ns, and this extension
        /// is 10.2.
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
            Preamble(sb);

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
            var bySource = plans.GroupBy(p => Full(p.Source)).ToList();

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
                    var dst = Full(plan.Destination);

                    // One static call, not a copy of the body. The body lives in one place so the
                    // registered delegate and this call site cannot drift, and the JIT inlines it.
                    sb.AppendLine($"{indent}        if (typeof(TDest) == typeof({dst}))");
                    sb.AppendLine($"{indent}        {{");
                    sb.AppendLine($"{indent}            return (TDest)(object)global::Mapsicle.Generated.MapsicleGeneratedMappers.{plan.MethodName}(source);");
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
    }
}
