using HPD.Events.Core;
using HPD.Events.Struct;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Events.DependencyInjection;

/// <summary>
/// Service collection extensions for HPD.Events.
/// </summary>
public static class HPDEventsServiceCollectionExtensions
{
    /// <summary>
    /// Register HPD.Events with default singleton class-event coordination.
    /// </summary>
    public static IServiceCollection AddHPDEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddHPDEvents(_ => { });
    }

    /// <summary>
    /// Register HPD.Events with explicit options.
    /// </summary>
    public static IServiceCollection AddHPDEvents(
        this IServiceCollection services,
        Action<HPDEventsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new HPDEventsOptions();
        configure(options);

        var lifetime = ToServiceLifetime(options.Lifetime);
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(EventCoordinator),
            typeof(EventCoordinator),
            lifetime));

        services.TryAdd(ServiceDescriptor.Describe(
            typeof(IEventCoordinator),
            sp => sp.GetRequiredService<EventCoordinator>(),
            lifetime));
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(IEventBus),
            sp => sp.GetRequiredService<EventCoordinator>(),
            lifetime));
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(IEventPublisher),
            sp => sp.GetRequiredService<EventCoordinator>(),
            lifetime));
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(IEventObserverBus),
            sp => sp.GetRequiredService<EventCoordinator>(),
            lifetime));
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(IEventInboxSource),
            sp => sp.GetRequiredService<EventCoordinator>(),
            lifetime));
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(IRequestResponseBus),
            sp => sp.GetRequiredService<EventCoordinator>(),
            lifetime));
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(IHierarchicalEventBus),
            sp => sp.GetRequiredService<EventCoordinator>(),
            lifetime));
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(IEventFlowRegistry),
            sp => sp.GetRequiredService<EventCoordinator>().EventFlows,
            lifetime));

        if (options.RegisterStructEvents)
        {
            services.TryAdd(ServiceDescriptor.Describe(
                typeof(StructEventHub),
                typeof(StructEventHub),
                lifetime));
            services.TryAdd(ServiceDescriptor.Describe(
                typeof(IStructEventHub),
                sp => sp.GetRequiredService<StructEventHub>(),
                lifetime));
        }

        if (options.RegisterEventStreams)
        {
            services.TryAdd(ServiceDescriptor.Describe(
                typeof(IEventStreamSource<>),
                typeof(EventStreamSource<>),
                lifetime));
        }

        return services;
    }

    private static ServiceLifetime ToServiceLifetime(HPDEventsServiceLifetime lifetime) =>
        lifetime switch
        {
            HPDEventsServiceLifetime.Singleton => ServiceLifetime.Singleton,
            HPDEventsServiceLifetime.Scoped => ServiceLifetime.Scoped,
            HPDEventsServiceLifetime.Transient => ServiceLifetime.Transient,
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unknown HPD.Events service lifetime.")
        };
}
