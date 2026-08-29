using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace Mapsicle.ApiSurface.Tests
{
    /// <summary>
    /// The public surface of every shipped package, compared against a committed baseline.
    /// </summary>
    /// <remarks>
    /// CLAUDE.md section 7 promises that within a major version no public member is removed and no
    /// signature changes. That is a real promise to everyone compiling against these packages, and
    /// nothing checked it, so a signature change reached NuGet whenever someone made one.
    ///
    /// The baseline is captured in 2.0.0 because a major version is the window where breaks are
    /// allowed. Capturing it later would have recorded whatever happened to ship, including
    /// anything that shipped by accident.
    ///
    /// Adding to the surface is a visible diff in the approved file, which is the point: it makes
    /// growing the API a decision someone reviews rather than something that happens. Removing or
    /// changing a member fails until the file is edited deliberately.
    /// </remarks>
    public class PublicApiSurfaceTests
    {
        public static TheoryData<string> Assemblies()
        {
            var data = new TheoryData<string>();
            foreach (var name in new[]
            {
                "Mapsicle",
                "Mapsicle.AspNetCore",
                "Mapsicle.Audit",
                "Mapsicle.Caching",
                "Mapsicle.Dapper",
                "Mapsicle.DataAnnotations",
                "Mapsicle.DependencyInjection",
                "Mapsicle.EntityFramework",
                "Mapsicle.Fluent",
                "Mapsicle.Json",
                "Mapsicle.NamingConventions",
                "Mapsicle.Serilog",
                "Mapsicle.Validation",
            })
            {
                data.Add(name);
            }
            return data;
        }

        [Theory]
        [MemberData(nameof(Assemblies))]
        public void ThePublicSurface_MatchesTheApprovedBaseline(string assemblyName)
        {
            var actual = Describe(Assembly.Load(assemblyName));

            var approvedDir = Path.Combine(AppContext.BaseDirectory, "approved");
            Directory.CreateDirectory(approvedDir);

            var approvedPath = Path.Combine(approvedDir, assemblyName + ".approved.txt");
            var receivedPath = Path.Combine(approvedDir, assemblyName + ".received.txt");

            if (!File.Exists(approvedPath))
            {
                File.WriteAllText(receivedPath, actual);
                Assert.Fail(
                    $"No approved baseline for {assemblyName}. A received file was written next to it. " +
                    "Review it and commit it as the baseline if the surface is intended.");
            }

            var approved = Normalise(File.ReadAllText(approvedPath));

            if (approved != Normalise(actual))
            {
                File.WriteAllText(receivedPath, actual);

                Assert.Fail(BuildDiff(assemblyName, approved, Normalise(actual), receivedPath));
            }
        }

        private static string Normalise(string text) =>
            text.Replace("\r\n", "\n").TrimEnd() + "\n";

        private static string BuildDiff(string assemblyName, string approved, string actual, string receivedPath)
        {
            var approvedLines = approved.Split('\n').ToHashSet(StringComparer.Ordinal);
            var actualLines = actual.Split('\n').ToHashSet(StringComparer.Ordinal);

            var removed = approvedLines.Except(actualLines, StringComparer.Ordinal).Where(l => l.Length > 0).OrderBy(l => l, StringComparer.Ordinal).ToList();
            var added = actualLines.Except(approvedLines, StringComparer.Ordinal).Where(l => l.Length > 0).OrderBy(l => l, StringComparer.Ordinal).ToList();

            var message = new StringBuilder();
            message.AppendLine($"The public surface of {assemblyName} no longer matches its baseline.");

            if (removed.Count > 0)
            {
                message.AppendLine();
                message.AppendLine($"REMOVED or CHANGED ({removed.Count}). Within a major version this breaks every consumer");
                message.AppendLine("compiled against it. Add an overload and mark the old member [Obsolete] instead.");
                foreach (var line in removed.Take(40)) message.AppendLine("  - " + line);
                if (removed.Count > 40) message.AppendLine($"  ... and {removed.Count - 40} more");
            }

            if (added.Count > 0)
            {
                message.AppendLine();
                message.AppendLine($"ADDED ({added.Count}). Additions are fine; approve them by updating the baseline.");
                foreach (var line in added.Take(40)) message.AppendLine("  + " + line);
                if (added.Count > 40) message.AppendLine($"  ... and {added.Count - 40} more");
            }

            message.AppendLine();
            message.AppendLine($"If every change above is intended, copy the received file over the approved one:");
            message.AppendLine($"  {receivedPath}");

            return message.ToString();
        }

        /// <summary>
        /// Renders one assembly's public surface as sorted, stable text.
        /// </summary>
        /// <remarks>
        /// Reflection order is not defined, so everything is sorted before comparison. Without that
        /// the baseline would drift between runs and the gate would be reverted for being flaky,
        /// which is the usual way a real gate dies.
        /// </remarks>
        private static string Describe(Assembly assembly)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            var blocks = new List<string>();

            foreach (var type in assembly.GetExportedTypes())
            {
                var members = new List<string>();
                foreach (var member in type.GetMembers(flags))
                {
                    var text = Member(type, member);
                    if (text is not null) members.Add("    " + text);
                }

                // Sorted inside the type, and the types sorted between themselves, so a member
                // stays under the type that declares it. A globally sorted list would be just as
                // stable and useless to read: a reviewer could see that a line changed without
                // being able to tell what it belonged to.
                members.Sort(StringComparer.Ordinal);

                var header = Kind(type) + " " + TypeName(type);
                blocks.Add(members.Count == 0 ? header : header + "\n" + string.Join("\n", members));
            }

            blocks.Sort(StringComparer.Ordinal);
            return string.Join("\n", blocks) + "\n";
        }

        private static string Kind(Type t) =>
            t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : "class";

        private static string? Member(Type declaring, MemberInfo member)
        {
            switch (member)
            {
                case MethodInfo m:
                    if (!IsVisible(m)) return null;
                    if (m.IsSpecialName) return null; // property and event accessors, rendered with their property
                    return $"{Modifiers(m)}{TypeName(m.ReturnType)} {m.Name}{GenericParams(m)}({Parameters(m)}){GenericConstraints(m)}";

                case ConstructorInfo c:
                    if (!IsVisible(c)) return null;
                    return $"{(c.IsStatic ? "static " : "")}.ctor({Parameters(c)})";

                case PropertyInfo p:
                    {
                        var getter = p.GetMethod is not null && IsVisible(p.GetMethod);
                        var setter = p.SetMethod is not null && IsVisible(p.SetMethod);
                        if (!getter && !setter) return null;
                        var accessors = (getter ? "get;" : "") + (setter ? "set;" : "");
                        var owner = p.GetMethod ?? p.SetMethod!;
                        return $"{Modifiers(owner)}{TypeName(p.PropertyType)} {p.Name} {{ {accessors} }}";
                    }

                case FieldInfo f:
                    if (!f.IsPublic && !f.IsFamily && !f.IsFamilyOrAssembly) return null;
                    if (declaring.IsEnum) return $"= {f.Name}";
                    return $"{(f.IsStatic ? "static " : "")}{TypeName(f.FieldType)} {f.Name}";

                case EventInfo e:
                    if (e.AddMethod is null || !IsVisible(e.AddMethod)) return null;
                    return $"event {TypeName(e.EventHandlerType!)} {e.Name}";

                default:
                    return null;
            }
        }

        private static bool IsVisible(MethodBase m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;

        /// <summary>
        /// Accessibility and binding, both of which are part of the contract.
        /// </summary>
        /// <remarks>
        /// Accessibility is recorded because narrowing a member from public to protected breaks
        /// every consumer that called it while leaving the name and signature identical. Without it
        /// the baseline would render both the same way and the gate would pass a genuine break.
        /// </remarks>
        private static string Modifiers(MethodBase m) =>
            Accessibility(m)
            + (m.IsStatic ? "static " : "")
            + (m.IsAbstract ? "abstract " : "")
            + (m.IsVirtual && !m.IsAbstract && !m.IsFinal ? "virtual " : "");

        private static string Accessibility(MethodBase m) =>
            m.IsPublic ? "public " : m.IsFamilyOrAssembly ? "protected internal " : m.IsFamily ? "protected " : "";

        /// <summary>
        /// Generic parameters with their constraints.
        /// </summary>
        /// <remarks>
        /// A constraint is part of what a caller must satisfy, so adding <c>where T : new()</c> to an
        /// existing method breaks callers that were passing a type without a parameterless
        /// constructor. Recording only the parameter names would render that change invisible.
        /// </remarks>
        private static string GenericParams(MethodInfo m) =>
            m.IsGenericMethodDefinition
                ? "<" + string.Join(",", m.GetGenericArguments().Select(a => a.Name)) + ">"
                : "";

        // Rendered after the parameter list, where C# puts them, so the baseline reads as a
        // signature rather than as reflection output.
        private static string GenericConstraints(MethodInfo m) =>
            m.IsGenericMethodDefinition
                ? string.Join("", m.GetGenericArguments().Select(Constraints))
                : "";

        private static string Constraints(Type arg)
        {
            var parts = new List<string>();
            var attrs = arg.GenericParameterAttributes;

            if (attrs.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint)) parts.Add("class");
            if (attrs.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) parts.Add("struct");

            foreach (var c in arg.GetGenericParameterConstraints().OrderBy(TypeName, StringComparer.Ordinal))
            {
                if (c == typeof(ValueType)) continue; // implied by the struct constraint above
                parts.Add(TypeName(c));
            }

            if (attrs.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint)
                && !attrs.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint))
            {
                parts.Add("new()");
            }

            return parts.Count == 0 ? "" : $" where {arg.Name} : {string.Join(", ", parts)}";
        }

        /// <summary>
        /// Parameters, including the by-reference kind.
        /// </summary>
        /// <remarks>
        /// <c>ref</c>, <c>out</c> and <c>in</c> all render as the same by-ref type in reflection, so
        /// without this a change from <c>ref</c> to <c>out</c> produces an identical baseline while
        /// breaking every call site.
        /// </remarks>
        private static string Parameters(MethodBase m) =>
            string.Join(", ", m.GetParameters().Select(p =>
                ByRefKind(p) + TypeName(p.ParameterType) + " " + p.Name + (p.IsOptional ? " = default" : "")));

        private static string ByRefKind(ParameterInfo p)
        {
            if (!p.ParameterType.IsByRef) return "";
            if (p.IsOut) return "out ";
            if (p.GetCustomAttributes(typeof(System.Runtime.InteropServices.InAttribute), false).Length > 0) return "in ";
            return "ref ";
        }

        private static string TypeName(Type t)
        {
            if (t.IsByRef) return TypeName(t.GetElementType()!) + "&";
            if (t.IsArray) return TypeName(t.GetElementType()!) + "[]";
            if (!t.IsGenericType) return t.FullName ?? t.Name;

            var name = (t.GetGenericTypeDefinition().FullName ?? t.Name);
            var tick = name.IndexOf('`');
            if (tick >= 0) name = name.Substring(0, tick);
            return name + "<" + string.Join(",", t.GetGenericArguments().Select(TypeName)) + ">";
        }
    }
}
