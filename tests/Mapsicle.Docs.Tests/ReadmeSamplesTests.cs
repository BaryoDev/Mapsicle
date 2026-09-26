using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Mapsicle.Audit;
using Mapsicle.Caching;
using Mapsicle.DataAnnotations;
using Mapsicle.Fluent;
using Mapsicle.Json;
using Mapsicle.Serilog;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Serilog.Sinks.InMemory;
using Xunit;

namespace Mapsicle.Docs.Tests
{
    /// <summary>
    /// README samples, compiled and executed, along with the behaviour claims made around them.
    /// </summary>
    /// <remarks>
    /// Every Serilog sample in the README once failed to compile, seven errors across a class that
    /// did not exist, a method with the wrong type arguments and a static that was really an
    /// options property. The API reference built a MapperOptions with a property it does not have,
    /// and the limitations section said flattening stopped at one level when three levels map.
    /// Nothing ran the README, so nothing noticed.
    ///
    /// If you change a sample in the README, change it here in the same commit.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class ReadmeSamplesTests : IDisposable
    {
        public ReadmeSamplesTests() => Mapper.ClearCache();

        public void Dispose()
        {
            SerilogExtensions.Reset();
            Mapper.Logger = null;
        }

        // ---- Package 6: Mapsicle.Serilog -------------------------------------------------------

        private static (ILogger Logger, InMemorySink Sink) Logger()
        {
            var sink = new InMemorySink();
            var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
            return (logger, sink);
        }

        [Fact]
        public void Serilog_MapWithLogging_LogsThroughTheGlobalLogger()
        {
            var (logger, sink) = Logger();
            SerilogExtensions.UseSerilog(logger);

            var dto = new DocUser { Id = 1, Name = "arnel" }.MapWithLogging<DocUserDto>();
            var dtos = new List<DocUser> { new() { Id = 2 } }.MapCollectionWithLogging<DocUserDto>();

            Assert.Equal("arnel", dto!.Name);
            Assert.Single(dtos);
            Assert.Contains(sink.LogEvents, e => e.MessageTemplate.Text.StartsWith("[Mapsicle] Mapped {SourceType} -> {DestType}"));
            Assert.Contains(sink.LogEvents, e => e.MessageTemplate.Text.StartsWith("[Mapsicle] Mapped collection of {Count} items"));
        }

        [Fact]
        public void Serilog_SlowMappingThreshold_IsOffByDefault_AndSetThroughUseSerilog()
        {
            Assert.Null(new LoggingOptions().SlowMappingThreshold);

            var (logger, sink) = Logger();
            SerilogExtensions.UseSerilog(logger, options =>
            {
                options.SlowMappingThreshold = TimeSpan.Zero;
            });

            new DocUser { Id = 1 }.MapWithLogging<DocUserDto>();

            Assert.Contains(sink.LogEvents, e => e.MessageTemplate.Text.StartsWith("[Mapsicle] Slow mapping detected"));
        }

        [Fact]
        public void Serilog_Scope_CountsOnlyWhatIsRecorded()
        {
            var (logger, sink) = Logger();
            SerilogExtensions.UseSerilog(logger);
            var orders = new List<DocUser> { new() { Id = 1 }, new() { Id = 2 } };

            using (var scope = SerilogExtensions.BeginMappingScope("OrderProcessing"))
            {
                foreach (var order in orders)
                {
                    order.MapWithLogging<DocUserDto>();
                    scope.RecordMapping();
                }

                // Not recorded, so not counted: the README says mapping calls are not tracked automatically.
                orders[0].MapWithLogging<DocUserDto>();
            }

            var completed = sink.LogEvents.Single(e => e.MessageTemplate.Text.StartsWith("[Mapsicle] Completed"));
            Assert.Equal("2", completed.Properties["MappingCount"].ToString());
            Assert.Equal("\"OrderProcessing\"", completed.Properties["OperationName"].ToString());
        }

        // ---- API reference: MapperFactory ------------------------------------------------------

        [Fact]
        public void MapperFactory_OptionsSample()
        {
            using var mapper = MapperFactory.Create(new MapperOptions
            {
                MaxDepth = 16,
                MaxCacheSize = 100,
                Logger = Console.WriteLine
            });

            var dto = mapper.MapTo<DocUserDto>(new DocUser { Id = 4, Name = "x" });

            Assert.Equal(4, dto!.Id);
        }

        // ---- Migration table: ConstructUsing ---------------------------------------------------

        [Fact]
        public void ConstructUsing_SkipsConventionMapping_ButForMemberAndAfterMapStillRun()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<DocUser, DocUserDto>()
                   .ConstructUsing(s => new DocUserDto { Id = 99 })
                   .ForMember(d => d.Email, o => o.MapFrom(s => s.Email))
                   .AfterMap((s, d) => d.Stamp = "after")).CreateMapper();

            var dto = mapper.Map<DocUserDto>(new DocUser { Id = 1, Name = "arnel", Email = "a@b.c" });

            Assert.Equal(99, dto!.Id);          // kept as the factory built it
            Assert.Null(dto.Name);               // convention did not run
            Assert.Equal("a@b.c", dto.Email);    // ForMember did
            Assert.Equal("after", dto.Stamp);    // AfterMap did
        }

        [Fact]
        public void WithoutConstructUsing_ConventionFillsTheSameMembers()
        {
            // Positive control: Name is empty above because of the factory, not because it cannot map.
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<DocUser, DocUserDto>()).CreateMapper();

            Assert.Equal("arnel", mapper.Map<DocUserDto>(new DocUser { Name = "arnel" })!.Name);
        }

        // ---- Comparison table and Known Limitations: cycles and flattening ----------------------

        [Fact]
        public void ASelfReferencingNode_IsExpandedToMaxDepthCopies_ThenStops()
        {
            var node = new DocNode { Name = "self" };
            node.Next = node;

            var dto = ((object)node).MapTo<DocNodeDto>();

            var copies = 0;
            for (var n = dto; n != null; n = n.Next) copies++;

            Assert.Equal(Mapper.MaxDepth + 1, copies);
            Assert.Equal(33, copies);
        }

        [Fact]
        public void FlatteningReachesThreeLevels()
        {
            var source = new DocOuter { Middle = new DocMiddle { Leaf = new DocLeaf { Value = "deep" } } };

            var dto = ((object)source).MapTo<DocFlatDto>();

            Assert.Equal("deep", dto!.MiddleLeafValue);
        }

        // ---- Package 9: Mapsicle.Json ----------------------------------------------------------

        [Fact]
        public void Json_Sample()
        {
            var user = new DocUser { Id = 5, Name = "json" };
            var users = new List<DocUser> { user };

            string? json = user.MapToJson<DocUserDto>();
            DocUserDto? dto = json.MapFromJson<DocUser, DocUserDto>();

            string? arrayJson = users.MapCollectionToJson<DocUserDto>();
            List<DocUserDto> dtos = arrayJson.MapCollectionFromJson<DocUser, DocUserDto>();

            Assert.Equal("json", dto!.Name);
            Assert.Equal(5, dtos.Single().Id);
        }

        // ---- Package 10: Mapsicle.Caching ------------------------------------------------------

        [Fact]
        public void Caching_KeyedByTheCaller_ServesTheCachedValueUntilInvalidated()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var user = new DocUser { Id = 6, Name = "first" };

            var dto = user.MapToCached<DocUserDto>(memoryCache, $"user:{user.Id}");
            user.Name = "second";

            Assert.Equal("first", user.MapToCached<DocUserDto>(memoryCache, $"user:{user.Id}")!.Name);

            memoryCache.InvalidateMappingCache($"user:{user.Id}");

            Assert.Equal("second", user.MapToCached<DocUserDto>(memoryCache, $"user:{user.Id}")!.Name);
            Assert.Equal("first", dto!.Name);
        }

        [Fact]
        public void Caching_CachedMapper_MapsAChangedSourceAgain()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var mapper = new MapperConfiguration(_ => { }).CreateMapper();
            var cached = new CachedMapper(mapper, memoryCache);
            var user = new DocUser { Id = 7, Name = "first" };

            Assert.Equal("first", cached.Map<DocUserDto>(user)!.Name);
            user.Name = "second";
            Assert.Equal("second", cached.Map<DocUserDto>(user)!.Name);
        }

        // ---- Package 11: Mapsicle.Audit --------------------------------------------------------

        [Fact]
        public void Audit_Sample()
        {
            var user = new DocUser { Id = 8, Name = "audit" };

            var result = user.MapWithAudit<DocUserDto>();
            DocUserDto dto = result.GetValueOrThrow();
            IEnumerable<string> missed = result.Audit.UnmappedProperties;

            Assert.Equal("audit", dto.Name);
            Assert.Contains(nameof(DocUserDto.Stamp), missed);

            var existingDto = new DocUserDto { Id = 8, Name = "old" };
            var detected = user.MapAndDetectChanges(existingDto);
            Assert.True(detected.HasChanges);
            Assert.Contains(detected.Changes, c => c.PropertyName == nameof(DocUserDto.Name)
                && (string?)c.OldValue == "old" && (string?)c.NewValue == "audit");

            var before = new DocUser { Id = 1, Name = "a" };
            var after = new DocUser { Id = 1, Name = "b" };
            List<PropertyChange> diff = before.Diff(after);
            Assert.Equal(nameof(DocUser.Name), diff.Single().PropertyName);
        }

        [Fact]
        public void Audit_Diff_ComparesCollectionsByReference_AsTheReadmeWarns()
        {
            // Pins the documented caveat. When #91 is fixed this fails, and the README sentence goes.
            var before = new DocTagged { Tags = new List<string> { "a" } };
            var after = new DocTagged { Tags = new List<string> { "a" } };

            Assert.Single(before.Diff(after));
        }

        // ---- Package 12: Mapsicle.DataAnnotations ----------------------------------------------

        [Fact]
        public void DataAnnotations_Sample()
        {
            var request = new DocSignup { Email = null };

            var result = request.MapAndValidateAnnotations<DocAccount>();

            Assert.False(result.IsValid);
            IDictionary<string, string[]> errors = result.ErrorsByProperty;
            Assert.Equal(new[] { "The Email field is required." }, errors["Email"]);

            var user = new DocAccount { Email = "a@b.c" };
            bool ok = user.IsValidAnnotations();
            List<ValidationResult> problems = user.GetValidationErrors();
            Assert.True(ok);
            Assert.Empty(problems);

            Assert.False(((object?)null).MapAndValidateAnnotations<DocAccount>().IsValid);
        }

        public class DocUser
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public string? Email { get; set; }
        }

        public class DocUserDto
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public string? Email { get; set; }
            public string? Stamp { get; set; }
        }

        public class DocNode { public string? Name { get; set; } public DocNode? Next { get; set; } }
        public class DocNodeDto { public string? Name { get; set; } public DocNodeDto? Next { get; set; } }

        public class DocLeaf { public string? Value { get; set; } }
        public class DocMiddle { public DocLeaf? Leaf { get; set; } }
        public class DocOuter { public DocMiddle? Middle { get; set; } }
        public class DocFlatDto { public string? MiddleLeafValue { get; set; } }

        public class DocTagged { public List<string>? Tags { get; set; } }

        public class DocSignup { public string? Email { get; set; } }
        public class DocAccount { [Required] public string? Email { get; set; } }
    }
}
