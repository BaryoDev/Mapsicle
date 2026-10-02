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
        private static readonly ConcurrentDictionary<(Type, Type), HashSet<string>> _boundMemberCache = new();
        private static readonly ConcurrentDictionary<Type, Dictionary<string, object?>> _initialValueCache = new();

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

                if (sourceProp?.GetGetMethod() != null && destProp?.CanWrite == true)
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
                    catch (Exception)
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

            // "Not mapped yet" used to mean equal to default(T) and nothing else. A string initialised
            // to "" or a list initialised to a new instance was therefore never filled, and a member
            // the configuration ignored or resolved to its default was filled over. The mapper and
            // its configuration are asked first, and the value only decides what neither accounts for.
            var typeMap = (mapper as FluentMapper)?.Configuration.GetTypeMap(sourceType, destType);
            var bound = _boundMemberCache.GetOrAdd(
                (source.GetType(), dest.GetType()),
                pair => new HashSet<string>(Mapper.GetBoundMembers(pair.Item1, pair.Item2).Keys, StringComparer.OrdinalIgnoreCase));
            var initialValues = _initialValueCache.GetOrAdd(destType, _ => ReadInitialValues(new TDest()));

            foreach (var mapping in propertyMappings)
            {
                if (bound.Contains(mapping.Value)) continue;
                if (typeMap?.IsIgnored(mapping.Value) == true || typeMap?.HasCustomMapping(mapping.Value) == true) continue;

                var sourceProp = sourceType.GetProperty(mapping.Key);
                var destProp = destType.GetProperty(mapping.Value);

                if (sourceProp?.GetGetMethod() != null && destProp?.CanWrite == true)
                {
                    initialValues.TryGetValue(destProp.Name, out var initialValue);

                    if (StillUnset(destProp.GetValue(dest), initialValue))
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
                    .Where(p => p.GetGetMethod() != null)
                    .ToList();
                var destProps = typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanWrite && p.GetCustomAttribute<IgnoreMapAttribute>() == null)
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
        public static void ClearMappingCache()
        {
            _propertyMappingCache.Clear();
            _boundMemberCache.Clear();
            _initialValueCache.Clear();
        }

        // A hook such as AfterMap can set a member nothing else accounts for, and a value that moved
        // off its initial one is the only sign of it. An initializer that builds a new instance
        // gives every destination a different reference, so there the comparison says nothing and
        // the member is filled.
        private static bool StillUnset(object? current, object? initial) =>
            Equals(current, initial) || initial is not (null or string or ValueType);

        private static Dictionary<string, object?> ReadInitialValues(object fresh)
        {
            var values = new Dictionary<string, object?>();
            foreach (var prop in fresh.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetGetMethod() == null || prop.GetIndexParameters().Length > 0) continue;
                try
                {
                    values[prop.Name] = prop.GetValue(fresh);
                }
                catch (Exception)
                {
                    values[prop.Name] = null;
                }
            }
            return values;
        }

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
            catch (Exception)
            {
                return null;
            }
        }
    }
}
