using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Mapsicle
{
    #region Attributes

    /// <summary>
    /// Marks a property to be ignored during mapping.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class IgnoreMapAttribute : Attribute { }

    /// <summary>
    /// Specifies the source property name to map from.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class MapFromAttribute : Attribute
    {
        public string SourcePropertyName { get; }
        public MapFromAttribute(string sourcePropertyName) => SourcePropertyName = sourcePropertyName;
    }

    #endregion

    #region Cache Info

    /// <summary>
    /// Contains information about the mapper cache state.
    /// </summary>
    public readonly struct MapperCacheInfo
    {
        public MapperCacheInfo(int mapToEntries, int mapEntries)
        {
            MapToEntries = mapToEntries;
            MapEntries = mapEntries;
        }
        public int MapToEntries { get; }
        public int MapEntries { get; }
        public int Total => MapToEntries + MapEntries;
    }

    #endregion

    public static class Mapper
    {
        private static readonly ConcurrentDictionary<(Type, Type), Delegate> _mapToCache = new();
        private static readonly ConcurrentDictionary<(Type, Type), Action<object, object>> _mapCache = new();

        #region Cache Management

        /// <summary>
        /// Clears all cached mapping delegates. Useful for testing or when types change dynamically.
        /// </summary>
        public static void ClearCache()
        {
            _mapToCache.Clear();
            _mapCache.Clear();
        }

        /// <summary>
        /// Gets information about the current cache state.
        /// </summary>
        public static MapperCacheInfo CacheInfo() => new(_mapToCache.Count, _mapCache.Count);

        #endregion

        #region MapTo<T> - Single Object

        /// <summary>
        /// Maps the source object to a new instance of the destination type T.
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <returns>A new instance of T mapped from source, or default(T) if source is null.</returns>
        /// <remarks>
        /// Supports strict type mapping, type coercion (e.g. string conversions), nested objects, collections, and flattening.
        /// Does NOT support circular references (will cause StackOverflow).
        /// </remarks>
        public static T? MapTo<T>(this object? source)
        {
            if (source is null) return default;
            var key = (source.GetType(), typeof(T));

            var mapFunction = (Func<object, T>)_mapToCache.GetOrAdd(key, k =>
            {
                var sourceType = k.Item1;
                var destType = k.Item2;
                var sourceParam = Expression.Parameter(typeof(object), "source");
                bool isSourceVisible = sourceType.IsVisible;
                var typedSource = isSourceVisible ? Expression.Convert(sourceParam, sourceType) : null;

                // --- 0. Direct Primitive/Value Mapping ---
                if (sourceType.IsValueType || sourceType == typeof(string))
                {
                    if (destType.IsAssignableFrom(sourceType))
                    {
                        var castSrc = isSourceVisible ? typedSource! : Expression.Convert(sourceParam, sourceType);
                        return Expression.Lambda<Func<object, T>>(Expression.Convert(castSrc, destType), sourceParam).Compile();
                    }
                    if (destType == typeof(string))
                    {
                        var castSrc = isSourceVisible ? typedSource! : Expression.Convert(sourceParam, sourceType);
                        var toStringCall = Expression.Call(castSrc, typeof(object).GetMethod("ToString")!);
                        return Expression.Lambda<Func<object, T>>(toStringCall, sourceParam).Compile();
                    }
                    var underlyingDest = Nullable.GetUnderlyingType(destType) ?? destType;
                    var underlyingSource = Nullable.GetUnderlyingType(sourceType) ?? sourceType;

                    if (underlyingDest.IsAssignableFrom(underlyingSource))
                    {
                        var castSrc = isSourceVisible ? typedSource! : Expression.Convert(sourceParam, sourceType);
                        return Expression.Lambda<Func<object, T>>(Expression.Convert(castSrc, destType), sourceParam).Compile();
                    }
                }

                // --- 0.5 Collection Mapping Support ---
                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(sourceType) &&
                    typeof(System.Collections.IEnumerable).IsAssignableFrom(destType) &&
                    sourceType != typeof(string) && destType != typeof(string))
                {
                    var destEnumerableInt = destType.GetInterfaces()
                        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

                    Type targetItemType = typeof(object);
                    if (destEnumerableInt != null)
                    {
                        targetItemType = destEnumerableInt.GetGenericArguments()[0];
                    }
                    else if (destType.IsGenericType)
                    {
                        targetItemType = destType.GetGenericArguments()[0];
                    }
                    else if (destType.IsArray)
                    {
                        targetItemType = destType.GetElementType()!;
                    }

                    var collectionMapMethod = typeof(Mapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .First(m => m.Name == "MapTo" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(System.Collections.IEnumerable))
                        .MakeGenericMethod(targetItemType);

                    var call = Expression.Call(collectionMapMethod, Expression.Convert(sourceParam, typeof(System.Collections.IEnumerable)));

                    // Handle array destination
                    if (destType.IsArray)
                    {
                        var toArrayMethod = typeof(Enumerable).GetMethod("ToArray")!.MakeGenericMethod(targetItemType);
                        var toArrayCall = Expression.Call(toArrayMethod, call);
                        return Expression.Lambda<Func<object, T>>(Expression.Convert(toArrayCall, destType), sourceParam).Compile();
                    }

                    if (destType.IsAssignableFrom(collectionMapMethod.ReturnType))
                    {
                        return Expression.Lambda<Func<object, T>>(Expression.Convert(call, destType), sourceParam).Compile();
                    }
                }

                var bindings = new List<MemberBinding>();
                var sourceProps = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
                    .ToArray();
                var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                // --- 1. Parameterless Constructor Path ---
                if (destType.GetConstructor(Type.EmptyTypes) != null || destType.IsValueType)
                {
                    foreach (var destProp in destProps)
                    {
                        if (!destProp.CanWrite) continue;
                        if (destProp.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;

                        var mapFromAttr = destProp.GetCustomAttribute<MapFromAttribute>();
                        string sourcePropertyName = mapFromAttr?.SourcePropertyName ?? destProp.Name;

                        var sourceProp = FindSourceProperty(sourceProps, sourcePropertyName, destProp.Name);

                        if (sourceProp != null)
                        {
                            var binding = CreatePropertyBinding(destProp, sourceProp, typedSource!, sourceParam, isSourceVisible);
                            if (binding != null) bindings.Add(binding);
                        }
                        else
                        {
                            // Try flattening: AddressCity -> Address.City
                            var flattenedBinding = TryCreateFlattenedBinding(destProp, sourceProps, typedSource!, sourceParam, isSourceVisible);
                            if (flattenedBinding != null) bindings.Add(flattenedBinding);
                        }
                    }
                    var init = Expression.MemberInit(Expression.New(destType), bindings);
                    return Expression.Lambda<Func<object, T>>(init, sourceParam).Compile();
                }

                // --- 2. Constructor / Record Path ---
                var ctor = destType.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault();

                if (ctor != null)
                {
                    var args = new List<Expression>();
                    foreach (var param in ctor.GetParameters())
                    {
                        var sourceProp = sourceProps.FirstOrDefault(p =>
                            p.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase) &&
                            p.CanRead);

                        if (sourceProp != null)
                        {
                            var propExp = Expression.Property(typedSource!, sourceProp);
                            if (param.ParameterType.IsAssignableFrom(sourceProp.PropertyType))
                            {
                                args.Add(Expression.Convert(propExp, param.ParameterType));
                            }
                            else if (param.ParameterType == typeof(string))
                            {
                                var toStringCall = Expression.Call(propExp, typeof(object).GetMethod("ToString")!);
                                args.Add(toStringCall);
                            }
                            else if (sourceProp.PropertyType.IsEnum && (param.ParameterType == typeof(int) || param.ParameterType == typeof(long)))
                            {
                                args.Add(Expression.Convert(propExp, param.ParameterType));
                            }
                            else
                            {
                                args.Add(Expression.Default(param.ParameterType));
                            }
                        }
                        else
                        {
                            args.Add(Expression.Default(param.ParameterType));
                        }
                    }
                    var newExp = Expression.New(ctor, args);
                    return Expression.Lambda<Func<object, T>>(newExp, sourceParam).Compile();
                }

                return Expression.Lambda<Func<object, T>>(Expression.Default(destType), sourceParam).Compile();
            });

            return mapFunction(source);
        }

        #endregion

        #region Map - Update Existing

        /// <summary>
        /// Maps properties from the source object to an existing destination object.
        /// </summary>
        public static TDestination Map<TDestination>(this object? source, TDestination destination)
        {
            if (source is null || destination is null) return destination;

            var key = (source.GetType(), typeof(TDestination));

            var mapAction = _mapCache.GetOrAdd(key, k =>
            {
                var sourceType = k.Item1;
                var destType = k.Item2;
                var sourceParam = Expression.Parameter(typeof(object), "source");
                var destParam = Expression.Parameter(typeof(object), "destination");

                var typedSource = Expression.Convert(sourceParam, sourceType);
                var typedDest = Expression.Convert(destParam, destType);

                var assignments = new List<Expression>();
                var sourceProps = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
                    .ToArray();
                var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var destProp in destProps)
                {
                    if (!destProp.CanWrite) continue;
                    if (destProp.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;

                    var mapFromAttr = destProp.GetCustomAttribute<MapFromAttribute>();
                    string sourcePropertyName = mapFromAttr?.SourcePropertyName ?? destProp.Name;

                    var sourceProp = FindSourceProperty(sourceProps, sourcePropertyName, destProp.Name);

                    if (sourceProp != null)
                    {
                        var propExp = Expression.Property(typedSource, sourceProp);
                        var targetType = destProp.PropertyType;
                        var srcType = sourceProp.PropertyType;
                        var destPropExp = Expression.Property(typedDest, destProp);

                        if (targetType.IsAssignableFrom(srcType))
                        {
                            assignments.Add(Expression.Assign(destPropExp, Expression.Convert(propExp, targetType)));
                        }
                        else if (srcType.IsClass && targetType.IsClass && srcType != typeof(string) && targetType != typeof(string))
                        {
                            var mapMethod = typeof(Mapper).GetMethods()
                                .First(m => m.Name == "MapTo" && m.GetParameters().Length == 1 && m.GetGenericArguments().Length == 1)
                                .MakeGenericMethod(targetType);

                            var recursiveCall = Expression.Call(null, mapMethod, propExp);
                            assignments.Add(Expression.Assign(destPropExp, recursiveCall));
                        }
                        else if (targetType == typeof(string))
                        {
                            var toStringCall = Expression.Call(propExp, typeof(object).GetMethod("ToString")!);
                            assignments.Add(Expression.Assign(destPropExp, toStringCall));
                        }
                    }
                }

                if (assignments.Count == 0)
                {
                    return (s, d) => { };
                }

                var block = Expression.Block(assignments);
                return Expression.Lambda<Action<object, object>>(block, sourceParam, destParam).Compile();
            });

            mapAction(source, destination);
            return destination;
        }

        #endregion

        #region MapTo<T> - Collection

        /// <summary>
        /// Maps a collection of objects to a List of type T.
        /// </summary>
        public static List<T> MapTo<T>(this System.Collections.IEnumerable? source)
        {
            if (source is null) return new List<T>();

            var result = new List<T>();
            foreach (var item in source)
            {
                if (item is null)
                {
                    result.Add(default!);
                }
                else
                {
                    result.Add(item.MapTo<T>()!);
                }
            }
            return result;
        }

        /// <summary>
        /// Maps a collection of objects to an array of type T.
        /// </summary>
        public static T[] MapToArray<T>(this System.Collections.IEnumerable? source)
        {
            return source.MapTo<T>().ToArray();
        }

        #endregion

        #region Dictionary Mapping

        /// <summary>
        /// Converts an object to a Dictionary with property names as keys.
        /// </summary>
        public static Dictionary<string, object?> ToDictionary(this object? source)
        {
            if (source is null) return new Dictionary<string, object?>();

            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead);

            foreach (var prop in props)
            {
                if (prop.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;
                dict[prop.Name] = prop.GetValue(source);
            }

            return dict;
        }

        /// <summary>
        /// Maps a dictionary to an object of type T.
        /// </summary>
        public static T? MapTo<T>(this IDictionary<string, object?>? source) where T : new()
        {
            if (source is null) return default;

            var dest = new T();
            var destProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.GetCustomAttribute<IgnoreMapAttribute>() == null);

            foreach (var prop in destProps)
            {
                var mapFromAttr = prop.GetCustomAttribute<MapFromAttribute>();
                string key = mapFromAttr?.SourcePropertyName ?? prop.Name;

                // Case-insensitive key lookup
                var matchingKey = source.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (matchingKey != null && source.TryGetValue(matchingKey, out var value) && value != null)
                {
                    try
                    {
                        if (prop.PropertyType.IsAssignableFrom(value.GetType()))
                        {
                            prop.SetValue(dest, value);
                        }
                        else if (prop.PropertyType == typeof(string))
                        {
                            prop.SetValue(dest, value.ToString());
                        }
                        else if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(prop.PropertyType))
                        {
                            var converted = Convert.ChangeType(value, prop.PropertyType);
                            prop.SetValue(dest, converted);
                        }
                    }
                    catch
                    {
                        // Skip incompatible types silently
                    }
                }
            }

            return dest;
        }

        #endregion

        #region Private Helpers

        private static PropertyInfo? FindSourceProperty(PropertyInfo[] sourceProps, string primaryName, string fallbackName)
        {
            return sourceProps.FirstOrDefault(p => p.Name.Equals(primaryName, StringComparison.OrdinalIgnoreCase) && p.CanRead)
                ?? sourceProps.FirstOrDefault(p => p.Name.Equals(fallbackName, StringComparison.OrdinalIgnoreCase) && p.CanRead);
        }

        private static MemberBinding? CreatePropertyBinding(PropertyInfo destProp, PropertyInfo sourceProp,
            Expression typedSource, ParameterExpression sourceParam, bool isSourceVisible)
        {
            Expression propExp;
            if (isSourceVisible && sourceProp.GetGetMethod()?.IsPublic == true)
            {
                propExp = Expression.Property(typedSource, sourceProp);
            }
            else
            {
                var getValue = typeof(PropertyInfo).GetMethod("GetValue", new[] { typeof(object), typeof(object[]) })!;
                var call = Expression.Call(Expression.Constant(sourceProp), getValue, sourceParam, Expression.Constant(null, typeof(object[])));
                propExp = Expression.Convert(call, sourceProp.PropertyType);
            }

            var targetType = destProp.PropertyType;
            var srcType = sourceProp.PropertyType;

            if (targetType.IsAssignableFrom(srcType))
            {
                return Expression.Bind(destProp, Expression.Convert(propExp, targetType));
            }
            else if (srcType.IsClass && targetType.IsClass && srcType != typeof(string) && targetType != typeof(string))
            {
                var mapMethod = typeof(Mapper).GetMethods()
                    .First(m => m.Name == "MapTo" && m.GetParameters().Length == 1 && m.GetGenericArguments().Length == 1)
                    .MakeGenericMethod(targetType);
                var recursiveCall = Expression.Call(null, mapMethod, propExp);
                return Expression.Bind(destProp, recursiveCall);
            }
            else if (targetType == typeof(string))
            {
                var toStringCall = Expression.Call(propExp, typeof(object).GetMethod("ToString")!);
                return Expression.Bind(destProp, toStringCall);
            }
            else if (srcType.IsEnum && (targetType == typeof(int) || targetType == typeof(long)))
            {
                return Expression.Bind(destProp, Expression.Convert(propExp, targetType));
            }
            else
            {
                var underlyingTarget = Nullable.GetUnderlyingType(targetType);
                var underlyingSource = Nullable.GetUnderlyingType(srcType);

                if (underlyingSource != null && targetType.IsAssignableFrom(underlyingSource))
                {
                    var coalesce = Expression.Coalesce(propExp, Expression.Default(targetType));
                    return Expression.Bind(destProp, coalesce);
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to create a binding for flattened properties (e.g., AddressCity -> Address.City).
        /// </summary>
        private static MemberBinding? TryCreateFlattenedBinding(PropertyInfo destProp, PropertyInfo[] sourceProps,
            Expression typedSource, ParameterExpression sourceParam, bool isSourceVisible)
        {
            string destName = destProp.Name;

            // Try to find nested properties by splitting the destination name
            foreach (var sourceProp in sourceProps)
            {
                if (!sourceProp.PropertyType.IsClass || sourceProp.PropertyType == typeof(string)) continue;
                if (!destName.StartsWith(sourceProp.Name, StringComparison.OrdinalIgnoreCase)) continue;

                string remainder = destName.Substring(sourceProp.Name.Length);
                if (string.IsNullOrEmpty(remainder)) continue;

                var nestedProps = sourceProp.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead);

                var nestedProp = nestedProps.FirstOrDefault(p => p.Name.Equals(remainder, StringComparison.OrdinalIgnoreCase));
                if (nestedProp != null && destProp.PropertyType.IsAssignableFrom(nestedProp.PropertyType))
                {
                    // Build: source.Address?.City ?? default
                    var parentAccess = Expression.Property(typedSource, sourceProp);
                    var nestedAccess = Expression.Property(parentAccess, nestedProp);

                    // Handle null parent with conditional
                    var nullCheck = Expression.Equal(parentAccess, Expression.Constant(null, sourceProp.PropertyType));
                    var safeAccess = Expression.Condition(
                        nullCheck,
                        Expression.Default(destProp.PropertyType),
                        Expression.Convert(nestedAccess, destProp.PropertyType)
                    );

                    return Expression.Bind(destProp, safeAccess);
                }
            }

            return null;
        }

        #endregion
    }
}
