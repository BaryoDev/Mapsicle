using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.Fluent.Tests
{
    /// <summary>
    /// A configured pair with a list member mapped into a collection interface or ImmutableList member.
    /// </summary>
    public class CollectionInterfaceMemberTests
    {
        public class CimItem { public int A { get; set; } }
        public class CimItemDto { public int A { get; set; } }

        public class CimSource { public List<CimItem> Items { get; set; } = new(); }

        public class CimDest
        {
            public IReadOnlyList<CimItemDto>? Items { get; set; }
            public IList<CimItemDto>? Same { get; set; }
            public ImmutableList<CimItemDto>? Frozen { get; set; }
        }

        public class CimSource2
        {
            public List<CimItem> Items { get; set; } = new();
            public List<CimItem> Same { get; set; } = new();
            public List<CimItem> Frozen { get; set; } = new();
        }

        [Fact]
        public void ConfiguredPairsFillInterfaceAndImmutableMembers()
        {
            var mapper = new MapperConfiguration(c =>
            {
                c.CreateMap<CimItem, CimItemDto>();
                c.CreateMap<CimSource2, CimDest>();
            }).CreateMapper();

            var source = new CimSource2
            {
                Items = new List<CimItem> { new() { A = 1 }, new() { A = 2 } },
                Same = new List<CimItem> { new() { A = 3 } },
                Frozen = new List<CimItem> { new() { A = 4 }, new() { A = 5 } },
            };

            var dest = mapper.Map<CimDest>(source)!;

            Assert.Equal(new[] { 1, 2 }, dest.Items!.Select(i => i.A).ToArray());
            Assert.Equal(new[] { 3 }, dest.Same!.Select(i => i.A).ToArray());
            Assert.Equal(new[] { 4, 5 }, dest.Frozen!.Select(i => i.A).ToArray());
        }
    }
}
