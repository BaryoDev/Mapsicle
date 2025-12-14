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
        private static readonly ConcurrentDictionary<(Type, Type), Delegate> _cache = new();

        public static T? MapTo<T>(this object? source)
        {
            if (source is null) return default;
            var key = (source.GetType(), typeof(T));
            
            var mapFunction = (Func<object, T>)_cache.GetOrAdd(key, k =>
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

                    if (sourceProp != null && destProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
                    {
                        var propExp = Expression.Property(typedSource, sourceProp);
                        bindings.Add(Expression.Bind(destProp, propExp));
                    }
                }

                var init = Expression.MemberInit(Expression.New(destType), bindings);
                return Expression.Lambda<Func<object, T>>(init, sourceParam).Compile();
            });

            return mapFunction(source);
        }

        public static IEnumerable<T> MapTo<T>(this IEnumerable<object>? source)
        {
            if (source is null) return Enumerable.Empty<T>();
            return source.Select(x => x is null ? default : x.MapTo<T>())!;
        }
    }
}
