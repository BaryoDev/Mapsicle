using System.Collections.Generic;
using Xunit;
using Mapsicle;

namespace Mapsicle.Tests
{
    public class ComplexMappingTests
    {
        // --- Test Models ---
        public class ItemA { public string Name { get; set; } = string.Empty; }
        public class ItemB { public string Name { get; set; } = string.Empty; }

        public class ContainerA { public List<ItemA> Items { get; set; } = new(); }
        public class ContainerB { public List<ItemB> Items { get; set; } = new(); }

        public class IndexedClass
        {
            private readonly List<string> _items = new();
            public string this[int index]
            {
                get => _items[index];
                set => _items[index] = value;
            }
            public string Name { get; set; } = "Indexed";
        }

        public class SimpleDto { public string Name { get; set; } = string.Empty; }

        [Fact]
        public void NestedList_ShouldMap_WithoutCrashing()
        {
            // Regression test for "Indexer Crash" and "Collection Recursion"
            var source = new ContainerA
            {
                Items = new List<ItemA> { new ItemA { Name = "Test" } }
            };

            var dest = source.MapTo<ContainerB>();

            Assert.NotNull(dest);
            Assert.NotNull(dest.Items);
            Assert.Single(dest.Items);
            Assert.Equal("Test", dest.Items[0].Name);
        }

        [Fact]
        public void CollectionMapping_ShouldReturnList_Directly()
        {
            // Verifies MapTo<T>(IEnumerable) optimization and return type
            var list = new List<ItemA> { new ItemA { Name = "A" }, new ItemA { Name = "B" } };

            List<ItemB> mapped = list.MapTo<ItemB>();

            Assert.Equal(2, mapped.Count);
            Assert.IsType<List<ItemB>>(mapped);
            Assert.Equal("A", mapped[0].Name);
            Assert.Equal("B", mapped[1].Name);
        }

        [Fact]
        public void ClassWithIndexer_ShouldMap_IgnoringIndexer()
        {
            // Explicit test for preventing the "Indexer Crash"
            var source = new IndexedClass { Name = "Source" };

            // This should succeed by ignoring the 'this[]' property
            var dest = source.MapTo<SimpleDto>();

            Assert.NotNull(dest);
            Assert.Equal("Source", dest.Name);
        }
    }
}
