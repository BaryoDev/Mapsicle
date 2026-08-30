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

        public class ClAddress { public string City { get; set; } = ""; public string Country { get; set; } = ""; }
        public class ClAddressDto { public string City { get; set; } = ""; public string Country { get; set; } = ""; }
        public class ClUser { public int Id { get; set; } public ClAddress? Address { get; set; } }
        public class ClUserDto { public int Id { get; set; } public ClAddressDto? Address { get; set; } }
        public class ClUserFlatDto { public int Id { get; set; } public string AddressCity { get; set; } = ""; }

        [Fact]
        public void AnElementTypeHoldingANestedReferenceMapsThroughTheCompiledLoop()
        {
            // This shape was excluded by the first version of the loop, which refused any element
            // type that answers yes to depth tracking. That is every type holding a nested
            // reference, which is most DTOs, including the one the performance claim measures. The
            // loop takes depth once for the whole collection now instead of refusing the type.
            Mapper.ClearCache();
            var source = Enumerable.Range(1, 4).Select(i => new ClUser
            {
                Id = i,
                Address = new ClAddress { City = "city" + i, Country = "PH" },
            }).ToList();

            var dtos = ((IEnumerable)source).MapTo<ClUserDto>();

            Assert.Equal(4, dtos.Count);
            Assert.Equal(new[] { 1, 2, 3, 4 }, dtos.Select(d => d.Id));
            Assert.Equal("city3", dtos[2].Address?.City);
            Assert.Equal("PH", dtos[2].Address?.Country);
        }

        [Fact]
        public void ANullNestedReferenceInAnElementStaysNull()
        {
            Mapper.ClearCache();
            var source = new List<ClUser> { new() { Id = 1, Address = null } };

            var dtos = ((IEnumerable)source).MapTo<ClUserDto>();

            Assert.Equal(1, dtos[0].Id);
            Assert.Null(dtos[0].Address);
        }

        [Fact]
        public void FlatteningStillWorksThroughTheCompiledLoop()
        {
            // The loop inlines whatever the single object path builds, and flattening is part of
            // that. If it inlined something narrower this would come back empty.
            Mapper.ClearCache();
            var source = new List<ClUser> { new() { Id = 1, Address = new ClAddress { City = "Cebu" } } };

            var dtos = ((IEnumerable)source).MapTo<ClUserFlatDto>();

            Assert.Equal("Cebu", dtos[0].AddressCity);
        }

        [Fact]
        public void AListAndAnArrayOfNestedElementsAgree()
        {
            // The array keeps the old loop, so this checks the two implementations against each
            // other on the shape that used to be excluded.
            Mapper.ClearCache();
            var items = Enumerable.Range(1, 5).Select(i => new ClUser
            {
                Id = i,
                Address = new ClAddress { City = "c" + i, Country = "PH" },
            }).ToList();

            var fromList = ((IEnumerable)items).MapTo<ClUserDto>();
            var fromArray = ((IEnumerable)items.ToArray()).MapTo<ClUserDto>();

            Assert.Equal(fromList.Select(d => d.Id), fromArray.Select(d => d.Id));
            Assert.Equal(fromList.Select(d => d.Address?.City), fromArray.Select(d => d.Address?.City));
        }

        public interface IClShape { string Name { get; set; } }
        public class ClSquare : ClAnimal, IClShape { }
        public abstract class ClBaseThing { public string Name { get; set; } = ""; }
        public class ClRealThing : ClBaseThing { }

        [Fact]
        public void AListOfObjectStillMapsByEachElementsRuntimeType()
        {
            // The loop is compiled for the declared element type. For List<object> that type is
            // object, which has nothing to map, so every element would fail the runtime check and
            // take the fallback one entry point call at a time: measured 10.9x slower than
            // List<T>. These lists keep the previous loop, which resolves against the first
            // element's runtime type. This is the shape the library exists for, items whose types
            // are only known at runtime, so it must not be the slow one.
            Mapper.ClearCache();
            var source = Enumerable.Range(1, 4)
                .Select(i => (object)new ClSource { Id = i, Name = "n" + i })
                .ToList();

            var dtos = ((IEnumerable)source).MapTo<ClDest>();

            Assert.Equal(4, dtos.Count);
            Assert.Equal(new[] { 1, 2, 3, 4 }, dtos.Select(d => d.Id));
            Assert.Equal("n4", dtos[3].Name);
        }

        [Fact]
        public void AListOfAnAbstractTypeMapsByRuntimeType()
        {
            Mapper.ClearCache();
            var source = new List<ClBaseThing> { new ClRealThing { Name = "real" } };

            var dtos = ((IEnumerable)source).MapTo<ClAnimalDto>();

            Assert.Equal("real", dtos[0].Name);
        }

        [Fact]
        public void AListOfAnInterfaceMapsByRuntimeType()
        {
            Mapper.ClearCache();
            var source = new List<IClShape> { new ClSquare { Name = "square" } };

            var dtos = ((IEnumerable)source).MapTo<ClAnimalDto>();

            Assert.Equal("square", dtos[0].Name);
        }

        [Fact]
        public void AListOfListsMapsTheInnerContentsNotTheInnerListsProperties()
        {
            // The destination element is itself a list. A member initialiser for a list maps its
            // properties, so every inner list came back empty with nothing raised. The loop only
            // inlines a member initialiser for destinations the single object builder would have
            // used one for.
            Mapper.ClearCache();
            var source = new List<List<ClSource>>
            {
                new() { new ClSource { Id = 1, Name = "a" }, new ClSource { Id = 2, Name = "b" } },
                new() { new ClSource { Id = 3, Name = "c" } },
            };

            var mapped = ((IEnumerable)source).MapTo<List<ClDest>>();

            Assert.Equal(2, mapped.Count);
            Assert.Equal(2, mapped[0].Count);
            Assert.Single(mapped[1]);
            Assert.Equal(1, mapped[0][0].Id);
            Assert.Equal("a", mapped[0][0].Name);
            Assert.Equal(3, mapped[1][0].Id);
        }

        [Fact]
        public void AListOfArraysMapsTheInnerContents()
        {
            Mapper.ClearCache();
            var source = new List<ClSource[]>
            {
                new[] { new ClSource { Id = 1, Name = "a" } },
            };

            var mapped = ((IEnumerable)source).MapTo<ClDest[]>();

            Assert.Single(mapped);
            Assert.Single(mapped[0]);
            Assert.Equal("a", mapped[0][0].Name);
        }

        [Fact]
        public void AnElementMappedToItsOwnBaseTypeIsCopiedRatherThanHandedOver()
        {
            // Recorded rather than assumed: I expected the reference to be handed over and it is
            // not, a new base instance is built and the shared members copied. The compiled loop
            // refuses these pairs so the answer keeps coming from one place.
            Mapper.ClearCache();
            var dog = new ClDog { Name = "rex", Breed = "lab" };

            var mapped = ((IEnumerable)new List<ClDog> { dog }).MapTo<ClAnimal>();

            Assert.Single(mapped);
            Assert.NotSame(dog, mapped[0]);
            Assert.Equal("rex", mapped[0].Name);
        }

        [Fact]
        public void MappingAListToItsOwnElementTypeStillWorks()
        {
            Mapper.ClearCache();
            var item = new ClSource { Id = 4, Name = "same" };

            var mapped = ((IEnumerable)new List<ClSource> { item }).MapTo<ClSource>();

            Assert.Single(mapped);
            Assert.Equal(4, mapped[0].Id);
            Assert.Equal("same", mapped[0].Name);
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
