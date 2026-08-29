using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// One conversion table, run through every public entry point, asserting they agree.
    /// </summary>
    /// <remarks>
    /// Three of the defects found in the 2.0.0 audit were the same shape: one entry point converts a
    /// value and another silently does not. In-place <c>Map</c> left widening, enum and nullable
    /// conversions untouched while <c>MapTo</c> performed them. Constructor and record parameters
    /// ignored widening while member init applied it. The dictionary path coerced values the object
    /// path dropped.
    ///
    /// None of them were visible to a suite organised by feature, because every one of those suites
    /// exercises a single door. A test that maps through <c>MapTo</c> proves nothing about
    /// <c>Map</c>. This file is organised by door instead: each case states the source value and the
    /// one correct answer, and every door has to produce it.
    ///
    /// A failure here names the door that disagrees and what it returned, because "expected 42, got
    /// 0" without knowing which of six entry points produced it is most of the debugging.
    ///
    /// Type names are unique to this file. Compiled mappers are cached in static fields keyed by
    /// (source runtime type, destination type), so reusing a pair from another file would exercise a
    /// delegate compiled earlier and prove nothing.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class EntryPointConformanceTests
    {
        // ---- The doors -------------------------------------------------------------------------

        /// <summary>
        /// Runs one source object through every entry point that can map it into a
        /// <typeparamref name="TDest"/> with a parameterless constructor, and asserts they all
        /// produce <paramref name="expected"/>.
        /// </summary>
        private static void AssertAllDoorsAgree<TSource, TDest>(
            TSource source,
            object? expected,
            Func<TDest, object?> read)
            where TSource : class
            where TDest : class, new()
        {
            var disagreements = new List<string>();

            void Check(string door, Func<object?> map)
            {
                // Each door gets a cold cache. A door that reads a delegate another door compiled
                // proves nothing about the door itself.
                Mapper.ClearCache();

                object? actual;
                try
                {
                    actual = map();
                }
                catch (Exception ex)
                {
                    disagreements.Add($"{door}: threw {ex.GetType().Name}: {ex.Message}");
                    return;
                }

                if (!Equals(expected, actual))
                {
                    disagreements.Add($"{door}: expected {Format(expected)}, got {Format(actual)}");
                }
            }

            Check("MapTo<TSource, TDest>(source)", () =>
            {
                var result = source.MapTo<TSource, TDest>();
                return result is null ? null : read(result);
            });

            Check("MapTo<T>(object)", () =>
            {
                var result = ((object)source).MapTo<TDest>();
                return result is null ? null : read(result);
            });

            Check("Map(source, destination)", () => read(((object)source).Map(new TDest())));

            Check("IMapperInstance.MapTo<T>(object)", () =>
            {
                using var mapper = MapperFactory.Create();
                var result = mapper.MapTo<TDest>(source);
                return result is null ? null : read(result);
            });

            Check("IMapperInstance.Map(source, destination)", () =>
            {
                using var mapper = MapperFactory.Create();
                return read(mapper.Map(source, new TDest()));
            });

            if (disagreements.Count > 0)
            {
                var message = new StringBuilder();
                message.AppendLine($"{disagreements.Count} of 5 entry points disagree on {typeof(TSource).Name} into {typeof(TDest).Name}:");
                foreach (var line in disagreements)
                {
                    message.AppendLine("  " + line);
                }
                Assert.Fail(message.ToString());
            }
        }

        private static string Format(object? value) =>
            value is null ? "null" : $"{value} ({value.GetType().Name})";

        // ---- Widening numerics -----------------------------------------------------------------
        // int 42 into a long landed as 0 through in-place Map and through record constructors.

        [Fact]
        public void IntToLong_EveryDoorWidens()
        {
            AssertAllDoorsAgree<ConfIntSource, ConfLongDest>(
                new ConfIntSource { Value = 42 }, 42L, d => d.Value);
        }

        [Fact]
        public void IntToDecimal_EveryDoorWidens()
        {
            AssertAllDoorsAgree<ConfIntSource, ConfDecimalDest>(
                new ConfIntSource { Value = 42 }, 42m, d => d.Value);
        }

        [Fact]
        public void IntToDouble_EveryDoorWidens()
        {
            AssertAllDoorsAgree<ConfIntSource, ConfDoubleDest>(
                new ConfIntSource { Value = 42 }, 42d, d => d.Value);
        }

        // ---- Enum ------------------------------------------------------------------------------

        [Fact]
        public void EnumToInt_EveryDoorConverts()
        {
            AssertAllDoorsAgree<ConfEnumSource, ConfIntDest>(
                new ConfEnumSource { Value = ConfColour.Green }, 1, d => d.Value);
        }

        // ---- Nullable --------------------------------------------------------------------------

        [Fact]
        public void NullableIntWithValue_ToNonNullable_EveryDoorConverts()
        {
            AssertAllDoorsAgree<ConfNullableIntSource, ConfIntDest>(
                new ConfNullableIntSource { Value = 7 }, 7, d => d.Value);
        }

        [Fact]
        public void NullableIntWithoutValue_ToNonNullable_EveryDoorYieldsDefault()
        {
            AssertAllDoorsAgree<ConfNullableIntSource, ConfIntDest>(
                new ConfNullableIntSource { Value = null }, 0, d => d.Value);
        }

        [Fact]
        public void NullableIntToNullableLong_EveryDoorCarriesNullThrough()
        {
            AssertAllDoorsAgree<ConfNullableIntSource, ConfNullableLongDest>(
                new ConfNullableIntSource { Value = null }, null, d => d.Value);
        }

        // ---- ToString --------------------------------------------------------------------------

        [Fact]
        public void NullReferenceToString_EveryDoorYieldsNull()
        {
            AssertAllDoorsAgree<ConfStringSource, ConfStringDest>(
                new ConfStringSource { Value = null }, null, d => d.Value);
        }

        [Fact]
        public void IntToString_EveryDoorFormats()
        {
            AssertAllDoorsAgree<ConfIntSource, ConfStringDest>(
                new ConfIntSource { Value = 42 }, "42", d => d.Value);
        }

        // ---- Nested objects --------------------------------------------------------------------

        [Fact]
        public void NestedComplexObject_EveryDoorMapsIt()
        {
            AssertAllDoorsAgree<ConfNestedSource, ConfNestedDest>(
                new ConfNestedSource { Item = new ConfInner { Name = "inner" } },
                "inner",
                d => d.Item?.Name);
        }

        // ---- Interface-typed source member ------------------------------------------------------
        // An interface is not IsClass, so the nested-object branch never fired and the member was
        // dropped as if it were absent from the source.

        [Fact]
        public void InterfaceTypedSourceMember_EveryDoorMapsIt()
        {
            AssertAllDoorsAgree<ConfInterfaceSource, ConfConcreteDest>(
                new ConfInterfaceSource { Item = new ConfThing { Name = "thing" } },
                "thing",
                d => d.Item?.Name);
        }

        // ---- Positive controls ------------------------------------------------------------------
        // A conformance suite where every row is a known gap passes just as well when the harness
        // itself is broken. These two pass before and after the fixes, deliberately.

        [Fact]
        public void SameType_EveryDoorCopies()
        {
            AssertAllDoorsAgree<ConfIntSource, ConfIntDest>(
                new ConfIntSource { Value = 99 }, 99, d => d.Value);
        }

        [Fact]
        public void NarrowingLongToInt_EveryDoorLeavesItUnmapped()
        {
            // Narrowing stays unmapped by design: long 5_000_000_000 does not fit an int, and a
            // mapper that silently truncates is worse than one that leaves the default.
            AssertAllDoorsAgree<ConfLongSource, ConfIntDest>(
                new ConfLongSource { Value = 5_000_000_000L }, 0, d => d.Value);
        }

        // ---- Reference cycles through a collection ----------------------------------------------
        // The predicate deciding whether a type needs cycle protection treated any IEnumerable
        // property as harmless, so a node holding a List of its own type was judged acyclic, depth
        // tracking was skipped, and a back edge overflowed the stack. StackOverflowException cannot
        // be caught in .NET, so this did not throw: it terminated the process. These tests running
        // at all is most of the assertion.

        [Fact]
        public void CollectionCycle_TypedMapTo_DoesNotCrashTheProcess()
        {
            Mapper.ClearCache();

            var root = NewCycle();
            var result = root.MapTo<ConfNode, ConfNodeDto>();

            Assert.NotNull(result);
            Assert.Equal("root", result!.Name);
        }

        [Fact]
        public void CollectionCycle_UntypedMapTo_DoesNotCrashTheProcess()
        {
            Mapper.ClearCache();

            var result = ((object)NewCycle()).MapTo<ConfNodeDto>();

            Assert.NotNull(result);
            Assert.Equal("root", result!.Name);
        }

        [Fact]
        public void CollectionCycle_InstanceMapper_DoesNotCrashTheProcess()
        {
            using var mapper = MapperFactory.Create();

            var result = mapper.MapTo<ConfNodeDto>(NewCycle());

            Assert.NotNull(result);
            Assert.Equal("root", result!.Name);
        }

        [Fact]
        public void DeepAcyclicCollectionGraph_StillMapsAllTheWayDown()
        {
            // The positive control for the fix above. Cycle protection that works by refusing to
            // descend would also pass the three tests above while quietly truncating legitimate
            // data, which is a worse defect than the crash because nothing announces it.
            Mapper.ClearCache();

            var root = new ConfNode { Name = "0", Children = new List<ConfNode>() };
            var current = root;
            for (int depth = 1; depth <= 5; depth++)
            {
                var next = new ConfNode { Name = depth.ToString(), Children = new List<ConfNode>() };
                current.Children.Add(next);
                current = next;
            }

            var result = ((object)root).MapTo<ConfNodeDto>();

            var walked = result;
            for (int depth = 1; depth <= 5; depth++)
            {
                Assert.NotNull(walked);
                Assert.Single(walked!.Children!);
                walked = walked.Children![0];
                Assert.Equal(depth.ToString(), walked.Name);
            }
        }

        // ---- [MapFrom] naming a property that does not exist -------------------------------------
        // Found while collapsing the duplicated binding loops, not by the audit. The strongly-typed
        // path resolved [MapFrom] with its own inline scan that matched only the named property,
        // while every other door fell back to the destination member's own name. So the same two
        // types produced a value through one overload and null through another, decided by which
        // one the caller happened to reach for.

        [Fact]
        public void MapFromNamingAMissingProperty_FallsBackToTheMemberName_OnEveryDoor()
        {
            AssertAllDoorsAgree<ConfMapFromSource, ConfMapFromDest>(
                new ConfMapFromSource { Name = "value" }, "value", d => d.Name);
        }

        [Fact]
        public void MapFromNamingARealProperty_StillWins_OnEveryDoor()
        {
            // The positive control. Falling back unconditionally would satisfy the test above while
            // breaking [MapFrom] for everyone using it as intended.
            AssertAllDoorsAgree<ConfRenameSource, ConfRenameDest>(
                new ConfRenameSource { Original = "from-attribute", Renamed = "from-name" },
                "from-attribute",
                d => d.Renamed);
        }

        // ---- Non-List collection destinations ---------------------------------------------------
        // The collection path materialised a List<T> and assigned it only when the destination was
        // assignable from that list, or converted to an array. Anything else fell through to the
        // member-init path, which constructed the collection and populated nothing, so the caller
        // got a non-null empty collection and never saw the loss.

        [Fact]
        public void ListIntoHashSet_IsPopulated()
        {
            Mapper.ClearCache();

            var result = ((object)new List<string> { "a", "b" }).MapTo<HashSet<string>>();

            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
            Assert.Contains("a", result);
            Assert.Contains("b", result);
        }

        [Fact]
        public void ListIntoSortedSet_IsPopulated()
        {
            Mapper.ClearCache();

            var result = ((object)new List<string> { "b", "a" }).MapTo<SortedSet<string>>();

            Assert.NotNull(result);
            Assert.Equal(new[] { "a", "b" }, result!);
        }

        [Fact]
        public void ListIntoQueue_IsPopulated()
        {
            Mapper.ClearCache();

            var result = ((object)new List<int> { 1, 2, 3 }).MapTo<Queue<int>>();

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.Equal(1, result.Dequeue());
        }

        [Fact]
        public void DictionaryIntoDictionary_IsPopulated()
        {
            Mapper.ClearCache();

            var source = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 };
            var result = ((object)source).MapTo<Dictionary<string, int>>();

            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
            Assert.Equal(1, result["one"]);
        }

        [Fact]
        public void ListIntoHashSet_ViaInstanceMapper_IsPopulated()
        {
            using var mapper = MapperFactory.Create();

            var result = mapper.MapTo<HashSet<string>>(new List<string> { "a", "b" });

            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
        }

        [Fact]
        public void ListIntoArray_StillWorks()
        {
            // Positive control: the array path already worked and must keep working.
            Mapper.ClearCache();

            var result = ((object)new List<int> { 1, 2, 3 }).MapTo<int[]>();

            Assert.NotNull(result);
            Assert.Equal(new[] { 1, 2, 3 }, result!);
        }

        // ---- Cycle detection has to survive being asked about cyclic type graphs ------------------
        // Found by adversarial review of the fix for the collection-cycle crash, before merge.
        // The predicate deciding whether a type can take part in a cycle walked collection element
        // types, and a type declared as IEnumerable<Self> is its own element type, so the predicate
        // recursed forever and overflowed the stack. The fix for a stack overflow had a stack
        // overflow in it.
        //
        // Two calls in each test, deliberately. The predicate is only consulted on the warm path of
        // the untyped door, so a single map never reaches it and would pass either way.

        [Fact]
        public void ATypeThatIsACollectionOfItself_DoesNotHangTheCycleCheck()
        {
            Mapper.ClearCache();

            var holder = new ConfSelfCollectionHolder { Name = "x", Item = new ConfSelfCollection() };

            var first = ((object)holder).MapTo<ConfSelfCollectionDto>();
            var second = ((object)holder).MapTo<ConfSelfCollectionDto>();

            Assert.Equal("x", first!.Name);
            Assert.Equal("x", second!.Name);
        }

        [Fact]
        public void ATypeThatIsACollectionOfItself_IsSafeThroughTheTypedDoor()
        {
            Mapper.ClearCache();

            var holder = new ConfSelfCollectionHolder { Name = "y", Item = new ConfSelfCollection() };
            var result = holder.MapTo<ConfSelfCollectionHolder, ConfSelfCollectionDto>();

            Assert.Equal("y", result!.Name);
        }

        [Fact]
        public void ACycleThroughADictionary_IsDepthTrackedLikeAnyOther()
        {
            // A dictionary enumerates as KeyValuePair, a struct. Treating structs as inert made the
            // predicate judge a dictionary of nodes acyclic, which put the original crash back for
            // anyone whose graph recursed through a dictionary instead of a list.
            Mapper.ClearCache();

            var root = new ConfDictNode { Name = "root", Children = new Dictionary<string, ConfDictNode>() };
            var child = new ConfDictNode { Name = "child", Children = new Dictionary<string, ConfDictNode>() };
            child.Children["back"] = root;
            root.Children["fwd"] = child;

            var first = ((object)root).MapTo<ConfDictNodeDto>();
            var second = ((object)root).MapTo<ConfDictNodeDto>();

            Assert.Equal("root", first!.Name);
            Assert.Equal("root", second!.Name);
        }

        [Fact]
        public void ADictionaryWithMappedValues_IsPopulated_NotThrown()
        {
            // Building the destination through Dictionary(IEnumerable<KeyValuePair<..>>) mapped each
            // pair as an object. KeyValuePair's properties are read only, so every pair came back
            // as a default with a null key and the constructor threw ArgumentNullException. Keys and
            // values are mapped separately now.
            Mapper.ClearCache();

            var source = new Dictionary<string, ConfInner>
            {
                ["a"] = new ConfInner { Name = "first" },
                ["b"] = new ConfInner { Name = "second" },
            };

            var result = ((object)source).MapTo<Dictionary<string, ConfInnerDto>>();

            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
            Assert.Equal("first", result["a"].Name);
            Assert.Equal("second", result["b"].Name);
        }

        [Fact]
        public void ADictionaryOfPlainValues_StillWorks()
        {
            // Positive control for the two above: the simple case must not have been broken by
            // routing dictionaries down a different path.
            Mapper.ClearCache();

            var source = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 };
            var result = ((object)source).MapTo<Dictionary<string, int>>();

            Assert.Equal(2, result!.Count);
            Assert.Equal(1, result["one"]);
        }

        // ---- Building a collection must never throw at map time -----------------------------------
        // Found by adversarial review before merge. Building non-List destinations through their
        // IEnumerable<T> constructor fixed the silently-empty HashSet, but a constructor can reject
        // what it is handed: SortedSet<T> throws when T has no ordering. That turned silent data
        // loss into a map-time exception, which is the trade PropertyConversion's own rule refuses.

        [Fact]
        public void ACollectionConstructorThatRejectsItsItems_DegradesInsteadOfThrowing()
        {
            Mapper.ClearCache();

            var messages = new List<string>();
            var previousLogger = Mapper.Logger;
            Mapper.Logger = messages.Add;
            try
            {
                var source = new List<ConfUnordered> { new() { Name = "a" }, new() { Name = "b" } };

                var result = ((object)source).MapTo<SortedSet<ConfUnordered>>();

                // Degrading is the contract. Whether it lands as null or empty is not the point;
                // not throwing is, and saying so rather than failing silently is the other half.
                Assert.True(result is null || result.Count == 0);
                Assert.Contains(messages, m => m.Contains("Could not build"));
            }
            finally
            {
                Mapper.Logger = previousLogger;
            }
        }

        [Fact]
        public void ACollectionConstructorThatAcceptsItsItems_StillPopulates()
        {
            // Positive control. Swallowing every construction would satisfy the test above while
            // silently emptying every HashSet destination, which is the defect that started this.
            Mapper.ClearCache();

            var result = ((object)new List<string> { "a", "b" }).MapTo<SortedSet<string>>();

            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
        }

        // ---- The validator has to answer the way the mapper acts ----------------------------------
        // Also found before merge. Making [MapFrom] fall back to the member's own name fixed the
        // mapper on the typed door but left GetUnmappedProperties deciding for itself, so it called
        // a member unmapped that the mapper fills and AssertMappingValid threw for a mapping that
        // works. A validator that raises a false alarm is the same defect as one that certifies a
        // property the mapper skips, which is what #3 was.

        [Fact]
        public void TheValidatorAgreesWithTheMapper_OnAMapFromNamingAMissingProperty()
        {
            Mapper.ClearCache();

            var mapped = ((object)new ConfValidatorSource { Name = "v" }).MapTo<ConfValidatorDest>();
            var unmapped = Mapper.GetUnmappedProperties<ConfValidatorSource, ConfValidatorDest>();

            Assert.Equal("v", mapped!.Name);
            Assert.DoesNotContain("Name", unmapped);
            Mapper.AssertMappingValid<ConfValidatorSource, ConfValidatorDest>();
        }

        [Fact]
        public void TheValidator_StillReportsAMemberNothingCanFill()
        {
            // Positive control: agreeing by never reporting anything would pass the test above.
            Mapper.ClearCache();

            var unmapped = Mapper.GetUnmappedProperties<ConfValidatorSource, ConfGenuinelyUnmappedDest>();

            Assert.Contains("NothingSuppliesThis", unmapped);
            Assert.Throws<InvalidOperationException>(
                () => Mapper.AssertMappingValid<ConfValidatorSource, ConfGenuinelyUnmappedDest>());
        }

        private static ConfNode NewCycle()
        {
            var root = new ConfNode { Name = "root", Children = new List<ConfNode>() };
            var child = new ConfNode { Name = "child", Children = new List<ConfNode>() };
            child.Children.Add(root);
            root.Children.Add(child);
            return root;
        }

        // ---- Types, unique to this file ---------------------------------------------------------

        public enum ConfColour { Red = 0, Green = 1 }

        public interface IConfThing { string? Name { get; set; } }

        public class ConfThing : IConfThing { public string? Name { get; set; } }

        public class ConfIntSource { public int Value { get; set; } }
        public class ConfLongSource { public long Value { get; set; } }
        public class ConfEnumSource { public ConfColour Value { get; set; } }
        public class ConfNullableIntSource { public int? Value { get; set; } }
        public class ConfStringSource { public string? Value { get; set; } }

        public class ConfIntDest { public int Value { get; set; } }
        public class ConfLongDest { public long Value { get; set; } }
        public class ConfDecimalDest { public decimal Value { get; set; } }
        public class ConfDoubleDest { public double Value { get; set; } }
        public class ConfNullableLongDest { public long? Value { get; set; } }
        public class ConfStringDest { public string? Value { get; set; } }

        public class ConfInner { public string? Name { get; set; } }
        public class ConfInnerDto { public string? Name { get; set; } }
        public class ConfNestedSource { public ConfInner? Item { get; set; } }
        public class ConfNestedDest { public ConfInnerDto? Item { get; set; } }

        public class ConfInterfaceSource { public IConfThing? Item { get; set; } }
        public class ConfConcreteDest { public ConfThing? Item { get; set; } }

        public class ConfMapFromSource { public string? Name { get; set; } }
        public class ConfMapFromDest
        {
            [MapFrom("DoesNotExist")]
            public string? Name { get; set; }
        }

        public class ConfRenameSource { public string? Original { get; set; } public string? Renamed { get; set; } }
        public class ConfRenameDest
        {
            [MapFrom("Original")]
            public string? Renamed { get; set; }
        }

        public class ConfUnordered { public string? Name { get; set; } }

        public class ConfValidatorSource { public string? Name { get; set; } }
        public class ConfValidatorDest
        {
            [MapFrom("DoesNotExist")]
            public string? Name { get; set; }
        }
        public class ConfGenuinelyUnmappedDest { public string? NothingSuppliesThis { get; set; } }

        public class ConfSelfCollection : System.Collections.Generic.IEnumerable<ConfSelfCollection>
        {
            private readonly List<ConfSelfCollection> _items = new();
            public System.Collections.Generic.IEnumerator<ConfSelfCollection> GetEnumerator() => _items.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        }

        public class ConfSelfCollectionHolder { public string? Name { get; set; } public ConfSelfCollection? Item { get; set; } }
        public class ConfSelfCollectionDto { public string? Name { get; set; } }

        public class ConfDictNode { public string? Name { get; set; } public Dictionary<string, ConfDictNode>? Children { get; set; } }
        public class ConfDictNodeDto { public string? Name { get; set; } public Dictionary<string, ConfDictNodeDto>? Children { get; set; } }

        public class ConfNode
        {
            public string? Name { get; set; }
            public List<ConfNode>? Children { get; set; }
        }

        public class ConfNodeDto
        {
            public string? Name { get; set; }
            public List<ConfNodeDto>? Children { get; set; }
        }
    }
}
