using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.InMemory;
using Xunit;

namespace Mapsicle.Serilog.Tests
{
    public class SerilogExtensionsTests : IDisposable
    {
        private readonly InMemorySink _sink;
        private readonly ILogger _logger;

        public SerilogExtensionsTests()
        {
            _sink = new InMemorySink();
            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Sink(_sink)
                .CreateLogger();

            Mapper.ClearCache();
        }

        public void Dispose()
        {
            Mapper.ClearCache();
            SerilogExtensions.Reset();
        }

        #region Test Models

        public class SourceModel
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public DateTime Created { get; set; }
        }

        public class DestModel
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public DateTime Created { get; set; }
        }

        public class ComplexSource
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public List<string> Tags { get; set; } = new();
        }

        public class ComplexDest
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public List<string> Tags { get; set; } = new();
        }

        #endregion

        #region UseSerilog Tests

        [Fact]
        public void UseSerilog_WithNullLogger_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => SerilogExtensions.UseSerilog(null!));
        }

        [Fact]
        public void UseSerilog_SetsUpMapperLogger()
        {
            SerilogExtensions.UseSerilog(_logger);

            // Trigger a log via Mapper.Logger
            Mapper.Logger?.Invoke("Test message");

            Assert.Contains(_sink.LogEvents, e =>
                e.MessageTemplate.Text.Contains("{Message}") &&
                e.Properties.ContainsKey("Message"));
        }

        [Fact]
        public void UseSerilog_WithOptions_ConfiguresSlowThreshold()
        {
            SerilogExtensions.UseSerilog(_logger, opts =>
            {
                opts.SlowMappingThreshold = TimeSpan.FromMilliseconds(1);
                opts.LogLevel = LogEventLevel.Debug;
            });

            var source = new SourceModel { Id = 1, Name = "Test" };

            // Simulate slow mapping by using reflection overhead repeatedly
            for (int i = 0; i < 10; i++)
            {
                source.MapWithLogging<DestModel>();
            }

            // At least one information log should exist
            Assert.Contains(_sink.LogEvents, e => e.Level == LogEventLevel.Information);
        }

        #endregion

        #region MapWithLogging Tests

        [Fact]
        public void MapWithLogging_WithNullSource_ReturnsDefault()
        {
            SerilogExtensions.UseSerilog(_logger);

            object? source = null;
            var result = source.MapWithLogging<DestModel>();

            Assert.Null(result);
            Assert.Contains(_sink.LogEvents, e =>
                e.Level == LogEventLevel.Debug &&
                e.MessageTemplate.Text.Contains("source is null"));
        }

        [Fact]
        public void MapWithLogging_WithValidSource_MapsSuccessfully()
        {
            SerilogExtensions.UseSerilog(_logger);

            var source = new SourceModel
            {
                Id = 42,
                Name = "Test User",
                Created = DateTime.UtcNow
            };

            var result = source.MapWithLogging<DestModel>();

            Assert.NotNull(result);
            Assert.Equal(42, result.Id);
            Assert.Equal("Test User", result.Name);
        }

        [Fact]
        public void MapWithLogging_LogsMappingInformation()
        {
            SerilogExtensions.UseSerilog(_logger);

            var source = new SourceModel { Id = 1, Name = "Test" };
            source.MapWithLogging<DestModel>();

            Assert.Contains(_sink.LogEvents, e =>
                e.Level == LogEventLevel.Information &&
                e.MessageTemplate.Text.Contains("Mapped"));
        }

        [Fact]
        public void MapWithLogging_LogsCacheStatus()
        {
            SerilogExtensions.UseSerilog(_logger, opts => opts.LogCacheStatus = true);

            var source = new SourceModel { Id = 1, Name = "Test" };

            // First mapping - should not be cached (cached: false)
            source.MapWithLogging<DestModel>();

            // Second mapping - should use cache (cached: true)
            source.MapWithLogging<DestModel>();

            // Check that both mappings logged and at least one shows cached
            Assert.Contains(_sink.LogEvents, e =>
                e.MessageTemplate.Text.Contains("cached"));

            // Check that second mapping shows cached: true
            var cachedEvents = _sink.LogEvents.Where(e =>
                e.Properties.TryGetValue("IsCached", out var prop) &&
                prop.ToString() == "True").ToList();
            Assert.NotEmpty(cachedEvents);
        }

        [Fact]
        public void MapWithLogging_WithSlowMapping_LogsWarning()
        {
            SerilogExtensions.UseSerilog(_logger, opts =>
            {
                opts.SlowMappingThreshold = TimeSpan.FromTicks(1); // Very low threshold
            });

            var source = new SourceModel { Id = 1, Name = "Test" };
            source.MapWithLogging<DestModel>();

            // Should log warning for slow mapping
            Assert.Contains(_sink.LogEvents, e =>
                e.Level == LogEventLevel.Warning &&
                e.MessageTemplate.Text.Contains("Slow mapping"));
        }

        #endregion

        #region MapCollectionWithLogging Tests

        [Fact]
        public void MapCollectionWithLogging_WithNullSource_ReturnsEmptyList()
        {
            SerilogExtensions.UseSerilog(_logger);

            IEnumerable<SourceModel>? source = null;
            var result = source.MapCollectionWithLogging<DestModel>();

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void MapCollectionWithLogging_WithValidSource_MapsAllItems()
        {
            SerilogExtensions.UseSerilog(_logger);

            var source = new List<SourceModel>
            {
                new() { Id = 1, Name = "First" },
                new() { Id = 2, Name = "Second" },
                new() { Id = 3, Name = "Third" }
            };

            var result = source.MapCollectionWithLogging<DestModel>();

            Assert.Equal(3, result.Count);
            Assert.Equal("First", result[0].Name);
            Assert.Equal("Second", result[1].Name);
            Assert.Equal("Third", result[2].Name);
        }

        [Fact]
        public void MapCollectionWithLogging_LogsCollectionCount()
        {
            SerilogExtensions.UseSerilog(_logger);

            var source = new List<SourceModel>
            {
                new() { Id = 1, Name = "First" },
                new() { Id = 2, Name = "Second" }
            };

            source.MapCollectionWithLogging<DestModel>();

            Assert.Contains(_sink.LogEvents, e =>
                e.Level == LogEventLevel.Information &&
                e.MessageTemplate.Text.Contains("collection") &&
                e.Properties.ContainsKey("Count"));
        }

        #endregion

        #region MappingLoggingScope Tests

        [Fact]
        public void BeginMappingScope_TracksOperationName()
        {
            SerilogExtensions.UseSerilog(_logger);

            using (var scope = SerilogExtensions.BeginMappingScope("TestOperation"))
            {
                scope.RecordMapping();
            }

            // Check that the operation name is captured in the log properties
            Assert.Contains(_sink.LogEvents, e =>
                e.Properties.TryGetValue("OperationName", out var prop) &&
                prop.ToString().Contains("TestOperation"));
        }

        [Fact]
        public void MappingLoggingScope_RecordMapping_IncrementsCount()
        {
            SerilogExtensions.UseSerilog(_logger);

            using (var scope = SerilogExtensions.BeginMappingScope("BatchMapping"))
            {
                scope.RecordMapping();
                scope.RecordMapping();
                scope.RecordMapping();
            }

            Assert.Contains(_sink.LogEvents, e =>
                e.MessageTemplate.Text.Contains("Completed") &&
                e.Properties.TryGetValue("MappingCount", out var prop) &&
                prop.ToString() == "3");
        }

        [Fact]
        public void MappingLoggingScope_RecordError_IncrementsErrorCount()
        {
            SerilogExtensions.UseSerilog(_logger);

            using (var scope = SerilogExtensions.BeginMappingScope("ErrorOperation"))
            {
                scope.RecordMapping();
                scope.RecordError();
            }

            Assert.Contains(_sink.LogEvents, e =>
                e.Level == LogEventLevel.Warning &&
                e.Properties.ContainsKey("ErrorCount"));
        }

        [Fact]
        public void MappingLoggingScope_LogsElapsedTime()
        {
            SerilogExtensions.UseSerilog(_logger);

            using (var scope = SerilogExtensions.BeginMappingScope("TimedOperation"))
            {
                Thread.Sleep(10); // Small delay
                scope.RecordMapping();
            }

            Assert.Contains(_sink.LogEvents, e =>
                e.MessageTemplate.Text.Contains("ElapsedMs"));
        }

        [Fact]
        public void MappingLoggingScope_SlowOperation_LogsWarning()
        {
            SerilogExtensions.UseSerilog(_logger, opts =>
            {
                opts.SlowMappingThreshold = TimeSpan.FromMilliseconds(5);
            });

            using (var scope = SerilogExtensions.BeginMappingScope("SlowOperation"))
            {
                Thread.Sleep(20);
                scope.RecordMapping();
            }

            Assert.Contains(_sink.LogEvents, e =>
                e.Level == LogEventLevel.Warning &&
                e.MessageTemplate.Text.Contains("Slow operation"));
        }

        [Fact]
        public void MappingLoggingScope_DoubleDispose_DoesNotThrow()
        {
            SerilogExtensions.UseSerilog(_logger);

            var scope = SerilogExtensions.BeginMappingScope("TestOperation");
            scope.Dispose();
            scope.Dispose(); // Should not throw
        }

        #endregion

        #region LoggingOptions Tests

        [Fact]
        public void LoggingOptions_DefaultValues()
        {
            var options = new LoggingOptions();

            Assert.Equal(LogEventLevel.Information, options.LogLevel);
            Assert.Null(options.SlowMappingThreshold);
            Assert.True(options.LogCacheStatus);
            Assert.False(options.IncludePropertyDetails);
        }

        [Fact]
        public void LoggingOptions_CanSetAllProperties()
        {
            var options = new LoggingOptions
            {
                LogLevel = LogEventLevel.Debug,
                SlowMappingThreshold = TimeSpan.FromSeconds(1),
                LogCacheStatus = false,
                IncludePropertyDetails = true
            };

            Assert.Equal(LogEventLevel.Debug, options.LogLevel);
            Assert.Equal(TimeSpan.FromSeconds(1), options.SlowMappingThreshold);
            Assert.False(options.LogCacheStatus);
            Assert.True(options.IncludePropertyDetails);
        }

        #endregion

        #region Complex Mapping Tests

        [Fact]
        public void MapWithLogging_ComplexType_MapsCorrectly()
        {
            SerilogExtensions.UseSerilog(_logger);

            var source = new ComplexSource
            {
                Id = 1,
                Title = "Complex Object",
                Tags = new List<string> { "tag1", "tag2", "tag3" }
            };

            var result = source.MapWithLogging<ComplexDest>();

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
            Assert.Equal("Complex Object", result.Title);
            Assert.Equal(3, result.Tags.Count);
        }

        [Fact]
        public void MapCollectionWithLogging_ComplexTypes_MapsCorrectly()
        {
            SerilogExtensions.UseSerilog(_logger);

            var source = new List<ComplexSource>
            {
                new() { Id = 1, Title = "First", Tags = new() { "a", "b" } },
                new() { Id = 2, Title = "Second", Tags = new() { "c" } }
            };

            var result = source.MapCollectionWithLogging<ComplexDest>();

            Assert.Equal(2, result.Count);
            Assert.Equal(2, result[0].Tags.Count);
            Assert.Single(result[1].Tags);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void FullWorkflow_MapsAndLogsCorrectly()
        {
            SerilogExtensions.UseSerilog(_logger, opts =>
            {
                opts.LogLevel = LogEventLevel.Debug;
                opts.SlowMappingThreshold = TimeSpan.FromMilliseconds(100);
                opts.LogCacheStatus = true;
            });

            using (var scope = SerilogExtensions.BeginMappingScope("UserImport"))
            {
                var users = new List<SourceModel>
                {
                    new() { Id = 1, Name = "Alice" },
                    new() { Id = 2, Name = "Bob" },
                    new() { Id = 3, Name = "Charlie" }
                };

                foreach (var user in users)
                {
                    var mapped = user.MapWithLogging<DestModel>();
                    if (mapped != null)
                    {
                        scope.RecordMapping();
                    }
                }
            }

            // Should have debug, info, and completion logs
            Assert.Contains(_sink.LogEvents, e => e.Level == LogEventLevel.Debug);
            Assert.Contains(_sink.LogEvents, e => e.Level == LogEventLevel.Information);
            Assert.Contains(_sink.LogEvents, e => e.MessageTemplate.Text.Contains("Completed"));
        }

        #endregion
    }
}
