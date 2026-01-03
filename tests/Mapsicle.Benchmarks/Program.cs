using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.EntityFrameworkCore;
using Mapsicle;
using Mapsicle.Fluent;
using Mapsicle.EntityFramework;
using System.Collections.Concurrent;

namespace Mapsicle.Benchmarks;

/// <summary>
/// Comprehensive benchmarks for all Mapsicle packages vs AutoMapper.
/// Covers: Core, Fluent, EntityFramework, Edge Cases, Real-World Scenarios
/// Run with: dotnet run -c Release
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("  Mapsicle Complete Benchmark Suite");
        Console.WriteLine("  Comparing: Mapsicle vs AutoMapper 13.0.1");
        Console.WriteLine("=================================================");
        Console.WriteLine();

        if (args.Length > 0 && args[0] == "--quick")
        {
            Console.WriteLine("Running quick smoke tests...\n");
            RunSmokeTests();
        }
        else if (args.Length > 0 && args[0] == "--edge")
        {
            BenchmarkRunner.Run<EdgeCaseBenchmarks>();
        }
        else if (args.Length > 0 && args[0] == "--realworld")
        {
            BenchmarkRunner.Run<RealWorldScenarioBenchmarks>();
        }
        else if (args.Length > 0 && args[0] == "--cache")
        {
            BenchmarkRunner.Run<CacheBenchmarks>();
        }
        else if (args.Length > 0 && args[0] == "--concurrency")
        {
            BenchmarkRunner.Run<ConcurrencyBenchmarks>();
        }
        else
        {
            // Full BenchmarkDotNet run
            var config = DefaultConfig.Instance
                .WithOptions(ConfigOptions.DisableOptimizationsValidator);

            BenchmarkRunner.Run<CoreMapperBenchmarks>(config);
            BenchmarkRunner.Run<FluentMapperBenchmarks>(config);
            BenchmarkRunner.Run<EfCoreBenchmarks>(config);
            BenchmarkRunner.Run<EdgeCaseBenchmarks>(config);
            BenchmarkRunner.Run<RealWorldScenarioBenchmarks>(config);
            BenchmarkRunner.Run<ConcurrencyBenchmarks>(config);
            BenchmarkRunner.Run<CacheBenchmarks>(config);
        }
    }

    static void RunSmokeTests()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        // Core tests
        var user = new UserEntity { Id = 1, FirstName = "Test", LastName = "User", Email = "test@test.com" };
        for (int i = 0; i < 10000; i++)
            _ = user.MapTo<UserDto>();
        Console.WriteLine($"✓ Core: 10,000 mappings in {sw.ElapsedMilliseconds}ms");
        
        // Fluent tests
        sw.Restart();
        var config = new Mapsicle.Fluent.MapperConfiguration(cfg => cfg.CreateMap<UserEntity, UserDto>());
        var mapper = config.CreateMapper();
        for (int i = 0; i < 10000; i++)
            _ = mapper.Map<UserDto>(user);
        Console.WriteLine($"✓ Fluent: 10,000 mappings in {sw.ElapsedMilliseconds}ms");

        // Cycle detection test
        sw.Restart();
        var parent = new ParentNode { Id = 1, Name = "Parent" };
        var child = new ChildNode { Id = 2, Name = "Child", Parent = parent };
        parent.Child = child;
        var dto = parent.MapTo<ParentNodeDto>(); // Should not crash
        Console.WriteLine($"✓ Cycle detection: {(dto != null ? "Handled safely" : "Returned default")}");

        // Deeply nested test
        sw.Restart();
        var deepEntity = CreateDeeplyNestedEntity(10);
        for (int i = 0; i < 1000; i++)
            _ = deepEntity.MapTo<DeepDto>();
        Console.WriteLine($"✓ Deep nesting (10 levels): 1,000 mappings in {sw.ElapsedMilliseconds}ms");

        // Large collection
        sw.Restart();
        var largeList = Enumerable.Range(1, 10000).Select(i => new UserEntity { Id = i, FirstName = $"User{i}" }).ToList();
        _ = largeList.MapTo<UserDto>();
        Console.WriteLine($"✓ Large collection (10,000 items): {sw.ElapsedMilliseconds}ms");

        Console.WriteLine("\n✓ All smoke tests passed!");
    }

    static DeepEntity CreateDeeplyNestedEntity(int depth)
    {
        var root = new DeepEntity { Id = 0, Name = "Level0" };
        var current = root;
        for (int i = 1; i < depth; i++)
        {
            current.Nested = new DeepEntity { Id = i, Name = $"Level{i}" };
            current = current.Nested;
        }
        return root;
    }
}

#region Shared Models

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
}

public class UserEntity
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public AddressEntity? Address { get; set; }
}

public class AddressEntity
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string Country { get; set; } = "";
}

public class UserDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
}

public class UserFlatDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string Email { get; set; } = "";
    public string AddressCity { get; set; } = "";
    public string AddressCountry { get; set; } = "";
}

public class OrderEntity
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public CustomerEntity? Customer { get; set; }
    public List<OrderItemEntity> Items { get; set; } = new();
    public DateTime OrderDate { get; set; }
    public decimal Total { get; set; }
}

public class CustomerEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class OrderItemEntity
{
    public int Id { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class OrderDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public decimal Total { get; set; }
}

// Edge case models
public class ParentNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ChildNode? Child { get; set; }
}

public class ChildNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ParentNode? Parent { get; set; }  // Circular reference!
}

public class ParentNodeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ChildNodeDto? Child { get; set; }
}

public class ChildNodeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ParentNodeDto? Parent { get; set; }
}

public class DeepEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DeepEntity? Nested { get; set; }
}

public class DeepDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DeepDto? Nested { get; set; }
}

// Real-world complex models
public class ECommerceOrder
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public OrderStatus Status { get; set; }
    public Customer Customer { get; set; } = null!;
    public ShippingAddress ShippingAddress { get; set; } = null!;
    public BillingAddress? BillingAddress { get; set; }
    public List<OrderLine> Lines { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Shipping { get; set; }
    public decimal Total { get; set; }
    public string? CouponCode { get; set; }
    public decimal Discount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? Notes { get; set; }
}

public enum OrderStatus { Pending, Confirmed, Shipped, Delivered, Cancelled }
public enum PaymentMethod { CreditCard, PayPal, BankTransfer, CashOnDelivery }

public class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateTime JoinedAt { get; set; }
    public int LoyaltyPoints { get; set; }
}

public class ShippingAddress
{
    public string Line1 { get; set; } = "";
    public string? Line2 { get; set; }
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Country { get; set; } = "";
}

public class BillingAddress
{
    public string Line1 { get; set; } = "";
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Country { get; set; } = "";
}

public class OrderLine
{
    public int Id { get; set; }
    public string SKU { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}

public class ECommerceOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public string ShippingCity { get; set; } = "";
    public string ShippingCountry { get; set; } = "";
    public int ItemCount { get; set; }
    public decimal Total { get; set; }
}

#endregion

#region Core Benchmarks

[MemoryDiagnoser]
[RankColumn]
public class CoreMapperBenchmarks
{
    private UserEntity _user = null!;
    private List<UserEntity> _users = null!;
    private AutoMapper.IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _user = new UserEntity
        {
            Id = 1, FirstName = "Alice", LastName = "Smith",
            Email = "alice@test.com", IsActive = true, CreatedAt = DateTime.Now,
            Address = new AddressEntity { City = "NYC", Country = "USA", Street = "123 Main", State = "NY", ZipCode = "10001" }
        };

        _users = Enumerable.Range(1, 100).Select(i => new UserEntity
        {
            Id = i, FirstName = $"User{i}", LastName = $"Last{i}",
            Email = $"user{i}@test.com", IsActive = i % 2 == 0
        }).ToList();

        var config = new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<UserEntity, UserDto>();
            cfg.CreateMap<AddressEntity, AddressEntity>();
            cfg.CreateMap<UserEntity, UserFlatDto>()
                .ForMember(d => d.AddressCity, o => o.MapFrom(s => s.Address != null ? s.Address.City : ""))
                .ForMember(d => d.AddressCountry, o => o.MapFrom(s => s.Address != null ? s.Address.Country : ""));
        });
        _autoMapper = config.CreateMapper();

        // Warm up caches
        _ = _user.MapTo<UserDto>();
        Mapsicle.Mapper.ClearCache();
    }

    [Benchmark(Baseline = true)]
    public UserDto Manual_Single() => new()
    {
        Id = _user.Id, FirstName = _user.FirstName, LastName = _user.LastName,
        Email = _user.Email, IsActive = _user.IsActive
    };

    [Benchmark]
    public UserDto? Mapsicle_Single() => _user.MapTo<UserDto>();

    [Benchmark]
    public UserDto AutoMapper_Single() => _autoMapper.Map<UserDto>(_user);

    [Benchmark]
    public List<UserDto> Mapsicle_Collection() => _users.MapTo<UserDto>();

    [Benchmark]
    public List<UserDto> AutoMapper_Collection() => _autoMapper.Map<List<UserDto>>(_users);

    [Benchmark]
    public UserFlatDto? Mapsicle_Flattening() => _user.MapTo<UserFlatDto>();

    [Benchmark]
    public UserFlatDto AutoMapper_Flattening() => _autoMapper.Map<UserFlatDto>(_user);
}

#endregion

#region Fluent Benchmarks

[MemoryDiagnoser]
[RankColumn]
public class FluentMapperBenchmarks
{
    private UserEntity _user = null!;
    private Mapsicle.Fluent.IMapper _fluentMapper = null!;
    private AutoMapper.IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _user = new UserEntity { Id = 1, FirstName = "Alice", LastName = "Smith", Email = "alice@test.com" };

        var fluentConfig = new Mapsicle.Fluent.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<UserEntity, UserDto>();
        });
        _fluentMapper = fluentConfig.CreateMapper();

        var autoConfig = new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<UserEntity, UserDto>();
        });
        _autoMapper = autoConfig.CreateMapper();
    }

    [Benchmark(Baseline = true)]
    public UserDto? MapsicleCore() => _user.MapTo<UserDto>();

    [Benchmark]
    public UserDto? MapsicleFluent() => _fluentMapper.Map<UserDto>(_user);

    [Benchmark]
    public UserDto AutoMapper() => _autoMapper.Map<UserDto>(_user);
}

#endregion

#region EF Core Benchmarks

[MemoryDiagnoser]
public class EfCoreBenchmarks
{
    private TestDbContext _context = null!;
    private AutoMapper.IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new TestDbContext(options);

        _context.Users.AddRange(Enumerable.Range(1, 100).Select(i => new UserEntity
        {
            Id = i, FirstName = $"User{i}", LastName = $"Last{i}",
            Email = $"user{i}@test.com", IsActive = i % 2 == 0
        }));
        _context.SaveChanges();

        var config = new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<UserEntity, UserDto>();
        });
        _autoMapper = config.CreateMapper();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context.Dispose();
        QueryableExtensions.ClearProjectionCache();
    }

    [Benchmark(Baseline = true)]
    public List<UserDto> ManualSelect()
    {
        return _context.Users.Select(u => new UserDto
        {
            Id = u.Id, FirstName = u.FirstName, LastName = u.LastName,
            Email = u.Email, IsActive = u.IsActive
        }).ToList();
    }

    [Benchmark]
    public List<UserDto> MapsicleProjectTo()
    {
        return _context.Users.ProjectTo<UserEntity, UserDto>().ToList();
    }

    [Benchmark]
    public List<UserDto> AutoMapperProjectTo()
    {
        return _autoMapper.ProjectTo<UserDto>(_context.Users).ToList();
    }
}

#endregion

#region Edge Case Benchmarks (from Ruthless Criticism)

[MemoryDiagnoser]
[RankColumn]
public class EdgeCaseBenchmarks
{
    private ParentNode _circularRef = null!;
    private DeepEntity _deepNested = null!;
    private List<UserEntity> _largeCollection = null!;
    private AutoMapper.IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Circular reference setup
        _circularRef = new ParentNode { Id = 1, Name = "Parent" };
        var child = new ChildNode { Id = 2, Name = "Child" };
        _circularRef.Child = child;
        // Note: Not setting child.Parent to avoid stack overflow in AutoMapper

        // Deep nesting (tests depth limits)
        _deepNested = CreateDeeplyNestedEntity(15);

        // Large collection
        _largeCollection = Enumerable.Range(1, 10000)
            .Select(i => new UserEntity { Id = i, FirstName = $"User{i}", LastName = $"Last{i}" })
            .ToList();

        var config = new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<ParentNode, ParentNodeDto>();
            cfg.CreateMap<ChildNode, ChildNodeDto>();
            cfg.CreateMap<DeepEntity, DeepDto>().MaxDepth(32);
            cfg.CreateMap<UserEntity, UserDto>();
        });
        _autoMapper = config.CreateMapper();

        // Set max depth for Mapsicle
        Mapsicle.Mapper.MaxDepth = 32;
    }

    DeepEntity CreateDeeplyNestedEntity(int depth)
    {
        var root = new DeepEntity { Id = 0, Name = "Level0" };
        var current = root;
        for (int i = 1; i < depth; i++)
        {
            current.Nested = new DeepEntity { Id = i, Name = $"Level{i}" };
            current = current.Nested;
        }
        return root;
    }

    [Benchmark(Description = "Deep nesting (15 levels) - Mapsicle")]
    public DeepDto? Mapsicle_DeepNesting() => _deepNested.MapTo<DeepDto>();

    [Benchmark(Description = "Deep nesting (15 levels) - AutoMapper")]
    public DeepDto AutoMapper_DeepNesting() => _autoMapper.Map<DeepDto>(_deepNested);

    [Benchmark(Description = "Large collection (10K) - Mapsicle")]
    public List<UserDto> Mapsicle_LargeCollection() => _largeCollection.MapTo<UserDto>();

    [Benchmark(Description = "Large collection (10K) - AutoMapper")]
    public List<UserDto> AutoMapper_LargeCollection() => _autoMapper.Map<List<UserDto>>(_largeCollection);

    [Benchmark(Description = "Cold start (new type) - Mapsicle")]
    public DeepDto? Mapsicle_ColdStart()
    {
        Mapsicle.Mapper.ClearCache();
        return _deepNested.MapTo<DeepDto>();
    }

    [Benchmark(Description = "Cache hit - Mapsicle")]
    public DeepDto? Mapsicle_CacheHit() => _deepNested.MapTo<DeepDto>();
}

#endregion

#region Real-World Scenario Benchmarks

[MemoryDiagnoser]
[RankColumn]
public class RealWorldScenarioBenchmarks
{
    private List<ECommerceOrder> _orders = null!;
    private AutoMapper.IMapper _autoMapper = null!;
    private Mapsicle.Fluent.IMapper _fluentMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Simulate real e-commerce data (100 orders)
        _orders = Enumerable.Range(1, 100).Select(i => new ECommerceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"ORD-{i:D6}",
            CreatedAt = DateTime.Now.AddDays(-i),
            Status = (OrderStatus)(i % 5),
            Customer = new Customer
            {
                Id = i % 50 + 1,
                FirstName = $"First{i % 50}",
                LastName = $"Last{i % 50}",
                Email = $"customer{i % 50}@example.com",
                Phone = $"+1-555-{i:D4}",
                JoinedAt = DateTime.Now.AddMonths(-i),
                LoyaltyPoints = i * 100
            },
            ShippingAddress = new ShippingAddress
            {
                Line1 = $"{i} Main Street",
                City = "New York",
                State = "NY",
                PostalCode = "10001",
                Country = "USA"
            },
            Lines = Enumerable.Range(1, i % 5 + 1).Select(j => new OrderLine
            {
                Id = i * 100 + j,
                SKU = $"SKU-{j:D4}",
                ProductName = $"Product {j}",
                Quantity = j * 2,
                UnitPrice = 19.99m * j,
                TotalPrice = j * 2 * 19.99m * j
            }).ToList(),
            Subtotal = Enumerable.Range(1, i % 5 + 1).Sum(j => j * 2 * 19.99m * j),
            Tax = i * 2.5m,
            Shipping = 9.99m,
            Total = Enumerable.Range(1, i % 5 + 1).Sum(j => j * 2 * 19.99m * j) + i * 2.5m + 9.99m,
            PaymentMethod = (PaymentMethod)(i % 4),
            Notes = i % 3 == 0 ? null : $"Order notes for {i}"
        }).ToList();

        var fluentConfig = new Mapsicle.Fluent.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<ECommerceOrder, ECommerceOrderDto>()
                .ForMember(d => d.Status, o => o.ResolveUsing(s => s.Status.ToString()))
                .ForMember(d => d.CustomerName, o => o.ResolveUsing(s => $"{s.Customer.FirstName} {s.Customer.LastName}"))
                .ForMember(d => d.CustomerEmail, o => o.MapFrom(s => s.Customer.Email))
                .ForMember(d => d.ShippingCity, o => o.MapFrom(s => s.ShippingAddress.City))
                .ForMember(d => d.ShippingCountry, o => o.MapFrom(s => s.ShippingAddress.Country))
                .ForMember(d => d.ItemCount, o => o.ResolveUsing(s => s.Lines.Count));
        });
        _fluentMapper = fluentConfig.CreateMapper();

        var autoConfig = new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<ECommerceOrder, ECommerceOrderDto>()
                .ForMember(d => d.Status, o => o.MapFrom(s => s.Status.ToString()))
                .ForMember(d => d.CustomerName, o => o.MapFrom(s => $"{s.Customer.FirstName} {s.Customer.LastName}"))
                .ForMember(d => d.CustomerEmail, o => o.MapFrom(s => s.Customer.Email))
                .ForMember(d => d.ShippingCity, o => o.MapFrom(s => s.ShippingAddress.City))
                .ForMember(d => d.ShippingCountry, o => o.MapFrom(s => s.ShippingAddress.Country))
                .ForMember(d => d.ItemCount, o => o.MapFrom(s => s.Lines.Count));
        });
        _autoMapper = autoConfig.CreateMapper();
    }

    [Benchmark(Baseline = true, Description = "E-Commerce Orders - Manual")]
    public List<ECommerceOrderDto> Manual_Orders()
    {
        return _orders.Select(o => new ECommerceOrderDto
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            CreatedAt = o.CreatedAt,
            Status = o.Status.ToString(),
            CustomerName = $"{o.Customer.FirstName} {o.Customer.LastName}",
            CustomerEmail = o.Customer.Email,
            ShippingCity = o.ShippingAddress.City,
            ShippingCountry = o.ShippingAddress.Country,
            ItemCount = o.Lines.Count,
            Total = o.Total
        }).ToList();
    }

    [Benchmark(Description = "E-Commerce Orders - Mapsicle.Fluent")]
    public List<ECommerceOrderDto> MapsicleFluent_Orders()
    {
        return _orders.Select(o => _fluentMapper.Map<ECommerceOrderDto>(o)!).ToList();
    }

    [Benchmark(Description = "E-Commerce Orders - AutoMapper")]
    public List<ECommerceOrderDto> AutoMapper_Orders()
    {
        return _autoMapper.Map<List<ECommerceOrderDto>>(_orders);
    }

    [Benchmark(Description = "Single complex object - Mapsicle.Fluent")]
    public ECommerceOrderDto? Mapsicle_SingleComplex() => _fluentMapper.Map<ECommerceOrderDto>(_orders[0]);

    [Benchmark(Description = "Single complex object - AutoMapper")]
    public ECommerceOrderDto AutoMapper_SingleComplex() => _autoMapper.Map<ECommerceOrderDto>(_orders[0]);
}

#endregion

#region Concurrency Benchmarks

[MemoryDiagnoser]
public class ConcurrencyBenchmarks
{
    private UserEntity _user = null!;
    private AutoMapper.IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _user = new UserEntity { Id = 1, FirstName = "Alice", LastName = "Smith", Email = "alice@test.com" };

        var config = new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<UserEntity, UserDto>();
        });
        _autoMapper = config.CreateMapper();

        // Warm up
        _ = _user.MapTo<UserDto>();
    }

    [Benchmark(Baseline = true, Description = "1000 parallel mappings - Mapsicle")]
    public int Mapsicle_Concurrent()
    {
        var count = 0;
        Parallel.For(0, 1000, _ =>
        {
            var dto = _user.MapTo<UserDto>();
            if (dto != null) Interlocked.Increment(ref count);
        });
        return count;
    }

    [Benchmark(Description = "1000 parallel mappings - AutoMapper")]
    public int AutoMapper_Concurrent()
    {
        var count = 0;
        Parallel.For(0, 1000, _ =>
        {
            var dto = _autoMapper.Map<UserDto>(_user);
            if (dto != null) Interlocked.Increment(ref count);
        });
        return count;
    }

    [Benchmark(Description = "Mixed read/write cache - Mapsicle")]
    public int Mapsicle_CacheThrash()
    {
        var count = 0;
        Parallel.For(0, 100, i =>
        {
            // Simulate cache pressure
            if (i % 10 == 0) Mapsicle.Mapper.ClearCache();
            var dto = _user.MapTo<UserDto>();
            if (dto != null) Interlocked.Increment(ref count);
        });
        return count;
    }
}

#endregion

#region Cache Benchmarks

/// <summary>
/// Benchmarks for cache performance: cold start vs warm, LRU vs Unbounded, eviction overhead
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class CacheBenchmarks
{
    private UserEntity _user = null!;
    private List<UserEntity> _users = null!;

    [GlobalSetup]
    public void Setup()
    {
        _user = new UserEntity { Id = 1, FirstName = "Test", LastName = "User", Email = "test@test.com" };
        _users = Enumerable.Range(1, 1000)
            .Select(i => new UserEntity { Id = i, FirstName = $"User{i}", LastName = $"Last{i}", Email = $"user{i}@test.com" })
            .ToList();
    }

    [Benchmark(Baseline = true, Description = "Warm cache - single type")]
    public UserDto? WarmCache()
    {
        // First call warms cache
        _ = _user.MapTo<UserDto>();
        // Second call uses cached mapper
        return _user.MapTo<UserDto>();
    }

    [Benchmark(Description = "Cold start - cache cleared")]
    public UserDto? ColdStart()
    {
        Mapper.ClearCache();
        return _user.MapTo<UserDto>();
    }

    [Benchmark(Description = "Unbounded cache - 1000 mappings")]
    public List<UserDto> UnboundedCache_1000Mappings()
    {
        var originalUseLru = Mapper.UseLruCache;
        try
        {
            Mapper.UseLruCache = false;
            Mapper.ClearCache();
            return _users.MapTo<UserDto>();
        }
        finally
        {
            Mapper.UseLruCache = originalUseLru;
        }
    }

    [Benchmark(Description = "LRU cache - 1000 mappings")]
    public List<UserDto> LruCache_1000Mappings()
    {
        var originalUseLru = Mapper.UseLruCache;
        var originalMaxCache = Mapper.MaxCacheSize;
        try
        {
            Mapper.UseLruCache = true;
            Mapper.MaxCacheSize = 100;
            Mapper.ClearCache();
            return _users.MapTo<UserDto>();
        }
        finally
        {
            Mapper.UseLruCache = originalUseLru;
            Mapper.MaxCacheSize = originalMaxCache;
        }
    }

    [Benchmark(Description = "Cache hit ratio - repeated mappings")]
    public int CacheHitRatio()
    {
        Mapper.ClearCache();
        
        // Warm up cache
        _ = _user.MapTo<UserDto>();
        
        // Perform many mappings (should hit cache)
        int count = 0;
        for (int i = 0; i < 10000; i++)
        {
            var dto = _user.MapTo<UserDto>();
            if (dto != null) count++;
        }
        return count;
    }

    [Benchmark(Description = "Cache eviction overhead - LRU")]
    public List<UserDto> CacheEvictionOverhead()
    {
        var originalUseLru = Mapper.UseLruCache;
        var originalMaxCache = Mapper.MaxCacheSize;
        try
        {
            Mapper.UseLruCache = true;
            Mapper.MaxCacheSize = 10; // Very small cache to force evictions
            Mapper.ClearCache();
            
            // Map many times to cause evictions
            var results = new List<UserDto>();
            for (int i = 0; i < 100; i++)
            {
                results.AddRange(_users.Take(20).MapTo<UserDto>());
            }
            return results;
        }
        finally
        {
            Mapper.UseLruCache = originalUseLru;
            Mapper.MaxCacheSize = originalMaxCache;
        }
    }

    [Benchmark(Description = "PropertyInfo cache effectiveness")]
    public List<UserDto> PropertyInfoCacheEffectiveness()
    {
        Mapper.ClearCache();
        
        // First mapping builds PropertyInfo cache
        var first = _users.Take(10).MapTo<UserDto>();
        
        // Subsequent mappings benefit from cached PropertyInfo
        var second = _users.Skip(10).Take(990).MapTo<UserDto>();
        
        return first.Concat(second).ToList();
    }
}

#endregion
