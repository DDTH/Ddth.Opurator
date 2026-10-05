using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ddth.Opurator;

/// <summary>
/// Provides dependency injection registration for Ddth.Opurator.
/// </summary>
public static class OpuratorServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IBackgroundTaskManager"/>.
    /// </summary>
    /// <param name="services">The service collection to add the manager to.</param>
    /// <param name="configure">An optional action that configures the manager.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddOpurator(
        this IServiceCollection services,
        Action<BackgroundTaskManagerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<BackgroundTaskManagerOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton<IBackgroundTaskManager>(
            serviceProvider => new BackgroundTaskManager(
                serviceProvider
                    .GetRequiredService<IOptions<BackgroundTaskManagerOptions>>()
                    .Value));

        return services;
    }
}
