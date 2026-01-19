using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mapsicle.Fluent;

namespace Mapsicle.NamingConventions
{
    /// <summary>
    /// Extension methods for applying naming conventions to Mapsicle mappings.
    /// </summary>
    public static class NamingConventionExtensions
    {
        private static readonly ConcurrentDictionary<(Type, Type, string, string), Dictionary<string, string>> _propertyMappingCache = new();

        /// <summary>
        /// Creates a mapper that applies naming conventions when matching properties.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object to map.</param>
        /// <param name="sourceConvention">The naming convention of the source properties.</param>
        /// <param name="destConvention">The naming convention of the destination properties.</param>
        /// <returns>The mapped destination object.</returns>
        public static TDest? MapWithConvention<TSource, TDest>(
            this TSource source,
            NamingConvention sourceConvention,
            NamingConvention destConvention)
            where TDest : new()
        {
            if (source is null) return default;

            var dest = new TDest();
            var propertyMappings = GetPropertyMappings<TSource, TDest>(sourceConvention, destConvention);

            var sourceType = typeof(TSource);
            var destType = typeof(TDest);

            foreach (var mapping in propertyMappings)
            {
                var sourceProp = sourceType.GetProperty(mapping.Key);
                var destProp = destType.GetProperty(mapping.Value);

                if (sourceProp?.CanRead == true && destProp?.CanWrite == true)
                {
                    try
                    {
                        var value = sourceProp.GetValue(source);
                        if (value != null && destProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
                        {
                            destProp.SetValue(dest, value);
                        }
                        else if (value != null)
                        {
                            // Try basic conversion
                            var convertedValue = ConvertValue(value, destProp.PropertyType);
                            if (convertedValue != null)
                            {
                                destProp.SetValue(dest, convertedValue);
                            }
                        }
                    }
                    catch
                    {
                        // Skip properties that can't be mapped
                    }
                }
            }

            return dest;
        }

        /// <summary>
        /// Maps a source object to destination using naming conventions via IMapper.
        /// Falls back to standard mapping for properties that don't need convention conversion.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <param name="sourceConvention">The source naming convention.</param>
        /// <param name="destConvention">The destination naming convention.</param>
        /// <returns>The mapped destination object.</returns>
        public static TDest? MapWithConvention<TSource, TDest>(
            this IMapper mapper,
            TSource source,
            NamingConvention sourceConvention,
            NamingConvention destConvention)
            where TDest : class, new()
        {
            if (source is null) return default;

            // First do the standard mapping
            var dest = mapper.Map<TSource, TDest>(source);
            if (dest is null) return default;

            // Then apply convention-based mappings for properties that weren't mapped
            var propertyMappings = GetPropertyMappings<TSource, TDest>(sourceConvention, destConvention);
            var sourceType = typeof(TSource);
            var destType = typeof(TDest);

            foreach (var mapping in propertyMappings)
            {
                var sourceProp = sourceType.GetProperty(mapping.Key);
                var destProp = destType.GetProperty(mapping.Value);

                if (sourceProp?.CanRead == true && destProp?.CanWrite == true)
                {
                    // Only set if dest property is default/null (wasn't mapped by standard mapper)
                    var currentValue = destProp.GetValue(dest);
                    var defaultValue = destProp.PropertyType.IsValueType
                        ? Activator.CreateInstance(destProp.PropertyType)
                        : null;

                    if (Equals(currentValue, defaultValue))
                    {
                        try
                        {
                            var value = sourceProp.GetValue(source);
                            if (value != null && destProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
                            {
                                destProp.SetValue(dest, value);
                            }
                            else if (value != null)
                            {
                                var convertedValue = ConvertValue(value, destProp.PropertyType);
                                if (convertedValue != null)
                                {
                                    destProp.SetValue(dest, convertedValue);
                                }
                            }
                        }
                        catch
                        {
                            // Skip properties that can't be mapped
                        }
                    }
                }
            }

            return dest;
        }

        /// <summary>
        /// Gets the property name mappings between source and destination types based on naming conventions.
        /// </summary>
        public static Dictionary<string, string> GetPropertyMappings<TSource, TDest>(
            NamingConvention sourceConvention,
            NamingConvention destConvention)
        {
            var cacheKey = (typeof(TSource), typeof(TDest), sourceConvention.Name, destConvention.Name);
            return _propertyMappingCache.GetOrAdd(cacheKey, _ =>
            {
                var mappings = new Dictionary<string, string>();
                var sourceProps = typeof(TSource).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead)
                    .ToList();
                var destProps = typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanWrite)
                    .ToList();

                foreach (var sourceProp in sourceProps)
                {
                    // First try exact match
                    var exactMatch = destProps.FirstOrDefault(d =>
                        string.Equals(d.Name, sourceProp.Name, StringComparison.OrdinalIgnoreCase));
                    if (exactMatch != null)
                    {
                        mappings[sourceProp.Name] = exactMatch.Name;
                        continue;
                    }

                    // Then try convention-based match
                    foreach (var destProp in destProps)
                    {
                        if (NamingConvention.NamesMatch(sourceProp.Name, sourceConvention,
                                                         destProp.Name, destConvention))
                        {
                            mappings[sourceProp.Name] = destProp.Name;
                            break;
                        }
                    }
                }

                return mappings;
            });
        }

        /// <summary>
        /// Converts a property name from one naming convention to another.
        /// </summary>
        public static string ConvertName(this string name, NamingConvention from, NamingConvention to)
        {
            return NamingConvention.Convert(name, from, to);
        }

        /// <summary>
        /// Clears the property mapping cache. Useful for testing scenarios.
        /// </summary>
        public static void ClearMappingCache() => _propertyMappingCache.Clear();

        private static object? ConvertValue(object value, Type targetType)
        {
            try
            {
                if (targetType == typeof(string))
                    return value.ToString();

                if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
                    return Convert.ChangeType(value, targetType);

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
