using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mapsicle.Fluent
{
    /// <summary>
    /// Dependency Injection extension methods for Mapsicle.Fluent.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds Mapsicle with fluent configuration to the service collection.
        /// </summary>
        /// <remarks>
        /// Calling this more than once merges the configurations: every callback so far runs
        /// against one <see cref="MapperConfiguration"/>, in the order the calls were made.
        /// </remarks>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Configuration action.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddMapsicle(
            this IServiceCollection services,
            Action<IMapperConfigurationExpression> configure) =>
            AddMapsicle(services, configure, validateConfiguration: false);

        /// <summary>
        /// Adds Mapsicle with fluent configuration and optional validation.
        /// </summary>
        /// <remarks>
        /// Validation covers the merged configuration, including maps from earlier calls, and
        /// once any call asks for it every later call validates too.
        /// </remarks>
        public static IServiceCollection AddMapsicle(
            this IServiceCollection services,
            Action<IMapperConfigurationExpression> configure,
            bool validateConfiguration)
        {
            // Each call used to register its own configuration and mapper, and the container hands
            // out the last registration, so a second call silently dropped the first call's maps,
            // including an Ignore keeping a password out of a DTO.
            var registrations = services
                .FirstOrDefault(d => d.ServiceType == typeof(FluentRegistrations))
                ?.ImplementationInstance as FluentRegistrations;

            if (registrations is null)
            {
                registrations = new FluentRegistrations();
                services.AddSingleton(registrations);
            }

            var callbacks = new List<Action<IMapperConfigurationExpression>>(registrations.Callbacks) { configure };
            var validate = registrations.Validate || validateConfiguration;

            var config = new MapperConfiguration(cfg =>
            {
                foreach (var callback in callbacks) callback(cfg);
            });

            if (validate)
            {
                config.AssertConfigurationIsValid();
            }

            registrations.Callbacks.Add(configure);
            registrations.Validate = validate;

            services.RemoveAll<MapperConfiguration>();
            services.RemoveAll<IMapper>();
            services.AddSingleton(config);
            services.AddSingleton<IMapper>(config.CreateMapper());
            return services;
        }

        private sealed class FluentRegistrations
        {
            internal readonly List<Action<IMapperConfigurationExpression>> Callbacks = new();
            internal bool Validate;
        }
    }
}
