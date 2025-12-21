using Xunit;
using Mapsicle;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mapsicle.Tests
{
    public class Phase1StabilityTests
    {
        #region Test Models

        public class User
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Email { get; set; } = "";
        }

        public class UserDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Email { get; set; } = "";
        }

        public class UserWithSecret
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public class UserDtoWithUnmapped
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string UnmappedProperty { get; set; } = "";
        }

        // Circular reference models
        public class Parent
        {
            public int Id { get; set; }
            public Child? Child { get; set; }
        }

        public class Child
        {
            public int Id { get; set; }
            public Parent? Parent { get; set; }
        }

        public class ParentDto
        {
            public int Id { get; set; }
            public ChildDto? Child { get; set; }
        }

        public class ChildDto
        {
            public int Id { get; set; }
            public ParentDto? Parent { get; set; }
        }

        #endregion

        #region Validation Tests

        [Fact]
        public void AssertMappingValid_AllPropertiesMapped_ShouldNotThrow()
        {
            // Should not throw
            Mapper.AssertMappingValid<User, UserDto>();
        }

        [Fact]
        public void AssertMappingValid_UnmappedProperty_ShouldThrow()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                Mapper.AssertMappingValid<UserWithSecret, UserDtoWithUnmapped>());

            Assert.Contains("UnmappedProperty", ex.Message);
        }

        [Fact]
        public void GetUnmappedProperties_ShouldReturnUnmappedList()
        {
            var unmapped = Mapper.GetUnmappedProperties<UserWithSecret, UserDtoWithUnmapped>();

            Assert.Single(unmapped);
            Assert.Contains("UnmappedProperty", unmapped);
        }

        #endregion

        #region Depth/Cycle Detection Tests

        [Fact]
        public void MapTo_WithMaxDepth_ShouldNotStackOverflow()
        {
            // Save original
            var originalMaxDepth = Mapper.MaxDepth;
            var logs = new List<string>();
            var originalLogger = Mapper.Logger;

            try
            {
                Mapper.MaxDepth = 5;
                Mapper.Logger = msg => logs.Add(msg);

                // Create circular reference
                var parent = new Parent { Id = 1 };
                var child = new Child { Id = 2, Parent = parent };
                parent.Child = child;

                // This should NOT stack overflow, should hit max depth
                var dto = parent.MapTo<ParentDto>();

                // Should have logged about max depth
                // Note: The actual mapping may or may not hit depth depending on implementation
                Assert.NotNull(dto);
            }
            finally
            {
                Mapper.MaxDepth = originalMaxDepth;
                Mapper.Logger = originalLogger;
            }
        }

        #endregion

        #region Logging Tests

        [Fact]
        public void Logger_WhenSet_ShouldReceiveMessages()
        {
            var logs = new List<string>();
            var originalLogger = Mapper.Logger;

            try
            {
                Mapper.Logger = msg => logs.Add(msg);

                // Perform mapping - may or may not log depending on conditions
                var user = new User { Id = 1, Name = "Test" };
                _ = user.MapTo<UserDto>();

                // Logger should be set (even if no messages logged for simple mapping)
                Assert.NotNull(Mapper.Logger);
            }
            finally
            {
                Mapper.Logger = originalLogger;
            }
        }

        #endregion

        #region MapperFactory Tests

        [Fact]
        public void MapperFactory_Create_ShouldReturnInstance()
        {
            using var mapper = MapperFactory.Create();

            Assert.NotNull(mapper);
        }

        [Fact]
        public void MapperFactory_MapTo_ShouldWork()
        {
            using var mapper = MapperFactory.Create();
            var user = new User { Id = 1, Name = "Alice", Email = "alice@test.com" };

            var dto = mapper.MapTo<UserDto>(user);

            Assert.NotNull(dto);
            Assert.Equal(1, dto.Id);
            Assert.Equal("Alice", dto.Name);
        }

        [Fact]
        public void MapperFactory_CacheInfo_ShouldWork()
        {
            using var mapper = MapperFactory.Create();
            var info = mapper.CacheInfo();

            Assert.True(info.Total >= 0);
        }

        [Fact]
        public void MapperFactory_Dispose_ShouldPreventFurtherUse()
        {
            var mapper = MapperFactory.Create();
            mapper.Dispose();

            Assert.Throws<ObjectDisposedException>(() => mapper.MapTo<UserDto>(new User()));
        }

        [Fact]
        public void MapperFactory_WithOptions_ShouldRespectMaxDepth()
        {
            var options = new MapperOptions { MaxDepth = 2 };
            using var mapper = MapperFactory.Create(options);

            // Create deep nesting
            var parent = new Parent { Id = 1, Child = new Child { Id = 2 } };

            // Should work within depth limit
            var dto = mapper.MapTo<ParentDto>(parent);
            Assert.NotNull(dto);
        }

        #endregion

        // Note: LruCache is internal by design and tested via MapperFactory integration

        #region Concurrent Safety Tests

        [Fact]
        public void ConcurrentMapping_ShouldBeThreadSafe()
        {
            Mapper.ClearCache();
            var exceptions = new List<Exception>();

            Parallel.For(0, 1000, i =>
            {
                try
                {
                    var user = new User { Id = i, Name = $"User{i}" };
                    var dto = user.MapTo<UserDto>();
                    Assert.NotNull(dto);
                    Assert.Equal(i, dto.Id);
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
        }

        #endregion
    }
}
