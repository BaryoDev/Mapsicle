using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// One order aggregate carrying every shape that makes real mapping hard, mapped in one call.
    /// </summary>
    /// <remarks>
    /// The rest of the suite tests one behaviour at a time, which is the right way to localise a
    /// failure and the wrong way to find one that only appears when several meet. This file is the
    /// other half: a single realistic graph with three levels of nesting, a collection inside a
    /// collection, a polymorphic list, an enum widening to a string, a dictionary, a nullable, a
    /// self referencing category and a cycle from the order back to itself through its customer.
    ///
    /// It came from running the same aggregate through Mapsicle, AutoMapper and Mapperly. Mapperly
    /// died on the cycle with a stack overflow, exit code 139, and AutoMapper materialised more than
    /// fifty levels before the measurement gave up. Mapsicle stopped at its depth ceiling and
    /// returned a usable object, which is the behaviour this file exists to keep.
    ///
    /// The one thing it does not assert is three level flattening, because that does not work. See
    /// issue #56; the case is here and marked so it is not mistaken for coverage.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class ComplexAggregateTests
    {
        public enum CxState { Draft, Shipped }
        public enum CxChannel { Web, Mobile }

        public class CxMoney { public decimal Amount { get; set; } public string Currency { get; set; } = "PHP"; }
        public class CxMoneyDto { public decimal Amount { get; set; } public string Currency { get; set; } = ""; }

        public class CxCountry { public string Iso { get; set; } = ""; }
        public class CxCountryDto { public string Iso { get; set; } = ""; }

        public class CxAddress { public string City { get; set; } = ""; public CxCountry Country { get; set; } = new(); }
        public class CxAddressDto { public string City { get; set; } = ""; public CxCountryDto Country { get; set; } = new(); }

        public class CxCustomer
        {
            public string FullName { get; set; } = "";
            public CxAddress Address { get; set; } = new();
            public List<CxOrder> Orders { get; set; } = new();
        }

        public class CxCustomerDto
        {
            public string FullName { get; set; } = "";
            public CxAddressDto Address { get; set; } = new();
            public List<CxOrderDto> Orders { get; set; } = new();
        }

        public abstract class CxPayment { public CxMoney Amount { get; set; } = new(); }
        public class CxCard : CxPayment { public string Last4 { get; set; } = ""; }
        public class CxWallet : CxPayment { public string Provider { get; set; } = ""; }
        public class CxPaymentDto { public CxMoneyDto Amount { get; set; } = new(); }

        public class CxCategory { public string Name { get; set; } = ""; public CxCategory? Parent { get; set; } }
        public class CxCategoryDto { public string Name { get; set; } = ""; }

        public class CxProduct { public string Sku { get; set; } = ""; public CxCategory Category { get; set; } = new(); }
        public class CxProductDto { public string Sku { get; set; } = ""; public CxCategoryDto Category { get; set; } = new(); }

        public class CxDiscount { public string Code { get; set; } = ""; }
        public class CxDiscountDto { public string Code { get; set; } = ""; }

        public class CxLine
        {
            public int Quantity { get; set; }
            public CxProduct Product { get; set; } = new();
            public List<CxDiscount> Discounts { get; set; } = new();
        }

        public class CxLineDto
        {
            public int Quantity { get; set; }
            public CxProductDto Product { get; set; } = new();
            public List<CxDiscountDto> Discounts { get; set; } = new();
        }

        public class CxOrder
        {
            public int Id { get; set; }
            public CxState State { get; set; }
            public CxChannel Channel { get; set; }
            public CxCustomer Customer { get; set; } = new();
            public List<CxLine> Lines { get; set; } = new();
            public List<CxPayment> Payments { get; set; } = new();
            public DateTime? ShippedOn { get; set; }
            public Dictionary<string, string> Metadata { get; set; } = new();
        }

        public class CxOrderDto
        {
            public long Id { get; set; }
            public string State { get; set; } = "";
            public CxChannel Channel { get; set; }
            public CxCustomerDto Customer { get; set; } = new();
            public List<CxLineDto> Lines { get; set; } = new();
            public List<CxPaymentDto> Payments { get; set; } = new();
            public DateTime? ShippedOn { get; set; }
            public Dictionary<string, string> Metadata { get; set; } = new();
            public string CustomerFullName { get; set; } = "";
            public string CustomerAddressCity { get; set; } = "";
        }

        private static CxOrder Build()
        {
            var electronics = new CxCategory { Name = "Electronics" };
            var phones = new CxCategory { Name = "Phones", Parent = electronics };

            var customer = new CxCustomer
            {
                FullName = "Ada Lovelace",
                Address = new CxAddress { City = "Cebu", Country = new CxCountry { Iso = "PH" } },
            };

            var order = new CxOrder
            {
                Id = 1001,
                State = CxState.Shipped,
                Channel = CxChannel.Mobile,
                Customer = customer,
                ShippedOn = new DateTime(2026, 8, 31),
                Metadata = { ["source"] = "campaign-7" },
                Lines =
                {
                    new CxLine
                    {
                        Quantity = 2,
                        Product = new CxProduct { Sku = "PH-1", Category = phones },
                        Discounts = { new CxDiscount { Code = "LAUNCH" } },
                    },
                    new CxLine { Quantity = 1, Product = new CxProduct { Sku = "AC-9", Category = electronics } },
                },
                Payments =
                {
                    new CxCard { Last4 = "4242", Amount = new CxMoney { Amount = 20000m } },
                    new CxWallet { Provider = "GCash", Amount = new CxMoney { Amount = 4999.50m } },
                },
            };

            customer.Orders.Add(order);
            return order;
        }

        [Fact]
        public void TheWholeAggregateMapsInOneCall()
        {
            Mapper.ClearCache();

            var dto = ((object)Build()).MapTo<CxOrderDto>();

            Assert.NotNull(dto);
            Assert.Equal(1001L, dto!.Id);                       // widening
            Assert.Equal("Shipped", dto.State);                 // enum to string
            Assert.Equal(CxChannel.Mobile, dto.Channel);        // enum to enum
            Assert.Equal(new DateTime(2026, 8, 31), dto.ShippedOn);
        }

        [Fact]
        public void ThreeLevelsOfNestingSurvive()
        {
            Mapper.ClearCache();

            var dto = ((object)Build()).MapTo<CxOrderDto>();

            Assert.Equal("Cebu", dto!.Customer.Address.City);
            Assert.Equal("PH", dto.Customer.Address.Country.Iso);
        }

        [Fact]
        public void ACollectionInsideACollectionSurvives()
        {
            Mapper.ClearCache();

            var dto = ((object)Build()).MapTo<CxOrderDto>();

            Assert.Equal(2, dto!.Lines.Count);
            Assert.Equal("PH-1", dto.Lines[0].Product.Sku);
            Assert.Equal("Phones", dto.Lines[0].Product.Category.Name);
            Assert.Single(dto.Lines[0].Discounts);
            Assert.Equal("LAUNCH", dto.Lines[0].Discounts[0].Code);
            Assert.Empty(dto.Lines[1].Discounts);
        }

        [Fact]
        public void APolymorphicCollectionMapsItsSharedMembers()
        {
            // The list is declared as the abstract base and holds two derived types. Only what the
            // destination declares is mapped, and neither element is dropped.
            Mapper.ClearCache();

            var dto = ((object)Build()).MapTo<CxOrderDto>();

            Assert.Equal(2, dto!.Payments.Count);
            Assert.Equal(20000m, dto.Payments[0].Amount.Amount);
            Assert.Equal(4999.50m, dto.Payments[1].Amount.Amount);
        }

        [Fact]
        public void ADictionaryMemberIsCarried()
        {
            Mapper.ClearCache();

            var dto = ((object)Build()).MapTo<CxOrderDto>();

            Assert.Single(dto!.Metadata);
            Assert.Equal("campaign-7", dto.Metadata["source"]);
        }

        [Fact]
        public void ACycleTerminatesAndReturnsAUsableObject()
        {
            // Order to Customer to Orders to Order. Mapperly overflows the stack on this graph and
            // takes the process with it, and AutoMapper builds past fifty levels without stopping.
            // The requirement here is narrow and worth stating: it comes back, and what it comes
            // back with is usable at the top.
            Mapper.ClearCache();

            var dto = ((object)Build()).MapTo<CxOrderDto>();

            Assert.NotNull(dto);
            Assert.Equal(1001L, dto!.Id);
            Assert.Equal("Ada Lovelace", dto.Customer.FullName);

            var depth = 0;
            var cursor = dto;
            while (cursor?.Customer?.Orders is { Count: > 0 } && depth < 200)
            {
                cursor = cursor.Customer.Orders[0];
                depth++;
            }

            Assert.True(depth < 200, $"the cycle did not terminate, reached depth {depth}");
        }

        [Fact]
        public void ASelfReferencingCategoryDoesNotPreventMapping()
        {
            // Category.Parent points at another Category. The destination has no Parent, so nothing
            // should follow it, but the builder still has to cope with the type.
            Mapper.ClearCache();

            var dto = ((object)Build()).MapTo<CxOrderDto>();

            Assert.Equal("Phones", dto!.Lines[0].Product.Category.Name);
            Assert.Equal("Electronics", dto.Lines[1].Product.Category.Name);
        }

        [Fact]
        public void TwoLevelFlatteningResolves()
        {
            Mapper.ClearCache();

            var dto = ((object)Build()).MapTo<CxOrderDto>();

            Assert.Equal("Ada Lovelace", dto!.CustomerFullName);
        }

        [Fact]
        public void ThreeLevelFlatteningDoesNotResolveYet()
        {
            // Recorded, not endorsed. AutoMapper and Mapperly both fill this from
            // Customer.Address.City and Mapsicle leaves it empty, because flattening descends one
            // level. Issue #56. Inverting this assertion is what closing that issue looks like.
            Mapper.ClearCache();

            var dto = ((object)Build()).MapTo<CxOrderDto>();

            Assert.Equal("", dto!.CustomerAddressCity);
        }

        [Fact]
        public void MappingTheSameAggregateTwiceAgrees()
        {
            // The second map runs against a warm cache and a compiled loop. Nothing about the graph
            // changed, so nothing about the result should.
            Mapper.ClearCache();

            var first = ((object)Build()).MapTo<CxOrderDto>();
            var second = ((object)Build()).MapTo<CxOrderDto>();

            Assert.Equal(first!.Id, second!.Id);
            Assert.Equal(first.State, second.State);
            Assert.Equal(first.Lines.Count, second.Lines.Count);
            Assert.Equal(first.Lines[0].Discounts.Count, second.Lines[0].Discounts.Count);
            Assert.Equal(first.Customer.Address.Country.Iso, second.Customer.Address.Country.Iso);
        }

        [Fact]
        public void MappingACollectionOfAggregatesAgreesWithMappingOneAtATime()
        {
            // The compiled list loop takes a different path from the single object door, and this
            // aggregate is the hardest element type in the suite to hand it.
            Mapper.ClearCache();

            var orders = new List<CxOrder> { Build(), Build(), Build() };

            var viaCollection = ((System.Collections.IEnumerable)orders).MapTo<CxOrderDto>();
            var oneAtATime = orders.Select(o => ((object)o).MapTo<CxOrderDto>()).ToList();

            Assert.Equal(3, viaCollection.Count);
            Assert.Equal(oneAtATime.Select(d => d!.Id), viaCollection.Select(d => d.Id));
            Assert.Equal(oneAtATime.Select(d => d!.State), viaCollection.Select(d => d.State));
            Assert.Equal(
                oneAtATime.Select(d => d!.Lines[0].Product.Category.Name),
                viaCollection.Select(d => d.Lines[0].Product.Category.Name));
        }
    }
}
