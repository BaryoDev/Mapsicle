using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Mapsicle.EntityFramework;

namespace Mapsicle.EntityFramework.Tests
{
    public class ProjectToTests : IDisposable
    {
        private readonly TestDbContext _context;

        public ProjectToTests()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _context = new TestDbContext(options);
            SeedData();
        }

        public void Dispose()
        {
            _context.Dispose();
            QueryableExtensions.ClearProjectionCache();
        }

        private void SeedData()
        {
            _context.Users.AddRange(
                new UserEntity { Id = 1, FirstName = "Alice", LastName = "Smith", Email = "alice@test.com", IsActive = true },
                new UserEntity { Id = 2, FirstName = "Bob", LastName = "Jones", Email = "bob@test.com", IsActive = false }
            );

            _context.Orders.AddRange(
                new OrderEntity
                {
                    Id = 1,
                    CustomerId = 1,
                    Customer = new CustomerEntity { Id = 1, Name = "TechCorp", Email = "tech@corp.com" },
                    OrderDate = new DateTime(2024, 1, 15),
                    Total = 199.99m
                },
                new OrderEntity
                {
                    Id = 2,
                    CustomerId = 2,
                    Customer = new CustomerEntity { Id = 2, Name = "StartupInc", Email = "hello@startup.com" },
                    OrderDate = new DateTime(2024, 2, 20),
                    Total = 499.99m
                }
            );

            _context.SaveChanges();
        }

        #region Test Models

        public class TestDbContext : DbContext
        {
            public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
            public DbSet<UserEntity> Users => Set<UserEntity>();
            public DbSet<OrderEntity> Orders => Set<OrderEntity>();
        }

        public class UserEntity
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }

        public class UserDto
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }

        public class CustomerEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        public class OrderEntity
        {
            public int Id { get; set; }
            public int CustomerId { get; set; }
            public CustomerEntity? Customer { get; set; }
            public DateTime OrderDate { get; set; }
            public decimal Total { get; set; }
        }

        // Flattened DTO
        public class OrderFlatDto
        {
            public int Id { get; set; }
            public string CustomerName { get; set; } = string.Empty;
            public string CustomerEmail { get; set; } = string.Empty;
            public DateTime OrderDate { get; set; }
            public decimal Total { get; set; }
        }

        public class OrderDto
        {
            public int Id { get; set; }
            public DateTime OrderDate { get; set; }
            public decimal Total { get; set; }
        }

        #endregion

        [Fact]
        public void ProjectTo_BasicProperties_ShouldWork()
        {
            var dtos = _context.Users
                .ProjectTo<UserEntity, UserDto>()
                .ToList();

            Assert.Equal(2, dtos.Count);
            Assert.Contains(dtos, d => d.FirstName == "Alice" && d.Email == "alice@test.com");
            Assert.Contains(dtos, d => d.FirstName == "Bob" && d.Email == "bob@test.com");
        }

        [Fact]
        public void ProjectTo_WithWhere_ShouldFilter()
        {
            var dtos = _context.Users
                .Where(u => u.IsActive)
                .ProjectTo<UserEntity, UserDto>()
                .ToList();

            Assert.Single(dtos);
            Assert.Equal("Alice", dtos[0].FirstName);
        }

        [Fact]
        public void ProjectTo_Flattening_ShouldMapNestedProperties()
        {
            var dtos = _context.Orders
                .ProjectTo<OrderEntity, OrderFlatDto>()
                .ToList();

            Assert.Equal(2, dtos.Count);
            
            var techOrder = dtos.First(d => d.Id == 1);
            Assert.Equal("TechCorp", techOrder.CustomerName);
            Assert.Equal("tech@corp.com", techOrder.CustomerEmail);
            Assert.Equal(199.99m, techOrder.Total);
        }

        [Fact]
        public void ProjectTo_OrderBy_ShouldWork()
        {
            var dtos = _context.Users
                .OrderByDescending(u => u.Id)
                .ProjectTo<UserEntity, UserDto>()
                .ToList();

            Assert.Equal("Bob", dtos[0].FirstName);
            Assert.Equal("Alice", dtos[1].FirstName);
        }

        [Fact]
        public void ProjectTo_Take_ShouldLimit()
        {
            var dtos = _context.Users
                .OrderBy(u => u.Id)
                .Take(1)
                .ProjectTo<UserEntity, UserDto>()
                .ToList();

            Assert.Single(dtos);
            Assert.Equal("Alice", dtos[0].FirstName);
        }

        [Fact]
        public void ProjectTo_SimpleDto_ShouldWork()
        {
            var dtos = _context.Orders
                .ProjectTo<OrderEntity, OrderDto>()
                .ToList();

            Assert.Equal(2, dtos.Count);
            Assert.Contains(dtos, d => d.Total == 199.99m);
            Assert.Contains(dtos, d => d.Total == 499.99m);
        }

        [Fact]
        public void ProjectTo_NonGeneric_ShouldWork()
        {
            IQueryable query = _context.Users;
            var dtos = query.ProjectTo<UserDto>().ToList();

            Assert.Equal(2, dtos.Count);
        }
    }
}
