using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// A list and an array holding the same items must map to the same thing.
    /// </summary>
    /// <remarks>
    /// Only the list goes through the loop compiled for its element type. An array still walks the
    /// older loop, so it is an oracle: the two implementations are checked against each other
    /// rather than both against my expectations. Every shape here is one the compiled loop either
    /// takes or deliberately refuses, and the refusals matter as much as the acceptances, because
    /// a wrongly accepted shape is how List&lt;List&lt;T&gt;&gt; came back empty.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class ListVersusArrayDifferentialTests
    {
        public class DfInner { public string City { get; set; } = ""; }
        public class DfInnerDto { public string City { get; set; } = ""; }

        public class DfPlain { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class DfPlainDto { public int Id { get; set; } public string Name { get; set; } = ""; }

        public class DfNested { public int Id { get; set; } public DfInner? Inner { get; set; } }
        public class DfNestedDto { public int Id { get; set; } public DfInnerDto? Inner { get; set; } }
        public class DfFlatDto { public int Id { get; set; } public string InnerCity { get; set; } = ""; }

        public class DfWiden { public int Count { get; set; } }
        public class DfWidenDto { public long Count { get; set; } }

        public class DfBase { public string Name { get; set; } = ""; }
        public sealed class DfSealed { public string Name { get; set; } = ""; }
        public sealed class DfSealedDto { public string Name { get; set; } = ""; }

        private static void SameBothWays<TDest>(IEnumerable items, Func<TDest, object?> project)
        {
            Mapper.ClearCache();
            var asList = ((IEnumerable)items.Cast<object>().ToList()).MapTo<TDest>();

            Mapper.ClearCache();
            var asArray = ((IEnumerable)items.Cast<object>().ToArray()).MapTo<TDest>();

            Assert.Equal(asList.Count, asArray.Count);
            Assert.Equal(asList.Select(project), asArray.Select(project));
        }

        [Fact]
        public void APlainShapeAgrees()
        {
            var items = Enumerable.Range(1, 5).Select(i => new DfPlain { Id = i, Name = "n" + i }).ToList();

            Mapper.ClearCache();
            var fromList = ((IEnumerable)items).MapTo<DfPlainDto>();
            Mapper.ClearCache();
            var fromArray = ((IEnumerable)items.ToArray()).MapTo<DfPlainDto>();

            Assert.Equal(fromList.Select(d => (d.Id, d.Name)), fromArray.Select(d => (d.Id, d.Name)));
        }

        [Fact]
        public void ANestedShapeAgrees()
        {
            var items = Enumerable.Range(1, 4)
                .Select(i => new DfNested { Id = i, Inner = new DfInner { City = "c" + i } }).ToList();

            Mapper.ClearCache();
            var fromList = ((IEnumerable)items).MapTo<DfNestedDto>();
            Mapper.ClearCache();
            var fromArray = ((IEnumerable)items.ToArray()).MapTo<DfNestedDto>();

            Assert.Equal(fromList.Select(d => (d.Id, d.Inner?.City)), fromArray.Select(d => (d.Id, d.Inner?.City)));
        }

        [Fact]
        public void AFlattenedShapeAgrees()
        {
            var items = Enumerable.Range(1, 3)
                .Select(i => new DfNested { Id = i, Inner = new DfInner { City = "c" + i } }).ToList();

            Mapper.ClearCache();
            var fromList = ((IEnumerable)items).MapTo<DfFlatDto>();
            Mapper.ClearCache();
            var fromArray = ((IEnumerable)items.ToArray()).MapTo<DfFlatDto>();

            Assert.Equal(fromList.Select(d => d.InnerCity), fromArray.Select(d => d.InnerCity));
            Assert.Equal("c2", fromList[1].InnerCity);
        }

        [Fact]
        public void AWideningConversionAgrees()
        {
            var items = new List<DfWiden> { new() { Count = 7 }, new() { Count = 9 } };

            Mapper.ClearCache();
            var fromList = ((IEnumerable)items).MapTo<DfWidenDto>();
            Mapper.ClearCache();
            var fromArray = ((IEnumerable)items.ToArray()).MapTo<DfWidenDto>();

            Assert.Equal(fromList.Select(d => d.Count), fromArray.Select(d => d.Count));
            Assert.Equal(7L, fromList[0].Count);
        }

        [Fact]
        public void ASealedElementTypeAgrees()
        {
            // Sealed elements skip the runtime type check, since nothing can derive from them.
            var items = new List<DfSealed> { new() { Name = "a" }, new() { Name = "b" } };

            Mapper.ClearCache();
            var fromList = ((IEnumerable)items).MapTo<DfSealedDto>();
            Mapper.ClearCache();
            var fromArray = ((IEnumerable)items.ToArray()).MapTo<DfSealedDto>();

            Assert.Equal(fromList.Select(d => d.Name), fromArray.Select(d => d.Name));
            Assert.Equal(new[] { "a", "b" }, fromList.Select(d => d.Name));
        }

        [Fact]
        public void NullsInTheMiddleAgree()
        {
            var items = new List<DfPlain> { new() { Id = 1 }, null!, new() { Id = 3 } };

            Mapper.ClearCache();
            var fromList = ((IEnumerable)items).MapTo<DfPlainDto>();
            Mapper.ClearCache();
            var fromArray = ((IEnumerable)items.ToArray()).MapTo<DfPlainDto>();

            Assert.Equal(fromList.Select(d => d?.Id), fromArray.Select(d => d?.Id));
            Assert.Null(fromList[1]);
        }

        [Fact]
        public void AListOfListsAgreesWithAnArrayOfLists()
        {
            // The shape that came back empty. Both forms must produce the inner elements.
            var items = new List<List<DfPlain>>
            {
                new() { new DfPlain { Id = 1, Name = "a" } },
                new() { new DfPlain { Id = 2, Name = "b" }, new DfPlain { Id = 3, Name = "c" } },
            };

            Mapper.ClearCache();
            var fromList = ((IEnumerable)items).MapTo<List<DfPlainDto>>();
            Mapper.ClearCache();
            var fromArray = ((IEnumerable)items.ToArray()).MapTo<List<DfPlainDto>>();

            Assert.Equal(fromList.Select(l => l.Count), fromArray.Select(l => l.Count));
            Assert.Equal(new[] { 1, 2 }, fromList.Select(l => l.Count));
            Assert.Equal("c", fromList[1][1].Name);
        }

        [Fact]
        public void AnEmptySequenceAgrees()
        {
            Mapper.ClearCache();
            var fromList = ((IEnumerable)new List<DfPlain>()).MapTo<DfPlainDto>();
            Mapper.ClearCache();
            var fromArray = ((IEnumerable)Array.Empty<DfPlain>()).MapTo<DfPlainDto>();

            Assert.Empty(fromList);
            Assert.Empty(fromArray);
        }

        [Fact]
        public void ABoxedSequenceAgrees()
        {
            var items = Enumerable.Range(1, 4).Select(i => (object)new DfPlain { Id = i, Name = "n" + i }).ToList();
            SameBothWays<DfPlainDto>(items, d => (d.Id, d.Name));
        }

        [Fact]
        public void AHeterogeneousSequenceAgrees()
        {
            var items = new List<DfBase>
            {
                new DfBase { Name = "base" },
                new DfDerived { Name = "derived", Extra = "extra" },
            };

            Mapper.ClearCache();
            var fromList = ((IEnumerable)items).MapTo<DfDerivedDto>();
            Mapper.ClearCache();
            var fromArray = ((IEnumerable)items.ToArray()).MapTo<DfDerivedDto>();

            Assert.Equal(fromList.Select(d => (d.Name, d.Extra)), fromArray.Select(d => (d.Name, d.Extra)));
            // The element is a DfDerived in a List<DfBase>, so mapping it as the declared type
            // would fill Name and leave Extra empty.
            Assert.Equal("extra", fromList[1].Extra);
            Assert.Equal("", fromList[0].Extra);
        }

        public class DfDerived : DfBase { public string Extra { get; set; } = ""; }
        public class DfDerivedDto { public string Name { get; set; } = ""; public string Extra { get; set; } = ""; }
    }
}
