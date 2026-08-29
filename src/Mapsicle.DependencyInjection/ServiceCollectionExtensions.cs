using System;
using Microsoft.Extensions.DependencyInjection;

namespace Mapsicle.DependencyInjection
{
    /// <summary>
    /// Registers Mapsicle in a dependency injection container.
    /// </summary>
    /// <remarks>
    /// This package exists because the only registration used to live in Mapsicle.Fluent and both
    /// its overloads required an <c>Action&lt;IMapperConfigurationExpression&gt;</c>. A library
    /// whose whole argument is that no configuration is needed had no way to be registered without
    /// writing some, and it came from a package the core does not depend on.
    ///
    /// It is a separate package rather than part of the core because the core declares no
    /// dependencies, and that is enforced by a CI gate. Taking a reference on
    /// Microsoft.Extensions.DependencyInjection.Abstractions in the core would turn that gate red,
    /// so the door lives next to the core instead of inside it.
    /// </remarks>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a mapper with no configuration, mapping entirely by convention.
        /// </summary>
        /// <remarks>
        /// The registered <see cref="IMapperInstance"/> is a singleton holding its own delegate
        /// cache, which is the point: a mapper compiles a delegate per type pair on first use, so a
        /// scoped or transient registration would throw that work away on every request.
        ///
        /// Nothing needs registering per type pair. An unconfigured pair maps by convention rather
        /// than throwing, so there is no equivalent of AutoMapper's profile scanning to run here
        /// and no startup validation step to forget.
        /// </remarks>
        /// <param name="services">The service collection.</param>
        /// <returns>The same collection, for chaining.</returns>
        public static IServiceCollection AddMapsicle(this IServiceCollection services)
        {
            if (services is null) throw new ArgumentNullException(nameof(services));

            services.AddSingleton<IMapperInstance>(_ => MapperFactory.Create());
            return services;
        }

        /// <summary>
        /// Registers a mapper with tuning options, still with no per-pair configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Sets cache size, maximum depth and the diagnostic logger.</param>
        /// <returns>The same collection, for chaining.</returns>
        public static IServiceCollection AddMapsicle(this IServiceCollection services, Action<MapperOptions> configure)
        {
            if (services is null) throw new ArgumentNullException(nameof(services));
            if (configure is null) throw new ArgumentNullException(nameof(configure));

            services.AddSingleton<IMapperInstance>(_ =>
            {
                var options = new MapperOptions();
                configure(options);
                return MapperFactory.Create(options);
            });
            return services;
        }
    }
}
