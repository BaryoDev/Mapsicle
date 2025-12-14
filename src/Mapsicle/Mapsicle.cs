using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Mapsicle
{
    public static class Mapper
    {
        private static readonly ConcurrentDictionary<(Type, Type), Delegate> _mapToCache = new();
        private static readonly ConcurrentDictionary<(Type, Type), Action<object, object>> _mapCache = new();

        /// <summary>
        /// Maps the source object to a new instance of the destination type T.
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <returns>A new instance of T mapped from source, or default(T) if source is null.</returns>
        /// <remarks>
        /// Supports strict type mapping, type coercion (e.g. string conversions), nested objects, and collections.
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
                // Check if source type is accessible (public)
                bool isSourceVisible = sourceType.IsVisible;
                var typedSource = isSourceVisible ? Expression.Convert(sourceParam, sourceType) : null;

                // --- 0. Direct Primitive/Value Mapping ---
                // Handle cases like int -> int?, int -> string (when source is just an int, not an object with int property)
                // This is crucial for List<int>.MapTo<string>()
                if (sourceType.IsValueType || sourceType == typeof(string))
                {
                    if (destType.IsAssignableFrom(sourceType))
                    {
                        var castSrc = isSourceVisible ? typedSource! : Expression.Convert(sourceParam, sourceType);
                        return Expression.Lambda<Func<object, T>>(Expression.Convert(castSrc, destType), sourceParam).Compile();
                    }
                    if (destType == typeof(string))
                    {
                        // Any -> String
                        var castSrc = isSourceVisible ? typedSource! : Expression.Convert(sourceParam, sourceType);
                        var toStringCall = Expression.Call(castSrc, typeof(object).GetMethod("ToString"));
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

                // --- 0.5 Collection Mapping Support (Fix for nested lists) ---
                // If source implements IEnumerable and dest implements IEnumerable (but not string)
                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(sourceType) &&
                    typeof(System.Collections.IEnumerable).IsAssignableFrom(destType) &&
                    sourceType != typeof(string) && destType != typeof(string))
                {
                    // We need to determine the generic argument for the destination IEnumerable<T>
                    // logic: find MapTo<T>(IEnumerable)

                    var destEnumerableInt = destType.GetInterfaces()
                        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

                    // If dest is exactly IEnumerable<T> or List<T> etc.
                    // If destType is generic, likely List<T> or IEnumerable<T>
                    // We'll target the method: public static List<T> MapTo<T>(this IEnumerable source)

                    Type targetItemType = typeof(object);
                    if (destEnumerableInt != null)
                    {
                        targetItemType = destEnumerableInt.GetGenericArguments()[0];
                    }
                    else if (destType.IsGenericType)
                    {
                        // Fallback for List<T> direct
                        targetItemType = destType.GetGenericArguments()[0];
                    }

                    var collectionMapMethod = typeof(Mapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .First(m => m.Name == "MapTo" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(System.Collections.IEnumerable))
                        .MakeGenericMethod(targetItemType);

                    var call = Expression.Call(collectionMapMethod, Expression.Convert(sourceParam, typeof(System.Collections.IEnumerable)));

                    // If the return type of MapTo (List<T>) is assignable to destType (e.g. dest is List<T> or IEnumerable<T>), return it.
                    if (destType.IsAssignableFrom(collectionMapMethod.ReturnType))
                    {
                        return Expression.Lambda<Func<object, T>>(Expression.Convert(call, destType), sourceParam).Compile();
                    }
                    // If dest is an array, we might need ToArray... for now let's assume List<T> compatibility or fail gracefully to property mapping
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

                        var sourceProp = sourceProps.FirstOrDefault(p =>
                            p.Name.Equals(destProp.Name, StringComparison.OrdinalIgnoreCase) &&
                            p.CanRead);

                        if (sourceProp != null)
                        {
                            Expression propExp;
                            if (isSourceVisible && sourceProp.GetGetMethod()?.IsPublic == true)
                            {
                                propExp = Expression.Property(typedSource!, sourceProp);
                            }
                            else
                            {
                                // Fallback for internal types (Anonymous types): Use Reflection in the tree
                                var getValue = typeof(PropertyInfo).GetMethod("GetValue", new[] { typeof(object), typeof(object[]) });
                                var call = Expression.Call(Expression.Constant(sourceProp), getValue, sourceParam, Expression.Constant(null, typeof(object[])));
                                propExp = Expression.Convert(call, sourceProp.PropertyType);
                            }

                            var targetType = destProp.PropertyType;
                            var srcType = sourceProp.PropertyType;


                            if (targetType.IsAssignableFrom(srcType))
                            {
                                bindings.Add(Expression.Bind(destProp, Expression.Convert(propExp, targetType)));
                            }
                            else if (srcType.IsClass && targetType.IsClass && srcType != typeof(string) && targetType != typeof(string))
                            {
                                // Recursive MapTo
                                var mapMethod = typeof(Mapper).GetMethods()
                                    .First(m => m.Name == "MapTo" && m.GetParameters().Length == 1 && m.GetGenericArguments().Length == 1)
                                    .MakeGenericMethod(targetType);

                                var recursiveCall = Expression.Call(null, mapMethod, propExp);
                                bindings.Add(Expression.Bind(destProp, recursiveCall));
                            }
                            // --- Type Coercion ---
                            else if (targetType == typeof(string))
                            {
                                // Any -> String
                                var toStringCall = Expression.Call(propExp, typeof(object).GetMethod("ToString"));
                                bindings.Add(Expression.Bind(destProp, toStringCall));
                            }
                            else if (srcType.IsEnum && (targetType == typeof(int) || targetType == typeof(long)))
                            {
                                // Enum -> Int/Long
                                bindings.Add(Expression.Bind(destProp, Expression.Convert(propExp, targetType)));
                            }
                            // --- Nullable Handling ---
                            else
                            {
                                var underlyingTarget = Nullable.GetUnderlyingType(targetType);
                                var underlyingSource = Nullable.GetUnderlyingType(srcType);

                                // Case: T? -> T (Unwrapping)
                                if (underlyingSource != null && targetType.IsAssignableFrom(underlyingSource))
                                {
                                    // If source is null, it defaults to default(T) because of MemberInit?
                                    // No, we need explicit Coalesce
                                    var coalesce = Expression.Coalesce(propExp, Expression.Default(targetType));
                                    bindings.Add(Expression.Bind(destProp, coalesce));
                                }
                            }
                        }
                    }
                    var init = Expression.MemberInit(Expression.New(destType), bindings);
                    return Expression.Lambda<Func<object, T>>(init, sourceParam).Compile();
                }

                // --- 2. Constructor / Record Path ---
                // Find constructor with most parameters
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
                            var propExp = Expression.Property(typedSource, sourceProp);
                            if (param.ParameterType.IsAssignableFrom(sourceProp.PropertyType))
                            {
                                args.Add(Expression.Convert(propExp, param.ParameterType));
                            }
                            else if (param.ParameterType == typeof(string))
                            {
                                var toStringCall = Expression.Call(propExp, typeof(object).GetMethod("ToString"));
                                args.Add(toStringCall);
                            }
                            else
                            {
                                // If incompatible and no coercion logic fits (e.g. enum->int for ctor), pass default used?
                                // Let's support Enum->Int for records too
                                if (sourceProp.PropertyType.IsEnum && (param.ParameterType == typeof(int) || param.ParameterType == typeof(long)))
                                {
                                    args.Add(Expression.Convert(propExp, param.ParameterType));
                                }
                                else
                                {
                                    args.Add(Expression.Default(param.ParameterType));
                                }
                            }
                        }
                        else
                        {
                            args.Add(Expression.Default(param.ParameterType));
                        }
                    }
                    // Exception bubbling is preferred over swallowing/logging to file
                    var newExp = Expression.New(ctor, args);
                    return Expression.Lambda<Func<object, T>>(newExp, sourceParam).Compile();
                }

                return Expression.Lambda<Func<object, T>>(Expression.Default(destType), sourceParam).Compile();
            });

            return mapFunction(source);
        }

        /// <summary>
        /// Maps properties from the source object to an existing destination object.
        /// </summary>
        /// <typeparam name="TDestination">The type of the destination object.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="destination">The destination instance to populate.</param>
        /// <returns>The updated destination object.</returns>
        /// <remarks>
        /// Properties in destination with no matching source property are left unchanged.
        /// </remarks>
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
                var sourceProps = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var destProp in destProps)
                {
                    if (!destProp.CanWrite) continue;

                    var sourceProp = sourceProps.FirstOrDefault(p =>
                        p.Name.Equals(destProp.Name, StringComparison.OrdinalIgnoreCase) &&
                        p.CanRead);

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
                            // Recursive MapTo logic for assignment
                            // dest.Child = source.Child.MapTo<DestChild>();
                            // Note: This replaces the destination object. Strict deep update (keeping existing implementation) is harder. 
                            // Consistency decision: Use MapTo logic to create new instance.
                            var mapMethod = typeof(Mapper).GetMethods()
                                .First(m => m.Name == "MapTo" && m.GetParameters().Length == 1 && m.GetGenericArguments().Length == 1)
                                .MakeGenericMethod(targetType);

                            var recursiveCall = Expression.Call(null, mapMethod, propExp);
                            assignments.Add(Expression.Assign(destPropExp, recursiveCall));
                        }
                    }
                }

                var block = Expression.Block(assignments);
                return Expression.Lambda<Action<object, object>>(block, sourceParam, destParam).Compile();
            });

            mapAction(source, destination);
            return destination;
        }


        /// <summary>
        /// Maps a collection of objects to a collection of type T.
        /// </summary>
        /// <typeparam name="T">The target item type.</typeparam>
        /// <param name="source">The source collection.</param>
        /// <returns>A List of T.</returns>
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
    }
}
