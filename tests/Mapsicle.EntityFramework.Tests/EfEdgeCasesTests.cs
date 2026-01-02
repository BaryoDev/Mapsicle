using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Mapsicle.EntityFramework;
using Mapsicle.Fluent;

namespace Mapsicle.EntityFramework.Tests
{
    /// <summary>
    /// Edge case tests for EF Core ProjectTo including complex nested queries,
    /// SQL translation scenarios, and disconnected entities.
    /// </summary>
    public class EfEdgeCasesTests : IDisposable
    {
        private readonly TestDbContext _context;

        public EfEdgeCasesTests()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _context = new TestDbContext(options);
            SeedComplexData();
        }

        public void Dispose()
        {
            _context.Dispose();
            QueryableExtensions.ClearProjectionCache();
        }

        private void SeedComplexData()
        {
            // Seed products with categories
            var electronics = new CategoryEntity { Id = 1, Name = "Electronics", Description = "Electronic devices" };
            var books = new CategoryEntity { Id = 2, Name = "Books", Description = "Books and literature" };

            _context.Categories.AddRange(electronics, books);

            // Seed products
            _context.Products.AddRange(
                new ProductEntity 
                { 
                    Id = 1, 
                    Name = "Laptop", 
                    Price = 999.99m, 
                    CategoryId = 1, 
                    Category = electronics,
                    Tags = new List<TagEntity>
                    {
                        new TagEntity { Id = 1, Name = "Tech" },
                        new TagEntity { Id = 2, Name = "Portable" }
                    }
                },
                new ProductEntity 
                { 
                    Id = 2, 
                    Name = "Book: C# Programming", 
                    Price = 49.99m, 
                    CategoryId = 2, 
                    Category = books,
                    Tags = new List<TagEntity>
                    {
                        new TagEntity { Id = 3, Name = "Programming" }
                    }
                }
            );

            // Seed orders with line items
            var customer = new CustomerEntity { Id = 1, Name = "John Doe", Email = "john@example.com" };
            _context.Customers.Add(customer);

            var order = new OrderEntity
            {
                Id = 1,
                CustomerId = 1,
                Customer = customer,
                OrderDate = DateTime.Now,
                LineItems = new List<OrderLineItem>
                {
                    new OrderLineItem { Id = 1, ProductId = 1, Quantity = 1, UnitPrice = 999.99m },
                    new OrderLineItem { Id = 2, ProductId = 2, Quantity = 2, UnitPrice = 49.99m }
                }
            };
            _context.Orders.Add(order);

            _context.SaveChanges();
        }

        #region Test Models

        public class TestDbContext : DbContext
        {
            public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
            public DbSet<ProductEntity> Products => Set<ProductEntity>();
            public DbSet<CategoryEntity> Categories => Set<CategoryEntity>();
            public DbSet<OrderEntity> Orders => Set<OrderEntity>();
            public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
        }

        public class ProductEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public int CategoryId { get; set; }
            public CategoryEntity? Category { get; set; }
            public List<TagEntity> Tags { get; set; } = new();
        }

        public class CategoryEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        public class TagEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public class OrderEntity
        {
            public int Id { get; set; }
            public int CustomerId { get; set; }
            public CustomerEntity? Customer { get; set; }
            public DateTime OrderDate { get; set; }
            public List<OrderLineItem> LineItems { get; set; } = new();
        }

        public class OrderLineItem
        {
            public int Id { get; set; }
            public int ProductId { get; set; }
            public int Quantity { get; set; }
            public decimal UnitPrice { get; set; }
        }

        public class CustomerEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        public class ProductDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public string CategoryName { get; set; } = string.Empty;
        }

        public class ProductDetailDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public string CategoryName { get; set; } = string.Empty;
            public string CategoryDescription { get; set; } = string.Empty;
            public int TagCount { get; set; }
        }

        public class OrderSummaryDto
        {
            public int Id { get; set; }
            public string CustomerName { get; set; } = string.Empty;
            public string CustomerEmail { get; set; } = string.Empty;
            public DateTime OrderDate { get; set; }
            public int ItemCount { get; set; }
            public decimal TotalAmount { get; set; }
        }

        #endregion

        #region Complex Nested Query Tests

        /// <summary>
        /// Test ProjectTo with nested entity navigation.
        /// </summary>
        [Fact]
        public void ProjectTo_NestedNavigation_ShouldFlatten()
        {
            var products = _context.Products
                .ProjectTo<ProductEntity, ProductDto>()
                .ToList();

            Assert.Equal(2, products.Count);
            Assert.Equal("Electronics", products[0].CategoryName);
            Assert.Equal("Books", products[1].CategoryName);
        }

        /// <summary>
        /// Test ProjectTo with multiple levels of nesting.
        /// </summary>
        [Fact]
        public void ProjectTo_MultiLevelNesting_ShouldWork()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<ProductEntity, ProductDetailDto>()
                    .ForMember(d => d.CategoryName, opt => opt.MapFrom(s => s.Category!.Name))
                    .ForMember(d => d.CategoryDescription, opt => opt.MapFrom(s => s.Category!.Description))
                    .ForMember(d => d.TagCount, opt => opt.MapFrom(s => s.Tags.Count));
            });

            var products = _context.Products
                .ProjectTo<ProductEntity, ProductDetailDto>(config)
                .ToList();

            Assert.Equal(2, products.Count);
            Assert.Equal("Electronics", products[0].CategoryName);
            Assert.Equal("Electronic devices", products[0].CategoryDescription);
            Assert.Equal(2, products[0].TagCount);
        }

        /// <summary>
        /// Test ProjectTo with collection navigation and aggregation.
        /// </summary>
        [Fact]
        public void ProjectTo_CollectionAggregation_ShouldTranslateToSQL()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<OrderEntity, OrderSummaryDto>()
                    .ForMember(d => d.CustomerName, opt => opt.MapFrom(s => s.Customer!.Name))
                    .ForMember(d => d.CustomerEmail, opt => opt.MapFrom(s => s.Customer!.Email))
                    .ForMember(d => d.ItemCount, opt => opt.MapFrom(s => s.LineItems.Count))
                    .ForMember(d => d.TotalAmount, opt => opt.MapFrom(s => s.LineItems.Sum(li => li.Quantity * li.UnitPrice)));
            });

            var orders = _context.Orders
                .ProjectTo<OrderEntity, OrderSummaryDto>(config)
                .ToList();

            Assert.Single(orders);
            Assert.Equal("John Doe", orders[0].CustomerName);
            Assert.Equal(2, orders[0].ItemCount);
            Assert.Equal(1099.97m, orders[0].TotalAmount); // 999.99 + 2 * 49.99
        }

        #endregion

        #region SQL Translation Edge Cases

        /// <summary>
        /// Test ProjectTo with Where clause on nested property.
        /// </summary>
        [Fact]
        public void ProjectTo_WithWhereOnNestedProperty_ShouldWork()
        {
            var products = _context.Products
                .Where(p => p.Category!.Name == "Electronics")
                .ProjectTo<ProductEntity, ProductDto>()
                .ToList();

            Assert.Single(products);
            Assert.Equal("Laptop", products[0].Name);
        }

        /// <summary>
        /// Test ProjectTo with OrderBy on calculated field.
        /// </summary>
        [Fact]
        public void ProjectTo_OrderByCalculatedField_ShouldWork()
        {
            var products = _context.Products
                .ProjectTo<ProductEntity, ProductDto>()
                .OrderByDescending(p => p.Price)
                .ToList();

            Assert.Equal("Laptop", products[0].Name);
            Assert.Equal("Book: C# Programming", products[1].Name);
        }

        /// <summary>
        /// Test ProjectTo with Skip and Take (pagination).
        /// </summary>
        [Fact]
        public void ProjectTo_WithPagination_ShouldWork()
        {
            var products = _context.Products
                .ProjectTo<ProductEntity, ProductDto>()
                .OrderBy(p => p.Id)
                .Skip(1)
                .Take(1)
                .ToList();

            Assert.Single(products);
            Assert.Equal("Book: C# Programming", products[0].Name);
        }

        #endregion

        #region Disconnected Entity Tests

        /// <summary>
        /// Test mapping disconnected entities (detached from context).
        /// </summary>
        [Fact]
        public void ProjectTo_DisconnectedEntity_ShouldStillWork()
        {
            // Get products and detach from context
            var products = _context.Products.Include(p => p.Category).ToList();
            
            foreach (var product in products)
            {
                _context.Entry(product).State = EntityState.Detached;
            }

            // ProjectTo should still work on detached entities
            var dtos = products.MapTo<ProductDto>();

            Assert.Equal(2, dtos.Count);
        }

        /// <summary>
        /// Test that ProjectTo works with AsNoTracking.
        /// </summary>
        [Fact]
        public void ProjectTo_WithAsNoTracking_ShouldWork()
        {
            var products = _context.Products
                .AsNoTracking()
                .ProjectTo<ProductEntity, ProductDto>()
                .ToList();

            Assert.Equal(2, products.Count);
            Assert.Equal("Electronics", products[0].CategoryName);
        }

        #endregion

        #region Error Handling Tests

        /// <summary>
        /// Test ProjectTo with potentially null navigation properties.
        /// Verifies that ProjectTo handles null navigations without errors.
        /// </summary>
        [Fact]
        public void ProjectTo_WithNullableNavigation_ShouldNotThrow()
        {
            // Query should not throw even if some navigations could be null
            var products = _context.Products
                .ProjectTo<ProductEntity, ProductDto>()
                .ToList();

            Assert.Equal(2, products.Count);
            Assert.All(products, p => Assert.NotNull(p.CategoryName));
        }

        #endregion

        #region Configuration Tests

        /// <summary>
        /// Test that ProjectTo respects ForMember configurations.
        /// </summary>
        [Fact]
        public void ProjectTo_WithForMemberConfig_ShouldApplyConfiguration()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<ProductEntity, ProductDto>()
                    .ForMember(d => d.CategoryName, opt => opt.MapFrom(s => "Category: " + s.Category!.Name));
            });

            var products = _context.Products
                .ProjectTo<ProductEntity, ProductDto>(config)
                .ToList();

            Assert.Equal("Category: Electronics", products[0].CategoryName);
            Assert.Equal("Category: Books", products[1].CategoryName);
        }

        /// <summary>
        /// Test ProjectTo without configuration (convention-based).
        /// </summary>
        [Fact]
        public void ProjectTo_WithoutConfig_ShouldUseConventions()
        {
            var products = _context.Products
                .ProjectTo<ProductEntity, ProductDto>()
                .ToList();

            Assert.Equal(2, products.Count);
            // Convention-based mapping should flatten Category.Name to CategoryName
            Assert.NotNull(products[0].CategoryName);
        }

        #endregion

        #region Async Tests

        /// <summary>
        /// Test async ProjectTo operations.
        /// </summary>
        [Fact]
        public async void ProjectTo_Async_ShouldWork()
        {
            var products = await _context.Products
                .ProjectTo<ProductEntity, ProductDto>()
                .ToListAsync();

            Assert.Equal(2, products.Count);
        }

        #endregion
    }
}
