using System;
using System.Collections.Generic;
using System.Linq;
using Mapsicle;
using Mapsicle.EntityFramework;
using Mapsicle.Fluent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Mapsicle.EntityFramework.Tests
{
    public class NpIgAddress
    {
        public int Id { get; set; }
        public string City { get; set; } = "";
        public string GateCode { get; set; } = "";
    }

    public class NpIgCustomer
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Secret { get; set; } = "";
        public bool IsAdmin { get; set; }
        public NpIgAddress? Address { get; set; }
    }

    public class NpIgLine
    {
        public int Id { get; set; }
        public string Sku { get; set; } = "";
        public decimal Cost { get; set; }
        public int NpIgOrderId { get; set; }
    }

    public class NpIgOrder
    {
        public int Id { get; set; }
        public string Token { get; set; } = "";
        public NpIgCustomer? Customer { get; set; }
        public List<NpIgLine> Lines { get; set; } = new();
    }

    public class NpIgAddressDto
    {
        public string City { get; set; } = "";
        [IgnoreMap] public string? GateCode { get; set; }
    }

    public class NpIgCustomerDto
    {
        public string Name { get; set; } = "";
        [IgnoreMap] public string? Secret { get; set; }
        public bool IsAdmin { get; set; }
        public NpIgAddressDto? Address { get; set; }
    }

    public class NpIgLineDto
    {
        public string Sku { get; set; } = "";
        [IgnoreMap] public decimal Cost { get; set; }
    }

    public class NpIgOrderDto
    {
        public int Id { get; set; }
        [IgnoreMap] public string? Token { get; set; }
        public NpIgCustomerDto? Customer { get; set; }
        public List<NpIgLineDto> Lines { get; set; } = new();
    }

    public class NpIgNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public NpIgNode? Parent { get; set; }
    }

    public class NpIgNodeDto
    {
        public string Name { get; set; } = "";
        public NpIgNodeDto? Parent { get; set; }
    }

    public class NpIgContext : DbContext
    {
        public NpIgContext(DbContextOptions<NpIgContext> options) : base(options) { }
        public DbSet<NpIgOrder> Orders => Set<NpIgOrder>();
        public DbSet<NpIgNode> Nodes => Set<NpIgNode>();
    }

    [Collection("StaticMapperTests")]
    public class NestedProjectionIgnoreMapTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly NpIgContext _db;

        public NestedProjectionIgnoreMapTests()
        {
            Mapper.ClearCache();
            QueryableExtensions.ClearProjectionCache();
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _db = new NpIgContext(new DbContextOptionsBuilder<NpIgContext>().UseSqlite(_connection).Options);
            _db.Database.EnsureCreated();
            _db.Orders.Add(new NpIgOrder
            {
                Token = "tok",
                Customer = new NpIgCustomer
                {
                    Name = "ann",
                    Secret = "s3cret",
                    IsAdmin = true,
                    Address = new NpIgAddress { City = "Koronadal", GateCode = "4321" }
                },
                Lines = { new NpIgLine { Sku = "a", Cost = 9.5m }, new NpIgLine { Sku = "b", Cost = 3m } }
            });
            var root = new NpIgNode { Name = "root" };
            _db.Nodes.Add(new NpIgNode { Name = "leaf", Parent = root });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private NpIgOrderDto Project() => _db.Orders.ProjectTo<NpIgOrderDto>().Single();

        [Fact]
        public void Control_TopLevelIgnoreMapIsHonoured()
        {
            var dto = Project();
            Assert.Equal(1, dto.Id);
            Assert.Null(dto.Token);
        }

        [Fact]
        public void Control_NestedObjectStillMapsItsOtherMembers()
        {
            var customer = Project().Customer!;
            Assert.Equal("ann", customer.Name);
            Assert.True(customer.IsAdmin);
        }

        [Fact]
        public void NestedObject_HonoursIgnoreMap()
        {
            Assert.Null(Project().Customer!.Secret);
        }

        [Fact]
        public void NestedObject_TypedOverload_HonoursIgnoreMap()
        {
            var dto = _db.Orders.ProjectTo<NpIgOrder, NpIgOrderDto>().Single();
            Assert.Null(dto.Customer!.Secret);
        }

        [Fact]
        public void SecondLevelNestedObject_MapsAndHonoursIgnoreMap()
        {
            var address = Project().Customer!.Address;
            Assert.NotNull(address);
            Assert.Equal("Koronadal", address!.City);
            Assert.Null(address.GateCode);
        }

        [Fact]
        public void CollectionElements_MapAndHonourIgnoreMap()
        {
            var lines = Project().Lines.OrderBy(l => l.Sku).ToList();
            Assert.Equal(new[] { "a", "b" }, lines.Select(l => l.Sku).ToArray());
            Assert.All(lines, l => Assert.Equal(0m, l.Cost));
        }

        [Fact]
        public void NestedObject_HonoursFluentIgnoreOnTheNestedPair()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<NpIgOrder, NpIgOrderDto>();
                cfg.CreateMap<NpIgCustomer, NpIgCustomerDto>().ForMember(d => d.IsAdmin, o => o.Ignore());
            });

            var dto = _db.Orders.ProjectTo<NpIgOrder, NpIgOrderDto>(config).Single();

            Assert.Equal("ann", dto.Customer!.Name);
            Assert.False(dto.Customer.IsAdmin);
        }

        [Fact]
        public void Control_SelfReferencingTypeBuildsAndMapsOneLevel()
        {
            var leaf = _db.Nodes.Where(n => n.Name == "leaf").ProjectTo<NpIgNodeDto>().Single();
            Assert.Equal("leaf", leaf.Name);
            Assert.Equal("root", leaf.Parent!.Name);
        }
    }
}
