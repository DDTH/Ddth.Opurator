using Microsoft.Extensions.DependencyInjection;

namespace Ddth.Opurator.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddOpurator_RegistersManagerWithoutConfiguration()
    {
        var services = new ServiceCollection();

        var result = services.AddOpurator();

        Assert.Same(services, result);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IBackgroundTaskManager)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public async Task AddOpurator_RegistersConfiguredSingleton()
    {
        var services = new ServiceCollection();
        services.AddOpurator(options => options.MaxConcurrency = 3);

        await using var serviceProvider = services.BuildServiceProvider();

        var first = serviceProvider.GetRequiredService<IBackgroundTaskManager>();
        var second = serviceProvider.GetRequiredService<IBackgroundTaskManager>();

        Assert.Same(first, second);
        Assert.Equal(3, first.MaxConcurrency);
    }

    [Fact]
    public async Task AddOpurator_DoesNotReplaceExistingRegistration()
    {
        await using var existing = new BackgroundTaskManager(
            new BackgroundTaskManagerOptions
            {
                MaxConcurrency = 2
            });
        var services = new ServiceCollection();
        services.AddSingleton<IBackgroundTaskManager>(existing);
        services.AddOpurator(options => options.MaxConcurrency = 3);

        await using var serviceProvider = services.BuildServiceProvider();

        Assert.Same(
            existing,
            serviceProvider.GetRequiredService<IBackgroundTaskManager>());
    }

    [Fact]
    public async Task ServiceProviderDisposal_ShutsDownManager()
    {
        var services = new ServiceCollection();
        services.AddOpurator(options => options.MaxConcurrency = 2);
        var serviceProvider = services.BuildServiceProvider();
        var manager = serviceProvider.GetRequiredService<IBackgroundTaskManager>();

        await serviceProvider.DisposeAsync();

        Assert.Throws<InvalidOperationException>(
            () => manager.RunOnce(_ => Task.CompletedTask));
    }
}
