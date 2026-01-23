using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mapsicle.Fluent;

namespace Mapsicle.Audit
{
    /// <summary>
    /// Extension methods for auditing and change tracking during mapping operations.
    /// </summary>
    public static class AuditExtensions
    {
        #region MapWithAudit

        /// <summary>
        /// Maps the source object to the destination type and returns an audit trail of the mapping.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <returns>An audited mapping result containing the mapped object and change details.</returns>
        public static AuditedMappingResult<TDest> MapWithAudit<TDest>(this object? source)
            where TDest : new()
        {
            if (source is null)
            {
                return new AuditedMappingResult<TDest>(default, new MappingAudit
                {
                    SourceType = null,
                    DestinationType = typeof(TDest),
                    MappedAt = DateTime.UtcNow,
                    WasSuccessful = false,
                    FailureReason = "Source was null"
                });
            }

            var startTime = DateTime.UtcNow;
            var mapped = source.MapTo<TDest>();
            var endTime = DateTime.UtcNow;

            var audit = new MappingAudit
            {
                SourceType = source.GetType(),
                DestinationType = typeof(TDest),
                MappedAt = startTime,
                Duration = endTime - startTime,
                WasSuccessful = mapped is not null
            };

            // Collect property mappings
            if (mapped is not null)
            {
                var sourceProps = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                var destProps = typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var destProp in destProps.Where(p => p.CanRead))
                {
                    var destValue = destProp.GetValue(mapped);
                    var sourceProp = sourceProps.FirstOrDefault(p =>
                        p.Name.Equals(destProp.Name, StringComparison.OrdinalIgnoreCase));

                    var sourceValue = sourceProp?.GetValue(source);

                    audit.PropertyMappings.Add(new PropertyMappingInfo
                    {
                        PropertyName = destProp.Name,
                        SourcePropertyName = sourceProp?.Name,
                        SourceValue = sourceValue,
                        DestinationValue = destValue,
                        WasMapped = sourceProp is not null,
                        SourceType = sourceProp?.PropertyType,
                        DestinationType = destProp.PropertyType
                    });
                }
            }

            return new AuditedMappingResult<TDest>(mapped, audit);
        }

        /// <summary>
        /// Maps the source object to the destination type using IMapper and returns an audit trail.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <returns>An audited mapping result containing the mapped object and change details.</returns>
        public static AuditedMappingResult<TDest> MapWithAudit<TSource, TDest>(
            this IMapper mapper,
            TSource? source)
            where TDest : new()
        {
            if (source is null)
            {
                return new AuditedMappingResult<TDest>(default, new MappingAudit
                {
                    SourceType = typeof(TSource),
                    DestinationType = typeof(TDest),
                    MappedAt = DateTime.UtcNow,
                    WasSuccessful = false,
                    FailureReason = "Source was null"
                });
            }

            var startTime = DateTime.UtcNow;
            var mapped = mapper.Map<TSource, TDest>(source);
            var endTime = DateTime.UtcNow;

            var audit = new MappingAudit
            {
                SourceType = typeof(TSource),
                DestinationType = typeof(TDest),
                MappedAt = startTime,
                Duration = endTime - startTime,
                WasSuccessful = mapped is not null
            };

            // Collect property mappings
            if (mapped is not null)
            {
                var sourceProps = typeof(TSource).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                var destProps = typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var destProp in destProps.Where(p => p.CanRead))
                {
                    var destValue = destProp.GetValue(mapped);
                    var sourceProp = sourceProps.FirstOrDefault(p =>
                        p.Name.Equals(destProp.Name, StringComparison.OrdinalIgnoreCase));

                    var sourceValue = sourceProp?.GetValue(source);

                    audit.PropertyMappings.Add(new PropertyMappingInfo
                    {
                        PropertyName = destProp.Name,
                        SourcePropertyName = sourceProp?.Name,
                        SourceValue = sourceValue,
                        DestinationValue = destValue,
                        WasMapped = sourceProp is not null,
                        SourceType = sourceProp?.PropertyType,
                        DestinationType = destProp.PropertyType
                    });
                }
            }

            return new AuditedMappingResult<TDest>(mapped, audit);
        }

        #endregion

        #region Diff / Change Detection

        /// <summary>
        /// Compares two objects and returns the differences.
        /// </summary>
        /// <typeparam name="T">The type of objects to compare.</typeparam>
        /// <param name="original">The original object.</param>
        /// <param name="modified">The modified object.</param>
        /// <returns>A list of property changes between the two objects.</returns>
        public static List<PropertyChange> Diff<T>(this T? original, T? modified)
        {
            var changes = new List<PropertyChange>();

            if (original is null && modified is null) return changes;
            if (original is null)
            {
                changes.Add(new PropertyChange
                {
                    PropertyName = "(object)",
                    OldValue = null,
                    NewValue = modified,
                    ChangeType = ChangeType.Created
                });
                return changes;
            }
            if (modified is null)
            {
                changes.Add(new PropertyChange
                {
                    PropertyName = "(object)",
                    OldValue = original,
                    NewValue = null,
                    ChangeType = ChangeType.Deleted
                });
                return changes;
            }

            var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead);

            foreach (var prop in props)
            {
                var oldValue = prop.GetValue(original);
                var newValue = prop.GetValue(modified);

                if (!Equals(oldValue, newValue))
                {
                    changes.Add(new PropertyChange
                    {
                        PropertyName = prop.Name,
                        PropertyType = prop.PropertyType,
                        OldValue = oldValue,
                        NewValue = newValue,
                        ChangeType = ChangeType.Modified
                    });
                }
            }

            return changes;
        }

        /// <summary>
        /// Maps source to destination and compares with an existing destination to detect changes.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="existingDestination">The existing destination object to compare against.</param>
        /// <returns>A change detection result with mapped object and list of changes.</returns>
        public static ChangeDetectionResult<TDest> MapAndDetectChanges<TDest>(
            this object? source,
            TDest? existingDestination)
            where TDest : new()
        {
            if (source is null)
            {
                return new ChangeDetectionResult<TDest>(default, new List<PropertyChange>(), false);
            }

            var mapped = source.MapTo<TDest>();
            var changes = existingDestination.Diff(mapped);

            return new ChangeDetectionResult<TDest>(mapped, changes, changes.Count > 0);
        }

        /// <summary>
        /// Maps source to destination using IMapper and compares with existing destination.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <param name="existingDestination">The existing destination object to compare against.</param>
        /// <returns>A change detection result with mapped object and list of changes.</returns>
        public static ChangeDetectionResult<TDest> MapAndDetectChanges<TSource, TDest>(
            this IMapper mapper,
            TSource? source,
            TDest? existingDestination)
            where TDest : new()
        {
            if (source is null)
            {
                return new ChangeDetectionResult<TDest>(default, new List<PropertyChange>(), false);
            }

            var mapped = mapper.Map<TSource, TDest>(source);
            var changes = existingDestination.Diff(mapped);

            return new ChangeDetectionResult<TDest>(mapped, changes, changes.Count > 0);
        }

        #endregion

        #region HasChanges

        /// <summary>
        /// Checks if mapping would result in any changes to the destination.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="existingDestination">The existing destination to compare against.</param>
        /// <returns>True if mapping would result in changes.</returns>
        public static bool WouldChangeOnMap<TDest>(this object? source, TDest? existingDestination)
            where TDest : new()
        {
            if (source is null) return false;

            var mapped = source.MapTo<TDest>();
            var changes = existingDestination.Diff(mapped);

            return changes.Count > 0;
        }

        /// <summary>
        /// Gets only the changed property values after mapping.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="existingDestination">The existing destination to compare against.</param>
        /// <returns>Dictionary of changed property names and their new values.</returns>
        public static Dictionary<string, object?> GetChangedValues<TDest>(
            this object? source,
            TDest? existingDestination)
            where TDest : new()
        {
            var result = source.MapAndDetectChanges(existingDestination);
            return result.Changes.ToDictionary(c => c.PropertyName, c => c.NewValue);
        }

        #endregion
    }

    #region Result Types

    /// <summary>
    /// Result of an audited mapping operation.
    /// </summary>
    /// <typeparam name="T">The destination type.</typeparam>
    public class AuditedMappingResult<T>
    {
        /// <summary>
        /// The mapped value.
        /// </summary>
        public T? Value { get; }

        /// <summary>
        /// The audit information for the mapping.
        /// </summary>
        public MappingAudit Audit { get; }

        /// <summary>
        /// Whether the mapping was successful.
        /// </summary>
        public bool IsSuccess => Audit.WasSuccessful;

        public AuditedMappingResult(T? value, MappingAudit audit)
        {
            Value = value;
            Audit = audit;
        }

        /// <summary>
        /// Gets the mapped value or throws if unsuccessful.
        /// </summary>
        public T GetValueOrThrow()
        {
            if (!IsSuccess || Value is null)
            {
                throw new InvalidOperationException(
                    Audit.FailureReason ?? "Mapping was unsuccessful");
            }
            return Value;
        }
    }

    /// <summary>
    /// Audit information for a mapping operation.
    /// </summary>
    public class MappingAudit
    {
        /// <summary>
        /// The source type.
        /// </summary>
        public Type? SourceType { get; set; }

        /// <summary>
        /// The destination type.
        /// </summary>
        public Type? DestinationType { get; set; }

        /// <summary>
        /// When the mapping occurred.
        /// </summary>
        public DateTime MappedAt { get; set; }

        /// <summary>
        /// How long the mapping took.
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Whether the mapping was successful.
        /// </summary>
        public bool WasSuccessful { get; set; }

        /// <summary>
        /// Reason for failure if unsuccessful.
        /// </summary>
        public string? FailureReason { get; set; }

        /// <summary>
        /// Details about each property mapping.
        /// </summary>
        public List<PropertyMappingInfo> PropertyMappings { get; set; } = new();

        /// <summary>
        /// Gets the mapped property count.
        /// </summary>
        public int MappedPropertyCount => PropertyMappings.Count(p => p.WasMapped);

        /// <summary>
        /// Gets the unmapped property count.
        /// </summary>
        public int UnmappedPropertyCount => PropertyMappings.Count(p => !p.WasMapped);

        /// <summary>
        /// Gets the names of unmapped properties.
        /// </summary>
        public IEnumerable<string> UnmappedProperties =>
            PropertyMappings.Where(p => !p.WasMapped).Select(p => p.PropertyName);
    }

    /// <summary>
    /// Information about a single property mapping.
    /// </summary>
    public class PropertyMappingInfo
    {
        /// <summary>
        /// The destination property name.
        /// </summary>
        public string PropertyName { get; set; } = string.Empty;

        /// <summary>
        /// The source property name (if mapped).
        /// </summary>
        public string? SourcePropertyName { get; set; }

        /// <summary>
        /// The source value.
        /// </summary>
        public object? SourceValue { get; set; }

        /// <summary>
        /// The destination value.
        /// </summary>
        public object? DestinationValue { get; set; }

        /// <summary>
        /// Whether this property was mapped from source.
        /// </summary>
        public bool WasMapped { get; set; }

        /// <summary>
        /// The source property type.
        /// </summary>
        public Type? SourceType { get; set; }

        /// <summary>
        /// The destination property type.
        /// </summary>
        public Type? DestinationType { get; set; }
    }

    /// <summary>
    /// Result of a change detection operation.
    /// </summary>
    /// <typeparam name="T">The destination type.</typeparam>
    public class ChangeDetectionResult<T>
    {
        /// <summary>
        /// The mapped value.
        /// </summary>
        public T? Value { get; }

        /// <summary>
        /// List of property changes detected.
        /// </summary>
        public List<PropertyChange> Changes { get; }

        /// <summary>
        /// Whether any changes were detected.
        /// </summary>
        public bool HasChanges { get; }

        public ChangeDetectionResult(T? value, List<PropertyChange> changes, bool hasChanges)
        {
            Value = value;
            Changes = changes;
            HasChanges = hasChanges;
        }

        /// <summary>
        /// Gets changes for a specific property.
        /// </summary>
        public PropertyChange? GetChange(string propertyName)
        {
            return Changes.FirstOrDefault(c =>
                c.PropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Represents a change to a property.
    /// </summary>
    public class PropertyChange
    {
        /// <summary>
        /// The property name.
        /// </summary>
        public string PropertyName { get; set; } = string.Empty;

        /// <summary>
        /// The property type.
        /// </summary>
        public Type? PropertyType { get; set; }

        /// <summary>
        /// The old value.
        /// </summary>
        public object? OldValue { get; set; }

        /// <summary>
        /// The new value.
        /// </summary>
        public object? NewValue { get; set; }

        /// <summary>
        /// The type of change.
        /// </summary>
        public ChangeType ChangeType { get; set; }
    }

    /// <summary>
    /// Types of changes that can be detected.
    /// </summary>
    public enum ChangeType
    {
        /// <summary>
        /// Object was created.
        /// </summary>
        Created,

        /// <summary>
        /// Property was modified.
        /// </summary>
        Modified,

        /// <summary>
        /// Object was deleted.
        /// </summary>
        Deleted
    }

    #endregion
}
