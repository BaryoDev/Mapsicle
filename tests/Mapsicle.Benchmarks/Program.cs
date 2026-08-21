using System.Collections.Concurrent;
using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Mapsicle;
using Mapsicle.EntityFramework;
using Mapsicle.Fluent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Riok.Mapperly.Abstractions;

namespace Mapsicle.Benchmarks;

/// <summary>
/// Comprehensive benchmarks for all Mapsicle packages vs AutoMapper.
/// Covers: Core, Fluent, EntityFramework, Edge Cases, Real-World Scenarios
/// Run with: dotnet run -c Release
/// </summary>
public class Program
{
    /// <summary>
    /// Claims from the README that this run found to be untrue. A non-empty list fails the build.
    /// </summary>
    private static readonly List<string> ClaimFailures = new();

    /// <summary>
    /// Returns a non-zero exit code when a claim the project is sold on fails to hold.
    /// </summary>
    /// <remarks>
    /// The README leads with "faster than AutoMapper", and until this returned an exit
    /// code the suite measured exactly that and then printed it into a log nobody reads.
    /// A claim nothing checks is a claim that quietly stops being true.
    /// </remarks>
    public static int Main(string[] args)
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
        else if (args.Length > 0 && args[0] == "--gate")
        {
            return RunClaimGate();
        }
        else if (args.Length > 0 && args[0] == "--core")
        {
            // The single suite behind the README's headline table, on a short job so the numbers
            // can actually be refreshed when a claim is edited rather than only in principle.
            BenchmarkRunner.Run<CoreMapperBenchmarks>(
                DefaultConfig.Instance
                    .WithOptions(ConfigOptions.DisableOptimizationsValidator)
                    .AddJob(Job.ShortRun));
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

        if (ClaimFailures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("=================================================");
            Console.WriteLine("  CLAIM CHECK FAILED");
            foreach (var failure in ClaimFailures)
            {
                Console.WriteLine($"  - {failure}");
            }
            Console.WriteLine("=================================================");
            return 1;
        }

        return 0;
    }


    /// <summary>
    /// Checks the performance claim against BenchmarkDotNet's measurement, not a stopwatch loop.
    /// </summary>
    /// <remarks>
    /// This used to gate on a hand-rolled loop of 100,000 iterations timed with a Stopwatch. On the
    /// same machine that loop reported 290ns per single-object map where BenchmarkDotNet reported
    /// 33ns, a factor of nine, because the loop measures allocation and collection pressure along
    /// with the mapping and has no isolation between the two mappers. A gate is only as trustworthy
    /// as its instrument, and that one was not.
    ///
    /// BenchmarkDotNet already does the hard parts: warmup until the measurement stabilises,
    /// separate processes per benchmark, and outlier removal. Reading its Summary gives the same
    /// numbers the README publishes, from the same source, so the two cannot drift.
    /// </remarks>
    private static int RunClaimGate()
    {
        Console.WriteLine("Measuring the performance claim with BenchmarkDotNet (short job).\n");

        var summary = BenchmarkRunner.Run<CoreMapperBenchmarks>(
            DefaultConfig.Instance
                .WithOptions(ConfigOptions.DisableOptimizationsValidator)
                .AddJob(Job.ShortRun));

        double? MeanNs(string method)
        {
            foreach (var report in summary.Reports)
            {
                if (report.BenchmarkCase.Descriptor.WorkloadMethod.Name == method)
                {
                    return report.ResultStatistics?.Mean;
                }
            }
            return null;
        }

        var mapsicle = MeanNs("Mapsicle_Single");
        var autoMapper = MeanNs("AutoMapper_Single");

        if (mapsicle is null || autoMapper is null)
        {
            Console.WriteLine("Could not read both single-object results from the benchmark summary.");
            return 1;
        }

        var ratio = mapsicle.Value / autoMapper.Value;
        Console.WriteLine();
        Console.WriteLine($"  Mapsicle_Single:   {mapsicle.Value:F1} ns");
        Console.WriteLine($"  AutoMapper_Single: {autoMapper.Value:F1} ns");
        Console.WriteLine($"  ratio: {ratio:F2}x {(ratio < 1 ? "(faster)" : "(slower)")}");

        // Bounded at parity plus a tenth. The README states a number; this states only that the
        // direction of the comparison still holds, which is what survives a change of hardware.
        if (ratio > 1.10)
        {
            Console.WriteLine();
            Console.WriteLine("CLAIM CHECK FAILED");
            Console.WriteLine($"  Mapsicle is {ratio:F2}x AutoMapper on single-object mapping.");
            Console.WriteLine("  The README claims it is faster. Update the code or the claim.");
            return 1;
        }

        return 0;
    }

    static void RunSmokeTests()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Setup AutoMapper for comparison
        var autoConfig = new AutoMapper.MapperConfiguration(cfg => cfg.CreateMap<UserEntity, UserDto>(), NullLoggerFactory.Instance);
        var autoMapper = autoConfig.CreateMapper();

        var user = new UserEntity { Id = 1, FirstName = "Test", LastName = "User", Email = "test@test.com" };

        // Warm up both mappers
        _ = user.MapTo<UserDto>();
        _ = autoMapper.Map<UserDto>(user);
        Mapsicle.Mapper.ClearCache();

        // Cold start comparison
        sw.Restart();
        for (int i = 0; i < 10000; i++)
            _ = user.MapTo<UserDto>();
        var mapsicleTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"✓ Mapsicle (cold+warm): 10,000 mappings in {mapsicleTime}ms");

        sw.Restart();
        for (int i = 0; i < 10000; i++)
            _ = autoMapper.Map<UserDto>(user);
        var autoMapperTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"  AutoMapper (warm):    10,000 mappings in {autoMapperTime}ms");

        // Warm start comparison (cache hit scenario)
        Console.WriteLine("\n--- Warm Cache Performance (100,000 iterations) ---");

        // Mapsicle warm
        _ = user.MapTo<UserDto>(); // Ensure cached
        sw.Restart();
        for (int i = 0; i < 100000; i++)
            _ = user.MapTo<UserDto>();
        mapsicleTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"  Mapsicle:   {mapsicleTime}ms ({100000.0 / mapsicleTime * 1000:N0} ops/sec)");

        // AutoMapper warm
        sw.Restart();
        for (int i = 0; i < 100000; i++)
            _ = autoMapper.Map<UserDto>(user);
        autoMapperTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"  AutoMapper: {autoMapperTime}ms ({100000.0 / autoMapperTime * 1000:N0} ops/sec)");

        // Manual (baseline)
        sw.Restart();
        for (int i = 0; i < 100000; i++)
            _ = new UserDto { Id = user.Id, FirstName = user.FirstName, LastName = user.LastName, Email = user.Email, IsActive = user.IsActive };
        var manualTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"  Manual:     {manualTime}ms ({100000.0 / manualTime * 1000:N0} ops/sec)");

        var ratio = autoMapperTime > 0 ? (double)mapsicleTime / autoMapperTime : 0;
        Console.WriteLine($"\n  Mapsicle/AutoMapper ratio: {ratio:F2}x {(ratio < 1 ? "(FASTER)" : ratio > 1 ? "(slower)" : "(equal)")}");

        // Deliberately no assertion here. This loop is a smoke test, not a measurement: it
        // reports 290ns per map where BenchmarkDotNet reports 33ns on the same machine, because it
        // times allocation and collection along with the mapping. The claim is gated by --gate,
        // which reads BenchmarkDotNet's summary.

        // Strongly-typed performance test
        Console.WriteLine("\n--- Strongly-Typed Mapper Performance (100,000 iterations) ---");
        _ = user.MapTo<UserEntity, UserDto>(); // Ensure typed cache is built
        sw.Restart();
        for (int i = 0; i < 100000; i++)
            _ = user.MapTo<UserEntity, UserDto>();
        var typedTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"  Mapsicle<S,D>: {typedTime}ms ({100000.0 / typedTime * 1000:N0} ops/sec)");

        // Fluent tests
        Console.WriteLine("\n--- Other Tests ---");
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

        // Typed collection
        Mapsicle.Mapper.ClearCache();
        sw.Restart();
        _ = largeList.MapTo<UserEntity, UserDto>();
        Console.WriteLine($"✓ Typed collection (10,000 items): {sw.ElapsedMilliseconds}ms");

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

#region Mapperly Mappers (Source Generated)

/// <summary>
/// Mapperly source-generated mapper for UserEntity -> UserDto.
/// Generated at compile time with zero runtime overhead.
/// </summary>
[Mapper]
public partial class MapperlyUserMapper
{
    public partial UserDto Map(UserEntity source);
    public partial List<UserDto> MapList(List<UserEntity> source);
}

/// <summary>
/// Mapperly source-generated mapper for UserEntity -> UserFlatDto with flattening.
/// </summary>
[Mapper]
public partial class MapperlyFlatMapper
{
    [MapProperty("Address.City", "AddressCity")]
    [MapProperty("Address.Country", "AddressCountry")]
    public partial UserFlatDto Map(UserEntity source);
}

/// <summary>
/// Mapperly source-generated mapper for deep entities.
/// </summary>
[Mapper]
public partial class MapperlyDeepMapper
{
    public partial DeepDto Map(DeepEntity source);
}

/// <summary>
/// Mapperly source-generated mapper for e-commerce orders.
/// Uses partial method for custom mapping logic.
/// </summary>
[Mapper]
public partial class MapperlyOrderMapper
{
    [MapProperty("Customer.Email", "CustomerEmail")]
    [MapProperty("ShippingAddress.City", "ShippingCity")]
    [MapProperty("ShippingAddress.Country", "ShippingCountry")]
    [MapperIgnoreSource("Customer")]
    [MapperIgnoreSource("Lines")]
    [MapperIgnoreSource("BillingAddress")]
    [MapperIgnoreSource("Subtotal")]
    [MapperIgnoreSource("Tax")]
    [MapperIgnoreSource("Shipping")]
    [MapperIgnoreSource("CouponCode")]
    [MapperIgnoreSource("Discount")]
    [MapperIgnoreSource("PaymentMethod")]
    [MapperIgnoreSource("Notes")]
    public partial ECommerceOrderDto Map(ECommerceOrder source);

    // Custom mapping for Status enum to string
    private string MapOrderStatus(OrderStatus status) => status.ToString();
}

/// <summary>
/// Simple Mapperly mapper for orders (manual approach for complex mappings).
/// </summary>
public class MapperlyOrderMapperManual
{
    public ECommerceOrderDto Map(ECommerceOrder source) => new()
    {
        Id = source.Id,
        OrderNumber = source.OrderNumber,
        CreatedAt = source.CreatedAt,
        Status = source.Status.ToString(),
        CustomerName = $"{source.Customer.FirstName} {source.Customer.LastName}",
        CustomerEmail = source.Customer.Email,
        ShippingCity = source.ShippingAddress.City,
        ShippingCountry = source.ShippingAddress.Country,
        ItemCount = source.Lines.Count,
        Total = source.Total
    };

    public List<ECommerceOrderDto> MapList(List<ECommerceOrder> source) =>
        source.Select(Map).ToList();
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
    private MapperlyUserMapper _mapperlyMapper = null!;
    private MapperlyFlatMapper _mapperlyFlatMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _user = new UserEntity
        {
            Id = 1,
            FirstName = "Alice",
            LastName = "Smith",
            Email = "alice@test.com",
            IsActive = true,
            CreatedAt = DateTime.Now,
            Address = new AddressEntity { City = "NYC", Country = "USA", Street = "123 Main", State = "NY", ZipCode = "10001" }
        };

        _users = Enumerable.Range(1, 100).Select(i => new UserEntity
        {
            Id = i,
            FirstName = $"User{i}",
            LastName = $"Last{i}",
            Email = $"user{i}@test.com",
            IsActive = i % 2 == 0
        }).ToList();

        var config = new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<UserEntity, UserDto>();
            cfg.CreateMap<AddressEntity, AddressEntity>();
            cfg.CreateMap<UserEntity, UserFlatDto>()
                .ForMember(d => d.AddressCity, o => o.MapFrom(s => s.Address != null ? s.Address.City : ""))
                .ForMember(d => d.AddressCountry, o => o.MapFrom(s => s.Address != null ? s.Address.Country : ""));
        }, NullLoggerFactory.Instance);
        _autoMapper = config.CreateMapper();

        // Initialize Mapperly mappers (source-generated, no runtime overhead)
        _mapperlyMapper = new MapperlyUserMapper();
        _mapperlyFlatMapper = new MapperlyFlatMapper();

        // Warm up caches
        _ = _user.MapTo<UserDto>();
        Mapsicle.Mapper.ClearCache();
    }

    [Benchmark(Baseline = true)]
    public UserDto Manual_Single() => new()
    {
        Id = _user.Id,
        FirstName = _user.FirstName,
        LastName = _user.LastName,
        Email = _user.Email,
        IsActive = _user.IsActive
    };

    [Benchmark]
    public UserDto? Mapsicle_Single() => _user.MapTo<UserDto>();

    [Benchmark]
    public UserDto AutoMapper_Single() => _autoMapper.Map<UserDto>(_user);

    [Benchmark]
    public UserDto Mapperly_Single() => _mapperlyMapper.Map(_user);

    [Benchmark]
    public List<UserDto> Mapsicle_Collection() => _users.MapTo<UserDto>();

    [Benchmark]
    public List<UserDto> AutoMapper_Collection() => _autoMapper.Map<List<UserDto>>(_users);

    [Benchmark]
    public List<UserDto> Mapperly_Collection() => _mapperlyMapper.MapList(_users);

    [Benchmark]
    public UserFlatDto? Mapsicle_Flattening() => _user.MapTo<UserFlatDto>();

    [Benchmark]
    public UserFlatDto AutoMapper_Flattening() => _autoMapper.Map<UserFlatDto>(_user);

    [Benchmark]
    public UserFlatDto Mapperly_Flattening() => _mapperlyFlatMapper.Map(_user);
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
    private MapperlyUserMapper _mapperlyMapper = null!;

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
        }, NullLoggerFactory.Instance);
        _autoMapper = autoConfig.CreateMapper();

        _mapperlyMapper = new MapperlyUserMapper();
    }

    [Benchmark(Baseline = true)]
    public UserDto? MapsicleCore() => _user.MapTo<UserDto>();

    [Benchmark]
    public UserDto? MapsicleFluent() => _fluentMapper.Map<UserDto>(_user);

    [Benchmark]
    public UserDto AutoMapper() => _autoMapper.Map<UserDto>(_user);

    [Benchmark]
    public UserDto Mapperly() => _mapperlyMapper.Map(_user);
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
            Id = i,
            FirstName = $"User{i}",
            LastName = $"Last{i}",
            Email = $"user{i}@test.com",
            IsActive = i % 2 == 0
        }));
        _context.SaveChanges();

        var config = new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<UserEntity, UserDto>();
        }, NullLoggerFactory.Instance);
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
            Id = u.Id,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Email = u.Email,
            IsActive = u.IsActive
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
    private MapperlyDeepMapper _mapperlyDeepMapper = null!;
    private MapperlyUserMapper _mapperlyUserMapper = null!;

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
        }, NullLoggerFactory.Instance);
        _autoMapper = config.CreateMapper();

        // Initialize Mapperly mappers
        _mapperlyDeepMapper = new MapperlyDeepMapper();
        _mapperlyUserMapper = new MapperlyUserMapper();

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

    [Benchmark(Description = "Deep nesting (15 levels) - Mapperly")]
    public DeepDto Mapperly_DeepNesting() => _mapperlyDeepMapper.Map(_deepNested);

    [Benchmark(Description = "Large collection (10K) - Mapsicle")]
    public List<UserDto> Mapsicle_LargeCollection() => _largeCollection.MapTo<UserDto>();

    [Benchmark(Description = "Large collection (10K) - AutoMapper")]
    public List<UserDto> AutoMapper_LargeCollection() => _autoMapper.Map<List<UserDto>>(_largeCollection);

    [Benchmark(Description = "Large collection (10K) - Mapperly")]
    public List<UserDto> Mapperly_LargeCollection() => _mapperlyUserMapper.MapList(_largeCollection);

    [Benchmark(Description = "Cold start (new type) - Mapsicle")]
    public DeepDto? Mapsicle_ColdStart()
    {
        Mapsicle.Mapper.ClearCache();
        return _deepNested.MapTo<DeepDto>();
    }

    [Benchmark(Description = "Cache hit - Mapsicle")]
    public DeepDto? Mapsicle_CacheHit() => _deepNested.MapTo<DeepDto>();

    [Benchmark(Baseline = true, Description = "No cold start - Mapperly (compile-time)")]
    public DeepDto Mapperly_NoColdStart() => _mapperlyDeepMapper.Map(_deepNested);
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
    private MapperlyOrderMapperManual _mapperlyMapper = null!;

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
        }, NullLoggerFactory.Instance);
        _autoMapper = autoConfig.CreateMapper();

        // Initialize Mapperly mapper (using manual implementation for complex mapping)
        _mapperlyMapper = new MapperlyOrderMapperManual();
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

    [Benchmark(Description = "E-Commerce Orders - Mapperly")]
    public List<ECommerceOrderDto> Mapperly_Orders()
    {
        return _mapperlyMapper.MapList(_orders);
    }

    [Benchmark(Description = "Single complex object - Mapsicle.Fluent")]
    public ECommerceOrderDto? Mapsicle_SingleComplex() => _fluentMapper.Map<ECommerceOrderDto>(_orders[0]);

    [Benchmark(Description = "Single complex object - AutoMapper")]
    public ECommerceOrderDto AutoMapper_SingleComplex() => _autoMapper.Map<ECommerceOrderDto>(_orders[0]);

    [Benchmark(Description = "Single complex object - Mapperly")]
    public ECommerceOrderDto Mapperly_SingleComplex() => _mapperlyMapper.Map(_orders[0]);
}

#endregion

#region Concurrency Benchmarks

[MemoryDiagnoser]
public class ConcurrencyBenchmarks
{
    private UserEntity _user = null!;
    private AutoMapper.IMapper _autoMapper = null!;
    private MapperlyUserMapper _mapperlyMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _user = new UserEntity { Id = 1, FirstName = "Alice", LastName = "Smith", Email = "alice@test.com" };

        var config = new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<UserEntity, UserDto>();
        }, NullLoggerFactory.Instance);
        _autoMapper = config.CreateMapper();

        _mapperlyMapper = new MapperlyUserMapper();

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

    [Benchmark(Description = "1000 parallel mappings - Mapperly")]
    public int Mapperly_Concurrent()
    {
        var count = 0;
        Parallel.For(0, 1000, _ =>
        {
            var dto = _mapperlyMapper.Map(_user);
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
