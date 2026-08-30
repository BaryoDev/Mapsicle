using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// A List source is mapped by a loop compiled for its element type. These are the cases where
    /// compiling it could be wrong.
    /// </summary>
    /// <remarks>
    /// The loop inlines the element mapping, so it is specialised to one element type. Everything
    /// here is about what happens when the list does not hold only that type, or holds nothing, or
    /// holds nulls, or refers to itself.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class CompiledListLoopTests
    {
        public class ClAnimal { public string Name { get; set; } = ""; }
        public class ClDog : ClAnimal { public string Breed { get; set; } = ""; }
        public class ClAnimalDto { public string Name { get; set; } = ""; }
        public class ClDogDto { public string Name { get; set; } = ""; public string Breed { get; set; } = ""; }

        public class ClSource { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class ClDest { public int Id { get; set; } public string Name { get; set; } = ""; }

        public class ClNode { public string Name { get; set; } = ""; public ClNode? Next { get; set; } }
        public class ClNodeDto { public string Name { get; set; } = ""; public ClNodeDto? Next { get; set; } }

        [Fact]
        public void AHomogeneousListMapsEveryElement()
        {
            Mapper.ClearCache();
            var source = Enumerable.Range(1, 5).Select(i => new ClSource { Id = i, Name = "n" + i }).ToList();

            var dtos = ((IEnumerable)source).MapTo<ClDest>();

            Assert.Equal(5, dtos.Count);
            Assert.Equal(Enumerable.Range(1, 5), dtos.Select(d => d.Id));
            Assert.Equal("n3", dtos[2].Name);
        }

        [Fact]
        public void AListDeclaredAsABaseTypeMapsADerivedElementAsWhatItIs()
        {
            // The loop is compiled for ClAnimal. Applying that mapping to a ClDog is safe here
            // because ClDog is one, but the runtime check has to route it anyway or a derived type
            // with its own configured mapping would get the base one.
            Mapper.ClearCache();
            var source = new List<ClAnimal>
            {
                new ClAnimal { Name = "generic" },
                new ClDog { Name = "rex", Breed = "lab" },
                new ClAnimal { Name = "other" },
            };

            var dtos = ((IEnumerable)source).MapTo<ClAnimalDto>();

            Assert.Equal(new[] { "generic", "rex", "other" }, dtos.Select(d => d.Name));
        }

        [Fact]
        public void ADerivedElementIsMappedByItsRuntimeTypeNotTheListsDeclaredOne()
        {
            // The loop is compiled for ClAnimal, which has no Breed. A ClDog sitting in a
            // List<ClAnimal> has to be mapped as a ClDog or Breed is silently dropped. Applying the
            // base mapping would not throw, it would quietly return less, which is why this needs
            // its own test rather than relying on the base-typed one above.
            Mapper.ClearCache();
            var source = new List<ClAnimal>
            {
                new ClDog { Name = "rex", Breed = "labrador" },
                new ClAnimal { Name = "generic" },
            };

            var dtos = ((IEnumerable)source).MapTo<ClDogDto>();

            Assert.Equal("rex", dtos[0].Name);
            Assert.Equal("labrador", dtos[0].Breed);
            Assert.Equal("generic", dtos[1].Name);
            Assert.Equal("", dtos[1].Breed);
        }

        [Fact]
        public void AListAlternatingBetweenTypesStaysCorrect()
        {
            Mapper.ClearCache();
            var source = new List<ClAnimal>();
            for (var i = 0; i < 20; i++)
            {
                source.Add(i % 2 == 0 ? new ClAnimal { Name = "base" + i } : new ClDog { Name = "dog" + i });
            }

            var dtos = ((IEnumerable)source).MapTo<ClAnimalDto>();

            Assert.Equal(20, dtos.Count);
            for (var i = 0; i < 20; i++)
            {
                Assert.Equal(i % 2 == 0 ? "base" + i : "dog" + i, dtos[i].Name);
            }
        }

        [Fact]
        public void NullElementsBecomeNullsAndDoNotDisturbTheirNeighbours()
        {
            Mapper.ClearCache();
            var source = new List<ClSource> { new() { Id = 1 }, null!, new() { Id = 3 } };

            var dtos = ((IEnumerable)source).MapTo<ClDest>();

            Assert.Equal(3, dtos.Count);
            Assert.Equal(1, dtos[0].Id);
            Assert.Null(dtos[1]);
            Assert.Equal(3, dtos[2].Id);
        }

        [Fact]
        public void AnEmptyListGivesAnEmptyResult()
        {
            Mapper.ClearCache();
            Assert.Empty(((IEnumerable)new List<ClSource>()).MapTo<ClDest>());
        }

        [Fact]
        public void AListOfOneIsNotASpecialCase()
        {
            Mapper.ClearCache();
            var dtos = ((IEnumerable)new List<ClSource> { new() { Id = 9, Name = "x" } }).MapTo<ClDest>();
            Assert.Single(dtos);
            Assert.Equal(9, dtos[0].Id);
        }

        [Fact]
        public void ACyclicElementTypeStillTerminates()
        {
            // The compiled loop refuses element types that can form cycles, because depth is taken
            // once for the whole collection and the existing loop already does that correctly.
            Mapper.ClearCache();
            var a = new ClNode { Name = "a" };
            var b = new ClNode { Name = "b" };
            a.Next = b;
            b.Next = a;

            var dtos = ((IEnumerable)new List<ClNode> { a, b }).MapTo<ClNodeDto>();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("a", dtos[0].Name);
            Assert.Equal("b", dtos[1].Name);
        }

        [Fact]
        public void TheSecondMapOfThePairAgreesWithTheFirst()
        {
            Mapper.ClearCache();
            var source = Enumerable.Range(1, 3).Select(i => new ClSource { Id = i, Name = "n" + i }).ToList();

            var first = ((IEnumerable)source).MapTo<ClDest>();
            var second = ((IEnumerable)source).MapTo<ClDest>();

            Assert.Equal(first.Select(d => d.Id), second.Select(d => d.Id));
            Assert.Equal(first.Select(d => d.Name), second.Select(d => d.Name));
        }

        [Fact]
        public void ClearCacheDiscardsTheCompiledLoop()
        {
            Mapper.ClearCache();
            var source = new List<ClSource> { new() { Id = 1, Name = "a" } };

            Assert.Equal("a", ((IEnumerable)source).MapTo<ClDest>()[0].Name);
            Assert.True(Mapper.CacheInfo().Total > 0, "a compiled loop should be reported as cached");

            Mapper.ClearCache();
            Assert.Equal(0, Mapper.CacheInfo().Total);

            Assert.Equal("a", ((IEnumerable)source).MapTo<ClDest>()[0].Name);
        }

        [Fact]
        public void TheBoundedCacheStillMapsAListCorrectly()
        {
            var previous = Mapper.UseLruCache;
            Mapper.UseLruCache = true;
            try
            {
                Mapper.ClearCache();
                var source = Enumerable.Range(1, 4).Select(i => new ClSource { Id = i }).ToList();
                var dtos = ((IEnumerable)source).MapTo<ClDest>();
                Assert.Equal(new[] { 1, 2, 3, 4 }, dtos.Select(d => d.Id));
            }
            finally
            {
                Mapper.UseLruCache = previous;
                Mapper.ClearCache();
            }
        }

        [Fact]
        public void AListAndAnArrayOfTheSameItemsAgree()
        {
            // Only the List goes through the compiled loop. The array still walks the old path, so
            // this is the two implementations checked against each other.
            Mapper.ClearCache();
            var items = Enumerable.Range(1, 6).Select(i => new ClSource { Id = i, Name = "n" + i }).ToList();

            var fromList = ((IEnumerable)items).MapTo<ClDest>();
            var fromArray = ((IEnumerable)items.ToArray()).MapTo<ClDest>();

            Assert.Equal(fromList.Select(d => d.Id), fromArray.Select(d => d.Id));
            Assert.Equal(fromList.Select(d => d.Name), fromArray.Select(d => d.Name));
        }
    }
}
