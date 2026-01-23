using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.Dapper.Tests
{
    public class DapperExtensionsTests : IDisposable
    {
        private readonly SqliteConnection _connection;

        public DapperExtensionsTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            SetupDatabase();
            Mapper.ClearCache();
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
            Mapper.ClearCache();
        }

        private void SetupDatabase()
        {
            _connection.Execute(@"
                CREATE TABLE Users (
                    Id INTEGER PRIMARY KEY,
                    FirstName TEXT NOT NULL,
                    LastName TEXT NOT NULL,
                    Email TEXT NOT NULL,
                    Age INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL
                );

                CREATE TABLE Orders (
                    Id INTEGER PRIMARY KEY,
                    UserId INTEGER NOT NULL,
                    ProductName TEXT NOT NULL,
                    Quantity INTEGER NOT NULL,
                    Price REAL NOT NULL,
                    OrderDate TEXT NOT NULL,
                    FOREIGN KEY (UserId) REFERENCES Users(Id)
                );

                INSERT INTO Users (Id, FirstName, LastName, Email, Age, CreatedAt) VALUES
                (1, 'John', 'Doe', 'john@example.com', 30, '2024-01-01'),
                (2, 'Jane', 'Smith', 'jane@example.com', 25, '2024-01-02'),
                (3, 'Bob', 'Johnson', 'bob@example.com', 35, '2024-01-03');

                INSERT INTO Orders (Id, UserId, ProductName, Quantity, Price, OrderDate) VALUES
                (1, 1, 'Widget', 5, 9.99, '2024-02-01'),
                (2, 1, 'Gadget', 2, 24.99, '2024-02-02'),
                (3, 2, 'Widget', 3, 9.99, '2024-02-03');
            ");
        }

        #region Test Models

        public class User
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string Email { get; set; } = "";
            public int Age { get; set; }
            public string CreatedAt { get; set; } = "";
        }

        public class UserDto
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string Email { get; set; } = "";
            public int Age { get; set; }
            public string CreatedAt { get; set; } = "";
        }

        public class UserSummaryDto
        {
            public int Id { get; set; }
            public string FullName { get; set; } = "";
            public string Email { get; set; } = "";
        }

        public class Order
        {
            public int Id { get; set; }
            public int UserId { get; set; }
            public string ProductName { get; set; } = "";
            public int Quantity { get; set; }
            public double Price { get; set; }
            public string OrderDate { get; set; } = "";
        }

        public class OrderDto
        {
            public int Id { get; set; }
            public int UserId { get; set; }
            public string ProductName { get; set; } = "";
            public int Quantity { get; set; }
            public double Price { get; set; }
            public string OrderDate { get; set; } = "";
        }

        #endregion

        #region QueryAndMap Tests

        [Fact]
        public void QueryAndMap_ReturnsAllMappedResults()
        {
            var users = _connection.QueryAndMap<User, UserDto>("SELECT * FROM Users").ToList();

            Assert.Equal(3, users.Count);
            Assert.Equal("John", users[0].FirstName);
            Assert.Equal("Jane", users[1].FirstName);
            Assert.Equal("Bob", users[2].FirstName);
        }

        [Fact]
        public void QueryAndMap_WithParameters_ReturnsMappedResults()
        {
            var users = _connection.QueryAndMap<User, UserDto>(
                "SELECT * FROM Users WHERE Age > @MinAge",
                param: new { MinAge = 28 }).ToList();

            Assert.Equal(2, users.Count);
            Assert.Contains(users, u => u.FirstName == "John");
            Assert.Contains(users, u => u.FirstName == "Bob");
        }

        [Fact]
        public void QueryAndMap_WithNoResults_ReturnsEmptyCollection()
        {
            var users = _connection.QueryAndMap<User, UserDto>(
                "SELECT * FROM Users WHERE Age > 100").ToList();

            Assert.Empty(users);
        }

        [Fact]
        public void QueryAndMap_WithMapperConfiguration_UsesCustomMapping()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserSummaryDto>()
                    .ForMember(d => d.FullName, opt => opt.MapFrom(s => $"{s.FirstName} {s.LastName}"));
            });

            var users = _connection.QueryAndMap<User, UserSummaryDto>(
                "SELECT * FROM Users WHERE Id = 1", config).ToList();

            Assert.Single(users);
            Assert.Equal("John Doe", users[0].FullName);
        }

        [Fact]
        public void QueryAndMap_WithIMapper_UsesProvidedMapper()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserSummaryDto>()
                    .ForMember(d => d.FullName, opt => opt.MapFrom(s => $"{s.FirstName} {s.LastName}"));
            });
            var mapper = config.CreateMapper();

            var users = _connection.QueryAndMap<User, UserSummaryDto>(
                "SELECT * FROM Users WHERE Id = 2", mapper).ToList();

            Assert.Single(users);
            Assert.Equal("Jane Smith", users[0].FullName);
        }

        #endregion

        #region QueryAndMapAsync Tests

        [Fact]
        public async Task QueryAndMapAsync_ReturnsAllMappedResults()
        {
            var users = (await _connection.QueryAndMapAsync<User, UserDto>("SELECT * FROM Users")).ToList();

            Assert.Equal(3, users.Count);
        }

        [Fact]
        public async Task QueryAndMapAsync_WithParameters_ReturnsMappedResults()
        {
            var users = (await _connection.QueryAndMapAsync<User, UserDto>(
                "SELECT * FROM Users WHERE Email LIKE @Pattern",
                param: new { Pattern = "%@example.com" })).ToList();

            Assert.Equal(3, users.Count);
        }

        [Fact]
        public async Task QueryAndMapAsync_WithMapper_UsesProvidedMapper()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserSummaryDto>()
                    .ForMember(d => d.FullName, opt => opt.MapFrom(s => $"{s.FirstName} {s.LastName}"));
            });
            var mapper = config.CreateMapper();

            var users = (await _connection.QueryAndMapAsync<User, UserSummaryDto>(
                "SELECT * FROM Users", mapper)).ToList();

            Assert.Equal(3, users.Count);
            Assert.All(users, u => Assert.Contains(" ", u.FullName));
        }

        #endregion

        #region QuerySingleAndMap Tests

        [Fact]
        public void QuerySingleAndMap_ReturnsMappedResult()
        {
            var user = _connection.QuerySingleAndMap<User, UserDto>(
                "SELECT * FROM Users WHERE Id = @Id",
                param: new { Id = 1 });

            Assert.NotNull(user);
            Assert.Equal("John", user.FirstName);
            Assert.Equal("Doe", user.LastName);
        }

        [Fact]
        public void QuerySingleAndMap_WithNoResult_ReturnsDefault()
        {
            var user = _connection.QuerySingleAndMap<User, UserDto>(
                "SELECT * FROM Users WHERE Id = @Id",
                param: new { Id = 999 });

            Assert.Null(user);
        }

        [Fact]
        public void QuerySingleAndMap_WithMapper_UsesProvidedMapper()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserSummaryDto>()
                    .ForMember(d => d.FullName, opt => opt.MapFrom(s => $"{s.FirstName} {s.LastName}"));
            });
            var mapper = config.CreateMapper();

            var user = _connection.QuerySingleAndMap<User, UserSummaryDto>(
                "SELECT * FROM Users WHERE Id = @Id", mapper,
                param: new { Id = 3 });

            Assert.NotNull(user);
            Assert.Equal("Bob Johnson", user.FullName);
        }

        #endregion

        #region QuerySingleAndMapAsync Tests

        [Fact]
        public async Task QuerySingleAndMapAsync_ReturnsMappedResult()
        {
            var user = await _connection.QuerySingleAndMapAsync<User, UserDto>(
                "SELECT * FROM Users WHERE Id = @Id",
                param: new { Id = 2 });

            Assert.NotNull(user);
            Assert.Equal("Jane", user.FirstName);
        }

        [Fact]
        public async Task QuerySingleAndMapAsync_WithNoResult_ReturnsDefault()
        {
            var user = await _connection.QuerySingleAndMapAsync<User, UserDto>(
                "SELECT * FROM Users WHERE Id = @Id",
                param: new { Id = 999 });

            Assert.Null(user);
        }

        [Fact]
        public async Task QuerySingleAndMapAsync_WithMapper_UsesProvidedMapper()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, UserSummaryDto>()
                    .ForMember(d => d.FullName, opt => opt.MapFrom(s => $"{s.FirstName} {s.LastName}"));
            });
            var mapper = config.CreateMapper();

            var user = await _connection.QuerySingleAndMapAsync<User, UserSummaryDto>(
                "SELECT * FROM Users WHERE Id = @Id", mapper,
                param: new { Id = 1 });

            Assert.NotNull(user);
            Assert.Equal("John Doe", user.FullName);
        }

        #endregion

        #region QueryFirstAndMap Tests

        [Fact]
        public void QueryFirstAndMap_ReturnsFirstMappedResult()
        {
            var user = _connection.QueryFirstAndMap<User, UserDto>(
                "SELECT * FROM Users ORDER BY Id");

            Assert.NotNull(user);
            Assert.Equal(1, user.Id);
            Assert.Equal("John", user.FirstName);
        }

        [Fact]
        public void QueryFirstAndMap_WithNoResult_ReturnsDefault()
        {
            var user = _connection.QueryFirstAndMap<User, UserDto>(
                "SELECT * FROM Users WHERE Age > 100");

            Assert.Null(user);
        }

        [Fact]
        public async Task QueryFirstAndMapAsync_ReturnsFirstMappedResult()
        {
            var user = await _connection.QueryFirstAndMapAsync<User, UserDto>(
                "SELECT * FROM Users ORDER BY Age DESC");

            Assert.NotNull(user);
            Assert.Equal("Bob", user.FirstName);
        }

        #endregion

        #region Collection MapTo Tests

        [Fact]
        public void MapTo_IEnumerable_MapsAllItems()
        {
            var users = _connection.Query<User>("SELECT * FROM Users");
            var dtos = users.MapTo<User, UserDto>(
                new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>()).CreateMapper());

            Assert.Equal(3, dtos.Count);
        }

        [Fact]
        public void MapTo_WithNullSource_ReturnsEmptyList()
        {
            IEnumerable<User>? users = null;
            var config = new MapperConfiguration(cfg => cfg.CreateMap<User, UserDto>());
            var dtos = users.MapTo<User, UserDto>(config.CreateMapper());

            Assert.Empty(dtos);
        }

        #endregion

        #region Order Mapping Tests

        [Fact]
        public void QueryAndMap_Orders_MapsCorrectly()
        {
            var orders = _connection.QueryAndMap<Order, OrderDto>(
                "SELECT * FROM Orders WHERE UserId = @UserId",
                param: new { UserId = 1 }).ToList();

            Assert.Equal(2, orders.Count);
            Assert.Contains(orders, o => o.ProductName == "Widget");
            Assert.Contains(orders, o => o.ProductName == "Gadget");
        }

        [Fact]
        public async Task QueryAndMapAsync_Orders_MapsCorrectly()
        {
            var orders = (await _connection.QueryAndMapAsync<Order, OrderDto>(
                "SELECT * FROM Orders")).ToList();

            Assert.Equal(3, orders.Count);
        }

        #endregion

        #region Transaction Tests

        [Fact]
        public void QueryAndMap_WithTransaction_WorksCorrectly()
        {
            using var transaction = _connection.BeginTransaction();

            _connection.Execute(
                "INSERT INTO Users (FirstName, LastName, Email, Age, CreatedAt) VALUES ('Test', 'User', 'test@example.com', 40, '2024-03-01')",
                transaction: transaction);

            var users = _connection.QueryAndMap<User, UserDto>(
                "SELECT * FROM Users WHERE FirstName = 'Test'",
                transaction: transaction).ToList();

            Assert.Single(users);
            Assert.Equal("Test", users[0].FirstName);

            transaction.Rollback();
        }

        [Fact]
        public async Task QueryAndMapAsync_WithTransaction_WorksCorrectly()
        {
            using var transaction = _connection.BeginTransaction();

            await _connection.ExecuteAsync(
                "INSERT INTO Users (FirstName, LastName, Email, Age, CreatedAt) VALUES ('Async', 'Test', 'async@example.com', 45, '2024-03-01')",
                transaction: transaction);

            var users = (await _connection.QueryAndMapAsync<User, UserDto>(
                "SELECT * FROM Users WHERE FirstName = 'Async'",
                transaction: transaction)).ToList();

            Assert.Single(users);
            Assert.Equal("Async", users[0].FirstName);

            transaction.Rollback();
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void QueryAndMap_PreservesPropertyTypes()
        {
            var users = _connection.QueryAndMap<User, UserDto>("SELECT * FROM Users WHERE Id = 1").ToList();

            Assert.Single(users);
            Assert.IsType<int>(users[0].Id);
            Assert.IsType<string>(users[0].FirstName);
            Assert.IsType<int>(users[0].Age);
        }

        [Fact]
        public async Task QueryAndMapAsync_MultipleCalls_WorksCorrectly()
        {
            var task1 = _connection.QueryAndMapAsync<User, UserDto>("SELECT * FROM Users WHERE Id = 1");
            var task2 = _connection.QueryAndMapAsync<User, UserDto>("SELECT * FROM Users WHERE Id = 2");

            var results = await Task.WhenAll(task1, task2);

            Assert.Single(results[0]);
            Assert.Single(results[1]);
            Assert.Equal("John", results[0].First().FirstName);
            Assert.Equal("Jane", results[1].First().FirstName);
        }

        #endregion
    }
}
