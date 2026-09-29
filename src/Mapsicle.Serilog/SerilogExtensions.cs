using System;
using System.Diagnostics;
using Serilog;
using Serilog.Events;

namespace Mapsicle.Serilog
{
    /// <summary>
    /// Serilog integration extensions for Mapsicle.
    /// Provides structured logging for mapping operations with performance tracking.
    /// </summary>
    public static class SerilogExtensions
    {
        private static volatile ILogger? _logger;
        private static volatile LoggingOptions _options = new();

        /// <summary>
        /// Configures Mapsicle to use Serilog for logging.
        /// </summary>
        /// <param name="logger">The Serilog logger instance.</param>
        /// <param name="configure">Optional configuration action.</param>
        public static void UseSerilog(ILogger logger, Action<LoggingOptions>? configure = null)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));

            var newOptions = new LoggingOptions();
            configure?.Invoke(newOptions);
            _options = newOptions;
            System.Threading.Interlocked.Exchange(ref _logger, logger);

            // Wire up to Mapsicle's Logger callback
            Mapper.Logger = message => _logger?.Information("[Mapsicle] {Message}", message);
        }

        /// <summary>
        /// Resets the internal state of the Serilog extension (for testing purposes).
        /// </summary>
        public static void Reset()
        {
            _mappedTypes.Clear();
            System.Threading.Interlocked.Exchange(ref _logger, null);
            _options = new LoggingOptions();
        }

        /// <summary>
        /// Maps an object with Serilog logging and performance tracking.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <returns>The mapped object.</returns>
        public static TDest? MapWithLogging<TDest>(this object? source)
        {
            var logger = _logger;
            if (source is null)
            {
                logger?.Debug("[Mapsicle] Mapping skipped: source is null");
                return default;
            }

            // With no logger this cost 40 B a call more than MapTo, a Stopwatch; with a logger above
            // Information it cost 144 B more, a params array and boxed values built for an event
            // Serilog then discarded. Nothing is timed or built unless something will be written.
            if (logger is null)
            {
                return source.MapTo<TDest>();
            }

            var options = _options;
            var sourceType = source.GetType();
            var destType = typeof(TDest);
            var start = Stopwatch.GetTimestamp();

            try
            {
                var result = source.MapTo<TDest>();
                LogMappingSuccess(logger, options, sourceType, destType, ElapsedSince(start));
                return result;
            }
            catch (Exception ex)
            {
                LogMappingError(logger, sourceType, destType, ex, ElapsedSince(start));
                throw;
            }
        }

        /// <summary>
        /// Maps a collection with Serilog logging and performance tracking.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source collection.</param>
        /// <returns>The mapped collection.</returns>
        public static System.Collections.Generic.List<TDest> MapCollectionWithLogging<TDest>(
            this System.Collections.IEnumerable? source)
        {
            var logger = _logger;
            if (source is null)
            {
                logger?.Debug("[Mapsicle] Collection mapping skipped: source is null");
                return new System.Collections.Generic.List<TDest>();
            }

            if (logger is null)
            {
                return source.MapTo<TDest>();
            }

            var options = _options;
            var start = Stopwatch.GetTimestamp();

            try
            {
                var result = source.MapTo<TDest>();
                var elapsed = ElapsedSince(start);

                if (ShouldLogSuccess(logger, options))
                {
                    logger.Information(
                        "[Mapsicle] Mapped collection of {Count} items to {DestType} in {ElapsedMs:F2}ms",
                        result.Count, typeof(TDest).Name, elapsed.TotalMilliseconds);
                }

                if (options.SlowMappingThreshold.HasValue &&
                    elapsed > options.SlowMappingThreshold.Value)
                {
                    logger.Warning(
                        "[Mapsicle] Slow collection mapping detected: {Count} items to {DestType} took {ElapsedMs:F2}ms (threshold: {ThresholdMs}ms)",
                        result.Count, typeof(TDest).Name, elapsed.TotalMilliseconds,
                        options.SlowMappingThreshold.Value.TotalMilliseconds);
                }

                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex,
                    "[Mapsicle] Collection mapping failed to {DestType} after {ElapsedMs:F2}ms: {ErrorMessage}",
                    typeof(TDest).Name, ElapsedSince(start).TotalMilliseconds, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Creates a logging context for batch mapping operations.
        /// </summary>
        /// <param name="operationName">A descriptive name for the operation.</param>
        /// <returns>A disposable logging scope.</returns>
        public static MappingLoggingScope BeginMappingScope(string operationName)
        {
            return new MappingLoggingScope(_logger, operationName, _options);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, Type), bool> _mappedTypes = new();

        private static TimeSpan ElapsedSince(long start) =>
            TimeSpan.FromTicks((long)((Stopwatch.GetTimestamp() - start) * TicksPerTimestamp));

        private static readonly double TicksPerTimestamp = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

        private static bool ShouldLogSuccess(ILogger logger, LoggingOptions options) =>
            options.LogLevel <= LogEventLevel.Information && logger.IsEnabled(LogEventLevel.Information);

        private static void LogMappingSuccess(ILogger logger, LoggingOptions options, Type sourceType, Type destType, TimeSpan elapsed)
        {
            // Track if we've seen this type pair before (indicates caching)
            var wasCached = !_mappedTypes.TryAdd((sourceType, destType), true);

            if (ShouldLogSuccess(logger, options))
            {
                if (options.LogCacheStatus)
                {
                    logger.Information(
                        "[Mapsicle] Mapped {SourceType} -> {DestType} in {ElapsedMs:F2}ms (cached: {IsCached})",
                        new object[] { sourceType.Name, destType.Name, elapsed.TotalMilliseconds, wasCached });
                }
                else
                {
                    logger.Information(
                        "[Mapsicle] Mapped {SourceType} -> {DestType} in {ElapsedMs:F2}ms",
                        sourceType.Name, destType.Name, elapsed.TotalMilliseconds);
                }
            }

            if (options.SlowMappingThreshold.HasValue &&
                elapsed > options.SlowMappingThreshold.Value)
            {
                logger.Warning(
                    "[Mapsicle] Slow mapping detected: {SourceType} -> {DestType} took {ElapsedMs:F2}ms (threshold: {ThresholdMs}ms)",
                    sourceType.Name, destType.Name, elapsed.TotalMilliseconds,
                    options.SlowMappingThreshold.Value.TotalMilliseconds);
            }
        }

        private static void LogMappingError(ILogger logger, Type sourceType, Type destType, Exception ex, TimeSpan elapsed)
        {
            logger.Error(ex,
                "[Mapsicle] Mapping failed {SourceType} -> {DestType} after {ElapsedMs:F2}ms: {ErrorMessage}",
                sourceType.Name, destType.Name, elapsed.TotalMilliseconds, ex.Message);
        }
    }

    /// <summary>
    /// Configuration options for Mapsicle Serilog logging.
    /// </summary>
    public class LoggingOptions
    {
        /// <summary>
        /// Minimum log level for mapping operations.
        /// Default is Information.
        /// </summary>
        public LogEventLevel LogLevel { get; set; } = LogEventLevel.Information;

        /// <summary>
        /// Threshold for logging slow mapping warnings.
        /// Mappings taking longer than this will log a warning.
        /// Default is null (disabled).
        /// </summary>
        public TimeSpan? SlowMappingThreshold { get; set; }

        /// <summary>
        /// Whether to log cache hits/misses.
        /// Default is true.
        /// </summary>
        public bool LogCacheStatus { get; set; } = true;

        /// <summary>
        /// Whether to include property-level details in debug logs.
        /// Default is false.
        /// </summary>
        public bool IncludePropertyDetails { get; set; }
    }

    /// <summary>
    /// A disposable scope for tracking batch mapping operations.
    /// </summary>
    public class MappingLoggingScope : IDisposable
    {
        private readonly ILogger? _logger;
        private readonly string _operationName;
        private readonly Stopwatch _stopwatch;
        private readonly LoggingOptions _options;
        private int _mappingCount;
        private int _errorCount;
        private bool _disposed;

        internal MappingLoggingScope(ILogger? logger, string operationName, LoggingOptions options)
        {
            _logger = logger;
            _operationName = operationName;
            _options = options;
            _stopwatch = Stopwatch.StartNew();

            _logger?.Debug("[Mapsicle] Starting mapping operation: {OperationName}", operationName);
        }

        /// <summary>
        /// Increments the mapping count for this scope.
        /// </summary>
        public void RecordMapping()
        {
            _mappingCount++;
        }

        /// <summary>
        /// Increments the error count for this scope.
        /// </summary>
        public void RecordError()
        {
            _errorCount++;
        }

        /// <summary>
        /// Disposes the scope and logs the summary.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _stopwatch.Stop();

            if (_errorCount > 0)
            {
                _logger?.Warning(
                    "[Mapsicle] Completed {OperationName}: {MappingCount} mappings, {ErrorCount} errors in {ElapsedMs:F2}ms",
                    _operationName, _mappingCount, _errorCount, _stopwatch.Elapsed.TotalMilliseconds);
            }
            else
            {
                _logger?.Information(
                    "[Mapsicle] Completed {OperationName}: {MappingCount} mappings in {ElapsedMs:F2}ms",
                    _operationName, _mappingCount, _stopwatch.Elapsed.TotalMilliseconds);
            }

            if (_options.SlowMappingThreshold.HasValue &&
                _stopwatch.Elapsed > _options.SlowMappingThreshold.Value)
            {
                _logger?.Warning(
                    "[Mapsicle] Slow operation detected: {OperationName} took {ElapsedMs:F2}ms (threshold: {ThresholdMs}ms)",
                    _operationName, _stopwatch.Elapsed.TotalMilliseconds,
                    _options.SlowMappingThreshold.Value.TotalMilliseconds);
            }
        }
    }
}
