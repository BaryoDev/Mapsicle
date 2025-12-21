using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Mapsicle.Fluent
{
    #region Configuration

    /// <summary>
    /// Fluent configuration for Mapsicle mappings.
    /// </summary>
    public class MapperConfiguration
    {
        private readonly List<ITypeMapConfiguration> _typeMaps = new();
        private readonly Dictionary<(Type, Type), ITypeMapConfiguration> _typeMapLookup = new();
        private readonly Dictionary<(Type, Type), Func<object, object>> _typeConverters = new();
        private bool _isSealed;

        public MapperConfiguration(Action<IMapperConfigurationExpression> configure)
        {
            var expression = new MapperConfigurationExpression(this);
            configure(expression);
            Seal();
        }

        internal void AddTypeMap(ITypeMapConfiguration typeMap)
        {
            if (_isSealed) throw new InvalidOperationException("Configuration is sealed.");
            _typeMaps.Add(typeMap);
            _typeMapLookup[(typeMap.SourceType, typeMap.DestinationType)] = typeMap;
        }

        internal void AddTypeConverter(Type sourceType, Type destType, Func<object, object> converter)
        {
            if (_isSealed) throw new InvalidOperationException("Configuration is sealed.");
            _typeConverters[(sourceType, destType)] = converter;
        }

        internal void AddReverseMap(ITypeMapConfiguration reverseTypeMap)
        {
            if (_isSealed) throw new InvalidOperationException("Configuration is sealed.");
            // Add reverse mapping - this is called before Seal()
            _typeMaps.Add(reverseTypeMap);
            _typeMapLookup[(reverseTypeMap.SourceType, reverseTypeMap.DestinationType)] = reverseTypeMap;
        }

        private void Seal() => _isSealed = true;

        /// <summary>
        /// Validates that all destination members are mapped.
        /// Throws if any required members are unmapped.
        /// </summary>
        public void AssertConfigurationIsValid()
        {
            var errors = new List<string>();

            foreach (var typeMap in _typeMaps)
            {
                var destProps = typeMap.DestinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanWrite);
                var sourceProps = new HashSet<string>(
                    typeMap.SourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead)
                        .Select(p => p.Name),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var destProp in destProps)
                {
                    if (typeMap.IsIgnored(destProp.Name)) continue;
                    if (typeMap.HasCustomMapping(destProp.Name)) continue;
                    if (sourceProps.Contains(destProp.Name)) continue;

                    // Check for flattening match
                    bool hasFlattening = typeMap.SourceType.GetProperties()
                        .Any(sp => destProp.Name.StartsWith(sp.Name, StringComparison.OrdinalIgnoreCase));
                    if (hasFlattening) continue;

                    errors.Add($"Unmapped member '{destProp.Name}' on '{typeMap.DestinationType.Name}' from '{typeMap.SourceType.Name}'");
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Mapper configuration is invalid:\n{string.Join("\n", errors)}");
            }
        }

        /// <summary>
        /// Creates an IMapper instance from this configuration.
        /// </summary>
        public IMapper CreateMapper() => new FluentMapper(this);

        /// <summary>
        /// Gets the type map for the specified source and destination types.
        /// Used internally by Mapsicle.EntityFramework for ProjectTo.
        /// </summary>
        public ITypeMapConfiguration? GetTypeMap(Type sourceType, Type destType)
        {
            _typeMapLookup.TryGetValue((sourceType, destType), out var map);
            return map;
        }

        internal Func<object, object>? GetTypeConverter(Type sourceType, Type destType)
        {
            _typeConverters.TryGetValue((sourceType, destType), out var converter);
            return converter;
        }

        /// <summary>
        /// Gets all registered type maps.
        /// </summary>
        public IReadOnlyList<ITypeMapConfiguration> GetAllTypeMaps() => _typeMaps.AsReadOnly();
    }

    #endregion

    #region Configuration Expression

    public interface IMapperConfigurationExpression
    {
        ITypeMapExpression<TSource, TDest> CreateMap<TSource, TDest>();
        void CreateMap<TSource, TDest>(Action<ITypeMapExpression<TSource, TDest>> configure);
        
        /// <summary>
        /// Creates a global type converter that applies to all mappings between the given types.
        /// </summary>
        void CreateConverter<TSource, TDest>(Func<TSource, TDest> converter);
    }

    internal class MapperConfigurationExpression : IMapperConfigurationExpression
    {
        private readonly MapperConfiguration _config;

        public MapperConfigurationExpression(MapperConfiguration config) => _config = config;

        public ITypeMapExpression<TSource, TDest> CreateMap<TSource, TDest>()
        {
            var typeMap = new TypeMapConfiguration<TSource, TDest>();
            typeMap.SetParentConfiguration(_config);
            _config.AddTypeMap(typeMap);
            return typeMap;
        }

        public void CreateMap<TSource, TDest>(Action<ITypeMapExpression<TSource, TDest>> configure)
        {
            var expr = CreateMap<TSource, TDest>();
            configure(expr);
        }

        public void CreateConverter<TSource, TDest>(Func<TSource, TDest> converter)
        {
            _config.AddTypeConverter(typeof(TSource), typeof(TDest), src => converter((TSource)src)!);
        }
    }

    public interface ITypeMapConfiguration
    {
        Type SourceType { get; }
        Type DestinationType { get; }
        bool IsIgnored(string memberName);
        bool HasCustomMapping(string memberName);
        Func<object, object>? GetCustomMapping(string memberName);
        Func<object, bool>? GetCondition(string memberName);
        LambdaExpression? GetExpressionMapping(string memberName);
        Action<object, object>? GetBeforeMap();
        Action<object, object>? GetAfterMap();
        Func<object, object>? GetConstructorFactory();
        IReadOnlyList<(Type DerivedSource, Type DerivedDest)> GetDerivedMappings();
    }

    public interface ITypeMapExpression<TSource, TDest>
    {
        ITypeMapExpression<TSource, TDest> ForMember<TMember>(
            Expression<Func<TDest, TMember>> destinationMember,
            Action<IMemberConfigurationExpression<TSource, TDest, TMember>> memberOptions);
        
        ITypeMapExpression<TSource, TDest> ForAllMembers(Action<IMemberConfigurationExpression<TSource, TDest, object>> memberOptions);
        
        /// <summary>
        /// Executes before mapping occurs.
        /// </summary>
        ITypeMapExpression<TSource, TDest> BeforeMap(Action<TSource, TDest> action);
        
        /// <summary>
        /// Executes after mapping completes.
        /// </summary>
        ITypeMapExpression<TSource, TDest> AfterMap(Action<TSource, TDest> action);
        
        /// <summary>
        /// Includes a derived type mapping. For polymorphic scenarios.
        /// </summary>
        ITypeMapExpression<TSource, TDest> Include<TDerivedSource, TDerivedDest>()
            where TDerivedSource : TSource
            where TDerivedDest : TDest;

        /// <summary>
        /// Specifies a factory function to construct the destination object.
        /// </summary>
        ITypeMapExpression<TSource, TDest> ConstructUsing(Func<TSource, TDest> factory);
        
        ITypeMapExpression<TDest, TSource> ReverseMap();
    }

    internal class TypeMapConfiguration<TSource, TDest> : ITypeMapConfiguration, ITypeMapExpression<TSource, TDest>
    {
        private readonly Dictionary<string, Func<object, object>> _customMappings = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Func<object, bool>> _conditions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LambdaExpression> _expressionMappings = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ignoredMembers = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(Type DerivedSource, Type DerivedDest)> _derivedMappings = new();
        private Action<TSource, TDest>? _beforeMap;
        private Action<TSource, TDest>? _afterMap;
        private Func<object, object>? _constructorFactory;
        private MapperConfiguration? _parentConfig;

        public Type SourceType => typeof(TSource);
        public Type DestinationType => typeof(TDest);

        public bool IsIgnored(string memberName) => _ignoredMembers.Contains(memberName);
        public bool HasCustomMapping(string memberName) => _customMappings.ContainsKey(memberName);
        public Func<object, object>? GetCustomMapping(string memberName)
        {
            _customMappings.TryGetValue(memberName, out var mapping);
            return mapping;
        }
        public Func<object, bool>? GetCondition(string memberName)
        {
            _conditions.TryGetValue(memberName, out var condition);
            return condition;
        }
        public LambdaExpression? GetExpressionMapping(string memberName)
        {
            _expressionMappings.TryGetValue(memberName, out var expr);
            return expr;
        }
        public Action<object, object>? GetBeforeMap()
        {
            if (_beforeMap == null) return null;
            return (s, d) => _beforeMap((TSource)s, (TDest)d);
        }
        public Action<object, object>? GetAfterMap()
        {
            if (_afterMap == null) return null;
            return (s, d) => _afterMap((TSource)s, (TDest)d);
        }
        public Func<object, object>? GetConstructorFactory() => _constructorFactory;
        public IReadOnlyList<(Type DerivedSource, Type DerivedDest)> GetDerivedMappings() => _derivedMappings;

        public ITypeMapExpression<TSource, TDest> ForMember<TMember>(
            Expression<Func<TDest, TMember>> destinationMember,
            Action<IMemberConfigurationExpression<TSource, TDest, TMember>> memberOptions)
        {
            var memberName = GetMemberName(destinationMember);
            var memberConfig = new MemberConfigurationExpression<TSource, TDest, TMember>(this, memberName);
            memberOptions(memberConfig);
            return this;
        }

        public ITypeMapExpression<TSource, TDest> ForAllMembers(Action<IMemberConfigurationExpression<TSource, TDest, object>> memberOptions)
        {
            var destProps = typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite);
            
            foreach (var prop in destProps)
            {
                var memberConfig = new MemberConfigurationExpression<TSource, TDest, object>(this, prop.Name);
                memberOptions(memberConfig);
            }
            return this;
        }

        public ITypeMapExpression<TSource, TDest> BeforeMap(Action<TSource, TDest> action)
        {
            _beforeMap = action;
            return this;
        }

        public ITypeMapExpression<TSource, TDest> AfterMap(Action<TSource, TDest> action)
        {
            _afterMap = action;
            return this;
        }

        public ITypeMapExpression<TSource, TDest> Include<TDerivedSource, TDerivedDest>()
            where TDerivedSource : TSource
            where TDerivedDest : TDest
        {
            _derivedMappings.Add((typeof(TDerivedSource), typeof(TDerivedDest)));
            return this;
        }

        public ITypeMapExpression<TSource, TDest> ConstructUsing(Func<TSource, TDest> factory)
        {
            _constructorFactory = src => factory((TSource)src)!;
            return this;
        }

        public ITypeMapExpression<TDest, TSource> ReverseMap()
        {
            // Create reverse mapping and register with parent configuration
            var reverseMap = new TypeMapConfiguration<TDest, TSource>();
            reverseMap.SetParentConfiguration(_parentConfig);

            // Register reverse map with parent config if available
            _parentConfig?.AddReverseMap(reverseMap);

            return reverseMap;
        }

        internal void SetParentConfiguration(MapperConfiguration? config)
        {
            _parentConfig = config;
        }

        internal void AddIgnore(string memberName) => _ignoredMembers.Add(memberName);
        
        internal void AddCustomMapping(string memberName, Func<object, object> mapping)
        {
            _customMappings[memberName] = mapping;
        }

        internal void AddExpressionMapping(string memberName, LambdaExpression expression)
        {
            _expressionMappings[memberName] = expression;
        }

        internal void AddCondition(string memberName, Func<object, bool> condition)
        {
            _conditions[memberName] = condition;
        }

        private static string GetMemberName<TMember>(Expression<Func<TDest, TMember>> expression)
        {
            if (expression.Body is MemberExpression memberExpr)
                return memberExpr.Member.Name;
            throw new ArgumentException("Expression must be a member access expression");
        }
    }

    #endregion

    #region Member Configuration

    public interface IMemberConfigurationExpression<TSource, TDest, TMember>
    {
        void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember);
        void ResolveUsing<TResult>(Func<TSource, TResult> resolver);
        void Ignore();
        void Condition(Func<TSource, bool> condition);
    }

    internal class MemberConfigurationExpression<TSource, TDest, TMember> 
        : IMemberConfigurationExpression<TSource, TDest, TMember>
    {
        private readonly TypeMapConfiguration<TSource, TDest> _typeMap;
        private readonly string _memberName;

        public MemberConfigurationExpression(TypeMapConfiguration<TSource, TDest> typeMap, string memberName)
        {
            _typeMap = typeMap;
            _memberName = memberName;
        }

        public void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember)
        {
            var compiled = sourceMember.Compile();
            _typeMap.AddCustomMapping(_memberName, src => compiled((TSource)src)!);
            // Also store expression for ProjectTo SQL translation
            _typeMap.AddExpressionMapping(_memberName, sourceMember);
        }

        public void ResolveUsing<TResult>(Func<TSource, TResult> resolver)
        {
            _typeMap.AddCustomMapping(_memberName, src => resolver((TSource)src)!);
        }

        public void Ignore()
        {
            _typeMap.AddIgnore(_memberName);
        }

        public void Condition(Func<TSource, bool> condition)
        {
            _typeMap.AddCondition(_memberName, src => condition((TSource)src));
        }
    }

    #endregion

    #region IMapper

    /// <summary>
    /// Instance-based mapper created from MapperConfiguration.
    /// </summary>
    public interface IMapper
    {
        TDest? Map<TDest>(object? source);
        TDest? Map<TSource, TDest>(TSource? source);
        TDest Map<TSource, TDest>(TSource source, TDest destination);
    }

    internal class FluentMapper : IMapper
    {
        private readonly MapperConfiguration _config;
        private readonly ConcurrentDictionary<(Type, Type), Delegate> _compiledMappers = new();

        public FluentMapper(MapperConfiguration config) => _config = config;

        public TDest? Map<TDest>(object? source)
        {
            if (source is null) return default;
            return MapInternal<TDest>(source, source.GetType());
        }

        public TDest? Map<TSource, TDest>(TSource? source)
        {
            if (source is null) return default;
            return MapInternal<TDest>(source, typeof(TSource));
        }

        public TDest Map<TSource, TDest>(TSource source, TDest destination)
        {
            if (source is null || destination is null) return destination;
            
            var typeMap = _config.GetTypeMap(typeof(TSource), typeof(TDest));
            var sourceProps = typeof(TSource).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var destProps = typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var destProp in destProps)
            {
                if (!destProp.CanWrite) continue;
                if (typeMap?.IsIgnored(destProp.Name) == true) continue;

                // Check condition
                var condition = typeMap?.GetCondition(destProp.Name);
                if (condition != null && !condition(source!)) continue;

                // Check custom mapping
                var customMapping = typeMap?.GetCustomMapping(destProp.Name);
                if (customMapping != null)
                {
                    var value = customMapping(source!);
                    destProp.SetValue(destination, value);
                    continue;
                }

                // Standard property matching
                var sourceProp = sourceProps.FirstOrDefault(p => 
                    p.Name.Equals(destProp.Name, StringComparison.OrdinalIgnoreCase) && p.CanRead);
                
                if (sourceProp != null && destProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
                {
                    destProp.SetValue(destination, sourceProp.GetValue(source));
                }
            }

            return destination;
        }

        private TDest? MapInternal<TDest>(object source, Type sourceType)
        {
            var destType = typeof(TDest);

            // Check for type converter first
            var converter = _config.GetTypeConverter(sourceType, destType);
            if (converter != null)
            {
                return (TDest)converter(source);
            }

            // Get the type map - check for polymorphic mapping
            var typeMap = _config.GetTypeMap(sourceType, destType);

            // If no direct mapping, check for polymorphic mappings
            if (typeMap == null)
            {
                typeMap = FindPolymorphicTypeMap(sourceType, destType);
            }

            // Check for custom constructor factory
            var factory = typeMap?.GetConstructorFactory();
            TDest? result;
            if (factory != null)
            {
                result = (TDest)factory(source);
            }
            else
            {
                // Use core Mapsicle for basic mapping
                result = source.MapTo<TDest>();
            }
            if (result is null) return default;

            // Execute BeforeMap hook
            typeMap?.GetBeforeMap()?.Invoke(source, result);

            // OPTIMIZATION: Use cached compiled action for custom mappings
            if (typeMap != null && HasCustomMappingOrConditions(typeMap, destType))
            {
                var applyOverrides = GetOrBuildOverrideAction<TDest>(sourceType, typeMap);
                applyOverrides(source, result);
            }

            // Execute AfterMap hook
            typeMap?.GetAfterMap()?.Invoke(source, result);

            return result;
        }

        private bool HasCustomMappingOrConditions(ITypeMapConfiguration typeMap, Type destType)
        {
            var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var destProp in destProps)
            {
                if (!destProp.CanWrite) continue;
                if (typeMap.IsIgnored(destProp.Name)) return true;
                if (typeMap.GetCondition(destProp.Name) != null) return true;
                if (typeMap.GetCustomMapping(destProp.Name) != null) return true;
            }
            return false;
        }

        private Action<object, TDest> GetOrBuildOverrideAction<TDest>(Type sourceType, ITypeMapConfiguration typeMap)
        {
            var key = (sourceType, typeof(TDest));
            
            return (Action<object, TDest>)_compiledMappers.GetOrAdd(key, _ =>
            {
                var destType = typeof(TDest);
                var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                // Build a list of actions to invoke
                var actions = new List<Action<object, TDest>>();

                foreach (var destProp in destProps)
                {
                    if (!destProp.CanWrite) continue;

                    // Check if ignored - set to default
                    if (typeMap.IsIgnored(destProp.Name))
                    {
                        var defaultValue = GetDefault(destProp.PropertyType);
                        var prop = destProp; // Capture
                        actions.Add((s, d) => prop.SetValue(d, defaultValue));
                        continue;
                    }

                    // Check condition
                    var condition = typeMap.GetCondition(destProp.Name);
                    var customMapping = typeMap.GetCustomMapping(destProp.Name);

                    if (condition != null && customMapping != null)
                    {
                        var prop = destProp;
                        var defaultValue = GetDefault(destProp.PropertyType);
                        actions.Add((s, d) =>
                        {
                            if (!condition(s))
                                prop.SetValue(d, defaultValue);
                            else
                                prop.SetValue(d, customMapping(s));
                        });
                    }
                    else if (condition != null)
                    {
                        var prop = destProp;
                        var defaultValue = GetDefault(destProp.PropertyType);
                        actions.Add((s, d) =>
                        {
                            if (!condition(s)) prop.SetValue(d, defaultValue);
                        });
                    }
                    else if (customMapping != null)
                    {
                        var prop = destProp;
                        actions.Add((s, d) => prop.SetValue(d, customMapping(s)));
                    }
                }

                // Return combined action
                if (actions.Count == 0) return (Action<object, TDest>)((s, d) => { });
                if (actions.Count == 1) return actions[0];
                
                Action<object, TDest> combined = (s, d) =>
                {
                    foreach (var action in actions) action(s, d);
                };
                return combined;
            });
        }

        private static object? GetDefault(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        /// <summary>
        /// Finds a polymorphic type map for the given source and destination types.
        /// Checks if the source type is a derived type of any registered Include mapping.
        /// </summary>
        private ITypeMapConfiguration? FindPolymorphicTypeMap(Type sourceType, Type destType)
        {
            // Check all registered type maps for derived type mappings
            foreach (var registeredMap in _config.GetAllTypeMaps())
            {
                // Check if this map is a base for our source/dest types
                if (registeredMap.SourceType.IsAssignableFrom(sourceType) &&
                    registeredMap.DestinationType.IsAssignableFrom(destType))
                {
                    // Check derived mappings
                    var derivedMappings = registeredMap.GetDerivedMappings();
                    foreach (var (derivedSource, derivedDest) in derivedMappings)
                    {
                        // Check if our source type matches or is derived from the derived source
                        if (derivedSource.IsAssignableFrom(sourceType))
                        {
                            // Look for a registered map for this derived type
                            var derivedMap = _config.GetTypeMap(derivedSource, derivedDest);
                            if (derivedMap != null)
                            {
                                return derivedMap;
                            }
                        }
                    }

                    // If no specific derived mapping, use the base mapping
                    return registeredMap;
                }
            }

            return null;
        }
    }

    #endregion

    #region Extensions

    /// <summary>
    /// Extension methods for fluent mapper integration.
    /// </summary>
    public static class FluentMapperExtensions
    {
        /// <summary>
        /// Converts using a custom converter function.
        /// </summary>
        public static ITypeMapExpression<TSource, TDest> ConvertUsing<TSource, TDest>(
            this ITypeMapExpression<TSource, TDest> expression,
            Func<TSource, TDest> converter)
        {
            // This would need access to the configuration to register
            // For now, this is a placeholder for the API shape
            return expression;
        }
    }

    #endregion
}
