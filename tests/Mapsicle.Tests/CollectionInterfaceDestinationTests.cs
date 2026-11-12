using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// A list mapped into a destination member typed as a collection interface, or ImmutableList.
    /// </summary>
    /// <remarks>
    /// Only List and array destinations were filled. IList, ICollection, IEnumerable, IReadOnlyList,
    /// IReadOnlyCollection and ImmutableList members came back null through every entry point, with
    /// no error, while the same list into a List member worked.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class CollectionInterfaceDestinationTests
    {
        public class CidItem { public int A { get; set; } public string Name { get; set; } = ""; }
        public class CidItemDto { public int A { get; set; } public string Name { get; set; } = ""; }

        public class CidSource { public List<CidItem> Items { get; set; } = new(); }

        public class CidList { public List<CidItemDto>? Items { get; set; } }
        public class CidIList { public IList<CidItemDto>? Items { get; set; } }
        public class CidICollection { public ICollection<CidItemDto>? Items { get; set; } }
        public class CidIEnumerable { public IEnumerable<CidItemDto>? Items { get; set; } }
        public class CidIReadOnlyList { public IReadOnlyList<CidItemDto>? Items { get; set; } }
        public class CidIReadOnlyCollection { public IReadOnlyCollection<CidItemDto>? Items { get; set; } }
        public class CidImmutable { public ImmutableList<CidItemDto>? Items { get; set; } }

        private static CidSource Source() => new()
        {
            Items = new List<CidItem> { new() { A = 1, Name = "one" }, new() { A = 2, Name = "two" } },
        };

        private static void AssertFilled(IEnumerable<CidItemDto>? items)
        {
            Assert.NotNull(items);
            var array = items!.ToArray();
            Assert.Equal(2, array.Length);
            Assert.Equal(new[] { 1, 2 }, array.Select(i => i.A).ToArray());
            Assert.Equal(new[] { "one", "two" }, array.Select(i => i.Name).ToArray());
        }

        [Fact] public void ListStillWorks() => AssertFilled(Source().MapTo<CidList>()!.Items);
        [Fact] public void IList() => AssertFilled(Source().MapTo<CidIList>()!.Items);
        [Fact] public void ICollection() => AssertFilled(Source().MapTo<CidICollection>()!.Items);
        [Fact] public void IEnumerable() => AssertFilled(Source().MapTo<CidIEnumerable>()!.Items);
        [Fact] public void IReadOnlyList() => AssertFilled(Source().MapTo<CidIReadOnlyList>()!.Items);
        [Fact] public void IReadOnlyCollection() => AssertFilled(Source().MapTo<CidIReadOnlyCollection>()!.Items);
        [Fact] public void ImmutableListDestination() => AssertFilled(Source().MapTo<CidImmutable>()!.Items);

        [Fact]
        public void IReadOnlyListIsAnIndependentCopy()
        {
            var source = Source();
            var dto = source.MapTo<CidIReadOnlyList>()!;

            source.Items[0].A = 99;

            Assert.Equal(1, dto.Items![0].A);
        }

        [Fact]
        public void TheTypedEntryPoint()
        {
            AssertFilled(Source().MapTo<CidSource, CidIList>()!.Items);
            AssertFilled(Source().MapTo<CidSource, CidIReadOnlyList>()!.Items);
            AssertFilled(Source().MapTo<CidSource, CidImmutable>()!.Items);
        }

        [Fact]
        public void MapIntoAnExistingObject()
        {
            var ilist = new CidIList();
            Source().Map(ilist);
            AssertFilled(ilist.Items);

            var readOnly = new CidIReadOnlyCollection();
            Source().Map(readOnly);
            AssertFilled(readOnly.Items);

            var immutable = new CidImmutable();
            Source().Map(immutable);
            AssertFilled(immutable.Items);
        }

        [Fact]
        public void ThroughMapperFactory()
        {
            using var factory = MapperFactory.Create();

            AssertFilled(factory.MapTo<CidIList>(Source())!.Items);
            AssertFilled(factory.MapTo<CidICollection>(Source())!.Items);
            AssertFilled(factory.MapTo<CidIEnumerable>(Source())!.Items);
            AssertFilled(factory.MapTo<CidIReadOnlyList>(Source())!.Items);
            AssertFilled(factory.MapTo<CidIReadOnlyCollection>(Source())!.Items);
            AssertFilled(factory.MapTo<CidImmutable>(Source())!.Items);
        }

        [Fact]
        public void AnArraySourceFillsTheInterfaceToo()
        {
            var source = new CidArraySource { Items = Source().Items.ToArray() };

            AssertFilled(source.MapTo<CidIReadOnlyList>()!.Items);
        }

        public class CidArraySource { public CidItem[] Items { get; set; } = System.Array.Empty<CidItem>(); }

        [Fact]
        public void AnEmptyListGivesAnEmptyInterfaceMember()
        {
            var dto = new CidSource().MapTo<CidIReadOnlyList>()!;

            Assert.NotNull(dto.Items);
            Assert.Empty(dto.Items!);
        }

        [Fact]
        public void ScalarElementsFillTheInterface()
        {
            var dto = new CidInts { Values = new List<int> { 1, 2, 3 } }.MapTo<CidIntsDto>()!;

            Assert.Equal(new[] { 1, 2, 3 }, dto.Values!.ToArray());
        }

        public class CidInts { public List<int> Values { get; set; } = new(); }
        public class CidIntsDto { public IReadOnlyList<int>? Values { get; set; } }
    }
}
