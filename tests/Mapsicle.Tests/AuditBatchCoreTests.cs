using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// The core findings from the 2.3.0 audit, issues #72, #75, #76, #77, #83, #84 and #85.
    /// </summary>
    /// <remarks>
    /// Every lane is asked the same question here, because each of these was found by mapping the
    /// same source through static <c>MapTo</c>, <c>MapperFactory</c> and Fluent and comparing. A
    /// fix to one lane only is the drift section 3 of CLAUDE.md describes.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class AuditBatchCoreTests
    {
        private static T Static<T>(object source)
        {
            Mapper.ClearCache();
            return source.MapTo<T>()!;
        }

        private static T Factory<T>(object source)
        {
            using var mapper = MapperFactory.Create();
            return mapper.MapTo<T>(source)!;
        }

        // ---- #72 a private getter is not readable -------------------------------------------

        public class AbSecretSrc { public string Secret { private get; set; } = ""; public string Name { get; set; } = ""; }
        public class AbSecretDst { public string? Secret { get; set; } public string? Name { get; set; } }

        [Fact]
        public void APrivateGetterIsNotCopiedByStaticMapTo()
        {
            var dto = Static<AbSecretDst>(new AbSecretSrc { Secret = "hunter2", Name = "Ann" });

            Assert.Null(dto.Secret);
            Assert.Equal("Ann", dto.Name);
        }

        [Fact]
        public void APrivateGetterIsNotCopiedByTheFactory()
        {
            var dto = Factory<AbSecretDst>(new AbSecretSrc { Secret = "hunter2", Name = "Ann" });

            Assert.Null(dto.Secret);
            Assert.Equal("Ann", dto.Name);
        }

        [Fact]
        public void APrivateGetterIsNotCopiedInPlace()
        {
            Mapper.ClearCache();
            var dto = new AbSecretSrc { Secret = "hunter2", Name = "Ann" }.Map(new AbSecretDst());

            Assert.Null(dto.Secret);
            Assert.Equal("Ann", dto.Name);

            using var factory = MapperFactory.Create();
            var viaFactory = factory.Map(new AbSecretSrc { Secret = "hunter2", Name = "Ann" }, new AbSecretDst());

            Assert.Null(viaFactory.Secret);
            Assert.Equal("Ann", viaFactory.Name);
        }

        [Fact]
        public void APrivateGetterIsNotCopiedByTypedMapTo()
        {
            Mapper.ClearCache();
            var dto = new AbSecretSrc { Secret = "hunter2", Name = "Ann" }.MapTo<AbSecretSrc, AbSecretDst>()!;

            Assert.Null(dto.Secret);
            Assert.Equal("Ann", dto.Name);
        }

        [Fact]
        public void APrivateGetterIsNotInToDictionary()
        {
            var dictionary = new AbSecretSrc { Secret = "hunter2", Name = "Ann" }.ToDictionary();

            Assert.False(dictionary.ContainsKey("Secret"));
            Assert.Equal("Ann", dictionary["Name"]);
        }

        // ---- #75 an undefined enum value passes through ---------------------------------------

        public enum AbLeft { Web = 0, Store = 1, Kiosk = 2 }
        public enum AbRight { Web = 0, Store = 1 }
        public enum AbTiny : byte { Web = 0, Store = 1 }
        public class AbChannelSrc { public AbLeft Channel { get; set; } public AbLeft? Maybe { get; set; } }
        public class AbChannelDst { public AbRight Channel { get; set; } public AbRight? Maybe { get; set; } }
        public class AbTinyDst { public AbTiny Channel { get; set; } }

        [Fact]
        public void AnUndefinedEnumValuePassesThroughOnEveryLane()
        {
            var source = new AbChannelSrc { Channel = (AbLeft)99, Maybe = (AbLeft)98 };

            var viaStatic = Static<AbChannelDst>(source);
            var viaFactory = Factory<AbChannelDst>(source);

            Assert.Equal((AbRight)99, viaStatic.Channel);
            Assert.Equal((AbRight)98, viaStatic.Maybe);
            Assert.Equal((AbRight)99, viaFactory.Channel);
            Assert.Equal((AbRight)98, viaFactory.Maybe);
        }

        [Fact]
        public void ADefinedValueStillMatchesByNameAndAnUnmatchedNameIsTheDefault()
        {
            // Positive control for the pass-through: Kiosk is defined in the source and has no
            // counterpart, so it is a known value the destination cannot hold, not a number to copy.
            var matched = Static<AbChannelDst>(new AbChannelSrc { Channel = AbLeft.Store });
            var unmatched = Static<AbChannelDst>(new AbChannelSrc { Channel = AbLeft.Kiosk });

            Assert.Equal(AbRight.Store, matched.Channel);
            Assert.Equal(AbRight.Web, unmatched.Channel);
        }

        [Fact]
        public void AnUndefinedValueTooWideForTheDestinationIsTheDefault()
        {
            // 300 does not fit in a byte. Passing it through would truncate to 44, a number nobody
            // sent, so the value is dropped.
            var dto = Static<AbTinyDst>(new AbChannelSrc { Channel = (AbLeft)300 });

            Assert.Equal(AbTiny.Web, dto.Channel);
        }

        // ---- #76 lossy conversions are not widening ------------------------------------------

        public enum AbBig : long { Small = 1, Huge = (1L << 40) + 5 }
        public enum AbSmall : short { One = 1 }
        public class AbBigSrc { public AbBig Value { get; set; } }
        public class AbSmallSrc { public AbSmall Value { get; set; } }
        public class AbIntDst { public int Value { get; set; } = -1; }
        public class AbLongDst { public long Value { get; set; } = -1; }
        public class AbIntSrc { public int Value { get; set; } }
        public class AbLongSrc { public long Value { get; set; } }
        public class AbFloatDst { public float Value { get; set; } = -1f; }
        public class AbDoubleDst { public double Value { get; set; } = -1d; }

        [Fact]
        public void ALongEnumIsNotTruncatedIntoAnInt()
        {
            Assert.Equal(-1, Static<AbIntDst>(new AbBigSrc { Value = AbBig.Huge }).Value);
            Assert.Equal(-1, Factory<AbIntDst>(new AbBigSrc { Value = AbBig.Huge }).Value);
        }

        [Fact]
        public void EnumsStillWidenIntoIntegersThatHoldThem()
        {
            Assert.Equal((1L << 40) + 5, Static<AbLongDst>(new AbBigSrc { Value = AbBig.Huge }).Value);
            Assert.Equal(1, Static<AbIntDst>(new AbSmallSrc { Value = AbSmall.One }).Value);
        }

        [Fact]
        public void AnIntIsNotRoundedIntoAFloat()
        {
            Assert.Equal(-1f, Static<AbFloatDst>(new AbIntSrc { Value = 16_777_217 }).Value);
            Assert.Equal(-1f, Factory<AbFloatDst>(new AbIntSrc { Value = 16_777_217 }).Value);
        }

        [Fact]
        public void ALongIsNotRoundedIntoADouble()
        {
            Assert.Equal(-1d, Static<AbDoubleDst>(new AbLongSrc { Value = (1L << 53) + 1 }).Value);
        }

        [Fact]
        public void AnIntStillWidensIntoADouble()
        {
            Assert.Equal(16_777_217d, Static<AbDoubleDst>(new AbIntSrc { Value = 16_777_217 }).Value);
        }

        [Fact]
        public void ALongEnumDictionaryValueIsNotTruncated()
        {
            Mapper.ClearCache();
            var dto = new Dictionary<string, object?> { ["Value"] = AbBig.Huge }.MapTo<AbIntDst>()!;

            Assert.Equal(-1, dto.Value);
        }

        // ---- #77 a collection element that cannot convert drops the member --------------------

        public class AbLongList { public List<long> Values { get; set; } = new(); }
        public class AbIntList { public List<int>? Values { get; set; } }
        public class AbStringList { public List<string> Values { get; set; } = new(); }
        public class AbGuidList { public List<Guid>? Values { get; set; } }
        public class AbIntArray { public int[] Values { get; set; } = Array.Empty<int>(); }
        public class AbLongArrayDst { public long[]? Values { get; set; } }

        [Fact]
        public void ANarrowingListElementLeavesTheMemberUnmapped()
        {
            Assert.Null(Static<AbIntList>(new AbLongList { Values = { 5 } }).Values);
            Assert.Null(Factory<AbIntList>(new AbLongList { Values = { 5 } }).Values);
        }

        [Fact]
        public void AnUnparseableListElementLeavesTheMemberUnmapped()
        {
            Assert.Null(Static<AbGuidList>(new AbStringList { Values = { "not a guid" } }).Values);
        }

        [Fact]
        public void AWideningListElementStillMaps()
        {
            Assert.Equal(new long[] { 5, 6 }, Static<AbLongArrayDst>(new AbIntArray { Values = new[] { 5, 6 } }).Values);
        }

        // ---- #83 flattening in Map(existing) --------------------------------------------------

        public class AbCustomer { public string Name { get; set; } = ""; }
        public class AbOrder { public AbCustomer? Customer { get; set; } }
        public class AbOrderDto { public string CustomerName { get; set; } = "unset"; }

        [Fact]
        public void FlatteningWorksInStaticMapExisting()
        {
            Mapper.ClearCache();
            var order = new AbOrder { Customer = new AbCustomer { Name = "Ann" } };

            Assert.Equal("Ann", order.Map(new AbOrderDto()).CustomerName);
        }

        [Fact]
        public void FlatteningWorksInFactoryMapExisting()
        {
            using var factory = MapperFactory.Create();
            var order = new AbOrder { Customer = new AbCustomer { Name = "Ann" } };

            Assert.Equal("Ann", factory.Map(order, new AbOrderDto()).CustomerName);
        }

        [Fact]
        public void AFlattenedPathThroughANullWritesTheDefaultInPlace()
        {
            Mapper.ClearCache();

            Assert.Null(new AbOrder { Customer = null }.Map(new AbOrderDto()).CustomerName);
        }

        // ---- #84 the factory agrees with the static mapper ------------------------------------

        public class AbNode { public int Id { get; set; } public AbNode? Next { get; set; } }
        public class AbNodeDto { public int Id { get; set; } public AbNodeDto? Next { get; set; } }

        private static AbNode Chain(int length)
        {
            var head = new AbNode { Id = 0 };
            var current = head;
            for (var i = 1; i < length; i++)
            {
                current.Next = new AbNode { Id = i };
                current = current.Next;
            }
            return head;
        }

        private static int Length(AbNodeDto? node)
        {
            var count = 0;
            while (node != null) { count++; node = node.Next; }
            return count;
        }

        [Fact]
        public void TheFactoryMapsADeepAcyclicChainWhole()
        {
            Assert.Equal(40, Length(Factory<AbNodeDto>(Chain(40))));
            Assert.Equal(1200, Length(Factory<AbNodeDto>(Chain(1200))));
        }

        [Fact]
        public void TheFactoryStillStopsOnACycle()
        {
            var a = new AbNode { Id = 1 };
            a.Next = new AbNode { Id = 2, Next = a };

            var dto = Factory<AbNodeDto>(a);

            Assert.Equal(1, dto.Id);
            Assert.True(Length(dto) < 200);
        }

        public class AbItem { public string Sku { get; set; } = ""; }
        public class AbItemDto { public string Sku { get; set; } = ""; }
        public class AbBag { public Dictionary<string, AbItem> Items { get; set; } = new(); }
        public class AbBagDto { public Dictionary<string, AbItemDto>? Items { get; set; } }
        public class AbReadOnlyBagDto { public IReadOnlyDictionary<string, AbItemDto>? Items { get; set; } }
        public class AbListBag { public List<AbItem> Items { get; set; } = new(); }
        public class AbSortedBagDto { public SortedSet<AbItemDto>? Items { get; set; } }

        [Fact]
        public void TheFactoryMapsADictionaryMember()
        {
            var source = new AbBag { Items = { ["a"] = new AbItem { Sku = "A1" } } };

            Assert.Equal("A1", Factory<AbBagDto>(source).Items!["a"].Sku);
        }

        [Fact]
        public void TheFactoryAgreesWithStaticOnAReadOnlyDictionaryMember()
        {
            // The factory threw NullReferenceException here. Neither lane maps an interface typed
            // collection member yet, so agreement is what this pins, not the value.
            var source = new AbBag { Items = { ["a"] = new AbItem { Sku = "A1" } } };

            Assert.Equal(Static<AbReadOnlyBagDto>(source).Items, Factory<AbReadOnlyBagDto>(source).Items);
        }

        [Fact]
        public void TheFactoryDoesNotThrowOnACollectionItCannotBuild()
        {
            var source = new AbListBag { Items = { new AbItem { Sku = "A" }, new AbItem { Sku = "B" } } };

            Assert.Null(Static<AbSortedBagDto>(source).Items);
            Assert.Null(Factory<AbSortedBagDto>(source).Items);
        }

        public class AbFillSrc { public List<string> Tags { get; set; } = new(); public int Count { get; set; } }
        public class AbFillDst { public List<string> Tags { get; } = new(); public int Count; }

        [Fact]
        public void TheFactoryFillsGetterOnlyCollectionsAndFieldsInPlace()
        {
            using var factory = MapperFactory.Create();
            var dto = factory.Map(new AbFillSrc { Tags = { "x", "y" }, Count = 3 }, new AbFillDst());

            Assert.Equal(new[] { "x", "y" }, dto.Tags);
            Assert.Equal(3, dto.Count);
        }

        // ---- #85 structs --------------------------------------------------------------------

        public struct AbPoint { public int X { get; set; } public string? Label { get; set; } }
        public class AbPointDto { public int X { get; set; } public string? Label { get; set; } }
        public class AbHolder { public AbPoint Where { get; set; } public AbPoint? Maybe { get; set; } public AbPoint? Missing { get; set; } }
        public class AbHolderDto { public AbPointDto? Where { get; set; } public AbPointDto? Maybe { get; set; } public AbPointDto? Missing { get; set; } }

        [Fact]
        public void AStructMemberMapsIntoAClassMember()
        {
            var source = new AbHolder { Where = new AbPoint { X = 3, Label = "a" }, Maybe = new AbPoint { X = 4 } };

            foreach (var dto in new[] { Static<AbHolderDto>(source), Factory<AbHolderDto>(source) })
            {
                Assert.Equal(3, dto.Where!.X);
                Assert.Equal("a", dto.Where.Label);
                Assert.Equal(4, dto.Maybe!.X);
                Assert.Null(dto.Missing);
            }
        }

        [Fact]
        public void AStructSourceStillMapsAtTheTopLevel()
        {
            var dto = Static<AbPointDto>(new AbPoint { X = 7 });

            Assert.Equal(7, dto.X);
        }

        public class AbP { public int X { get; set; } public int Y { get; set; } }
        public struct AbPs { public int X { get; set; } public int Y { get; set; } }

        [Fact]
        public void MapIntoAStructReturnsTheMappedCopy()
        {
            Mapper.ClearCache();
            var mapped = new AbP { X = 3, Y = 4 }.Map(new AbPs());

            Assert.Equal(3, mapped.X);
            Assert.Equal(4, mapped.Y);

            using var factory = MapperFactory.Create();
            var viaFactory = factory.Map(new AbP { X = 5, Y = 6 }, new AbPs());

            Assert.Equal(5, viaFactory.X);
            Assert.Equal(6, viaFactory.Y);
        }
    }
}
