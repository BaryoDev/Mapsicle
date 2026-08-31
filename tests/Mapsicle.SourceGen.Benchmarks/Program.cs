using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Mapsicle;
using Riok.Mapperly.Abstractions;

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

// Identical shape, deliberately not declared, so the static door compiles an expression tree for it.
// Comparing the generated lane against the instance mapper would compare it against a different and
// slower implementation rather than against the engine people actually call.
public class SgPlain
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
}

public class SgPlainDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
}

/// <summary>
/// Mapperly, the source generator this one is measured against.
/// </summary>
/// <remarks>
/// It emits a direct call at the call site: no delegate, no cache, no lookup. That is the ceiling a
/// cache pre-loader is being compared to, and the reason the comparison is worth making rather than
/// assuming.
/// </remarks>
[Mapper]
public partial class SgMapperly
{
    public partial SgUserDto Map(SgUser source);
    public partial List<SgUserDto> MapList(List<SgUser> source);
}

/// <summary>
/// What the generated lane costs against the runtime lane, Mapperly and AutoMapper, in one process.
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
    private SgPlain _onePlain;
    private List<SgPlain> _hundredPlain;
    private IMapperInstance _runtime;
    private AutoMapper.IMapper _autoMapper;
    private SgMapperly _mapperly;
    private Func<SgUser, SgUserDto> _held;

    [GlobalSetup]
    public void Setup()
    {
        _one = new SgUser { Id = 1, FirstName = "Ada", LastName = "Lovelace", Email = "a@b.c", IsActive = true };
        _hundred = Enumerable.Range(0, 100).Select(i => new SgUser
        { Id = i, FirstName = "f", LastName = "l", Email = "e", IsActive = true }).ToList();

        _onePlain = new SgPlain { Id = 1, FirstName = "Ada", LastName = "Lovelace", Email = "a@b.c", IsActive = true };
        _hundredPlain = Enumerable.Range(0, 100).Select(i => new SgPlain
        { Id = i, FirstName = "f", LastName = "l", Email = "e", IsActive = true }).ToList();

        _runtime = MapperFactory.Create();
        _mapperly = new SgMapperly();

        // The generated delegate itself, pulled out of the typed cache. Invoking this is the
        // generated code with none of the engine around it.
        var entryField = typeof(Mapper).GetNestedType("TypedMapperCache`2", System.Reflection.BindingFlags.NonPublic)
            !.MakeGenericType(typeof(SgUser), typeof(SgUserDto))
            .GetProperty("Entry", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            !.GetValue(null)!;
        _held = (Func<SgUser, SgUserDto>)entryField.GetType()
            .GetField("CompiledMapper", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            !.GetValue(entryField)!;
        _autoMapper = new AutoMapper.MapperConfiguration(
            c => c.CreateMap<SgUser, SgUserDto>(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).CreateMapper();

        // Both lanes must produce the same thing or the timings are meaningless. Every member, not
        // a sample of two: a generated mapper that drops FirstName is faster than one that does not,
        // and checking Id and Email would have reported that as a win.
        var generated = ((object)_one).MapTo<SgUserDto>();
        var interpreted = _runtime.MapTo<SgUserDto>(_one);

        var disagreements = new List<string>();
        void Compare(string name, object? a, object? b)
        {
            if (!Equals(a, b)) disagreements.Add($"{name}: generated={a ?? "null"} engine={b ?? "null"}");
        }

        Compare(nameof(SgUserDto.Id), generated!.Id, interpreted!.Id);
        Compare(nameof(SgUserDto.FirstName), generated.FirstName, interpreted.FirstName);
        Compare(nameof(SgUserDto.LastName), generated.LastName, interpreted.LastName);
        Compare(nameof(SgUserDto.Email), generated.Email, interpreted.Email);
        Compare(nameof(SgUserDto.IsActive), generated.IsActive, interpreted.IsActive);

        if (disagreements.Count > 0)
        {
            throw new Exception(
                "the lanes disagree, so these numbers would be comparing different work:\n  "
                + string.Join("\n  ", disagreements));
        }

        Console.WriteLine($"lane agreement check ok, {5} members compared");
    }

    // ---- where the time goes ---------------------------------------------------------------
    //
    // Every row below maps the same object into the same destination. The only thing that varies is
    // how the mapping is reached, so the differences are the cost of the route rather than the cost
    // of the work.

    [Benchmark(Description = "route: hand written")]
    public SgUserDto RouteManual() => new SgUserDto
    {
        Id = _one.Id,
        FirstName = _one.FirstName,
        LastName = _one.LastName,
        Email = _one.Email,
        IsActive = _one.IsActive,
    };

    [Benchmark(Description = "route: Mapperly, direct call")]
    public SgUserDto RouteMapperly() => _mapperly.Map(_one);

    [Benchmark(Description = "route: generated delegate, held directly")]
    public SgUserDto RouteHeldDelegate() => _held(_one);

    [Benchmark(Description = "route: typed door, static field read")]
    public SgUserDto RouteTypedDoor() => _one.MapTo<SgUser, SgUserDto>();

    [Benchmark(Description = "route: untyped door, dictionary lookup")]
    public SgUserDto RouteUntypedDoor() => ((object)_one).MapTo<SgUserDto>();

    // No cast, so the compiler picks the most specific extension in scope. If the generator emitted
    // one for SgUser it binds here; if it did not, this is the object overload and the timing will
    // say so rather than the code review.
    [Benchmark(Description = "route: MapTo with no cast (binds to generated?)")]
    public SgUserDto RouteNoCast() => _one.MapTo<SgUserDto>();

    [Benchmark(Baseline = true, Description = "single, generated")]
    public SgUserDto SingleGenerated() => ((object)_one).MapTo<SgUserDto>();

    [Benchmark(Description = "single, engine (undeclared pair)")]
    public SgPlainDto SingleEngine() => ((object)_onePlain).MapTo<SgPlainDto>();

    [Benchmark(Description = "single, instance mapper")]
    public SgUserDto SingleInstance() => _runtime.MapTo<SgUserDto>(_one);

    [Benchmark(Description = "single, Mapperly")]
    public SgUserDto SingleMapperly() => _mapperly.Map(_one);

    [Benchmark(Description = "single, AutoMapper")]
    public SgUserDto SingleAutoMapper() => _autoMapper.Map<SgUserDto>(_one);

    [Benchmark(Description = "100, generated")]
    public List<SgUserDto> ManyGenerated() => ((IEnumerable)_hundred).MapTo<SgUserDto>();

    [Benchmark(Description = "100, engine (undeclared pair)")]
    public List<SgPlainDto> ManyEngine() => ((IEnumerable)_hundredPlain).MapTo<SgPlainDto>();

    [Benchmark(Description = "100, Mapperly")]
    public List<SgUserDto> ManyMapperly() => _mapperly.MapList(_hundred);

    [Benchmark(Description = "100, AutoMapper")]
    public List<SgUserDto> ManyAutoMapper() => _autoMapper.Map<List<SgUserDto>>(_hundred);
}

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--cold")
        {
            ColdStart.Report();
            return;
        }

        Run();
    }

    /// <summary>
    /// What the first map of a pair costs, which is what the generator actually removes.
    /// </summary>
    /// <remarks>
    /// Steady state is not where a cache pre-loader helps: once the engine has compiled a pair it
    /// invokes a delegate, and so does the generated lane. The difference is the compile, and a
    /// benchmark that measures the thousandth map cannot see it. Clearing the cache forces the
    /// engine to build again while a generated registration is re-applied, so the gap between these
    /// two is the Expression.Compile the generator avoids.
    /// </remarks>
    private static class ColdStart
    {
        internal static void Report()
        {
            var declared = new SgUser { Id = 1, FirstName = "Ada", LastName = "L", Email = "a@b.c", IsActive = true };
            var undeclared = new SgPlain { Id = 1, FirstName = "Ada", LastName = "L", Email = "a@b.c", IsActive = true };

            // Warm both paths once so neither pays for JIT during the measurement.
            _ = ((object)declared).MapTo<SgUserDto>();
            _ = ((object)undeclared).MapTo<SgPlainDto>();

            const int rounds = 200;

            var mapperly = new SgMapperly();
            _ = mapperly.Map(declared);

            var generated = Measure(rounds, () => _ = ((object)declared).MapTo<SgUserDto>());
            var engine = Measure(rounds, () => _ = ((object)undeclared).MapTo<SgPlainDto>());
            var mapperlyCold = Measure(rounds, () => _ = mapperly.Map(declared));

            Console.WriteLine();
            Console.WriteLine($"  first map after ClearCache, mean of {rounds}");
            Console.WriteLine($"    Mapsicle generated   {generated,10:N0} ns");
            Console.WriteLine($"    Mapsicle engine      {engine,10:N0} ns");
            Console.WriteLine($"    Mapperly             {mapperlyCold,10:N0} ns   (nothing to warm, ClearCache does not reach it)");
            Console.WriteLine($"    compile avoided      {engine - generated,10:N0} ns");
        }

        private static double Measure(int rounds, Action firstMap)
        {
            var total = 0L;
            for (var i = 0; i < rounds; i++)
            {
                Mapper.ClearCache();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                firstMap();
                sw.Stop();
                total += sw.ElapsedTicks;
            }
            return (double)total / rounds * (1_000_000_000.0 / System.Diagnostics.Stopwatch.Frequency);
        }
    }

    private static void Run() => BenchmarkRunner.Run<GeneratedVsRuntime>(
        DefaultConfig.Instance
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)
            .AddJob(Job.Default.WithWarmupCount(5).WithIterationCount(20)));
}
