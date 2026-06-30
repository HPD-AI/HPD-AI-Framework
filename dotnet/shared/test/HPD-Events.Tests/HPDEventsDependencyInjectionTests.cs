using HPD.Events;
using HPD.Events.Core;
using HPD.Events.DependencyInjection;
using HPD.Events.Struct;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Events.Tests;

public class HPDEventsDependencyInjectionTests
{
    private sealed record TestEvent(string Message) : Event;

    [Fact]
    public void AddHPDEvents_DefaultSingleton_MapsClassEventSurfacesToOneCoordinator()
    {
        var services = new ServiceCollection();

        services.AddHPDEvents();

        using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<EventCoordinator>();

        Assert.Same(coordinator, provider.GetRequiredService<IEventCoordinator>());
        Assert.Same(coordinator, provider.GetRequiredService<IEventBus>());
        Assert.Same(coordinator, provider.GetRequiredService<IEventPublisher>());
        Assert.Same(coordinator, provider.GetRequiredService<IEventObserverBus>());
        Assert.Same(coordinator, provider.GetRequiredService<IEventInboxSource>());
        Assert.Same(coordinator, provider.GetRequiredService<IRequestResponseBus>());
        Assert.Same(coordinator, provider.GetRequiredService<IHierarchicalEventBus>());
        Assert.Same(coordinator.EventFlows, provider.GetRequiredService<IEventFlowRegistry>());
    }

    [Fact]
    public void AddHPDEvents_Scoped_MapsClassEventSurfacesPerScope()
    {
        var services = new ServiceCollection();

        services.AddHPDEvents(options => options.Lifetime = HPDEventsServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstCoordinator = firstScope.ServiceProvider.GetRequiredService<EventCoordinator>();
        var secondCoordinator = secondScope.ServiceProvider.GetRequiredService<EventCoordinator>();

        Assert.Same(firstCoordinator, firstScope.ServiceProvider.GetRequiredService<IEventBus>());
        Assert.Same(firstCoordinator, firstScope.ServiceProvider.GetRequiredService<IEventInboxSource>());
        Assert.NotSame(firstCoordinator, secondCoordinator);
    }

    [Fact]
    public void AddHPDEvents_CanDisableOptionalStructAndStreamRegistrations()
    {
        var services = new ServiceCollection();

        services.AddHPDEvents(options =>
        {
            options.RegisterStructEvents = false;
            options.RegisterEventStreams = false;
        });

        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IStructEventHub>());
        Assert.Null(provider.GetService<IEventStreamSource<TestEvent>>());
    }

    [Fact]
    public void AddHPDEvents_RegistersOptionalStructHub()
    {
        var services = new ServiceCollection();

        services.AddHPDEvents();

        using var provider = services.BuildServiceProvider();
        var hub = provider.GetRequiredService<StructEventHub>();

        Assert.Same(hub, provider.GetRequiredService<IStructEventHub>());
    }

    [Fact]
    public async Task AddHPDEvents_RegistersEventStreamSourceUsingRegisteredCoordinator()
    {
        var services = new ServiceCollection();

        services.AddHPDEvents();

        using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<EventCoordinator>();
        var source = provider.GetRequiredService<IEventStreamSource<TestEvent>>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await source.OpenAsync(new EventStreamRequest<TestEvent>(), cts.Token);
        await using var enumerator = result.Value!.Items.GetAsyncEnumerator(cts.Token);

        coordinator.Emit(new TestEvent("di"));

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("di", enumerator.Current.Message);
    }
}
