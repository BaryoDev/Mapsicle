using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Mapsicle.NetStandard.Tests
{
    /// <summary>
    /// The core behaviours, run against the netstandard2.0 assembly rather than the net8.0 one.
    /// </summary>
    /// <remarks>
    /// This project exists because the netstandard2.0 build had never been executed. It was
    /// compiled, packed and published, and every test in the repository resolved the net8.0 asset
    /// instead, so the binary that .NET Framework consumers actually load was covered by nothing.
    ///
    /// The assertions here are deliberately the same ones the main suite makes, because the
    /// question is not whether these behaviours are correct. It is whether they are still correct
    /// when compiled for a target that lacks parts of the modern BCL.
    /// </remarks>
    public class NetStandardSurfaceTests
    {
        [Fact]
        public void TheAssemblyUnderTest_IsTheNetStandardBuild()
        {
            // Without this the whole project is theatre: if the reference silently resolved the
            // net8.0 asset, every test below would pass while proving nothing about ns2.0.
            var assembly = typeof(Mapper).Assembly;
            var target = assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();

            Assert.NotNull(target);
            Assert.Contains(".NETStandard,Version=v2.0", target!.FrameworkName);
        }

        [Fact]
        public void WideningConversion_Works()
        {
            Mapper.ClearCache();
            var result = new NsIntBox { Value = 42 }.MapTo<NsIntBox, NsLongBox>();
            Assert.Equal(42L, result!.Value);
        }

        [Fact]
        public void InPlaceMap_UsesTheSameCascade()
        {
            Mapper.ClearCache();
            var destination = ((object)new NsIntBox { Value = 42 }).Map(new NsLongBox());
            Assert.Equal(42L, destination.Value);
        }

        [Fact]
        public void RecordStyleConstructor_ReceivesWidenedValues()
        {
            Mapper.ClearCache();
            var result = ((object)new NsIntBox { Value = 42 }).MapTo<NsCtorTarget>();
            Assert.Equal(42L, result!.Value);
        }

        [Fact]
        public void NullReferenceToString_YieldsNull()
        {
            Mapper.ClearCache();
            var result = ((object)new NsTextSource { Value = null }).MapTo<NsTextDest>();
            Assert.Null(result!.Value);
        }

        [Fact]
        public void NumberToString_IsCultureInvariant()
        {
            Mapper.ClearCache();
            var result = ((object)new NsDecimalSource { Value = 1234.5m }).MapTo<NsTextDest>();
            Assert.Equal("1234.5", result!.Value);
        }

        [Fact]
        public void ACycleThroughACollection_DoesNotCrashTheProcess()
        {
            Mapper.ClearCache();

            var root = new NsNode { Name = "root", Children = new List<NsNode>() };
            var child = new NsNode { Name = "child", Children = new List<NsNode>() };
            child.Children.Add(root);
            root.Children.Add(child);

            var result = ((object)root).MapTo<NsNodeDto>();

            Assert.NotNull(result);
            Assert.Equal("root", result!.Name);
        }

        [Fact]
        public void ListIntoHashSet_IsPopulated()
        {
            Mapper.ClearCache();
            var result = ((object)new List<string> { "a", "b" }).MapTo<HashSet<string>>();
            Assert.Equal(2, result!.Count);
        }

        [Fact]
        public void DictionaryPath_DropsAWrongTypedValue()
        {
            var dict = new Dictionary<string, object?> { ["Value"] = "123" };
            var result = dict.MapTo<NsIntBox>();
            Assert.Equal(0, result!.Value);
        }

        [Fact]
        public void IgnoreMap_IsHonoured()
        {
            Mapper.ClearCache();
            var result = ((object)new NsSecretSource { Value = 1, Secret = 99 }).MapTo<NsSecretDest>();
            Assert.Equal(1, result!.Value);
            Assert.Equal(0, result.Secret);
        }

        public class NsIntBox { public int Value { get; set; } }
        public class NsLongBox { public long Value { get; set; } }
        public class NsTextSource { public string? Value { get; set; } }
        public class NsTextDest { public string? Value { get; set; } }
        public class NsDecimalSource { public decimal Value { get; set; } }

        public class NsCtorTarget
        {
            public NsCtorTarget(long value) => Value = value;
            public long Value { get; }
        }

        public class NsNode { public string? Name { get; set; } public List<NsNode>? Children { get; set; } }
        public class NsNodeDto { public string? Name { get; set; } public List<NsNodeDto>? Children { get; set; } }

        public class NsSecretSource { public int Value { get; set; } public int Secret { get; set; } }
        public class NsSecretDest { public int Value { get; set; } [IgnoreMap] public int Secret { get; set; } }
    }
}
