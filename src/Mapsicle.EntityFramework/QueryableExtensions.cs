using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Mapsicle.Fluent;

namespace Mapsicle.EntityFramework
{
    /// <summary>
    /// Provides ProjectTo extension for IQueryable that builds expression trees for SQL translation.
    /// </summary>
    public static class QueryableExtensions
    {
        private static readonly Dictionary<(Type, Type), LambdaExpression> _projectionCache = new();
        private static readonly object _lock = new();

        /// <summary>
        /// Projects each element of a query to a new form using the configured mapping.
        /// The projection is translated to SQL by EF Core.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source queryable.</param>
        /// <param name="configuration">The mapper configuration (optional, uses convention-based if null).</param>
        /// <returns>An IQueryable of the destination type.</returns>
        public static IQueryable<TDest> ProjectTo<TDest>(
            this IQueryable source,
            MapperConfiguration? configuration = null)
            where TDest : new()
        {
            var sourceType = source.ElementType;
            var destType = typeof(TDest);

            var projection = GetOrBuildProjection<TDest>(sourceType, destType, configuration);

            // Use Select with the built expression
            var selectMethod = typeof(Queryable).GetMethods()
                .First(m => m.Name == "Select" && m.GetParameters().Length == 2)
                .MakeGenericMethod(sourceType, destType);

            return (IQueryable<TDest>)selectMethod.Invoke(null, new object[] { source, projection })!;
        }

        /// <summary>
        /// Projects each element of a typed query to a new form.
        /// </summary>
        public static IQueryable<TDest> ProjectTo<TSource, TDest>(
            this IQueryable<TSource> source,
            MapperConfiguration? configuration = null)
            where TDest : new()
        {
            var projection = GetOrBuildProjection<TSource, TDest>(configuration);
            return source.Select(projection);
        }

        private static Expression<Func<TSource, TDest>> GetOrBuildProjection<TSource, TDest>(
            MapperConfiguration? configuration)
            where TDest : new()
        {
            var key = (typeof(TSource), typeof(TDest));

            lock (_lock)
            {
                if (_projectionCache.TryGetValue(key, out var cached))
                {
                    return (Expression<Func<TSource, TDest>>)cached;
                }

                var projection = BuildProjectionExpression<TSource, TDest>(configuration);
                _projectionCache[key] = projection;
                return projection;
            }
        }

        private static LambdaExpression GetOrBuildProjection<TDest>(
            Type sourceType,
            Type destType,
            MapperConfiguration? configuration)
            where TDest : new()
        {
            var key = (sourceType, destType);

            lock (_lock)
            {
                if (_projectionCache.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                var projection = BuildProjectionExpressionNonGeneric(sourceType, destType, configuration);
                _projectionCache[key] = projection;
                return projection;
            }
        }

        /// <summary>
        /// Builds an expression tree for projecting TSource to TDest.
        /// This expression can be translated to SQL by EF Core.
        /// </summary>
        private static Expression<Func<TSource, TDest>> BuildProjectionExpression<TSource, TDest>(
            MapperConfiguration? configuration)
            where TDest : new()
        {
            var sourceType = typeof(TSource);
            var destType = typeof(TDest);

            var sourceParam = Expression.Parameter(sourceType, "src");
            var bindings = new List<MemberBinding>();

            var typeMap = configuration?.GetTypeMap(sourceType, destType);
            var sourceProps = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToArray();
            var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite);

            foreach (var destProp in destProps)
            {
                // Check for [IgnoreMap] attribute
                if (destProp.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;
                if (typeMap?.IsIgnored(destProp.Name) == true) continue;

                Expression? valueExpression = null;

                // First check for expression mapping from ForMember/MapFrom (translatable to SQL)
                var expressionMapping = typeMap?.GetExpressionMapping(destProp.Name);
                if (expressionMapping != null)
                {
                    // Replace the parameter in the expression with our sourceParam
                    valueExpression = ReplaceParameter(expressionMapping.Body, expressionMapping.Parameters[0], sourceParam);

                    // Ensure the expression type matches the destination property type
                    if (valueExpression.Type != destProp.PropertyType)
                    {
                        valueExpression = Expression.Convert(valueExpression, destProp.PropertyType);
                    }

                    bindings.Add(Expression.Bind(destProp, valueExpression));
                    continue;
                }

                // Check for [MapFrom] attribute
                var mapFromAttr = destProp.GetCustomAttribute<MapFromAttribute>();
                string sourcePropName = mapFromAttr?.SourcePropertyName ?? destProp.Name;

                // Find matching source property
                var sourceProp = sourceProps.FirstOrDefault(p =>
                    p.Name.Equals(sourcePropName, StringComparison.OrdinalIgnoreCase));

                if (sourceProp != null)
                {
                    valueExpression = BuildPropertyExpression(sourceParam, sourceProp, destProp);
                }
                else
                {
                    // Try flattening: AddressCity -> Address.City
                    valueExpression = TryBuildFlattenedExpression(sourceParam, sourceProps, destProp);
                }

                if (valueExpression != null)
                {
                    bindings.Add(Expression.Bind(destProp, valueExpression));
                }
            }

            var memberInit = Expression.MemberInit(Expression.New(destType), bindings);
            return Expression.Lambda<Func<TSource, TDest>>(memberInit, sourceParam);
        }

        private static LambdaExpression BuildProjectionExpressionNonGeneric(
            Type sourceType,
            Type destType,
            MapperConfiguration? configuration)
        {
            var sourceParam = Expression.Parameter(sourceType, "src");
            var bindings = new List<MemberBinding>();

            var typeMap = configuration?.GetTypeMap(sourceType, destType);
            var sourceProps = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToArray();
            var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite);

            foreach (var destProp in destProps)
            {
                if (destProp.GetCustomAttribute<IgnoreMapAttribute>() != null) continue;
                if (typeMap?.IsIgnored(destProp.Name) == true) continue;

                Expression? valueExpression = null;

                // First check for expression mapping from ForMember/MapFrom (translatable to SQL)
                var expressionMapping = typeMap?.GetExpressionMapping(destProp.Name);
                if (expressionMapping != null)
                {
                    // Replace the parameter in the expression with our sourceParam
                    valueExpression = ReplaceParameter(expressionMapping.Body, expressionMapping.Parameters[0], sourceParam);

                    // Ensure the expression type matches the destination property type
                    if (valueExpression.Type != destProp.PropertyType)
                    {
                        valueExpression = Expression.Convert(valueExpression, destProp.PropertyType);
                    }

                    bindings.Add(Expression.Bind(destProp, valueExpression));
                    continue;
                }

                var mapFromAttr = destProp.GetCustomAttribute<MapFromAttribute>();
                string sourcePropName = mapFromAttr?.SourcePropertyName ?? destProp.Name;

                var sourceProp = sourceProps.FirstOrDefault(p =>
                    p.Name.Equals(sourcePropName, StringComparison.OrdinalIgnoreCase));

                if (sourceProp != null)
                {
                    valueExpression = BuildPropertyExpression(sourceParam, sourceProp, destProp);
                }
                else
                {
                    valueExpression = TryBuildFlattenedExpression(sourceParam, sourceProps, destProp);
                }

                if (valueExpression != null)
                {
                    bindings.Add(Expression.Bind(destProp, valueExpression));
                }
            }

            var memberInit = Expression.MemberInit(Expression.New(destType), bindings);
            var funcType = typeof(Func<,>).MakeGenericType(sourceType, destType);
            return Expression.Lambda(funcType, memberInit, sourceParam);
        }

        private static Expression? BuildPropertyExpression(
            ParameterExpression sourceParam,
            PropertyInfo sourceProp,
            PropertyInfo destProp)
        {
            var sourceAccess = Expression.Property(sourceParam, sourceProp);

            // Direct assignment if types match
            if (destProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
            {
                return sourceAccess;
            }

            // Type coercion: Any -> string via ToString()
            if (destProp.PropertyType == typeof(string))
            {
                // Handle null for reference types
                if (!sourceProp.PropertyType.IsValueType)
                {
                    var nullCheck = Expression.Equal(sourceAccess, Expression.Constant(null));
                    var toStringCall = Expression.Call(sourceAccess, typeof(object).GetMethod("ToString")!);
                    return Expression.Condition(nullCheck, Expression.Constant(null, typeof(string)), toStringCall);
                }
                return Expression.Call(sourceAccess, typeof(object).GetMethod("ToString")!);
            }

            // Enum -> int
            if (sourceProp.PropertyType.IsEnum && destProp.PropertyType == typeof(int))
            {
                return Expression.Convert(sourceAccess, typeof(int));
            }

            // Nullable handling: T -> T?
            var underlyingDest = Nullable.GetUnderlyingType(destProp.PropertyType);
            if (underlyingDest != null && underlyingDest.IsAssignableFrom(sourceProp.PropertyType))
            {
                return Expression.Convert(sourceAccess, destProp.PropertyType);
            }

            // Nullable handling: T? -> T
            var underlyingSource = Nullable.GetUnderlyingType(sourceProp.PropertyType);
            if (underlyingSource != null && destProp.PropertyType.IsAssignableFrom(underlyingSource))
            {
                return Expression.Coalesce(sourceAccess, Expression.Default(destProp.PropertyType));
            }

            // Nested object projection (recursive)
            if (sourceProp.PropertyType.IsClass && destProp.PropertyType.IsClass &&
                sourceProp.PropertyType != typeof(string) && destProp.PropertyType != typeof(string))
            {
                return BuildNestedProjection(sourceAccess, sourceProp.PropertyType, destProp.PropertyType);
            }

            return null;
        }

        private static Expression? BuildNestedProjection(
            Expression sourceAccess,
            Type sourceType,
            Type destType)
        {
            var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite);
            var sourceProps = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToArray();

            var bindings = new List<MemberBinding>();

            foreach (var destProp in destProps)
            {
                var sourceProp = sourceProps.FirstOrDefault(p =>
                    p.Name.Equals(destProp.Name, StringComparison.OrdinalIgnoreCase));

                if (sourceProp != null && destProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
                {
                    var propAccess = Expression.Property(sourceAccess, sourceProp);
                    bindings.Add(Expression.Bind(destProp, propAccess));
                }
            }

            if (bindings.Count == 0) return null;

            var memberInit = Expression.MemberInit(Expression.New(destType), bindings);

            // Handle null source object
            var nullCheck = Expression.Equal(sourceAccess, Expression.Constant(null, sourceType));
            return Expression.Condition(nullCheck, Expression.Constant(null, destType), memberInit);
        }

        private static Expression? TryBuildFlattenedExpression(
            ParameterExpression sourceParam,
            PropertyInfo[] sourceProps,
            PropertyInfo destProp)
        {
            string destName = destProp.Name;

            foreach (var sourceProp in sourceProps)
            {
                if (!sourceProp.PropertyType.IsClass || sourceProp.PropertyType == typeof(string)) continue;
                if (!destName.StartsWith(sourceProp.Name, StringComparison.OrdinalIgnoreCase)) continue;

                string remainder = destName.Substring(sourceProp.Name.Length);
                if (string.IsNullOrEmpty(remainder)) continue;

                var nestedProps = sourceProp.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead);

                var nestedProp = nestedProps.FirstOrDefault(p =>
                    p.Name.Equals(remainder, StringComparison.OrdinalIgnoreCase));

                if (nestedProp != null && destProp.PropertyType.IsAssignableFrom(nestedProp.PropertyType))
                {
                    var parentAccess = Expression.Property(sourceParam, sourceProp);
                    var nestedAccess = Expression.Property(parentAccess, nestedProp);

                    // Handle null parent: source.Address == null ? default : source.Address.City
                    var nullCheck = Expression.Equal(parentAccess, Expression.Constant(null, sourceProp.PropertyType));
                    return Expression.Condition(
                        nullCheck,
                        Expression.Default(destProp.PropertyType),
                        nestedAccess);
                }
            }

            return null;
        }

        /// <summary>
        /// Clears the projection cache. Useful for testing.
        /// </summary>
        public static void ClearProjectionCache()
        {
            lock (_lock)
            {
                _projectionCache.Clear();
            }
        }

        /// <summary>
        /// Replaces a parameter expression in an expression tree with another expression.
        /// </summary>
        private static Expression ReplaceParameter(Expression expression, ParameterExpression oldParam, Expression newParam)
        {
            return new ParameterReplacer(oldParam, newParam).Visit(expression);
        }

        /// <summary>
        /// Expression visitor that replaces a parameter with another expression.
        /// </summary>
        private class ParameterReplacer : ExpressionVisitor
        {
            private readonly ParameterExpression _oldParam;
            private readonly Expression _newParam;

            public ParameterReplacer(ParameterExpression oldParam, Expression newParam)
            {
                _oldParam = oldParam;
                _newParam = newParam;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                return node == _oldParam ? _newParam : base.VisitParameter(node);
            }
        }
    }
}
