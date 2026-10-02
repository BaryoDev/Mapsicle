using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Mapsicle.SourceGen.Tests
{
    /// <summary>
    /// What the generator does when the project's language version cannot compile its output.
    /// </summary>
    public class LanguageVersionTests
    {
        private const string Declared = @"
using Mapsicle;
[assembly: MapsicleGenerate(typeof(LvBox), typeof(LvBoxDto))]
public class LvBox { public int X { get; set; } }
public class LvBoxDto { public int X { get; set; } }";

        private const string Undeclared = @"
public class LvBox { public int X { get; set; } }
public class LvBoxDto { public int X { get; set; } }";

        private static GeneratorDriverRunResult Run(LanguageVersion version, string source)
        {
            var options = new CSharpParseOptions(version);

            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));

            var compilation = CSharpCompilation.Create(
                "LanguageVersionProbe",
                new[] { CSharpSyntaxTree.ParseText(source, options) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return CSharpGeneratorDriver
                .Create(new[] { new MapperGenerator().AsSourceGenerator() }, parseOptions: options)
                .RunGenerators(compilation)
                .GetRunResult();
        }

        [Theory]
        [InlineData(LanguageVersion.CSharp7_3, "7.3")]
        [InlineData(LanguageVersion.CSharp8, "8.0")]
        public void BelowCSharp9NothingIsEmittedAndOneWarningSaysWhy(LanguageVersion version, string shown)
        {
            var result = Run(version, Declared);

            Assert.Empty(result.GeneratedTrees);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("MSG003", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Contains($"compiles with C# {shown}", diagnostic.GetMessage());
            Assert.Contains("<LangVersion>9.0</LangVersion>", diagnostic.GetMessage());
        }

        [Fact]
        public void BelowCSharp9AskingForEveryPairGetsTheSameSingleWarning()
        {
            var result = Run(LanguageVersion.CSharp7_3, "[assembly: Mapsicle.MapsicleGenerateAll]" + Undeclared);

            Assert.Empty(result.GeneratedTrees);
            Assert.Equal("MSG003", Assert.Single(result.Diagnostics).Id);
        }

        [Fact]
        public void AtCSharp9ThePairIsEmittedWithNoDiagnostic()
        {
            var result = Run(LanguageVersion.CSharp9, Declared);

            Assert.Empty(result.Diagnostics);
            Assert.Equal(2, result.GeneratedTrees.Length);
        }

        [Fact]
        public void AnOldLanguageVersionWithNothingDeclaredIsSilent()
        {
            var result = Run(LanguageVersion.CSharp7_3, Undeclared);

            Assert.Empty(result.Diagnostics);
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void AProjectThatDeclaresTheAttributeItselfGetsNoSecondCopy()
        {
            const string ownCopy = @"
namespace System.Runtime.CompilerServices
{
    internal sealed class ModuleInitializerAttribute : System.Attribute { }
}";

            var result = Run(LanguageVersion.CSharp9, Declared + ownCopy);

            Assert.Equal(2, result.GeneratedTrees.Length);
            Assert.DoesNotContain(
                result.GeneratedTrees,
                tree => tree.ToString().Contains("class ModuleInitializerAttribute"));
        }

        [Fact]
        public void ATargetThatHasTheAttributeGetsNoSecondCopy()
        {
            var result = Run(LanguageVersion.CSharp9, Declared);

            Assert.DoesNotContain(
                result.GeneratedTrees,
                tree => tree.ToString().Contains("class ModuleInitializerAttribute"));
            Assert.Contains(
                result.GeneratedTrees,
                tree => tree.ToString().Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]"));
        }
    }
}
