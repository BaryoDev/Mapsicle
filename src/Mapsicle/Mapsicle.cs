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

        public static T? MapTo<T>(this object? source)
        {
            if (source is null) return default;
            var key = (source.GetType(), typeof(T));
            
            var mapFunction = (Func<object, T>)_mapToCache.GetOrAdd(key, k =>
            {
                var sourceType = k.Item1;
                var destType = k.Item2;
                var sourceParam = Expression.Parameter(typeof(object), "source");
                var typedSource = Expression.Convert(sourceParam, sourceType);

                var bindings = new List<MemberBinding>();
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

                        if (targetType.IsAssignableFrom(srcType))
                        {
                            bindings.Add(Expression.Bind(destProp, propExp));
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
                    }
                }

                var init = Expression.MemberInit(Expression.New(destType), bindings);
                return Expression.Lambda<Func<object, T>>(init, sourceParam).Compile();
            });

            return mapFunction(source);
        }

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
                            assignments.Add(Expression.Assign(destPropExp, propExp));
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

        public static IEnumerable<T> MapTo<T>(this IEnumerable<object>? source)
        {
            if (source is null) return Enumerable.Empty<T>();
            return source.Select(x => x is null ? default : x.MapTo<T>())!;
        }
    }
}
