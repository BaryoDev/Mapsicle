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
        private int _version;

        /// <summary>
        /// Bumped whenever anything a mapping plan is derived from changes.
        /// </summary>
        /// <remarks>
        /// <c>CreateMap</c> returns a live expression, so a member can be ignored, resolved or
        /// conditioned after a map has already run, and the next map has to honour it. That is why
        /// the override pass originally rebuilt its answer on every call. Caching the answer is
        /// only safe if every one of those mutators is visible here, so each of them touches this.
        /// </remarks>
        internal int Version => System.Threading.Volatile.Read(ref _version);

        internal void Touch() => System.Threading.Interlocked.Increment(ref _version);

        /// <summary>Builds a configuration from a callback.</summary>
        /// <param name="configure">Receives the configuration expression.</param>
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
            Touch();
        }

        internal void AddTypeConverter(Type sourceType, Type destType, Func<object, object> converter)
        {
            if (_isSealed) throw new InvalidOperationException("Configuration is sealed.");
            _typeConverters[(sourceType, destType)] = converter;
            Touch();
        }

        internal void AddReverseMap(ITypeMapConfiguration reverseTypeMap)
        {
            if (_isSealed) throw new InvalidOperationException("Configuration is sealed.");
            // Add reverse mapping - this is called before Seal()
            _typeMaps.Add(reverseTypeMap);
            _typeMapLookup[(reverseTypeMap.SourceType, reverseTypeMap.DestinationType)] = reverseTypeMap;
            Touch();
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

    /// <summary>
    /// The configuration surface handed to the callback passed to <see cref="MapperConfiguration"/>.
    /// </summary>
    /// <remarks>
    /// Configuring a pair here is optional. An unconfigured pair still maps by convention, so this
    /// is for the members convention gets wrong rather than for registration.
    /// </remarks>
    public interface IMapperConfigurationExpression
    {
        /// <summary>Begins configuring the map from <typeparamref name="TSource"/> to <typeparamref name="TDest"/>.</summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <returns>The expression used to configure individual members.</returns>
        ITypeMapExpression<TSource, TDest> CreateMap<TSource, TDest>();

        /// <summary>Configures the map from <typeparamref name="TSource"/> to <typeparamref name="TDest"/> inline.</summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="configure">Receives the same expression the other overload returns.</param>
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

    /// <summary>
    /// The configuration for one type pair, read by the mapper when it compiles that pair.
    /// </summary>
    public interface ITypeMapConfiguration
    {
        /// <summary>The type being mapped from.</summary>
        Type SourceType { get; }

        /// <summary>The type being mapped to.</summary>
        Type DestinationType { get; }

        /// <summary>Whether the named destination member was configured to be skipped.</summary>
        /// <param name="memberName">The destination member name.</param>
        bool IsIgnored(string memberName);

        /// <summary>Whether the named destination member has a configured resolver.</summary>
        /// <param name="memberName">The destination member name.</param>
        bool HasCustomMapping(string memberName);

        /// <summary>The configured resolver for the named member, or null.</summary>
        /// <param name="memberName">The destination member name.</param>
        Func<object, object>? GetCustomMapping(string memberName);

        /// <summary>The predicate deciding whether the named member is mapped at all, or null.</summary>
        /// <param name="memberName">The destination member name.</param>
        Func<object, bool>? GetCondition(string memberName);

        /// <summary>The configured source expression for the named member, or null.</summary>
        /// <remarks>Kept as an expression rather than a delegate so it can be translated by a query provider.</remarks>
        /// <param name="memberName">The destination member name.</param>
        LambdaExpression? GetExpressionMapping(string memberName);

        /// <summary>The hook to run before the pair is mapped, or null.</summary>
        Action<object, object>? GetBeforeMap();

        /// <summary>The hook to run after the pair is mapped, or null.</summary>
        Action<object, object>? GetAfterMap();

        /// <summary>The factory that constructs the destination, or null to construct it normally.</summary>
        Func<object, object>? GetConstructorFactory();

        /// <summary>The derived pairs registered for polymorphic mapping.</summary>
        IReadOnlyList<(Type DerivedSource, Type DerivedDest)> GetDerivedMappings();
    }

    /// <summary>
    /// Configures how one type pair maps. Every method returns the expression, so calls chain.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    public interface ITypeMapExpression<TSource, TDest>
    {
        /// <summary>Configures one destination member.</summary>
        /// <typeparam name="TMember">The destination member type.</typeparam>
        /// <param name="destinationMember">Selects the member, for example <c>d => d.FullName</c>.</param>
        /// <param name="memberOptions">Configures where that member's value comes from.</param>
        /// <returns>This expression, for chaining.</returns>
        ITypeMapExpression<TSource, TDest> ForMember<TMember>(
            Expression<Func<TDest, TMember>> destinationMember,
            Action<IMemberConfigurationExpression<TSource, TDest, TMember>> memberOptions);

        /// <summary>Applies the same configuration to every destination member.</summary>
        /// <param name="memberOptions">Configuration applied to each member in turn.</param>
        /// <returns>This expression, for chaining.</returns>
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

        /// <summary>Registers the inverse pair and returns its expression for configuring.</summary>
        /// <returns>The expression for the destination-to-source map.</returns>
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
            _parentConfig?.Touch();
            return this;
        }

        public ITypeMapExpression<TSource, TDest> AfterMap(Action<TSource, TDest> action)
        {
            _afterMap = action;
            _parentConfig?.Touch();
            return this;
        }

        public ITypeMapExpression<TSource, TDest> Include<TDerivedSource, TDerivedDest>()
            where TDerivedSource : TSource
            where TDerivedDest : TDest
        {
            _derivedMappings.Add((typeof(TDerivedSource), typeof(TDerivedDest)));
            _parentConfig?.Touch();
            return this;
        }

        public ITypeMapExpression<TSource, TDest> ConstructUsing(Func<TSource, TDest> factory)
        {
            _constructorFactory = src => factory((TSource)src)!;
            _parentConfig?.Touch();
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

        internal void AddIgnore(string memberName)
        {
            _ignoredMembers.Add(memberName);
            _parentConfig?.Touch();
        }

        internal void AddCustomMapping(string memberName, Func<object, object> mapping)
        {
            _customMappings[memberName] = mapping;
            _parentConfig?.Touch();
        }

        internal void AddExpressionMapping(string memberName, LambdaExpression expression)
        {
            _expressionMappings[memberName] = expression;
            _parentConfig?.Touch();
        }

        internal void AddCondition(string memberName, Func<object, bool> condition)
        {
            _conditions[memberName] = condition;
            _parentConfig?.Touch();
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

    /// <summary>
    /// Configures where one destination member's value comes from.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <typeparam name="TMember">The destination member type.</typeparam>
    public interface IMemberConfigurationExpression<TSource, TDest, TMember>
    {
        /// <summary>Reads the member from a source expression rather than by name.</summary>
        /// <typeparam name="TSourceMember">The source member type.</typeparam>
        /// <param name="sourceMember">Selects the source value, for example <c>s => s.First + " " + s.Last</c>.</param>
        void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember);

        /// <summary>Computes the member with a delegate.</summary>
        /// <remarks>Unlike <see cref="MapFrom"/> this is opaque to a query provider, so it cannot be translated to SQL.</remarks>
        /// <typeparam name="TResult">The computed type.</typeparam>
        /// <param name="resolver">Produces the value from the source.</param>
        void ResolveUsing<TResult>(Func<TSource, TResult> resolver);

        /// <summary>Leaves this member at its destination default.</summary>
        void Ignore();

        /// <summary>Maps this member only when the predicate holds for the source.</summary>
        /// <param name="condition">Decides whether the member is mapped.</param>
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
        /// <summary>Maps a source of unknown static type into a new <typeparamref name="TDest"/>.</summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object, or null.</param>
        /// <returns>The mapped instance, or the destination default when source is null.</returns>
        TDest? Map<TDest>(object? source);

        /// <summary>Maps a statically-typed source into a new <typeparamref name="TDest"/>.</summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object, or null.</param>
        /// <returns>The mapped instance, or the destination default when source is null.</returns>
        TDest? Map<TSource, TDest>(TSource? source);

        /// <summary>Maps onto an existing destination instead of constructing one.</summary>
        /// <remarks>
        /// A directly-assignable reference-typed member is shared with the source rather than
        /// copied, so mutating the source afterwards reaches into the destination.
        /// </remarks>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="destination">The instance to populate.</param>
        /// <returns>The same destination instance.</returns>
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
            var plan = InPlacePlan<TSource, TDest>.Get();

            typeMap?.GetBeforeMap()?.Invoke(source, destination);

            var steps = plan.Steps;
            for (var i = 0; i < steps.Length; i++)
            {
                var step = steps[i];

                if (typeMap?.IsIgnored(step.DestName) == true) continue;

                var condition = typeMap?.GetCondition(step.DestName);
                if (condition != null && !condition(source!)) continue;

                var customMapping = typeMap?.GetCustomMapping(step.DestName);
                if (customMapping != null)
                {
                    // Arbitrary Func<object, object> from configuration, so this one still goes
                    // through reflection. It is the uncommon path.
                    step.DestProp.SetValue(destination, customMapping(source!));
                    continue;
                }

                step.Assign?.Invoke(source, destination);
            }

            typeMap?.GetAfterMap()?.Invoke(source, destination);

            return destination;
        }

        /// <summary>
        /// The property pairing for one source/destination pair, resolved once and compiled.
        /// </summary>
        /// <remarks>
        /// This method used to reflect over both types on every call: two
        /// <c>GetProperties()</c> array allocations, a LINQ closure per destination property, and a
        /// <c>PropertyInfo.SetValue</c> per assignment. Measured at 616 bytes and roughly 33 times
        /// the cost of the core mapper's in-place <c>Map</c>, which allocates nothing.
        ///
        /// The pairing depends only on the two types, so it is resolved once per closed pair and the
        /// assignment compiled to a delegate. Everything that depends on configuration rather than
        /// on the types (ignores, conditions, custom mappings, before and after hooks) is still
        /// evaluated per call, so behaviour is unchanged including for a configuration built after
        /// the first map.
        /// </remarks>
        private static class InPlacePlan<TSource, TDest>
        {
            // volatile because the Plan holds a Step[] whose elements hold compiled delegates. On a
            // weak memory model such as arm64, a plain write can let another thread observe the
            // Plan reference before the array element writes are visible, and read a default Step
            // with a null Assign. That would silently skip properties rather than fail loudly,
            // which is the worst shape a mapping bug can take.
            private static volatile Plan? _plan;

            internal static Plan Get() => _plan ??= Build();

            internal sealed class Plan
            {
                internal readonly Step[] Steps;
                internal Plan(Step[] steps) => Steps = steps;
            }

            internal readonly struct Step
            {
                internal readonly string DestName;
                internal readonly PropertyInfo DestProp;
                internal readonly Action<TSource, TDest>? Assign;

                internal Step(PropertyInfo destProp, Action<TSource, TDest>? assign)
                {
                    DestProp = destProp;
                    DestName = destProp.Name;
                    Assign = assign;
                }
            }

            private static Plan Build()
            {
                var sourceProps = typeof(TSource).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                var destProps = typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance);

                var steps = new List<Step>(destProps.Length);

                foreach (var destProp in destProps)
                {
                    if (!destProp.CanWrite) continue;

                    PropertyInfo? sourceProp = null;
                    foreach (var candidate in sourceProps)
                    {
                        if (candidate.CanRead &&
                            candidate.Name.Equals(destProp.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            sourceProp = candidate;
                            break;
                        }
                    }

                    Action<TSource, TDest>? assign = null;
                    if (sourceProp != null && destProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
                    {
                        assign = CompileAssign(sourceProp, destProp);
                    }

                    // Kept even with no standard assignment: configuration may supply a custom
                    // mapping for this destination property, and dropping the step would silently
                    // stop honouring it.
                    steps.Add(new Step(destProp, assign));
                }

                return new Plan(steps.ToArray());
            }

            private static Action<TSource, TDest> CompileAssign(PropertyInfo sourceProp, PropertyInfo destProp)
            {
                var src = Expression.Parameter(typeof(TSource), "source");
                var dst = Expression.Parameter(typeof(TDest), "destination");

                Expression value = Expression.Property(src, sourceProp);
                if (destProp.PropertyType != sourceProp.PropertyType)
                {
                    value = Expression.Convert(value, destProp.PropertyType);
                }

                var body = Expression.Assign(Expression.Property(dst, destProp), value);
                return Expression.Lambda<Action<TSource, TDest>>(body, src, dst).Compile();
            }
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

            // A collection destination maps element by element through the configured map. Without
            // this the whole call fell through to the core mapper, which knows nothing about this
            // configuration, so ForMember, Condition and Ignore were all skipped for collections
            // while working for a single object. An ignored member is a control, and it protected
            // one object but not a list of them.
            if (source is System.Collections.IEnumerable sequence
                && source is not string
                && CollectionShape.Of(destType) is { } shape)
            {
                return (TDest)shape.Map(this, sequence);
            }

            // Get the type map - check for polymorphic mapping
            var typeMap = _config.GetTypeMap(sourceType, destType);

            // If no direct mapping, check for polymorphic mappings
            if (typeMap == null)
            {
                typeMap = FindPolymorphicTypeMap(sourceType, destType);
            }

            return MapResolved<TDest>(source, sourceType, typeMap);
        }

        /// <summary>
        /// Maps one object once the configuration lookups for its pair have already been done.
        /// </summary>
        /// <remarks>
        /// Split out so a collection can do those lookups once instead of once per element. The
        /// element loop used to call back into the full entry point, paying a converter lookup, a
        /// collection-shape lookup, a type map lookup and a plan lookup for every item in the list.
        /// </remarks>
        private TDest? MapResolved<TDest>(object source, Type sourceType, ITypeMapConfiguration? typeMap)
        {
            // 1. Create destination
            var factory = typeMap?.GetConstructorFactory();
            TDest? result;
            bool usedFactory = false;
            if (factory != null)
            {
                result = (TDest)factory(source);
                usedFactory = true;
            }
            else
            {
                var construct = Constructor<TDest>.Compiled;
                if (construct != null)
                {
                    result = construct();
                }
                else
                {
                    // Fall back to core Mapsicle for types without parameterless constructors
                    result = source.MapTo<TDest>();
                    if (result is null) return default;

                    // Apply overrides and hooks on the already-mapped result
                    typeMap?.GetBeforeMap()?.Invoke(source, result);
                    if (typeMap != null
                        && GetOverridePlan<TDest>(sourceType, typeMap).Apply is Action<object, TDest> overrides)
                    {
                        overrides(source, result);
                    }
                    typeMap?.GetAfterMap()?.Invoke(source, result);
                    return result;
                }
            }
            if (result is null) return default;

            // 2. BeforeMap on the empty/factory-created destination
            typeMap?.GetBeforeMap()?.Invoke(source, result);

            // 3. Core mapping (map properties into existing object)
            // Skip when factory was used — factory is expected to produce a fully initialized object
            var plan = typeMap is null ? null : GetOverridePlan<TDest>(sourceType, typeMap);

            if (!usedFactory)
            {
                if (plan != null)
                {
                    plan.Convention(source, result);
                }
                else
                {
                    source.Map(result);
                }
            }

            // 4. Custom overrides
            if (plan?.Apply is Action<object, TDest> applyOverrides)
            {
                applyOverrides(source, result);
            }

            // 5. AfterMap
            typeMap?.GetAfterMap()?.Invoke(source, result);

            return result;
        }

        /// <summary>
        /// What the override pass has to do for one source/destination pair, worked out once.
        /// </summary>
        /// <remarks>
        /// Deciding whether any override applied used to walk every writable destination property
        /// and ask three case-insensitive dictionaries about each, on every single call. For a ten
        /// property DTO that is thirty string lookups to answer a question whose inputs are a type
        /// pair and a configuration. Measured at 269 ns per map on arm64, about a third of the gap
        /// to AutoMapper on a complex object.
        ///
        /// Applying them then wrote each member with <c>PropertyInfo.SetValue</c>, which boxes a
        /// value type and costs far more than the assignment it performs.
        ///
        /// Both are settled here once per pair per configuration version and reused. The version is
        /// the part that matters: configuration can legally change after a map has run, so a plan
        /// records the version it was built under and is rebuilt when that moves.
        /// </remarks>
        private sealed class OverridePlan
        {
            internal readonly int Version;
            internal readonly Delegate? Apply;

            /// <summary>
            /// Members the override pass always writes, so the convention pass can skip them.
            /// </summary>
            /// <remarks>
            /// An ignored member and a custom-mapped one are both written unconditionally by the
            /// override pass, so resolving them by convention first is work thrown away. A member
            /// carrying only a condition is not here: when the condition holds, the value kept is
            /// the one convention produced.
            /// </remarks>
            internal readonly string[]? Skip;

            /// <summary>
            /// The convention pass for this pair, already resolved, already skipping Skip.
            /// </summary>
            internal readonly Action<object, object> Convention;

            internal OverridePlan(int version, Delegate? apply, string[]? skip, Action<object, object> convention)
            {
                Version = version;
                Apply = apply;
                Skip = skip;
                Convention = convention;
            }
        }

        private readonly ConcurrentDictionary<(Type, Type), OverridePlan> _overridePlans = new();

        private OverridePlan GetOverridePlan<TDest>(Type sourceType, ITypeMapConfiguration typeMap)
        {
            var key = (sourceType, typeof(TDest));
            var version = _config.Version;

            if (_overridePlans.TryGetValue(key, out var existing) && existing.Version == version)
            {
                return existing;
            }

            var skip = UnconditionallyOverwritten<TDest>(typeMap);
            var plan = new OverridePlan(
                version,
                BuildOverrideAction<TDest>(typeMap),
                skip,
                Mapper.GetInPlaceMapper(sourceType, typeof(TDest), skip));
            _overridePlans[key] = plan;
            return plan;
        }

        /// <summary>
        /// The destination members the override pass writes no matter what the source says.
        /// </summary>
        private static string[]? UnconditionallyOverwritten<TDest>(ITypeMapConfiguration typeMap)
        {
            List<string>? names = null;

            foreach (var destProp in typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!destProp.CanWrite) continue;

                // A condition decides between the convention value and the default, so the
                // convention value still has to exist.
                if (typeMap.GetCondition(destProp.Name) != null) continue;

                if (typeMap.IsIgnored(destProp.Name) || typeMap.GetCustomMapping(destProp.Name) != null)
                {
                    (names ??= new List<string>()).Add(destProp.Name);
                }
            }

            return names?.ToArray();
        }

        /// <summary>
        /// Builds the override action, or null when no member on this pair has one.
        /// </summary>
        private static Action<object, TDest>? BuildOverrideAction<TDest>(ITypeMapConfiguration typeMap)
        {
            var destProps = typeof(TDest).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var actions = new List<Action<object, TDest>>();

            foreach (var destProp in destProps)
            {
                if (!destProp.CanWrite) continue;

                if (typeMap.IsIgnored(destProp.Name))
                {
                    var setIgnored = CompileSetter<TDest>(destProp);
                    var ignoredDefault = GetDefault(destProp.PropertyType);
                    actions.Add((s, d) => setIgnored(d, ignoredDefault));
                    continue;
                }

                var condition = typeMap.GetCondition(destProp.Name);
                var customMapping = typeMap.GetCustomMapping(destProp.Name);

                if (condition is null && customMapping is null) continue;

                var set = CompileSetter<TDest>(destProp);
                var defaultValue = GetDefault(destProp.PropertyType);

                if (condition != null && customMapping != null)
                {
                    actions.Add((s, d) => set(d, condition(s) ? customMapping(s) : defaultValue));
                }
                else if (condition != null)
                {
                    actions.Add((s, d) => { if (!condition(s)) set(d, defaultValue); });
                }
                else
                {
                    actions.Add((s, d) => set(d, customMapping!(s)));
                }
            }

            if (actions.Count == 0) return null;
            if (actions.Count == 1) return actions[0];

            var steps = actions.ToArray();
            return (s, d) =>
            {
                for (var i = 0; i < steps.Length; i++) steps[i](s, d);
            };
        }

        /// <summary>
        /// Compiles an assignment to one property, standing in for PropertyInfo.SetValue.
        /// </summary>
        /// <remarks>
        /// A null assigned to a value type member has to land as the default rather than throw,
        /// because that is what SetValue does: a custom mapping returning null for an int property
        /// wrote 0 and raised nothing. A bare Convert to int would throw a NullReferenceException
        /// from inside a lambda_method frame instead, which is both a behaviour change and one of
        /// the least legible stack traces this library can produce.
        /// </remarks>
        private static Action<TDest, object?> CompileSetter<TDest>(PropertyInfo prop)
        {
            var dest = Expression.Parameter(typeof(TDest), "d");
            var value = Expression.Parameter(typeof(object), "v");

            Expression converted = Expression.Convert(value, prop.PropertyType);
            if (prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) is null)
            {
                converted = Expression.Condition(
                    Expression.ReferenceEqual(value, Expression.Constant(null, typeof(object))),
                    Expression.Default(prop.PropertyType),
                    converted);
            }

            var body = Expression.Assign(Expression.Property(dest, prop), converted);
            return Expression.Lambda<Action<TDest, object?>>(body, dest, value).Compile();
        }

        /// <summary>
        /// A compiled parameterless constructor, or null when the type has none.
        /// </summary>
        /// <remarks>
        /// This was <c>Activator.CreateInstance&lt;TDest&gt;()</c> inside a try/catch, with the
        /// catch doubling as the detection of a type that cannot be constructed that way. Measured
        /// at 37.2 ns against roughly 5 for a compiled new, and using an exception as a type test
        /// meant the cold path for such a type threw and caught on the way to its answer. Asking
        /// for the constructor up front answers the same question without raising anything.
        /// </remarks>
        private static class Constructor<TDest>
        {
            internal static readonly Func<TDest>? Compiled = Build();

            private static Func<TDest>? Build()
            {
                var type = typeof(TDest);
                if (!type.IsValueType && type.GetConstructor(Type.EmptyTypes) is null) return null;
                if (type.IsAbstract || type.IsInterface) return null;

                try
                {
                    return Expression.Lambda<Func<TDest>>(Expression.New(type)).Compile();
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// A destination type that holds many elements, and how to fill it.
        /// </summary>
        /// <remarks>
        /// Resolved once per destination type. The element mapping itself goes back through the
        /// single-object path, so a collection and one object cannot disagree about what the
        /// configuration says: there is one implementation of that and this is not a second one.
        /// </remarks>
        private sealed class CollectionShape
        {
            private static readonly ConcurrentDictionary<Type, CollectionShape?> Shapes = new();

            private readonly Func<FluentMapper, System.Collections.IEnumerable, object> _map;

            private CollectionShape(Func<FluentMapper, System.Collections.IEnumerable, object> map) => _map = map;

            internal object Map(FluentMapper mapper, System.Collections.IEnumerable source) => _map(mapper, source);

            internal static CollectionShape? Of(Type destType) => Shapes.GetOrAdd(destType, Build);

            private static CollectionShape? Build(Type destType)
            {
                if (destType == typeof(string)) return null;

                Type? elementType = null;
                var asArray = false;

                if (destType.IsArray && destType.GetArrayRank() == 1)
                {
                    elementType = destType.GetElementType();
                    asArray = true;
                }
                else if (destType.IsGenericType)
                {
                    var definition = destType.GetGenericTypeDefinition();
                    if (definition == typeof(List<>)
                        || definition == typeof(IEnumerable<>)
                        || definition == typeof(ICollection<>)
                        || definition == typeof(IList<>)
                        || definition == typeof(IReadOnlyCollection<>)
                        || definition == typeof(IReadOnlyList<>))
                    {
                        elementType = destType.GetGenericArguments()[0];
                    }
                }

                if (elementType is null) return null;

                var builder = typeof(CollectionShape)
                    .GetMethod(nameof(BuildFor), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(elementType);

                return (CollectionShape)builder.Invoke(null, new object[] { asArray })!;
            }

            private static CollectionShape BuildFor<TElement>(bool asArray)
            {
                if (asArray)
                {
                    return new CollectionShape(static (mapper, source) => Collect<TElement>(mapper, source).ToArray());
                }
                return new CollectionShape(static (mapper, source) => Collect<TElement>(mapper, source));
            }

            private static List<TElement> Collect<TElement>(FluentMapper mapper, System.Collections.IEnumerable source)
            {
                var result = source is System.Collections.ICollection sized
                    ? new List<TElement>(sized.Count)
                    : new List<TElement>();

                // The configuration for a pair does not change between two elements of one list,
                // so it is looked up when the first element's runtime type is seen and reused
                // while that holds. A list declared List<Animal> may hold a Dog and then a Cat,
                // so the type is still checked per element and an odd one out goes the long way.
                Type? resolvedFor = null;
                ITypeMapConfiguration? typeMap = null;
                var hasConverter = false;

                foreach (var item in source)
                {
                    if (item is null)
                    {
                        result.Add(default!);
                        continue;
                    }

                    var itemType = item.GetType();

                    if (!ReferenceEquals(itemType, resolvedFor))
                    {
                        resolvedFor = itemType;
                        hasConverter = mapper._config.GetTypeConverter(itemType, typeof(TElement)) != null;
                        typeMap = mapper._config.GetTypeMap(itemType, typeof(TElement))
                                  ?? mapper.FindPolymorphicTypeMap(itemType, typeof(TElement));
                    }

                    // A converter replaces the whole mapping, and a nested collection element is
                    // not a shape this loop resolves, so both go back through the entry point.
                    result.Add(hasConverter
                        ? mapper.MapInternal<TElement>(item, itemType)!
                        : mapper.MapResolved<TElement>(item, itemType, typeMap)!);
                }

                return result;
            }
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

}
