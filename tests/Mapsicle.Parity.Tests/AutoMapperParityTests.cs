using System;
using System.Collections.Generic;
using AutoMapper;
using Xunit;
using Mapsicle;

namespace Mapsicle.Parity.Tests
{
    /// <summary>
    /// The same inputs through AutoMapper and through Mapsicle, asserting Mapsicle is never worse.
    /// </summary>
    /// <remarks>
    /// The claim this project is sold on is that Mapsicle can be trusted at least as much as
    /// AutoMapper. That claim was checked once, by hand, during an audit, and then not again. Four
    /// behaviours failed it at the time and one of them crashed the host process where AutoMapper
    /// stayed up, so it is not a claim that can be left to periodic review.
    ///
    /// AutoMapper needs an explicit CreateMap where Mapsicle works by convention, so what is
    /// compared is the result once a map exists, not the effort of configuring one.
    ///
    /// The bar for a row is: Mapsicle matches AutoMapper's answer, or fails closed in a way stated
    /// here on purpose. Never worse on a hostile or malformed input.
    /// </remarks>
    [Collection("Parity")]
    public class AutoMapperParityTests
    {
        // ---- The gap that inverted the pitch ----------------------------------------------------

        [Fact]
        public void CyclicGraphThroughACollection_NeitherMapperCrashes()
        {
            // AutoMapper degrades to a returned object. Mapsicle used to terminate the process with
            // an uncatchable StackOverflowException, so this row was not "slower" or "different",
            // it was the one behaviour that made the comparison table false.
            var root = new Node { Name = "root", Children = new List<Node>() };
            var child = new Node { Name = "child", Children = new List<Node>() };
            child.Children.Add(root);
            root.Children.Add(child);

            var automapper = NewConfig(c => c.CreateMap<Node, NodeDto>()).Map<NodeDto>(root);
            Mapper.ClearCache();
            var mapsicle = ((object)root).MapTo<NodeDto>();

            Assert.NotNull(automapper);
            Assert.NotNull(mapsicle);
            Assert.Equal(automapper!.Name, mapsicle!.Name);
        }

        // ---- Conversions where the two must agree ------------------------------------------------

        [Fact]
        public void IntIntoARecordLongParameter_BothProduceTheValue()
        {
            var source = new IntBox { Value = 42 };

            var automapper = NewConfig(c => c.CreateMap<IntBox, CtorRecord>()).Map<CtorRecord>(source);
            Mapper.ClearCache();
            var mapsicle = ((object)source).MapTo<CtorRecord>();

            Assert.Equal(42L, automapper.Value);
            Assert.Equal(automapper.Value, mapsicle!.Value);
        }

        [Fact]
        public void ListIntoAHashSet_BothArePopulated()
        {
            var source = new List<string> { "a", "b" };

            var automapper = NewConfig(_ => { }).Map<HashSet<string>>(source);
            Mapper.ClearCache();
            var mapsicle = ((object)source).MapTo<HashSet<string>>();

            Assert.Equal(2, automapper.Count);
            Assert.Equal(automapper.Count, mapsicle!.Count);
        }

        [Fact]
        public void InPlaceMap_BothApplyTheSameConversions()
        {
            var source = new MixedSource { Number = 42, Colour = Colour.Green, Optional = 7 };

            var automapperDest = new MixedDest();
            NewConfig(c => c.CreateMap<MixedSource, MixedDest>()).Map(source, automapperDest);

            Mapper.ClearCache();
            var mapsicleDest = ((object)source).Map(new MixedDest());

            Assert.Equal(42L, automapperDest.Number);
            Assert.Equal(automapperDest.Number, mapsicleDest.Number);
            Assert.Equal(automapperDest.Colour, mapsicleDest.Colour);
            Assert.Equal(automapperDest.Optional, mapsicleDest.Optional);
        }

        [Fact]
        public void IntIntoLong_BothWiden()
        {
            // Already at parity before the audit. It is here as the positive control: a parity
            // suite in which every row is a known gap passes just as well when the harness itself
            // is broken.
            var source = new IntBox { Value = 42 };

            var automapper = NewConfig(c => c.CreateMap<IntBox, LongBox>()).Map<LongBox>(source);
            Mapper.ClearCache();
            var mapsicle = source.MapTo<IntBox, LongBox>();

            Assert.Equal(42L, automapper.Value);
            Assert.Equal(automapper.Value, mapsicle!.Value);
        }

        // ---- Hostile and malformed input ---------------------------------------------------------

        [Fact]
        public void ANullSourceMemberIntoAString_NeitherThrows()
        {
            var source = new TextSource { Value = null };

            var automapper = NewConfig(c => c.CreateMap<TextSource, TextDest>()).Map<TextDest>(source);
            Mapper.ClearCache();
            var mapsicle = ((object)source).MapTo<TextDest>();

            Assert.Null(automapper.Value);
            Assert.Equal(automapper.Value, mapsicle!.Value);
        }

        [Fact]
        public void ADeeplyNestedGraph_NeitherCrashes()
        {
            var root = new Node { Name = "0", Children = new List<Node>() };
            var current = root;
            for (int i = 1; i <= 200; i++)
            {
                var next = new Node { Name = i.ToString(), Children = new List<Node>() };
                current.Children.Add(next);
                current = next;
            }

            var automapper = NewConfig(c => c.CreateMap<Node, NodeDto>()).Map<NodeDto>(root);
            Mapper.ClearCache();
            var mapsicle = ((object)root).MapTo<NodeDto>();

            Assert.NotNull(automapper);
            Assert.NotNull(mapsicle);
        }

        [Fact]
        public void AMixedRuntimeTypeCollection_NeitherThrows()
        {
            var source = new List<Animal> { new Dog { Name = "rex" }, new Cat { Name = "tom" } };

            var automapper = NewConfig(c =>
            {
                c.CreateMap<Animal, AnimalDto>();
                c.CreateMap<Dog, AnimalDto>();
                c.CreateMap<Cat, AnimalDto>();
            }).Map<List<AnimalDto>>(source);

            Mapper.ClearCache();
            var mapsicle = ((System.Collections.IEnumerable)source).MapTo<AnimalDto>();

            Assert.Equal(2, automapper.Count);
            Assert.Equal(automapper.Count, mapsicle.Count);
            Assert.Equal(automapper[0].Name, mapsicle[0].Name);
            Assert.Equal(automapper[1].Name, mapsicle[1].Name);
        }

        private static IMapper NewConfig(Action<IMapperConfigurationExpression> configure) =>
            new MapperConfiguration(configure, new LoggerFactoryStub()).CreateMapper();

        private sealed class LoggerFactoryStub : Microsoft.Extensions.Logging.ILoggerFactory
        {
            public void AddProvider(Microsoft.Extensions.Logging.ILoggerProvider provider) { }
            public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            public void Dispose() { }
        }

        public class IntBox { public int Value { get; set; } }
        public class LongBox { public long Value { get; set; } }
        public class CtorRecord
        {
            public CtorRecord(long value) => Value = value;
            public long Value { get; }
        }

        public enum Colour { Red = 0, Green = 1 }
        public class MixedSource { public int Number { get; set; } public Colour Colour { get; set; } public int? Optional { get; set; } }
        public class MixedDest { public long Number { get; set; } public int Colour { get; set; } public int Optional { get; set; } }

        public class TextSource { public string? Value { get; set; } }
        public class TextDest { public string? Value { get; set; } }

        public class Node { public string? Name { get; set; } public List<Node>? Children { get; set; } }
        public class NodeDto { public string? Name { get; set; } public List<NodeDto>? Children { get; set; } }

        public class Animal { public string? Name { get; set; } }
        public class Dog : Animal { }
        public class Cat : Animal { }
        public class AnimalDto { public string? Name { get; set; } }
    }
}
