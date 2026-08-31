using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Mapsicle;

[assembly: MapsicleGenerate(typeof(SgUser), typeof(SgUserDto))]

public class SgUser
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
}

public class SgUserDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
}

/// <summary>
/// What the generated lane costs against the runtime lane and AutoMapper, in one process.
/// </summary>
/// <remarks>
/// The generated pair is registered by a module initializer, so the static door invokes generated
/// code. An instance mapper never sees a registration and always compiles an expression tree, so it
/// is the runtime lane. Same objects, same destination, one process.
/// </remarks>
[MemoryDiagnoser]
public class GeneratedVsRuntime
{
    private SgUser _one;
    private List<SgUser> _hundred;
    private IMapperInstance _runtime;
    private AutoMapper.IMapper _autoMapper;

    [GlobalSetup]
    public void Setup()
    {
        _one = new SgUser { Id = 1, FirstName = "Ada", LastName = "Lovelace", Email = "a@b.c", IsActive = true };
        _hundred = Enumerable.Range(0, 100).Select(i => new SgUser
        { Id = i, FirstName = "f", LastName = "l", Email = "e", IsActive = true }).ToList();

        _runtime = MapperFactory.Create();
        _autoMapper = new AutoMapper.MapperConfiguration(
            c => c.CreateMap<SgUser, SgUserDto>(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).CreateMapper();

        // Both lanes must produce the same thing or the timings are meaningless.
        var generated = ((object)_one).MapTo<SgUserDto>();
        var interpreted = _runtime.MapTo<SgUserDto>(_one);
        if (generated.Id != interpreted.Id || generated.Email != interpreted.Email)
            throw new Exception("the lanes disagree, so these numbers would be comparing different work");
        Console.WriteLine("lane agreement check ok");
    }

    [Benchmark(Baseline = true, Description = "single, generated")]
    public SgUserDto SingleGenerated() => ((object)_one).MapTo<SgUserDto>();

    [Benchmark(Description = "single, runtime engine")]
    public SgUserDto SingleRuntime() => _runtime.MapTo<SgUserDto>(_one);

    [Benchmark(Description = "single, AutoMapper")]
    public SgUserDto SingleAutoMapper() => _autoMapper.Map<SgUserDto>(_one);

    [Benchmark(Description = "100, generated")]
    public List<SgUserDto> ManyGenerated() => ((IEnumerable)_hundred).MapTo<SgUserDto>();

    [Benchmark(Description = "100, AutoMapper")]
    public List<SgUserDto> ManyAutoMapper() => _autoMapper.Map<List<SgUserDto>>(_hundred);
}

public static class Program
{
    public static void Main() => BenchmarkRunner.Run<GeneratedVsRuntime>(
        DefaultConfig.Instance
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)
            .AddJob(Job.Default.WithWarmupCount(5).WithIterationCount(20)));
}
