using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Events;
using HPD.Events.Core;
using HPD.Events.DependencyInjection;
using HPD.Events.Signals;
using HPD.Events.Struct;
using Microsoft.Extensions.DependencyInjection;

await Smoke.ClassBusInboxAndStreamAsync();
await Smoke.RequestResponseAsync();
await Smoke.ReplayAndStoreAsync();
await Smoke.StructEventsAsync();
await Smoke.SignalsAndMailboxAsync();
Smoke.SourceGeneratedJson();
Smoke.DependencyInjection();
Smoke.EventShape();

Console.WriteLine("HPD.Events AOT smoke passed.");

internal static class Smoke
{
    public static async Task ClassBusInboxAndStreamAsync()
    {
        using var coordinator = new EventCoordinator();
        await using var inbox = coordinator.CreateInbox<SmokeEvent>();
        var streamSource = new EventStreamSource<SmokeEvent>(coordinator);
        var stream = await streamSource.OpenAsync(new EventStreamRequest<SmokeEvent>
        {
            StreamId = "aot.smoke",
            Capacity = 8
        });

        Ensure(stream.Succeeded && stream.Value is not null, "event stream opened");
        await using var enumerator = stream.Value.Items.GetAsyncEnumerator();

        coordinator.Emit(new SmokeEvent { Name = "class-bus" });

        var inboxEvent = await ReadOneAsync(inbox.Reader.ReadAllAsync());
        Ensure(inboxEvent.Name == "class-bus", "inbox receives class event");
        Ensure(await enumerator.MoveNextAsync(), "stream receives class event");
        Ensure(enumerator.Current.Name == "class-bus", "stream event payload");
    }

    public static async Task RequestResponseAsync()
    {
        using var coordinator = new EventCoordinator();
        using var subscription = coordinator.Subscribe<SmokeRequest>(request =>
        {
            coordinator.Respond(new SmokeResponse
            {
                RequestId = request.RequestId,
                SourceName = request.SourceName,
                Answer = "ok"
            });

            return ValueTask.CompletedTask;
        });

        var response = await coordinator.RequestAsync<SmokeRequest, SmokeResponse>(
            new SmokeRequest
            {
                RequestId = "request-1",
                SourceName = "aot",
                Question = "ready?"
            },
            TimeSpan.FromSeconds(5));

        Ensure(response.Answer == "ok", "request/response roundtrip");
    }

    public static async Task ReplayAndStoreAsync()
    {
        var store = new InMemoryEventStore<SmokeEvent>();
        await store.AppendAsync(new SmokeEvent
        {
            Name = "store",
            Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(2)
        });

        var timeline = ReplayTimeline<SmokeEvent>
            .Create()
            .AddSource("inline", [new SmokeEvent
            {
                Name = "inline",
                Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(1)
            }])
            .AddSource("store", store);

        var names = new List<string>();
        await foreach (var evt in timeline.ReadAsync(ReplayReadOptions.All))
            names.Add(evt.Name);

        Ensure(names.Count == 2, "replay count");
        Ensure(names[0] == "inline" && names[1] == "store", "replay ordering");
    }

    public static async Task StructEventsAsync()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<SmokeStructEvent>();
        using var inbox = route.CreateInbox();
        var emitter = route.CreateEmitter();

        var result = emitter.Emit(new SmokeStructEvent(42));

        Ensure(result.Status == StructEventEmitStatus.Accepted, "struct event accepted");
        Ensure(inbox.TryRead(out var evt) && evt.Value == 42, "struct event payload");
    }

    public static async Task SignalsAndMailboxAsync()
    {
        var signal = new EventSignal();
        signal.Signal();
        await signal.WaitAsync();
        Ensure(signal.TryConsume(), "signal consumed");

        await using var mailbox = new EventLoopMailbox<int>();
        Ensure(mailbox.TryWrite(7), "mailbox write");
        await mailbox.WaitToReadAsync();
        Ensure(mailbox.TryRead(out var value) && value == 7, "mailbox read");
    }

    public static void SourceGeneratedJson()
    {
        var evt = new AnnotatedSmokeEvent
        {
            Name = "json",
            Annotations =
            [
                new EventAnnotation
                {
                    Key = "visible",
                    Value = EventAnnotationValue.FromBoolean(true),
                    Visibility = EventAnnotationVisibility.Public
                }
            ]
        };

        var json = JsonSerializer.Serialize(
            evt,
            SmokeJsonContext.Default.AnnotatedSmokeEvent);
        var roundtrip = JsonSerializer.Deserialize(
            json,
            SmokeJsonContext.Default.AnnotatedSmokeEvent);

        Ensure(roundtrip is not null, "annotated event JSON roundtrip");
        Ensure(roundtrip.Annotations.Count == 1, "annotation count");
        Ensure(roundtrip.Annotations[0].Value.Boolean == true, "annotation value");
    }

    public static void DependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddHPDEvents(options =>
        {
            options.Lifetime = HPDEventsServiceLifetime.Singleton;
            options.RegisterStructEvents = true;
            options.RegisterEventStreams = true;
        });

        using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<EventCoordinator>();

        Ensure(ReferenceEquals(coordinator, provider.GetRequiredService<IEventCoordinator>()), "DI coordinator surface");
        Ensure(ReferenceEquals(coordinator, provider.GetRequiredService<IEventBus>()), "DI bus surface");
        Ensure(provider.GetRequiredService<IEventStreamSource<SmokeEvent>>() is not null, "DI event stream source");
        Ensure(provider.GetRequiredService<IStructEventHub>() is not null, "DI struct hub");
    }

    public static void EventShape()
    {
        Ensure(typeof(Event).GetProperty("Extensions") is null, "Event has no Extensions property");
    }

    private static async Task<T> ReadOneAsync<T>(IAsyncEnumerable<T> items)
    {
        await foreach (var item in items)
            return item;

        throw new InvalidOperationException("The stream ended before producing an item.");
    }

    private static void Ensure(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"HPD.Events AOT smoke failed: {name}.");
    }
}

internal sealed record SmokeEvent : Event
{
    public required string Name { get; init; }
}

internal sealed record AnnotatedSmokeEvent : Event, IAnnotatedEvent
{
    public required string Name { get; init; }

    public IReadOnlyList<EventAnnotation> Annotations { get; init; } = [];
}

internal sealed record SmokeRequest : Event, IRequestEvent
{
    public required string RequestId { get; init; }

    public required string SourceName { get; init; }

    public required string Question { get; init; }
}

internal sealed record SmokeResponse : Event, IResponseEvent
{
    public required string RequestId { get; init; }

    public required string SourceName { get; init; }

    public required string Answer { get; init; }
}

internal readonly record struct SmokeStructEvent(int Value) : IStructEvent
{
    public EventKind Kind => EventKind.Diagnostic;

    public long SequenceNumber => 0;

    public long TimestampNs => 0;
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(AnnotatedSmokeEvent))]
internal sealed partial class SmokeJsonContext : JsonSerializerContext;
