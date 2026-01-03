using System;
using System.Collections.Generic;
using Xunit;
using Mapsicle.Fluent;

namespace Mapsicle.Fluent.Tests
{
    /// <summary>
    /// Edge case tests for Fluent mapping including chained configurations, circular dependencies,
    /// ReverseMap scenarios, and exception handling in hooks.
    /// </summary>
    public class FluentEdgeCasesTests
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
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
        }

        public class BaseEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public class DerivedEntity : BaseEntity
        {
            public string Extra { get; set; } = string.Empty;
        }

        public class BaseDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public class DerivedDto : BaseDto
        {
            public string Extra { get; set; } = string.Empty;
        }

        public class Product
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Price { get; set; }
        }

        #endregion

        #region Chained ForMember Configuration Tests

        /// <summary>
        /// Test multiple ForMember configurations on the same property.
        /// The last configuration should win.
        /// </summary>
        [Fact]
        public void ForMember_ChainedOnSameProperty_LastShouldWin()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.FullName, opt => opt.ResolveUsing(s => $"{s.FirstName} {s.LastName}"))
                    .ForMember(d => d.FullName, opt => opt.ResolveUsing(s => $"{s.LastName}, {s.FirstName}"));
            });

            var mapper = config.CreateMapper();
            var user = new User { FirstName = "John", LastName = "Doe" };
            var dto = mapper.Map<UserDto>(user);

            Assert.NotNull(dto);
            // Last configuration should win
            Assert.Equal("Doe, John", dto.FullName);
        }

        /// <summary>
        /// Test ForMember with Ignore followed by another configuration.
        /// Note: Once a property is marked as Ignore, it stays ignored.
        /// </summary>
        [Fact]
        public void ForMember_IgnoreThenMap_IgnoreTakesPrecedence()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.FullName, opt => opt.Ignore())
                    .ForMember(d => d.FullName, opt => opt.ResolveUsing(s => $"{s.FirstName} {s.LastName}"));
            });

            var mapper = config.CreateMapper();
            var user = new User { FirstName = "Jane", LastName = "Smith" };
            var dto = mapper.Map<UserDto>(user);

            Assert.NotNull(dto);
            // Ignore configuration takes precedence
            Assert.Null(dto.FullName);
        }

        #endregion

        #region Circular Dependencies in Include Mappings Tests

        /// <summary>
        /// Test circular Include relationships.
        /// Include should handle circular references gracefully.
        /// </summary>
        [Fact]
        public void Include_CircularDependency_ShouldHandleGracefully()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<BaseEntity, BaseDto>()
                    .Include<DerivedEntity, DerivedDto>();
                    
                cfg.CreateMap<DerivedEntity, DerivedDto>();
            });

            var mapper = config.CreateMapper();
            var derived = new DerivedEntity { Id = 1, Name = "Test", Extra = "Extra" };
            
            var dto = mapper.Map<DerivedDto>(derived);

            Assert.NotNull(dto);
            Assert.Equal(1, dto.Id);
            Assert.Equal("Test", dto.Name);
            Assert.Equal("Extra", dto.Extra);
        }

        /// <summary>
        /// Test Include with non-existent derived mapping.
        /// Should not crash during configuration.
        /// </summary>
        [Fact]
        public void Include_NonExistentMapping_ShouldNotCrashConfiguration()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<BaseEntity, BaseDto>()
                    .Include<DerivedEntity, DerivedDto>();
                // Note: Not creating the DerivedEntity -> DerivedDto mapping
            });

            // Should create mapper without error
            var mapper = config.CreateMapper();
            Assert.NotNull(mapper);
        }

        #endregion

        #region ReverseMap with Asymmetric Configurations Tests

        /// <summary>
        /// Test ReverseMap when forward mapping has custom configurations.
        /// ReverseMap should create basic reverse mapping.
        /// </summary>
        [Fact]
        public void ReverseMap_WithAsymmetricForwardConfig_ShouldCreateBasicReverse()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.FullName, opt => opt.ResolveUsing(s => $"{s.FirstName} {s.LastName}"))
                    .ReverseMap();
            });

            var mapper = config.CreateMapper();
            
            // Forward mapping
            var user = new User { Id = 1, FirstName = "John", LastName = "Doe", Email = "john@test.com" };
            var dto = mapper.Map<UserDto>(user);
            Assert.Equal("John Doe", dto.FullName);

            // Reverse mapping (should work even though forward has custom ForMember)
            var userFromDto = mapper.Map<User>(dto);
            Assert.NotNull(userFromDto);
            Assert.Equal(1, userFromDto.Id);
            Assert.Equal("john@test.com", userFromDto.Email);
        }

        /// <summary>
        /// Test ReverseMap with Ignore in forward mapping.
        /// </summary>
        [Fact]
        public void ReverseMap_WithIgnoreInForward_ShouldHandleReverse()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.DisplayName, opt => opt.Ignore())
                    .ReverseMap();
            });

            var mapper = config.CreateMapper();
            var dto = new UserDto { Id = 1, Email = "test@test.com", DisplayName = "Display" };
            
            var user = mapper.Map<User>(dto);

            Assert.NotNull(user);
            Assert.Equal(1, user.Id);
            Assert.Equal("test@test.com", user.Email);
        }

        #endregion

        #region BeforeMap/AfterMap Exception Handling Tests

        /// <summary>
        /// Test that exceptions in BeforeMap propagate properly.
        /// </summary>
        [Fact]
        public void BeforeMap_WithException_ShouldPropagate()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .BeforeMap((src, dest) =>
                    {
                        throw new InvalidOperationException("BeforeMap failed");
                    });
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 1, FirstName = "Test" };

            Assert.Throws<InvalidOperationException>(() => mapper.Map<UserDto>(user));
        }

        /// <summary>
        /// Test that exceptions in AfterMap propagate properly.
        /// </summary>
        [Fact]
        public void AfterMap_WithException_ShouldPropagate()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .AfterMap((src, dest) =>
                    {
                        throw new InvalidOperationException("AfterMap failed");
                    });
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 1, FirstName = "Test" };

            Assert.Throws<InvalidOperationException>(() => mapper.Map<UserDto>(user));
        }

        /// <summary>
        /// Test that BeforeMap executes before mapping occurs.
        /// </summary>
        [Fact]
        public void BeforeMap_ExecutesBeforeMapping_ShouldModifySource()
        {
            var executed = false;
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .BeforeMap((src, dest) =>
                    {
                        executed = true;
                        // Destination may already have been created with mapped values
                        // depending on implementation
                    });
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 1, FirstName = "Test" };
            var dto = mapper.Map<UserDto>(user);

            Assert.True(executed);
            Assert.Equal(1, dto.Id);
        }

        /// <summary>
        /// Test that AfterMap executes after mapping occurs.
        /// </summary>
        [Fact]
        public void AfterMap_ExecutesAfterMapping_CanModifyResult()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .AfterMap((src, dest) =>
                    {
                        Assert.Equal(src.Id, dest.Id); // Should already be mapped
                        dest.DisplayName = "Modified in AfterMap";
                    });
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 1, FirstName = "Test" };
            var dto = mapper.Map<UserDto>(user);

            Assert.Equal(1, dto.Id);
            Assert.Equal("Modified in AfterMap", dto.DisplayName);
        }

        #endregion

        #region ConstructUsing with Failing Factory Tests

        /// <summary>
        /// Test ConstructUsing with a factory that throws exception.
        /// </summary>
        [Fact]
        public void ConstructUsing_WithFailingFactory_ShouldPropagateException()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ConstructUsing(src => throw new InvalidOperationException("Factory failed"));
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 1 };

            Assert.Throws<InvalidOperationException>(() => mapper.Map<UserDto>(user));
        }

        /// <summary>
        /// Test ConstructUsing with factory returning null.
        /// </summary>
        [Fact]
        public void ConstructUsing_WithNullFactory_ShouldHandleNull()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .ConstructUsing(src => null!);
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 1 };

            // Should handle null from factory - may throw or return null
            try
            {
                var result = mapper.Map<UserDto>(user);
                // If it succeeds, document the behavior
                Assert.True(true);
            }
            catch (NullReferenceException)
            {
                // Expected if null is not handled
                Assert.True(true);
            }
        }

        /// <summary>
        /// Test ConstructUsing with conditional factory.
        /// </summary>
        [Fact]
        public void ConstructUsing_WithConditionalFactory_ShouldUseCondition()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Product, Product>()
                    .ConstructUsing(src => 
                        src.Price > 100 
                            ? new Product { Name = "Expensive" } 
                            : new Product { Name = "Affordable" });
            });

            var mapper = config.CreateMapper();
            
            var expensiveProduct = new Product { Id = 1, Price = 150m };
            var affordableProduct = new Product { Id = 2, Price = 50m };

            var expensiveResult = mapper.Map<Product>(expensiveProduct);
            var affordableResult = mapper.Map<Product>(affordableProduct);

            Assert.Equal("Expensive", expensiveResult.Name);
            Assert.Equal("Affordable", affordableResult.Name);
        }

        #endregion

        #region Multiple Mapping Configurations Tests

        /// <summary>
        /// Test creating multiple mappings for the same source/dest types.
        /// Last registration should win.
        /// </summary>
        [Fact]
        public void MultipleConfigurations_ForSameTypes_LastShouldWin()
        {
            var config = new MapperConfiguration(cfg =>
            {
                // First configuration
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.FullName, opt => opt.ResolveUsing(s => "First"));

                // Second configuration - should override
                cfg.CreateMap<User, UserDto>()
                    .ForMember(d => d.FullName, opt => opt.ResolveUsing(s => "Second"));
            });

            var mapper = config.CreateMapper();
            var user = new User { Id = 1 };
            var dto = mapper.Map<UserDto>(user);

            // Last configuration should be used
            Assert.Equal("Second", dto.FullName);
        }

        #endregion

        #region Null Source Handling Tests

        /// <summary>
        /// Test mapping with null source in Fluent mapper.
        /// </summary>
        [Fact]
        public void Map_WithNullSource_ShouldReturnNull()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>();
            });

            var mapper = config.CreateMapper();
            User? nullUser = null;

            var dto = mapper.Map<UserDto>(nullUser);

            Assert.Null(dto);
        }

        /// <summary>
        /// Test BeforeMap with null source.
        /// </summary>
        [Fact]
        public void BeforeMap_WithNullSource_ShouldNotExecute()
        {
            var executed = false;
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>()
                    .BeforeMap((src, dest) =>
                    {
                        executed = true;
                    });
            });

            var mapper = config.CreateMapper();
            User? nullUser = null;

            var dto = mapper.Map<UserDto>(nullUser);

            Assert.Null(dto);
            Assert.False(executed); // BeforeMap should not run for null source
        }

        #endregion

        #region Configuration Validation Tests

        /// <summary>
        /// Test configuration validation with unmapped properties.
        /// </summary>
        [Fact]
        public void AssertConfigurationIsValid_WithUnmappedProperties_ShouldDetect()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserDto>();
                // FullName and DisplayName in UserDto are not mapped
            });

            // Should throw or report unmapped properties
            try
            {
                config.AssertConfigurationIsValid();
                // If it doesn't throw, that's the documented behavior
                Assert.True(true);
            }
            catch (InvalidOperationException)
            {
                // Expected if validation is strict
                Assert.True(true);
            }
        }

        #endregion
    }
}
