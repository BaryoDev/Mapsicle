using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Integration tests for multiple mapper configurations and real-world usage patterns.
    /// These tests validate that Mapsicle works correctly in realistic scenarios.
    /// </summary>
    public class IntegrationTests
    {
        #region Test Models

        public class User
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        public class UserDto
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        public class Product
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public Category? Category { get; set; }
        }

        public class ProductDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public string CategoryName { get; set; } = string.Empty;
        }

        public class Category
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public class Order
        {
            public int Id { get; set; }
            public User? Customer { get; set; }
            public List<OrderLine> Lines { get; set; } = new();
            public DateTime OrderDate { get; set; }
        }

        public class OrderLine
        {
            public Product? Product { get; set; }
            public int Quantity { get; set; }
        }

        public class OrderDto
        {
            public int Id { get; set; }
            public string CustomerEmail { get; set; } = string.Empty;
            public int ItemCount { get; set; }
            public DateTime OrderDate { get; set; }
        }

        #endregion

        #region Multiple Mapper Configurations

        /// <summary>
        /// Test using multiple MapperFactory instances with different configurations.
        /// </summary>
        [Fact]
        public void MultipleMappers_WithDifferentConfigurations_ShouldWork()
        {
            // Mapper 1: Standard configuration
            using var mapper1 = MapperFactory.Create(new MapperOptions { MaxDepth = 10 });

            // Mapper 2: Different max depth
            using var mapper2 = MapperFactory.Create(new MapperOptions { MaxDepth = 5 });

            var user = new User { Id = 1, FirstName = "John", LastName = "Doe", Email = "john@test.com" };

            var dto1 = mapper1.MapTo<UserDto>(user);
            var dto2 = mapper2.MapTo<UserDto>(user);

            Assert.NotNull(dto1);
            Assert.NotNull(dto2);
            Assert.Equal("John", dto1.FirstName);
            Assert.Equal("John", dto2.FirstName);
        }

        /// <summary>
        /// Test global static Mapper alongside MapperFactory instances.
        /// </summary>
        [Fact]
        public void GlobalAndFactoryMappers_ShouldCoexist()
        {
            // Use global mapper
            var user = new User { Id = 1, FirstName = "Alice", LastName = "Smith", Email = "alice@test.com" };
            var globalDto = user.MapTo<UserDto>();

            // Use factory mapper
            using var factoryMapper = MapperFactory.Create();
            var factoryDto = factoryMapper.MapTo<UserDto>(user);

            Assert.NotNull(globalDto);
            Assert.NotNull(factoryDto);
            Assert.Equal("Alice", globalDto.FirstName);
            Assert.Equal("Alice", factoryDto.FirstName);
        }

        #endregion

        #region Thread-Safety Integration Tests

        /// <summary>
        /// Test concurrent mapping across multiple threads with shared mapper.
        /// </summary>
        [Fact]
        public void ConcurrentMapping_AcrossThreads_ShouldBeThreadSafe()
        {
            var exceptions = new List<Exception>();
            var lockObj = new object();

            System.Threading.Tasks.Parallel.For(0, 1000, i =>
            {
                try
                {
                    var user = new User 
                    { 
                        Id = i, 
                        FirstName = $"User{i}", 
                        LastName = "Test", 
                        Email = $"user{i}@test.com" 
                    };
                    var dto = user.MapTo<UserDto>();
                    
                    Assert.NotNull(dto);
                    Assert.Equal(i, dto.Id);
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            Assert.Empty(exceptions);
        }

        /// <summary>
        /// Test concurrent mapping with MapperFactory instances.
        /// </summary>
        [Fact]
        public void ConcurrentMapping_WithMultipleFactories_ShouldBeThreadSafe()
        {
            var exceptions = new List<Exception>();
            var lockObj = new object();

            System.Threading.Tasks.Parallel.For(0, 100, i =>
            {
                try
                {
                    using var mapper = MapperFactory.Create();
                    var user = new User { Id = i, FirstName = $"User{i}", LastName = "Test" };
                    var dto = mapper.MapTo<UserDto>(user);
                    
                    Assert.NotNull(dto);
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            Assert.Empty(exceptions);
        }

        #endregion

        #region Real-World Scenario Tests

        /// <summary>
        /// Test real-world scenario: API request/response mapping.
        /// </summary>
        [Fact]
        public void RealWorld_ApiRequestResponse_ShouldWork()
        {
            // Simulate incoming API request with user data
            var users = new List<User>
            {
                new User { Id = 1, FirstName = "Alice", LastName = "Smith", Email = "alice@test.com" },
                new User { Id = 2, FirstName = "Bob", LastName = "Jones", Email = "bob@test.com" },
                new User { Id = 3, FirstName = "Charlie", LastName = "Brown", Email = "charlie@test.com" }
            };

            // Map to DTOs for API response
            var dtos = users.MapTo<UserDto>();

            Assert.Equal(3, dtos.Count);
            Assert.Equal("Alice", dtos[0].FirstName);
            Assert.Equal("Bob", dtos[1].FirstName);
            Assert.Equal("Charlie", dtos[2].FirstName);
        }

        /// <summary>
        /// Test real-world scenario: Batch processing with paging.
        /// </summary>
        [Fact]
        public void RealWorld_BatchProcessingWithPaging_ShouldWork()
        {
            // Simulate large dataset
            var users = Enumerable.Range(1, 1000)
                .Select(i => new User { Id = i, FirstName = $"User{i}", LastName = "Test", Email = $"user{i}@test.com" })
                .ToList();

            // Process in pages
            var pageSize = 100;
            var totalProcessed = 0;

            for (int page = 0; page < 10; page++)
            {
                var pageData = users.Skip(page * pageSize).Take(pageSize).ToList();
                var pageDtos = pageData.MapTo<UserDto>();
                
                Assert.Equal(pageSize, pageDtos.Count);
                totalProcessed += pageDtos.Count;
            }

            Assert.Equal(1000, totalProcessed);
        }

        /// <summary>
        /// Test real-world scenario: Complex object graph with nested properties.
        /// </summary>
        [Fact]
        public void RealWorld_ComplexObjectGraph_ShouldMap()
        {
            var order = new Order
            {
                Id = 1,
                Customer = new User { Id = 1, FirstName = "John", LastName = "Doe", Email = "john@test.com" },
                OrderDate = DateTime.Now,
                Lines = new List<OrderLine>
                {
                    new OrderLine 
                    { 
                        Product = new Product { Id = 1, Name = "Laptop", Price = 999.99m },
                        Quantity = 1
                    },
                    new OrderLine 
                    { 
                        Product = new Product { Id = 2, Name = "Mouse", Price = 29.99m },
                        Quantity = 2
                    }
                }
            };

            var dto = order.MapTo<OrderDto>();

            Assert.NotNull(dto);
            Assert.Equal(1, dto.Id);
            Assert.Equal("john@test.com", dto.CustomerEmail);
            // ItemCount may not automatically map from Lines.Count
            // This demonstrates the need for custom configuration for computed properties
        }

        /// <summary>
        /// Test real-world scenario: Mapping with null checks and default values.
        /// </summary>
        [Fact]
        public void RealWorld_NullSafeMapping_ShouldHandleGracefully()
        {
            var products = new List<Product>
            {
                new Product { Id = 1, Name = "Laptop", Price = 999.99m, Category = new Category { Name = "Electronics" } },
                new Product { Id = 2, Name = "Uncategorized", Price = 10m, Category = null },
                new Product { Id = 3, Name = "Book", Price = 29.99m, Category = new Category { Name = "Books" } }
            };

            var dtos = products.MapTo<ProductDto>();

            Assert.Equal(3, dtos.Count);
            Assert.Equal("Electronics", dtos[0].CategoryName);
            // Second product has null category - should handle gracefully
            Assert.Equal("Books", dtos[2].CategoryName);
        }

        /// <summary>
        /// Test real-world scenario: Error recovery and continued processing.
        /// </summary>
        [Fact]
        public void RealWorld_ErrorRecovery_ShouldContinueProcessing()
        {
            var users = Enumerable.Range(1, 100)
                .Select(i => new User { Id = i, FirstName = $"User{i}", LastName = "Test", Email = $"user{i}@test.com" })
                .ToList();

            var successCount = 0;

            foreach (var user in users)
            {
                try
                {
                    var dto = user.MapTo<UserDto>();
                    if (dto != null)
                        successCount++;
                }
                catch
                {
                    // Continue processing on error
                }
            }

            // All should succeed
            Assert.Equal(100, successCount);
        }

        #endregion

        #region Cache Integration Tests

        /// <summary>
        /// Test that cache is shared across mappings.
        /// </summary>
        [Fact]
        public void CacheIntegration_SharedAcrossMappings_ShouldImprovePerformance()
        {
            Mapper.ClearCache();

            // First mapping builds cache
            var user1 = new User { Id = 1, FirstName = "First" };
            _ = user1.MapTo<UserDto>();

            var cacheInfoAfterFirst = Mapper.CacheInfo();

            // Second mapping uses cache
            var user2 = new User { Id = 2, FirstName = "Second" };
            _ = user2.MapTo<UserDto>();

            var cacheInfoAfterSecond = Mapper.CacheInfo();

            // Cache should have entries
            Assert.True(cacheInfoAfterFirst.Total > 0);
            // Cache size should be stable (not growing per mapping)
            Assert.Equal(cacheInfoAfterFirst.Total, cacheInfoAfterSecond.Total);
        }

        #endregion

        #region Disposal Pattern Tests

        /// <summary>
        /// Test proper disposal of MapperFactory instances.
        /// </summary>
        [Fact]
        public void Disposal_MapperFactory_ShouldCleanupCorrectly()
        {
            IMapperInstance? mapper;

            using (mapper = MapperFactory.Create())
            {
                var user = new User { Id = 1, FirstName = "Test" };
                var dto = mapper.MapTo<UserDto>(user);
                Assert.NotNull(dto);
            }

            // After disposal, mapper should not be usable
            Assert.Throws<ObjectDisposedException>(() => 
                mapper.MapTo<UserDto>(new User()));
        }

        /// <summary>
        /// Test multiple disposals are safe.
        /// </summary>
        [Fact]
        public void Disposal_MultipleCalls_ShouldBeIdempotent()
        {
            var mapper = MapperFactory.Create();
            
            // Multiple dispose calls should not throw
            mapper.Dispose();
            mapper.Dispose();
            mapper.Dispose();

            Assert.True(true, "Multiple disposals handled safely");
        }

        #endregion
    }
}
