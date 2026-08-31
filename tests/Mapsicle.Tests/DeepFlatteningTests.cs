using System;
using System.Collections;
using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Flattening across more than one level, and the ways descending can go wrong.
    /// </summary>
    /// <remarks>
    /// Issue #56. Flattening resolved one level, so <c>CustomerFullName</c> found
    /// <c>Customer.FullName</c> and <c>CustomerAddressCity</c> found nothing and left the member at
    /// its default. AutoMapper and Mapperly both resolve it, and the failure is silent, which is why
    /// it is worth fixing rather than documenting.
    ///
    /// Descending is not free of hazards, so most of this file is about them: a type that refers to
    /// itself must not make the builder recurse forever, a name that could be read two ways needs a
    /// stated winner rather than whichever property came first, and a null anywhere along the path
    /// has to yield the destination default rather than throw.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class DeepFlatteningTests
    {
        public class DfCountry { public string Iso { get; set; } = ""; public string Name { get; set; } = ""; }
        public class DfAddress { public string City { get; set; } = ""; public DfCountry Country { get; set; } = new(); }
        public class DfCustomer { public string FullName { get; set; } = ""; public DfAddress Address { get; set; } = new(); }
        public class DfOrder { public int Id { get; set; } public DfCustomer Customer { get; set; } = new(); }

        private static DfOrder Sample() => new()
        {
            Id = 3,
            Customer = new DfCustomer
            {
                FullName = "Ada Lovelace",
                Address = new DfAddress
                {
                    City = "Cebu",
                    Country = new DfCountry { Iso = "PH", Name = "Philippines" },
                },
            },
        };

        public class DfTwoLevelDto { public string CustomerFullName { get; set; } = ""; }
        public class DfThreeLevelDto { public string CustomerAddressCity { get; set; } = ""; }
        public class DfFourLevelDto { public string CustomerAddressCountryIso { get; set; } = ""; }

        [Fact]
        public void TwoLevelsStillResolve()
        {
            // The control. Whatever descending changes, it must not break the level that worked.
            Mapper.ClearCache();

            Assert.Equal("Ada Lovelace", ((object)Sample()).MapTo<DfTwoLevelDto>()!.CustomerFullName);
        }

        [Fact]
        public void ThreeLevelsResolve()
        {
            Mapper.ClearCache();

            Assert.Equal("Cebu", ((object)Sample()).MapTo<DfThreeLevelDto>()!.CustomerAddressCity);
        }

        [Fact]
        public void FourLevelsResolve()
        {
            Mapper.ClearCache();

            Assert.Equal("PH", ((object)Sample()).MapTo<DfFourLevelDto>()!.CustomerAddressCountryIso);
        }

        public class DfNullAlongPathDto { public string CustomerAddressCity { get; set; } = ""; }

        [Fact]
        public void ANullPartWayAlongThePathYieldsTheDefault()
        {
            // Customer exists, Address does not. Reading through it must not throw.
            Mapper.ClearCache();
            var order = Sample();
            order.Customer.Address = null!;

            var dto = ((object)order).MapTo<DfNullAlongPathDto>();

            // Null rather than the member's initialiser, which is what AutoMapper does with the
            // same shape. Checked rather than assumed: the member is mapped, and what a mapped
            // member receives for a null path is the destination default.
            Assert.Null(dto!.CustomerAddressCity);
        }

        [Fact]
        public void ANullAtTheFirstStepYieldsTheDefault()
        {
            Mapper.ClearCache();
            var order = Sample();
            order.Customer = null!;

            Assert.Null(((object)order).MapTo<DfNullAlongPathDto>()!.CustomerAddressCity);
        }

        // A type holding itself. Descending without a ceiling would not terminate at build time.
        public class DfNode { public string Name { get; set; } = ""; public DfNode? Parent { get; set; } }
        public class DfNodeHolder { public DfNode Node { get; set; } = new(); }
        public class DfNodeDto { public string NodeName { get; set; } = ""; }
        public class DfDeepNodeDto { public string NodeParentName { get; set; } = ""; }

        [Fact]
        public void ASelfReferencingTypeDoesNotHangTheBuilder()
        {
            // If this test times out rather than fails, the descent has no ceiling.
            Mapper.ClearCache();
            var holder = new DfNodeHolder { Node = new DfNode { Name = "leaf" } };

            Assert.Equal("leaf", ((object)holder).MapTo<DfNodeDto>()!.NodeName);
        }

        [Fact]
        public void ASelfReferencingTypeStillFlattensThroughItself()
        {
            Mapper.ClearCache();
            var holder = new DfNodeHolder
            {
                Node = new DfNode { Name = "leaf", Parent = new DfNode { Name = "root" } },
            };

            Assert.Equal("root", ((object)holder).MapTo<DfDeepNodeDto>()!.NodeParentName);
        }

        // Two readings of one name. AddressCity is a real member, and Address.City is a real path.
        public class DfAmbiguousInner { public string City { get; set; } = "from-path"; }
        public class DfAmbiguousSource
        {
            public string AddressCity { get; set; } = "from-member";
            public DfAmbiguousInner Address { get; set; } = new();
        }
        public class DfAmbiguousDto { public string AddressCity { get; set; } = ""; }

        [Fact]
        public void ADirectMemberWinsOverAPathThatSpellsTheSameName()
        {
            // A member whose name matches exactly is not flattening at all, so it is resolved before
            // any descent is attempted. Stating it here because with more levels there are more
            // ways to spell the same destination name, and the winner should be written down rather
            // than left to whichever source property is enumerated first.
            Mapper.ClearCache();

            Assert.Equal("from-member", ((object)new DfAmbiguousSource()).MapTo<DfAmbiguousDto>()!.AddressCity);
        }

        [Fact]
        public void TheTypedDoorAgreesWithTheUntypedOne()
        {
            // Flattening had two implementations, one per builder, which is the drift shape this
            // project has shipped twice. They must not disagree.
            Mapper.ClearCache();
            var untyped = ((object)Sample()).MapTo<DfThreeLevelDto>();

            Mapper.ClearCache();
            var typed = Sample().MapTo<DfOrder, DfThreeLevelDto>();

            Assert.Equal(untyped!.CustomerAddressCity, typed!.CustomerAddressCity);
            Assert.Equal("Cebu", typed.CustomerAddressCity);
        }

        [Fact]
        public void TheInstanceMapperAgreesToo()
        {
            Mapper.ClearCache();
            using var instance = MapperFactory.Create();

            Assert.Equal("Cebu", instance.MapTo<DfThreeLevelDto>(Sample())!.CustomerAddressCity);
        }

        [Fact]
        public void ACollectionOfAggregatesFlattensEachElement()
        {
            Mapper.ClearCache();
            var orders = new List<DfOrder> { Sample(), Sample() };

            var dtos = ((IEnumerable)orders).MapTo<DfThreeLevelDto>();

            Assert.Equal(2, dtos.Count);
            Assert.All(dtos, d => Assert.Equal("Cebu", d.CustomerAddressCity));
        }
    }
}
