using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Mapsicle.DependencyInjection;
using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.Docs.Tests
{
    /// <summary>
    /// Every code sample in docs/migrating-from-automapper.md, compiled and executed.
    /// </summary>
    /// <remarks>
    /// A migration guide is read by someone deciding whether to trust the library with a codebase,
    /// so a sample that no longer compiles costs more than a missing page. These run on every
    /// build for the same reason the performance claim does.
    ///
    /// If you change a sample in the guide, change it here in the same commit.
    /// </remarks>
    public class MigrationGuideSamplesTests
    {
        [Fact]
        public void Registration_And_Injection_Sample()
        {
            var services = new ServiceCollection();
            services.AddMapsicle();
            services.AddTransient<OrderHandler>();

            var handler = services.BuildServiceProvider().GetRequiredService<OrderHandler>();

            var dto = handler.Handle(new Order { Id = 3, Reference = "ORD-3" });

            Assert.Equal(3, dto.Id);
            Assert.Equal("ORD-3", dto.Reference);
        }

        [Fact]
        public void DeletingTheProfile_StillMaps_IncludingNestedAndFlattened()
        {
            // The claim the guide makes about profiles: matching names map by convention, nested
            // objects included, and Address.City fills AddressCity.
            Mapper.ClearCache();

            var order = new Order
            {
                Id = 1,
                Reference = "ORD-1",
                Customer = new Customer { Name = "arnel" },
                Address = new Address { City = "Manila" },
            };

            var dto = ((object)order).MapTo<OrderWithNestedDto>();

            Assert.NotNull(dto);
            Assert.Equal("arnel", dto!.Customer?.Name);
            Assert.Equal("Manila", dto.AddressCity);
        }

        [Fact]
        public void ForMember_And_Ignore_Sample()
        {
            var config = new MapperConfiguration(c =>
                c.CreateMap<OrderWithLines, OrderTotalDto>()
                    .ForMember(d => d.Total, o => o.MapFrom(s => s.Lines.Sum(l => l.Price)))
                    .ForMember(d => d.InternalNote, o => o.Ignore()));

            var mapper = config.CreateMapper();

            var dto = mapper.Map<OrderTotalDto>(new OrderWithLines
            {
                Lines = new List<Line> { new() { Price = 10m }, new() { Price = 5m } },
                InternalNote = "do not ship this to the client",
            });

            Assert.Equal(15m, dto!.Total);
            Assert.Null(dto.InternalNote);
        }

        [Fact]
        public void AssertMappingValid_And_GetUnmappedProperties_Sample()
        {
            Mapper.ClearCache();

            var unmapped = Mapper.GetUnmappedProperties<Order, OrderDto>();
            Assert.Empty(unmapped);

            // The other half of the guide's claim: it actually reports a member convention misses.
            var missing = Mapper.GetUnmappedProperties<Order, OrderWithUnmappedDto>();
            Assert.Contains("SomethingConventionCannotFind", missing);
        }

        [Fact]
        public void ANullSource_MapsToNull_RatherThanThrowing()
        {
            // The guide tells the reader the return is nullable and why.
            Mapper.ClearCache();

            Order? order = null;
            var dto = ((object?)order).MapTo<OrderDto>();

            Assert.Null(dto);
        }

        [Fact]
        public void CoerceDictionaryValues_RestoresParsing_AsTheGuideSays()
        {
            var previous = Mapper.CoerceDictionaryValues;
            try
            {
                var dict = new Dictionary<string, object?> { ["Id"] = "7" };

                Mapper.CoerceDictionaryValues = false;
                Assert.Equal(0, dict.MapTo<OrderDto>()!.Id);

                Mapper.CoerceDictionaryValues = true;
                Assert.Equal(7, dict.MapTo<OrderDto>()!.Id);
            }
            finally
            {
                Mapper.CoerceDictionaryValues = previous;
            }
        }

        // The handler as written in the guide.
        public class OrderHandler
        {
            private readonly IMapperInstance _mapper;
            public OrderHandler(IMapperInstance mapper) => _mapper = mapper;

            public OrderDto Handle(Order order) => _mapper.MapTo<OrderDto>(order)!;
        }

        public class Order
        {
            public int Id { get; set; }
            public string? Reference { get; set; }
            public Customer? Customer { get; set; }
            public Address? Address { get; set; }
        }

        public class Customer { public string? Name { get; set; } }
        public class CustomerDto { public string? Name { get; set; } }
        public class Address { public string? City { get; set; } }

        public class OrderDto { public int Id { get; set; } public string? Reference { get; set; } }

        public class OrderWithNestedDto
        {
            public int Id { get; set; }
            public string? Reference { get; set; }
            public CustomerDto? Customer { get; set; }
            public string? AddressCity { get; set; }
        }

        public class OrderWithUnmappedDto
        {
            public int Id { get; set; }
            public string? SomethingConventionCannotFind { get; set; }
        }

        public class Line { public decimal Price { get; set; } }
        public class OrderWithLines
        {
            public List<Line> Lines { get; set; } = new();
            public string? InternalNote { get; set; }
        }
        public class OrderTotalDto
        {
            public decimal Total { get; set; }
            public string? InternalNote { get; set; }
        }
    }
}
