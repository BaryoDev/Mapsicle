using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Production-like test scenarios simulating real-world mapping use cases.
    /// </summary>
    public class ProductionScenarioTests
    {
        #region E-Commerce Domain Models

        public class Customer
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public CustomerAddress? ShippingAddress { get; set; }
            public CustomerAddress? BillingAddress { get; set; }
            public DateTime CreatedAt { get; set; }
            public bool IsActive { get; set; }
        }

        public class CustomerAddress
        {
            public string Street { get; set; } = string.Empty;
            public string City { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string ZipCode { get; set; } = string.Empty;
            public string Country { get; set; } = string.Empty;
        }

        public class Order
        {
            public int Id { get; set; }
            public int CustomerId { get; set; }
            public Customer? Customer { get; set; }
            public DateTime OrderDate { get; set; }
            public OrderStatus Status { get; set; }
            public List<OrderItem> Items { get; set; } = new();
            public decimal TotalAmount { get; set; }
            public string? Notes { get; set; }
        }

        public class OrderItem
        {
            public int Id { get; set; }
            public int ProductId { get; set; }
            public string ProductName { get; set; } = string.Empty;
            public int Quantity { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal LineTotal { get; set; }
        }

        public enum OrderStatus
        {
            Pending = 0,
            Processing = 1,
            Shipped = 2,
            Delivered = 3,
            Cancelled = 4
        }

        #endregion

        #region DTOs

        public class CustomerDto
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public AddressDto? ShippingAddress { get; set; }
            public bool IsActive { get; set; }
        }

        public class AddressDto
        {
            public string Street { get; set; } = string.Empty;
            public string City { get; set; } = string.Empty;
            public string ZipCode { get; set; } = string.Empty;
        }

        public class OrderSummaryDto
        {
            public int Id { get; set; }
            public string CustomerFirstName { get; set; } = string.Empty;
            public string CustomerEmail { get; set; } = string.Empty;
            public DateTime OrderDate { get; set; }
            public int Status { get; set; } // Enum -> int
            public int ItemCount { get; set; }
            public string TotalAmount { get; set; } = string.Empty; // decimal -> string
        }

        public class OrderDetailDto
        {
            public int Id { get; set; }
            public CustomerDto? Customer { get; set; }
            public DateTime OrderDate { get; set; }
            public OrderStatus Status { get; set; }
            public List<OrderItemDto> Items { get; set; } = new();
            public decimal TotalAmount { get; set; }
        }

        public class OrderItemDto
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; } = string.Empty;
            public int Quantity { get; set; }
            public string UnitPrice { get; set; } = string.Empty;
        }

        #endregion

        #region API Response Models

        public class ApiResponse<T>
        {
            public bool Success { get; set; }
            public string? Message { get; set; }
            public T? Data { get; set; }
            public List<string> Errors { get; set; } = new();
        }

        #endregion

        [Fact]
        public void Customer_FullMapping_WithNestedAddress()
        {
            var customer = new Customer
            {
                Id = 1,
                FirstName = "Juan",
                LastName = "Dela Cruz",
                Email = "juan@example.com",
                ShippingAddress = new CustomerAddress
                {
                    Street = "123 Main St",
                    City = "Manila",
                    State = "NCR",
                    ZipCode = "1000",
                    Country = "Philippines"
                },
                CreatedAt = DateTime.Now,
                IsActive = true
            };

            var dto = customer.MapTo<CustomerDto>();

            Assert.NotNull(dto);
            Assert.Equal(1, dto.Id);
            Assert.Equal("Juan", dto.FirstName);
            Assert.True(dto.IsActive);
            Assert.NotNull(dto.ShippingAddress);
            Assert.Equal("Manila", dto.ShippingAddress.City);
            Assert.Equal("1000", dto.ShippingAddress.ZipCode);
        }

        [Fact]
        public void Order_WithItems_CollectionMapping()
        {
            var order = new Order
            {
                Id = 100,
                CustomerId = 1,
                Customer = new Customer { Id = 1, FirstName = "Alice", Email = "alice@test.com" },
                OrderDate = new DateTime(2024, 12, 25),
                Status = OrderStatus.Processing,
                Items = new List<OrderItem>
                {
                    new() { Id = 1, ProductId = 10, ProductName = "Widget", Quantity = 2, UnitPrice = 29.99m, LineTotal = 59.98m },
                    new() { Id = 2, ProductId = 20, ProductName = "Gadget", Quantity = 1, UnitPrice = 149.99m, LineTotal = 149.99m },
                    new() { Id = 3, ProductId = 30, ProductName = "Gizmo", Quantity = 5, UnitPrice = 9.99m, LineTotal = 49.95m }
                },
                TotalAmount = 259.92m
            };

            var dto = order.MapTo<OrderDetailDto>();

            Assert.NotNull(dto);
            Assert.Equal(100, dto.Id);
            Assert.NotNull(dto.Customer);
            Assert.Equal("Alice", dto.Customer.FirstName);
            Assert.Equal(3, dto.Items.Count);
            Assert.Equal("Widget", dto.Items[0].ProductName);
            Assert.Equal("29.99", dto.Items[0].UnitPrice); // decimal -> string
            Assert.Equal(259.92m, dto.TotalAmount);
        }

        [Fact]
        public void Order_Flattening_CustomerProperties()
        {
            var order = new Order
            {
                Id = 200,
                Customer = new Customer { FirstName = "Bob", Email = "bob@test.com" },
                OrderDate = DateTime.Now,
                Status = OrderStatus.Shipped,
                TotalAmount = 99.99m
            };

            var summary = order.MapTo<OrderSummaryDto>();

            Assert.NotNull(summary);
            Assert.Equal(200, summary.Id);
            Assert.Equal("Bob", summary.CustomerFirstName); // Flattened
            Assert.Equal("bob@test.com", summary.CustomerEmail); // Flattened
            Assert.Equal((int)OrderStatus.Shipped, summary.Status); // Enum -> int
            Assert.Equal("99.99", summary.TotalAmount); // decimal -> string
        }

        [Fact]
        public void LargeCollection_100Items_ShouldMapEfficiently()
        {
            var items = Enumerable.Range(1, 100).Select(i => new OrderItem
            {
                Id = i,
                ProductId = i * 10,
                ProductName = $"Product_{i}",
                Quantity = i,
                UnitPrice = i * 1.99m,
                LineTotal = i * i * 1.99m
            }).ToList();

            var dtos = items.MapTo<OrderItemDto>();

            Assert.Equal(100, dtos.Count);
            Assert.Equal("Product_1", dtos[0].ProductName);
            Assert.Equal("Product_100", dtos[99].ProductName);
            Assert.StartsWith("1", dtos[0].UnitPrice);
            Assert.StartsWith("199", dtos[99].UnitPrice);
        }

        [Fact]
        public void ArrayMapping_ShouldWork()
        {
            var customers = new List<Customer>
            {
                new() { Id = 1, FirstName = "A", Email = "a@test.com" },
                new() { Id = 2, FirstName = "B", Email = "b@test.com" }
            };

            CustomerDto[] array = customers.MapToArray<CustomerDto>();

            Assert.Equal(2, array.Length);
            Assert.IsType<CustomerDto[]>(array);
            Assert.Equal("A", array[0].FirstName);
        }

        [Fact]
        public void DictionaryRoundTrip_ShouldPreserveData()
        {
            var customer = new Customer
            {
                Id = 1,
                FirstName = "Test",
                LastName = "User",
                Email = "test@test.com",
                IsActive = true
            };

            var dict = customer.ToDictionary();
            Assert.True(dict.ContainsKey("FirstName"));
            Assert.Equal("Test", dict["FirstName"]);

            // Map back (partial - no nested objects)
            var restored = dict.MapTo<CustomerDto>();
            Assert.NotNull(restored);
            Assert.Equal(1, restored.Id);
            Assert.Equal("Test", restored.FirstName);
        }

        [Fact]
        public void MixedNullableTypes_ShouldHandleGracefully()
        {
            var order = new Order
            {
                Id = 300,
                Customer = null, // Null nested object
                OrderDate = DateTime.Now,
                Status = OrderStatus.Pending,
                Items = new List<OrderItem>(), // Empty collection
                TotalAmount = 0,
                Notes = null // Null string
            };

            var dto = order.MapTo<OrderDetailDto>();

            Assert.NotNull(dto);
            Assert.Equal(300, dto.Id);
            Assert.Null(dto.Customer);
            Assert.Empty(dto.Items);
        }

        [Fact]
        public void CacheInfo_ShouldReportStatistics()
        {
            Mapper.ClearCache();

            // Trigger some mappings
            var c = new Customer { Id = 1 };
            _ = c.MapTo<CustomerDto>();

            var info = Mapper.CacheInfo();
            Assert.True(info.MapToEntries >= 1);
            Assert.True(info.Total >= 1);
        }

        [Fact]
        public void ClearCache_ShouldResetState()
        {
            Mapper.ClearCache();
            var c = new Customer { Id = 1 };
            _ = c.MapTo<CustomerDto>();

            var infoBefore = Mapper.CacheInfo();
            Assert.True(infoBefore.Total >= 1);

            Mapper.ClearCache();
            var infoAfter = Mapper.CacheInfo();

            Assert.Equal(0, infoAfter.Total);
        }

        [Fact]
        public void GenericApiResponse_ShouldMap()
        {
            var response = new ApiResponse<Customer>
            {
                Success = true,
                Message = "Customer retrieved",
                Data = new Customer { Id = 1, FirstName = "Test" },
                Errors = new List<string>()
            };

            var dto = response.MapTo<ApiResponse<CustomerDto>>();

            Assert.NotNull(dto);
            Assert.True(dto.Success);
            Assert.NotNull(dto.Data);
            Assert.Equal("Test", dto.Data.FirstName);
        }
    }
}
