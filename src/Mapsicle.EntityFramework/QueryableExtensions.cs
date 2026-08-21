using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mapsicle.Fluent;

namespace Mapsicle.EntityFramework
{
    /// <summary>
    /// Provides ProjectTo extension for IQueryable that builds expression trees for SQL translation.
    /// </summary>
    public static class QueryableExtensions
    {
        // Projections built with no configuration. One entry per type pair, for the life of the
        // process, which is correct: there is nothing per-caller to key on.
        private static readonly ConcurrentDictionary<(Type, Type), LambdaExpression> _defaultProjectionCache = new();

        // Projections built from a MapperConfiguration, held in a table keyed weakly by that
        // configuration.
        //
        // This used to be one static dictionary keyed partly on RuntimeHelpers.GetHashCode of the
        // configuration, which is its identity hash. Every MapperConfiguration instance therefore
        // got its own entry and nothing ever removed it, so an application constructing a
        // configuration per request or per scope grew this dictionary without bound for the life of
        // the process. Fifty structurally identical configurations produced fifty entries.
        //
        // A ConditionalWeakTable holds no strong reference to its key, so a configuration's
        // projections become collectable at the same moment the configuration itself does. It does
        // not deduplicate two structurally identical configurations, which would need a stable
        // fingerprint over the configuration model; that is a larger change and this one removes
        // the unbounded growth, which is the reported harm.
        private static readonly ConditionalWeakTable<MapperConfiguration, ConcurrentDictionary<(Type, Type), LambdaExpression>>
            _configuredProjectionCache = new();

        private static ConcurrentDictionary<(Type, Type), LambdaExpression> CacheFor(MapperConfiguration? configuration)
        {
            if (configuration is null)
            {
                return _defaultProjectionCache;
            }

            return _configuredProjectionCache.GetValue(configuration, static _ => new ConcurrentDictionary<(Type, Type), LambdaExpression>());
        }

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

            // Use Select with the built expression.
            // Queryable has two 2-parameter Select overloads (selector and indexed selector);
            // pick the non-indexed one by its Expression<Func<TSource, TResult>> selector arity
            // instead of relying on reflection ordering.
            var selectMethod = typeof(Queryable).GetMethods()
                .First(m => m.Name == "Select"
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType // Expression<Func<TSource, TResult>>
                        .GetGenericArguments()[0]         // Func<TSource, TResult>
                        .GetGenericArguments().Length == 2)
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

            return (Expression<Func<TSource, TDest>>)CacheFor(configuration).GetOrAdd(key, _ =>
                BuildProjectionExpression<TSource, TDest>(configuration));
        }

        private static LambdaExpression GetOrBuildProjection<TDest>(
            Type sourceType,
            Type destType,
            MapperConfiguration? configuration)
            where TDest : new()
        {
            var key = (sourceType, destType);

            return CacheFor(configuration).GetOrAdd(key, _ =>
                BuildProjectionExpressionNonGeneric(sourceType, destType, configuration));
        }

        /// <summary>
        /// Builds an expression tree for projecting TSource to TDest.
        /// This expression can be translated to SQL by EF Core.
        /// </summary>
        private static Expression<Func<TSource, TDest>> BuildProjectionExpression<TSource, TDest>(
            MapperConfiguration? configuration)
            where TDest : new()
        {
            return (Expression<Func<TSource, TDest>>)BuildProjectionExpressionCore(
                typeof(TSource), typeof(TDest), configuration);
        }

        private static LambdaExpression BuildProjectionExpressionNonGeneric(
            Type sourceType,
            Type destType,
            MapperConfiguration? configuration)
        {
            return BuildProjectionExpressionCore(sourceType, destType, configuration);
        }

        private static LambdaExpression BuildProjectionExpressionCore(
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
                    valueExpression = ReplaceParameter(expressionMapping.Body, expressionMapping.Parameters[0], sourceParam);

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
            _defaultProjectionCache.Clear();
            foreach (var entry in _configuredProjectionCache)
            {
                entry.Value.Clear();
            }
        }

        /// <summary>
        /// Gets the current cache size. Useful for diagnostics.
        /// </summary>
        /// <remarks>
        /// Counts projections held for configurations that are still reachable, plus the
        /// unconfigured ones. A configuration that has been collected contributes nothing, which is
        /// the property that makes this bounded.
        /// </remarks>
        public static int CacheSize
        {
            get
            {
                var total = _defaultProjectionCache.Count;
                foreach (var entry in _configuredProjectionCache)
                {
                    total += entry.Value.Count;
                }
                return total;
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
