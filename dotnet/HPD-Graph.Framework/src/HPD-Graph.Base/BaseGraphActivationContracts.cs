using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Graph.Abstractions.Artifacts;
using HPD.Graph.Abstractions.Config;
using HPD.Graph.Abstractions.Invocation;
using HPD.Graph.Abstractions.Registry;
using HPD.Graph.Core.Config;
using HPD.Graph.Core.Context;
using HPD.Graph.Core.Orchestration;

namespace HPD.Graph.Base;

/// <summary>Contains one closed durable graph-execution activation input.</summary>
public sealed record BaseGraphActivationInput
{
    /// <summary>Gets the exact graph definition identity.</summary>
    public required string GraphId { get; init; }
    /// <summary>Gets the exact graph semantic version.</summary>
    public required string GraphVersion { get; init; }
    /// <summary>Gets the SHA-256 checksum of the installed graph definition.</summary>
    public required ImmutableArray<byte> GraphChecksum { get; init; }
    /// <summary>Gets the stable logical execution identity.</summary>
    public required string ExecutionId { get; init; }
    /// <summary>Gets canonical graph input encoded as UTF-8 JSON.</summary>
    public required ImmutableArray<byte> CanonicalInput { get; init; }
    /// <summary>Gets the optional logical interval start as Unix milliseconds.</summary>
    public long? LogicalIntervalStart { get; init; }
    /// <summary>Gets the optional logical interval end as Unix milliseconds.</summary>
    public long? LogicalIntervalEnd { get; init; }
    /// <summary>Gets the optional authoritative checkpoint identity used for resumption.</summary>
    public string? CheckpointId { get; init; }
}

/// <summary>Contains one closed durable graph-execution activation result.</summary>
public sealed record BaseGraphActivationResult
{
    /// <summary>Gets the stable logical execution identity.</summary>
    public required string ExecutionId { get; init; }
    /// <summary>Gets the terminal graph execution state.</summary>
    public required BaseGraphActivationOutcome Outcome { get; init; }
    /// <summary>Gets the canonical checksum of completed node identities.</summary>
    public required ImmutableArray<byte> CompletedNodesChecksum { get; init; }
}

/// <summary>Defines the terminal outcome of one graph activation.</summary>
public enum BaseGraphActivationOutcome
{
    /// <summary>The graph completed successfully.</summary>
    Succeeded = 0,
}

/// <summary>Contains one sealed graph activation registration and its input authority.</summary>
public sealed class BaseGraphActivationDefinition
{
    internal BaseGraphActivationDefinition(
        BaseActivationHandlerRegistration<BaseGraphActivationInput, BaseGraphActivationResult> registration,
        byte[] graphChecksum,
        string graphId,
        string graphVersion)
    {
        Registration = registration;
        GraphChecksum = graphChecksum.ToArray();
        GraphId = new string(graphId.AsSpan());
        GraphVersion = new string(graphVersion.AsSpan());
    }

    /// <summary>Gets the graph-owned activation registration installed into HPD.Base.</summary>
    public BaseActivationHandlerRegistration<BaseGraphActivationInput, BaseGraphActivationResult> Registration { get; }
    /// <summary>Gets the exact installed graph checksum.</summary>
    public ReadOnlyMemory<byte> GraphChecksum { get; }
    /// <summary>Gets the exact graph identity.</summary>
    public string GraphId { get; }
    /// <summary>Gets the exact graph semantic version.</summary>
    public string GraphVersion { get; }

    /// <summary>Creates one graph-bound activation input from canonical UTF-8 JSON.</summary>
    public BaseGraphActivationInput CreateInput(
        string executionId,
        ReadOnlyMemory<byte> canonicalInput,
        long? logicalIntervalStart = null,
        long? logicalIntervalEnd = null,
        string? checkpointId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        byte[] normalized = BaseGraphActivationRegistration.CanonicalJson(canonicalInput.Span);
        if (!normalized.AsSpan().SequenceEqual(canonicalInput.Span))
            throw new InvalidOperationException("hpd.graph.activation.inputNotCanonical");
        if (logicalIntervalStart is < 0 || logicalIntervalEnd is < 0
            || logicalIntervalStart is not null && logicalIntervalEnd is not null
                && logicalIntervalStart > logicalIntervalEnd)
            throw new InvalidOperationException("hpd.graph.activation.intervalInvalid");
        return new BaseGraphActivationInput
        {
            GraphId = GraphId,
            GraphVersion = GraphVersion,
            GraphChecksum = GraphChecksum.ToArray().ToImmutableArray(),
            ExecutionId = new string(executionId.AsSpan()),
            CanonicalInput = canonicalInput.ToArray().ToImmutableArray(),
            LogicalIntervalStart = logicalIntervalStart,
            LogicalIntervalEnd = logicalIntervalEnd,
            CheckpointId = checkpointId is null ? null : new string(checkpointId.AsSpan()),
        };
    }

    /// <summary>Creates one durable Base schedule targeting this exact graph activation.</summary>
    public BaseScheduleDefinition CreateSchedule(
        GraphScheduleConfig schedule,
        string scheduleId,
        int scheduleVersion,
        string manageGrantId,
        string materializeGrantId,
        int priority = 0,
        long maximumSplayMilliseconds = 0)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scheduleVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(manageGrantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(materializeGrantId);
        byte[] seedBytes = schedule.DefaultInput is { } seed
            ? JsonSerializer.SerializeToUtf8Bytes(seed)
            : "{}"u8.ToArray();
        byte[] canonicalSeed = BaseGraphActivationRegistration.CanonicalJson(seedBytes);
        BaseGraphActivationInput input = CreateInput($"schedule:{scheduleId}", canonicalSeed);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(input, BaseGraphActivationJsonContext.Default.BaseGraphActivationInput);
        BaseScheduleMisfirePolicy misfire = schedule.MisfirePolicy switch
        {
            ScheduleMisfirePolicyConfig.Skip => BaseScheduleMisfirePolicy.Skip,
            ScheduleMisfirePolicyConfig.RunOnce => BaseScheduleMisfirePolicy.RunLatest,
            ScheduleMisfirePolicyConfig.RunAllMissed => BaseScheduleMisfirePolicy.RunAll,
            _ => throw new InvalidOperationException("hpd.graph.activation.scheduleInvalid"),
        };
        BaseScheduleOverlapPolicy overlap = schedule.ConcurrencyPolicy switch
        {
            ScheduleConcurrencyPolicyConfig.AllowOverlap => BaseScheduleOverlapPolicy.Allow,
            ScheduleConcurrencyPolicyConfig.SkipIfRunning => BaseScheduleOverlapPolicy.SkipWhileActive,
            ScheduleConcurrencyPolicyConfig.Queue => BaseScheduleOverlapPolicy.Queue,
            ScheduleConcurrencyPolicyConfig.CancelPrevious => BaseScheduleOverlapPolicy.CancelPrevious,
            _ => throw new InvalidOperationException("hpd.graph.activation.scheduleInvalid"),
        };
        return BaseScheduleDefinitionBuilder.Create(new BaseScheduleDefinition
        {
            Id = scheduleId,
            Version = scheduleVersion,
            OwningModuleId = "hpd.graph",
            ManageGrantId = manageGrantId,
            MaterializeGrantId = materializeGrantId,
            Activation = new BaseActivationDefinitionKey
            {
                Id = Registration.Definition.Id,
                Version = Registration.Definition.Version,
                Checksum = Registration.Definition.Checksum.ToArray().ToImmutableArray(),
            },
            CanonicalInput = bytes.ToImmutableArray(),
            InputChecksum = SHA256.HashData(bytes).ToImmutableArray(),
            Expression = new BaseCronSchedule(schedule.CronExpression, schedule.TimeZoneId),
            GapPolicy = BaseTimeGapPolicy.NextValid,
            TimeOverlapPolicy = BaseTimeOverlapPolicy.Both,
            MisfirePolicy = misfire,
            ActivationOverlapPolicy = overlap,
            OverlapKeyKind = BaseScheduleOverlapKeyKind.Schedule,
            Priority = priority,
            MaximumSplayMilliseconds = maximumSplayMilliseconds,
            Checksum = [],
        });
    }
}

/// <summary>Builds exact HPD-Graph activation registrations for HPD.Base.</summary>
public static class BaseGraphActivationRegistration
{
    /// <summary>Creates one graph-version-bound activation and Native-AOT-safe handler factory.</summary>
    public static BaseGraphActivationDefinition Create(
        GraphConfig graph,
        int definitionVersion,
        BaseActivationGrantSet grants,
        BaseActivationLimits limits,
        ImmutableArray<string> sourceGrantIds)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(definitionVersion);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(limits);
        byte[] graphBytes = CanonicalGraphBytes(graph);
        byte[] graphChecksum = SHA256.HashData(graphBytes);
        string definitionId = $"hpd.graph.execute.{graph.GraphId}";
        byte[] handlerChecksum = HandlerChecksum(graph.GraphId, graph.GraphVersion, graphChecksum);
        var definition = new BaseActivationDefinition
        {
            Id = definitionId,
            Version = definitionVersion,
            OwningModuleId = "hpd.graph",
            ExecutionClass = BaseActivationExecutionClass.AtLeastOnceWorker,
            InputTypeId = "hpd.graph.activation.input",
            ResultTypeId = "hpd.graph.activation.result",
            Grants = grants,
            SourceGrantIds = sourceGrantIds,
            Retry = new BaseActivationRetryProfile
            {
                MaximumAttempts = limits.MaximumAttempts,
                InitialDelayMilliseconds = 1_000,
                MaximumDelayMilliseconds = 300_000,
                MultiplierNumerator = 2,
                MultiplierDenominator = 1,
                JitterBasisPoints = 1_000,
                RetryableFailureCodes = ["hpd.graph.execution.transient"],
            },
            Limits = limits,
            Handler = new BaseActivationHandlerBinding
            {
                Id = "hpd.graph.execution-handler",
                Version = 1,
                FactoryId = $"hpd.graph.execution-handler.{graph.GraphId}.{graph.GraphVersion}",
                InputTypeId = "hpd.graph.activation.input",
                ResultTypeId = "hpd.graph.activation.result",
                WorkerSubjectKind = AccessSubjectKind.ServicePrincipal,
                Checksum = handlerChecksum.ToImmutableArray(),
            },
            Checksum = [],
        };
        GraphConfig retained = JsonSerializer.Deserialize(
            graphBytes, HPD.Graph.Abstractions.Serialization.GraphConfigJsonSerializerContext.Default.GraphConfig)
            ?? throw new InvalidOperationException("hpd.graph.activation.definitionInvalid");
        BaseActivationHandlerRegistration<BaseGraphActivationInput, BaseGraphActivationResult> registration = BaseActivationDefinitionBuilder.Create(
            definition,
            BaseGraphActivationJsonContext.Default.BaseGraphActivationInput,
            BaseGraphActivationJsonContext.Default.BaseGraphActivationResult,
            services => new BaseGraphActivationHandler(services, retained, graphChecksum));
        return new BaseGraphActivationDefinition(registration, graphChecksum, graph.GraphId, graph.GraphVersion);
    }

    private static byte[] HandlerChecksum(string id, string version, byte[] graphChecksum)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd.graph.activation.handler.v1\0"u8);
        hash.AppendData(Encoding.UTF8.GetBytes(id));
        hash.AppendData([0]);
        hash.AppendData(Encoding.UTF8.GetBytes(version));
        hash.AppendData([0]);
        hash.AppendData(graphChecksum);
        return hash.GetHashAndReset();
    }

    private static byte[] CanonicalGraphBytes(GraphConfig graph)
    {
        byte[] source = JsonSerializer.SerializeToUtf8Bytes(
            graph, HPD.Graph.Abstractions.Serialization.GraphConfigJsonSerializerContext.Default.GraphConfig);
        using JsonDocument document = JsonDocument.Parse(source);
        using var stream = new MemoryStream(source.Length);
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, document.RootElement);
        return stream.ToArray();
    }

    internal static byte[] CanonicalJson(ReadOnlySpan<byte> source)
    {
        using JsonDocument document = JsonDocument.Parse(source.ToArray());
        using var stream = new MemoryStream(source.Length);
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, document.RootElement);
        return stream.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject().OrderBy(static item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(value.GetString()); break;
            case JsonValueKind.Number: writer.WriteRawValue(value.GetRawText(), skipInputValidation: false); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new JsonException("Unsupported graph definition token.");
        }
    }
}

internal sealed class BaseGraphActivationHandler(
    IServiceProvider services,
    GraphConfig graph,
    byte[] graphChecksum) : IBaseActivationHandler<BaseGraphActivationInput, BaseGraphActivationResult>
{
    public async ValueTask<BaseActivationHandlerResult<BaseGraphActivationResult>> ExecuteAsync(
        BaseActivationContext context,
        BaseGraphActivationInput input,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(input.GraphId, graph.GraphId, StringComparison.Ordinal)
            || !string.Equals(input.GraphVersion, graph.GraphVersion, StringComparison.Ordinal)
            || !CryptographicOperations.FixedTimeEquals(input.GraphChecksum.AsSpan(), graphChecksum)
            || string.IsNullOrWhiteSpace(input.ExecutionId)
            || input.CanonicalInput.IsDefault)
            return new BaseActivationHandlerResult<BaseGraphActivationResult>
            { FailureCode = "hpd.graph.execution.contractInvalid", Retryable = false };

        string executionId = context.OccurrenceId ?? input.ExecutionId;
        var runtimeGraph = new GraphConfigCompiler().Compile(graph);
        var graphContext = new GraphContext(executionId, runtimeGraph, services, enableSharedData: true);
        SeedInput(graphContext, input.CanonicalInput.AsSpan());
        graphContext.SharedData?["base.activation.id"] = context.Claim.ActivationId;
        graphContext.SharedData?["base.activation.occurrenceId"] = context.OccurrenceId;
        graphContext.SharedData?["base.activation.requestedDueAt"] = context.RequestedDueAt;
        graphContext.SharedData?["base.activation.effectiveDueAt"] = context.EffectiveDueAt;
        graphContext.SharedData?["base.activation.logicalIntervalStart"] = input.LogicalIntervalStart;
        graphContext.SharedData?["base.activation.logicalIntervalEnd"] = input.LogicalIntervalEnd;
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            artifactRegistry: services.GetService(typeof(IArtifactRegistry)) as IArtifactRegistry,
            graphRegistry: services.GetService(typeof(IGraphRegistry)) as IGraphRegistry,
            handlerRegistry: services.GetService(typeof(IGraphHandlerRegistry)) as IGraphHandlerRegistry);
        try
        {
            await orchestrator.ExecuteAsync(graphContext, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return new BaseActivationHandlerResult<BaseGraphActivationResult>
            { FailureCode = "hpd.graph.execution.failed", Retryable = false };
        }
        byte[] completed = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
            '\n', graphContext.CompletedNodes.Order(StringComparer.Ordinal))));
        return new BaseActivationHandlerResult<BaseGraphActivationResult>
        {
            Result = new BaseGraphActivationResult
            {
                ExecutionId = executionId,
                Outcome = BaseGraphActivationOutcome.Succeeded,
                CompletedNodesChecksum = completed.ToImmutableArray(),
            },
        };
    }

    private static void SeedInput(GraphContext context, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;
        using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
        object value = ConvertJson(document.RootElement);
        context.Channels["input:workflow"].Set(value);
        context.SharedData?["input"] = value;
        if (document.RootElement.ValueKind != JsonValueKind.Object) return;
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            object propertyValue = ConvertJson(property.Value);
            context.Channels[$"input:{property.Name}"].Set(propertyValue);
            context.SharedData?[property.Name] = propertyValue;
        }
    }

    private static object ConvertJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            static property => property.Name, static property => ConvertJson(property.Value), StringComparer.Ordinal),
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJson).ToArray(),
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number when element.TryGetInt64(out long integer) => integer,
        JsonValueKind.Number when element.TryGetDecimal(out decimal number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null!,
        _ => throw new JsonException("Unsupported canonical graph input token."),
    };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(BaseGraphActivationInput))]
[JsonSerializable(typeof(BaseGraphActivationResult))]
public partial class BaseGraphActivationJsonContext : JsonSerializerContext;
