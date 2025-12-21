using System;
using Xunit;
using Mapsicle.Fluent;

namespace Mapsicle.Fluent.Tests
{
    public class FluentMapperTests
    {
        #region Test Models

        public class User
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }

        public class UserDto
        {
            public int Id { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
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

        [Fact]
        public void CreateMapper_ShouldWork()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>();
            });

            var mapper = config.CreateMapper();
            Assert.NotNull(mapper);
        }

        [Fact]
        public void Map_BasicProperties_ShouldWork()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>();
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 1, Email = "test@test.com" };

            var dto = mapper.Map<UserDto>(user);

            Assert.NotNull(dto);
            Assert.Equal(1, dto.Id);
            Assert.Equal("test@test.com", dto.Email);
        }

        [Fact]
        public void ForMember_MapFrom_Expression_ShouldWork()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.FullName, opt => opt.ResolveUsing(s => $"{s.FirstName} {s.LastName}"));
            });

            var mapper = config.CreateMapper();
            var user = new User { FirstName = "John", LastName = "Doe" };

            var dto = mapper.Map<UserDto>(user);

            Assert.NotNull(dto);
            Assert.Equal("John Doe", dto.FullName);
        }

        [Fact]
        public void ForMember_Ignore_ShouldNotMap()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.Password, opt => opt.Ignore());
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 1, Password = "secret123" };

            var dto = mapper.Map<UserDto>(user);

            Assert.NotNull(dto);
            Assert.Null(dto.Password); // Should be default, not mapped
        }

        [Fact]
        public void ForMember_Condition_ShouldApply()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.Status, opt => opt.ResolveUsing(s => "Active"))
                    .ForMember(d => d.Status, opt => opt.Condition(s => s.IsActive));
            });

            var mapper = config.CreateMapper();
            
            var activeUser = new User { Id = 1, IsActive = true };
            var inactiveUser = new User { Id = 2, IsActive = false };

            var activeDto = mapper.Map<UserDto>(activeUser);
            var inactiveDto = mapper.Map<UserDto>(inactiveUser);

            Assert.Equal("Active", activeDto!.Status);
            Assert.Null(inactiveDto!.Status); // Condition failed
        }

        [Fact]
        public void ForMember_CalculatedProperty_ShouldWork()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Order, OrderDto>()
                    .ForMember(d => d.Total, opt => opt.ResolveUsing(s => s.Quantity * s.UnitPrice));
            });

            var mapper = config.CreateMapper();
            var order = new Order { Id = 1, Quantity = 5, UnitPrice = 10.50m };

            var dto = mapper.Map<OrderDto>(order);

            Assert.NotNull(dto);
            Assert.Equal(52.50m, dto.Total);
        }

        [Fact]
        public void AssertConfigurationIsValid_WithMissingMember_ShouldThrow()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>();
                // FullName and Status are not mapped and not on source
            });

            var ex = Assert.Throws<InvalidOperationException>(() => config.AssertConfigurationIsValid());
            Assert.Contains("Unmapped member", ex.Message);
        }

        [Fact]
        public void AssertConfigurationIsValid_WithAllMembersMapped_ShouldPass()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.FullName, opt => opt.ResolveUsing(s => s.FirstName))
                    .ForMember(d => d.Status, opt => opt.Ignore());
            });

            // Should not throw
            config.AssertConfigurationIsValid();
        }

        [Fact]
        public void Map_WithGenericTypes_ShouldWork()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>();
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 1, Email = "test@test.com" };

            var dto = mapper.Map<User, UserDto>(user);

            Assert.NotNull(dto);
            Assert.Equal(1, dto.Id);
        }

        [Fact]
        public void Map_ToExisting_ShouldUpdate()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.Password, opt => opt.Ignore());
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 2, Email = "new@test.com" };
            var existing = new UserDto { Id = 1, Email = "old@test.com", Password = "keep" };

            var result = mapper.Map(user, existing);

            Assert.Same(existing, result);
            Assert.Equal(2, result.Id);
            Assert.Equal("new@test.com", result.Email);
            Assert.Equal("keep", result.Password); // Not updated (ignored)
        }

        [Fact]
        public void Map_Null_ShouldReturnDefault()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>();
            });

            var mapper = config.CreateMapper();
            User? user = null;

            var dto = mapper.Map<UserDto>(user);

            Assert.Null(dto);
        }
    }
}
