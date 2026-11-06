using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using System.Text.Json.Serialization;

namespace HPD.Agent.ToolHarness.AotFixture;

public sealed record ExternalExecutionSettings
{
    [HarnessRunSetting]
    public int Value { get; init; } = 1;
}

[Collapse(
    "Cross-assembly Native AOT harness",
    SystemPrompt = "Use the configured AOT harness value.",
    FunctionResult = "expanded",
    Middlewares = [typeof(ExternalExecutionMiddleware)])]
public partial class ExternalExecutionHarness(IHarnessConfig<ExternalExecutionSettings> configuration)
{
    static ExternalExecutionHarness()
    {
        HpdAgentJsonUtilities.RegisterTypeInfoResolver(AotToolOutputJsonContext.Default);
    }

    public static int LastConfiguredValue { get; private set; }

    [AIFunction]
    public string Ping() => "pong";

    [AIFunction]
    public string ConfiguredValue(FunctionExecutionContext context)
    {
        var value = configuration.For(context).Value;
        LastConfiguredValue = value;
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [AIFunction]
    public AotOffloadResult OffloadEvidence(FunctionExecutionContext context)
    {
        var capture = context.Outputs.CreatePart(
            "capture",
            "text/plain",
            ContentSensitivity.Internal);
        capture.WriteAsync("EVIDENCE-AOT"u8.ToArray()).AsTask().GetAwaiter().GetResult();
        return new AotOffloadResult("AOT offload summary", "EVIDENCE-AOT", capture);
    }
}

/// <summary>Cross-assembly AOT offload result.</summary>
public sealed record AotOffloadResult(
    string Summary,
    [property: Offload] string Evidence,
    [property: Offload] ContentCapturePart Capture);

/// <summary>Source-generated metadata for AOT offload results.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    IncludeFields = true)]
[JsonSerializable(typeof(AotOffloadResult))]
public partial class AotToolOutputJsonContext : JsonSerializerContext;

public sealed partial class DerivedExecutionHarness(
    IHarnessConfig<ExternalExecutionSettings> configuration)
    : ExternalExecutionHarness(configuration)
{
    [AIFunction]
    [CollapseFunction(
        "Expand to use the derived AOT function.",
        FunctionResult = "DerivedPing is now available.")]
    public string DerivedPing() => Ping() + "-derived";
}

public sealed class ExternalExecutionMiddleware
    : IToolHarnessMiddleware, IAsyncDisposable
{
    private static int _created;
    private static int _activated;
    private static int _disposed;

    public ExternalExecutionMiddleware() => Interlocked.Increment(ref _created);

    public static int CreatedCount => Volatile.Read(ref _created);
    public static int ActivatedCount => Volatile.Read(ref _activated);
    public static int DisposedCount => Volatile.Read(ref _disposed);

    public ValueTask OnHarnessActivatedAsync(
        ToolHarnessActivationContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activated);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnHarnessDeactivatingAsync(
        ToolHarnessDeactivationContext context,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposed);
        return ValueTask.CompletedTask;
    }
}

[ToolHarnessMiddleware(typeof(ExternalEagerMiddleware))]
public sealed class ExternalEagerHarness
{
    [AIFunction]
    public string EagerPing() => "pong";
}

public sealed class ExternalEagerMiddleware : IToolHarnessMiddleware
{
    private static int _activated;
    private static int _turned;
    private static int _deactivated;

    public static int ActivatedCount => Volatile.Read(ref _activated);
    public static int TurnedCount => Volatile.Read(ref _turned);
    public static int DeactivatedCount => Volatile.Read(ref _deactivated);

    public ValueTask OnHarnessActivatedAsync(
        ToolHarnessActivationContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activated);
        return ValueTask.CompletedTask;
    }

    public Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _turned);
        return Task.CompletedTask;
    }

    public ValueTask OnHarnessDeactivatingAsync(
        ToolHarnessDeactivationContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _deactivated);
        return ValueTask.CompletedTask;
    }
}
