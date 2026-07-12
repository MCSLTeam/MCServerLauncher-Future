using MCServerLauncher.Daemon.ApplicationCore.Events;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCServerLauncher.ProtocolTests;

internal sealed class DomainEventPortTestHost : IDisposable
{
    private readonly ServiceProvider _services;

    private DomainEventPortTestHost(ServiceProvider services)
    {
        _services = services;
        Port = services.GetRequiredService<DomainEventPort>();
    }

    internal DomainEventPort Port { get; }

    internal static DomainEventPortTestHost Create(
        ILogger<DomainEventPort>? logger = null,
        DomainEventDispatchPolicy? policy = null)
    {
        var services = new ServiceCollection();
        services.AddMessagePipe(options =>
        {
            options.EnableAutoRegistration = false;
            options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
            options.InstanceLifetime = InstanceLifetime.Singleton;
            options.EnableCaptureStackTrace = false;
        });
        services.AddSingleton(logger ?? NullLogger<DomainEventPort>.Instance);
        services.AddSingleton(policy ?? DomainEventDispatchPolicy.Default);
        services.AddSingleton<DomainEventPort>();
        return new DomainEventPortTestHost(services.BuildServiceProvider());
    }

    public void Dispose()
    {
        _services.Dispose();
    }
}
