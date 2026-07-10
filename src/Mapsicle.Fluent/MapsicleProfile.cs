using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Mapsicle.Fluent
{
    /// <summary>
    /// Base class for organizing mapping configurations into reusable profiles.
    /// Similar to AutoMapper's Profile concept.
    /// </summary>
    public abstract class MapsicleProfile
    {
        private readonly List<Action<IMapperConfigurationExpression>> _configurations = new();

        /// <summary>
        /// Override this method to configure mappings for this profile.
        /// </summary>
        protected abstract void Configure();

        /// <summary>
        /// Creates a mapping between source and destination types.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <returns>The type map expression for further configuration.</returns>
        protected ITypeMapExpression<TSource, TDest> CreateMap<TSource, TDest>()
        {
            // Don't add initial action — DeferredTypeMapExpression owns the registration entirely
            return new DeferredTypeMapExpression<TSource, TDest>(_configurations);
        }

        /// <summary>
        /// Creates a global type converter.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="converter">The conversion function.</param>
        protected void CreateConverter<TSource, TDest>(Func<TSource, TDest> converter)
        {
            _configurations.Add(cfg => cfg.CreateConverter(converter));
        }

        /// <summary>
        /// Applies this profile's configuration to the given configuration expression.
        /// Called internally by MapperConfiguration.
        /// </summary>
        internal void ApplyTo(IMapperConfigurationExpression cfg)
        {
            Configure();
            foreach (var config in _configurations)
            {
                config(cfg);
            }
        }
    }

    /// <summary>
    /// A deferred type map expression that collects configuration until ApplyTo is called.
    /// </summary>
    internal class DeferredTypeMapExpression<TSource, TDest> : ITypeMapExpression<TSource, TDest>
    {
        private readonly List<Action<IMapperConfigurationExpression>> _configurations;
        private readonly List<Action<ITypeMapExpression<TSource, TDest>>> _deferredActions = new();

        public DeferredTypeMapExpression(List<Action<IMapperConfigurationExpression>> configurations)
            : this(configurations, register: true)
        {
        }

        private DeferredTypeMapExpression(List<Action<IMapperConfigurationExpression>> configurations, bool register)
        {
            _configurations = configurations;
            if (register)
            {
                // Single action that does both CreateMap + all deferred actions — no removal needed
                _configurations.Add(cfg => Apply(cfg.CreateMap<TSource, TDest>()));
            }
        }

        private void Apply(ITypeMapExpression<TSource, TDest> typeMap)
        {
            foreach (var action in _deferredActions)
            {
                action(typeMap);
            }
        }

        public ITypeMapExpression<TSource, TDest> ForMember<TMember>(
            System.Linq.Expressions.Expression<Func<TDest, TMember>> destinationMember,
            Action<IMemberConfigurationExpression<TSource, TDest, TMember>> memberOptions)
        {
            _deferredActions.Add(typeMap => typeMap.ForMember(destinationMember, memberOptions));
            return this;
        }

        public ITypeMapExpression<TSource, TDest> ForAllMembers(
            Action<IMemberConfigurationExpression<TSource, TDest, object>> memberOptions)
        {
            _deferredActions.Add(typeMap => typeMap.ForAllMembers(memberOptions));
            return this;
        }

        public ITypeMapExpression<TSource, TDest> BeforeMap(Action<TSource, TDest> action)
        {
            _deferredActions.Add(typeMap => typeMap.BeforeMap(action));
            return this;
        }

        public ITypeMapExpression<TSource, TDest> AfterMap(Action<TSource, TDest> action)
        {
            _deferredActions.Add(typeMap => typeMap.AfterMap(action));
            return this;
        }

        public ITypeMapExpression<TSource, TDest> Include<TDerivedSource, TDerivedDest>()
            where TDerivedSource : TSource
            where TDerivedDest : TDest
        {
            _deferredActions.Add(typeMap => typeMap.Include<TDerivedSource, TDerivedDest>());
            return this;
        }

        public ITypeMapExpression<TSource, TDest> ConstructUsing(Func<TSource, TDest> factory)
        {
            _deferredActions.Add(typeMap => typeMap.ConstructUsing(factory));
            return this;
        }

        public ITypeMapExpression<TDest, TSource> ReverseMap()
        {
            // The returned expression must configure the map produced by typeMap.ReverseMap();
            // registering a second CreateMap<TDest, TSource> here would leave an unconfigured
            // duplicate in the configuration that fails AssertConfigurationIsValid.
            var reverse = new DeferredTypeMapExpression<TDest, TSource>(_configurations, register: false);
            _deferredActions.Add(typeMap => reverse.Apply(typeMap.ReverseMap()));
            return reverse;
        }
    }

    /// <summary>
    /// Extension methods for adding profile support to MapperConfiguration.
    /// </summary>
    public static class ProfileExtensions
    {
        /// <summary>
        /// Adds a profile to the configuration.
        /// </summary>
        /// <typeparam name="TProfile">The profile type.</typeparam>
        /// <param name="cfg">The configuration expression.</param>
        public static void AddProfile<TProfile>(this IMapperConfigurationExpression cfg)
            where TProfile : MapsicleProfile, new()
        {
            var profile = new TProfile();
            profile.ApplyTo(cfg);
        }

        /// <summary>
        /// Adds a profile instance to the configuration.
        /// </summary>
        /// <param name="cfg">The configuration expression.</param>
        /// <param name="profile">The profile instance.</param>
        public static void AddProfile(this IMapperConfigurationExpression cfg, MapsicleProfile profile)
        {
            profile.ApplyTo(cfg);
        }

        /// <summary>
        /// Adds all profiles from an assembly.
        /// </summary>
        /// <param name="cfg">The configuration expression.</param>
        /// <param name="assembly">The assembly to scan.</param>
        public static void AddProfiles(this IMapperConfigurationExpression cfg, Assembly assembly)
        {
            var profileTypes = assembly.GetTypes()
                .Where(t => typeof(MapsicleProfile).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null);

            foreach (var profileType in profileTypes)
            {
                var profile = (MapsicleProfile)Activator.CreateInstance(profileType)!;
                profile.ApplyTo(cfg);
            }
        }

        /// <summary>
        /// Adds all profiles from the assembly containing the specified type.
        /// </summary>
        /// <typeparam name="T">A type in the assembly to scan.</typeparam>
        /// <param name="cfg">The configuration expression.</param>
        public static void AddProfilesFromAssemblyOf<T>(this IMapperConfigurationExpression cfg)
        {
            cfg.AddProfiles(typeof(T).Assembly);
        }
    }
}
