using System;
using Xunit;
using Mapsicle.Fluent;

namespace Mapsicle.Fluent.Tests
{
    public class Phase2EnterpriseTests
    {
        #region Test Models

        public class User
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string Email { get; set; } = "";
        }

        public class UserDto
        {
            public int Id { get; set; }
            public string FullName { get; set; } = "";
            public string Email { get; set; } = "";
            public DateTime MappedAt { get; set; }
            public bool WasProcessed { get; set; }
        }

        public class Order
        {
            public int Id { get; set; }
            public decimal Quantity { get; set; }
            public decimal UnitPrice { get; set; }
        }

        public class OrderDto
        {
            public int Id { get; set; }
            public decimal Total { get; set; }
        }

        #endregion

        #region BeforeMap / AfterMap Tests

        [Fact]
        public void BeforeMap_ShouldExecuteBeforeMapping()
        {
            DateTime beforeTime = default;

            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .BeforeMap((src, dest) => dest.MappedAt = DateTime.UtcNow)
                    .ForMember(d => d.FullName, opt => opt.ResolveUsing(s => $"{s.FirstName} {s.LastName}"));
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 1, FirstName = "Alice", LastName = "Smith" };

            var dto = mapper.Map<UserDto>(user);

            Assert.NotNull(dto);
            Assert.NotEqual(default, dto.MappedAt);
        }

        [Fact]
        public void AfterMap_ShouldExecuteAfterMapping()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.FullName, opt => opt.ResolveUsing(s => $"{s.FirstName} {s.LastName}"))
                    .AfterMap((src, dest) => dest.WasProcessed = true);
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 1, FirstName = "Bob", LastName = "Jones" };

            var dto = mapper.Map<UserDto>(user);

            Assert.NotNull(dto);
            Assert.True(dto.WasProcessed);
            Assert.Equal("Bob Jones", dto.FullName);
        }

        [Fact]
        public void BeforeAndAfterMap_ShouldBothExecute()
        {
            var executionOrder = new System.Collections.Generic.List<string>();

            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .BeforeMap((src, dest) => executionOrder.Add("before"))
                    .ForMember(d => d.FullName, opt => opt.ResolveUsing(s => s.FirstName))
                    .AfterMap((src, dest) => executionOrder.Add("after"));
            });

            var mapper = config.CreateMapper();
            var dto = mapper.Map<UserDto>(new User { FirstName = "Test" });

            Assert.Equal(2, executionOrder.Count);
            Assert.Equal("before", executionOrder[0]);
            Assert.Equal("after", executionOrder[1]);
        }

        #endregion

        #region Expression Storage Tests

        [Fact]
        public void MapFrom_ShouldStoreExpressionForProjectTo()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Order, OrderDto>()
                    .ForMember(d => d.Total, opt => opt.MapFrom(s => s.Quantity * s.UnitPrice));
            });

            var mapper = config.CreateMapper();
            var order = new Order { Id = 1, Quantity = 5, UnitPrice = 10m };

            var dto = mapper.Map<OrderDto>(order);

            Assert.NotNull(dto);
            Assert.Equal(50m, dto.Total);
        }

        #endregion
    }
}
