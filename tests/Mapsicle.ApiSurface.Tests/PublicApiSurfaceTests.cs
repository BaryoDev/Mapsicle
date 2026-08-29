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
                    return $"{Modifiers(m)}{TypeName(m.ReturnType)} {m.Name}{Generics(m)}({Parameters(m)})";

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

        private static string Modifiers(MethodBase m) =>
            (m.IsStatic ? "static " : "") + (m.IsAbstract ? "abstract " : "") + (m.IsVirtual && !m.IsAbstract && !m.IsFinal ? "virtual " : "");

        private static string Generics(MethodInfo m) =>
            m.IsGenericMethodDefinition ? "<" + string.Join(",", m.GetGenericArguments().Select(a => a.Name)) + ">" : "";

        private static string Parameters(MethodBase m) =>
            string.Join(", ", m.GetParameters().Select(p => TypeName(p.ParameterType) + " " + p.Name));

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
