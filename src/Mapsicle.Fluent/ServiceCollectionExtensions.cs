using System;
using Microsoft.Extensions.DependencyInjection;

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
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Configuration action.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddMapsicle(
            this IServiceCollection services,
            Action<IMapperConfigurationExpression> configure)
        {
            var config = new MapperConfiguration(configure);
            services.AddSingleton(config);
            services.AddSingleton<IMapper>(config.CreateMapper());
            return services;
        }

        /// <summary>
        /// Adds Mapsicle with fluent configuration and optional validation.
        /// </summary>
        public static IServiceCollection AddMapsicle(
            this IServiceCollection services,
            Action<IMapperConfigurationExpression> configure,
            bool validateConfiguration)
        {
            var config = new MapperConfiguration(configure);
            if (validateConfiguration)
            {
                config.AssertConfigurationIsValid();
            }
            services.AddSingleton(config);
            services.AddSingleton<IMapper>(config.CreateMapper());
            return services;
        }
    }
}
