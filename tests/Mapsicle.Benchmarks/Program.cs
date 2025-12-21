using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Mapsicle;

namespace Mapsicle.Benchmarks;

/// <summary>
/// Benchmarks comparing Mapsicle vs AutoMapper vs Manual Mapping.
/// Run with: dotnet run -c Release
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class MapperBenchmarks
{
    #region Test Models

    public class UserEntity
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public Address? Address { get; set; }
    }

    public class Address
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
    }

    public class UserDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public AddressDto? Address { get; set; }
    }

    public class AddressDto
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
    }

    public class UserFlatDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string AddressCity { get; set; } = string.Empty;
        public string AddressCountry { get; set; } = string.Empty;
    }

    #endregion

    private UserEntity _singleUser = null!;
    private List<UserEntity> _userList = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Single user with nested address
        _singleUser = new UserEntity
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            CreatedAt = DateTime.Now,
            IsActive = true,
            Address = new Address
            {
                Street = "123 Main St",
                City = "New York",
                State = "NY",
                ZipCode = "10001",
                Country = "USA"
            }
        };

        // Collection of 100 users
        _userList = Enumerable.Range(1, 100).Select(i => new UserEntity
        {
            Id = i,
            FirstName = $"User{i}",
            LastName = $"Last{i}",
            Email = $"user{i}@example.com",
            CreatedAt = DateTime.Now,
            IsActive = i % 2 == 0,
            Address = new Address
            {
                Street = $"{i} Test St",
                City = "City",
                State = "ST",
                ZipCode = $"{10000 + i}",
                Country = "Country"
            }
        }).ToList();

        // Configure AutoMapper
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<UserEntity, UserDto>();
            cfg.CreateMap<Address, AddressDto>();
            cfg.CreateMap<UserEntity, UserFlatDto>()
                .ForMember(d => d.AddressCity, opt => opt.MapFrom(s => s.Address != null ? s.Address.City : string.Empty))
                .ForMember(d => d.AddressCountry, opt => opt.MapFrom(s => s.Address != null ? s.Address.Country : string.Empty));
        });
        _autoMapper = config.CreateMapper();

        // Warm up Mapsicle cache
        _ = _singleUser.MapTo<UserDto>();
        Mapsicle.Mapper.ClearCache();
    }

    #region Single Object Mapping

    [Benchmark(Baseline = true)]
    public UserDto Manual_SingleObject()
    {
        return new UserDto
        {
            Id = _singleUser.Id,
            FirstName = _singleUser.FirstName,
            LastName = _singleUser.LastName,
            Email = _singleUser.Email,
            CreatedAt = _singleUser.CreatedAt,
            IsActive = _singleUser.IsActive,
            Address = _singleUser.Address != null ? new AddressDto
            {
                Street = _singleUser.Address.Street,
                City = _singleUser.Address.City,
                State = _singleUser.Address.State,
                ZipCode = _singleUser.Address.ZipCode,
                Country = _singleUser.Address.Country
            } : null
        };
    }

    [Benchmark]
    public UserDto Mapsicle_SingleObject()
    {
        return _singleUser.MapTo<UserDto>()!;
    }

    [Benchmark]
    public UserDto AutoMapper_SingleObject()
    {
        return _autoMapper.Map<UserDto>(_singleUser);
    }

    #endregion

    #region Collection Mapping (100 items)

    [Benchmark]
    public List<UserDto> Manual_Collection()
    {
        return _userList.Select(u => new UserDto
        {
            Id = u.Id,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Email = u.Email,
            CreatedAt = u.CreatedAt,
            IsActive = u.IsActive,
            Address = u.Address != null ? new AddressDto
            {
                Street = u.Address.Street,
                City = u.Address.City,
                State = u.Address.State,
                ZipCode = u.Address.ZipCode,
                Country = u.Address.Country
            } : null
        }).ToList();
    }

    [Benchmark]
    public List<UserDto> Mapsicle_Collection()
    {
        return _userList.MapTo<UserDto>();
    }

    [Benchmark]
    public List<UserDto> AutoMapper_Collection()
    {
        return _autoMapper.Map<List<UserDto>>(_userList);
    }

    #endregion

    #region Flattening

    [Benchmark]
    public UserFlatDto Manual_Flattening()
    {
        return new UserFlatDto
        {
            Id = _singleUser.Id,
            FirstName = _singleUser.FirstName,
            Email = _singleUser.Email,
            AddressCity = _singleUser.Address?.City ?? string.Empty,
            AddressCountry = _singleUser.Address?.Country ?? string.Empty
        };
    }

    [Benchmark]
    public UserFlatDto Mapsicle_Flattening()
    {
        return _singleUser.MapTo<UserFlatDto>()!;
    }

    [Benchmark]
    public UserFlatDto AutoMapper_Flattening()
    {
        return _autoMapper.Map<UserFlatDto>(_singleUser);
    }

    #endregion

    #region First-Run (Cold Start) - measures setup overhead

    [Benchmark]
    public UserDto Mapsicle_ColdStart()
    {
        Mapsicle.Mapper.ClearCache();
        return _singleUser.MapTo<UserDto>()!;
    }

    #endregion
}

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("===========================================");
        Console.WriteLine("  Mapsicle vs AutoMapper Benchmark Suite   ");
        Console.WriteLine("===========================================");
        Console.WriteLine();

        var summary = BenchmarkRunner.Run<MapperBenchmarks>();

        Console.WriteLine();
        Console.WriteLine("Benchmark complete! Check the 'BenchmarkDotNet.Artifacts' folder for detailed results.");
    }
}
