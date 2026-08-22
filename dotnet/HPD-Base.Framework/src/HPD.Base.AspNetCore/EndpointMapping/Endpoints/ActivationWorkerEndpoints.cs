using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

internal static class ActivationWorkerEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        Map("/activations/enqueue", "base.activation.enqueue", HPDBaseEndpointOperation.ActivationEnqueue,
            HPDBaseCapabilities.ActivationEnqueue, EnqueueAsync);
        Map("/activations/claims/next", "base.activation.claim", HPDBaseEndpointOperation.ActivationClaim,
            HPDBaseCapabilities.ActivationClaim, ClaimAsync);
        Map("/activations/claims/renew", "base.activation.renew", HPDBaseEndpointOperation.ActivationRenew,
            HPDBaseCapabilities.ActivationRenew, RenewAsync);
        Map("/activations/complete", "base.activation.complete", HPDBaseEndpointOperation.ActivationComplete,
            HPDBaseCapabilities.ActivationComplete, CompleteAsync);
        Map("/activations/fail", "base.activation.fail", HPDBaseEndpointOperation.ActivationFail,
            HPDBaseCapabilities.ActivationFail, FailAsync);
        Map("/activations/cancel", "base.activation.cancel", HPDBaseEndpointOperation.ActivationCancel,
            HPDBaseCapabilities.ActivationCancel, CancelAsync);
        Map("/activations/receipts/resolve", "base.activation.receipt.resolve", HPDBaseEndpointOperation.ActivationReceiptResolve,
            HPDBaseCapabilities.ActivationReceiptResolve, ResolveReceiptAsync);
        Map("/activations/effects/begin", "base.activation.effect.begin", HPDBaseEndpointOperation.ActivationEffectBegin,
            HPDBaseCapabilities.ActivationEffectBegin, BeginEffectAsync);
        Map("/activations/effects/heartbeat", "base.activation.effect.heartbeat", HPDBaseEndpointOperation.ActivationEffectHeartbeat,
            HPDBaseCapabilities.ActivationEffectHeartbeat, HeartbeatEffectAsync);
        Map("/activation-executors/register", "base.activation.executor.register", HPDBaseEndpointOperation.ActivationExecutorRegister,
            HPDBaseCapabilities.ActivationExecutorRegister, RegisterExecutorAsync);
        Map("/activation-executors/heartbeat", "base.activation.executor.heartbeat", HPDBaseEndpointOperation.ActivationExecutorHeartbeat,
            HPDBaseCapabilities.ActivationExecutorHeartbeat, HeartbeatExecutorAsync);
        Map("/activation-executors/retire", "base.activation.executor.retire", HPDBaseEndpointOperation.ActivationExecutorRetire,
            HPDBaseCapabilities.ActivationExecutorRetire, RetireExecutorAsync);

        void Map(string route, string id, HPDBaseEndpointOperation operation, string capability, RequestDelegate handler) =>
            group.MapPost(route, handler)
                .WithHPDBaseEndpoint(id, HPDBaseEndpointAudience.Application, operation, capability)
                .WithName(id);
    }

    private static async Task EnqueueAsync(HttpContext context)
    {
        BaseActivationEnqueueHttpRequest? wire = await ReadAsync(
            context, BaseActivationWorkerJsonContext.Default.BaseActivationEnqueueHttpRequest).ConfigureAwait(false);
        Resolved? resolved = wire is null ? null : await ResolveAsync(context, wire.DefinitionId, wire.DefinitionVersion).ConfigureAwait(false);
        if (wire is null || resolved is null || wire.DueAt is < 0)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); return; }
        try
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(wire.Payload, BaseActivationWorkerJsonContext.Default.JsonElement);
            OperationResult<BaseActivationEnqueueResult> result = await resolved.Registration.EnqueueAsync(
                resolved.Runtime, resolved.Session, payload, wire.Identity.ToRuntime(),
                wire.DueAt is long due ? new BaseActivationEnqueueOptions { DueAt = DateTimeOffset.FromUnixTimeMilliseconds(due) } : null,
                context.RequestAborted).ConfigureAwait(false);
            await Write(context, result).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or ArgumentOutOfRangeException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); }
    }

    private static async Task ClaimAsync(HttpContext context)
    {
        BaseActivationClaimHttpRequest? wire = await ReadAsync(
            context, BaseActivationWorkerJsonContext.Default.BaseActivationClaimHttpRequest).ConfigureAwait(false);
        Resolved? resolved = wire is null ? null : await ResolveAsync(context, wire.DefinitionId, wire.DefinitionVersion).ConfigureAwait(false);
        if (wire is null || resolved is null)
        { await Problem(context, OperationStatus.PolicyDenied, "base.activation.unauthorized").ConfigureAwait(false); return; }
        OperationResult<BaseActivationDueObservation> observed = await resolved.Worker.ObserveAsync(
            resolved.Session, resolved.Definition, context.RequestAborted).ConfigureAwait(false);
        if (!observed.IsSuccess() || observed.Value is null)
        { await Problem(context, observed.Status, observed.Error?.Code ?? "base.activation.storeError").ConfigureAwait(false); return; }
        if (observed.Value.Earliest is null)
        {
            await Results.Json(new BaseActivationClaimHttpResult { Empty = true },
                BaseActivationWorkerJsonContext.Default.BaseActivationClaimHttpResult).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }
        OperationResult<BaseActivationClaimResult> claimed;
        try
        {
            claimed = await resolved.Worker.ClaimAsync(resolved.Session, resolved.Definition,
                observed.Value.Token, wire.Identity.ToRuntime(), context.RequestAborted).ConfigureAwait(false);
        }
        catch (ArgumentException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); return; }
        if (!claimed.IsSuccess())
        { await Problem(context, claimed.Status, claimed.Error?.Code ?? "base.activation.storeError").ConfigureAwait(false); return; }
        if (claimed.Value is not BaseActivationClaimedResult value)
        {
            await Results.Json(new BaseActivationClaimHttpResult { Empty = true },
                BaseActivationWorkerJsonContext.Default.BaseActivationClaimHttpResult).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }
        using JsonDocument payload = JsonDocument.Parse(value.Payload.CanonicalInput.ToArray());
        await Results.Json(new BaseActivationClaimHttpResult
        {
            Empty = false,
            ActivationId = value.Payload.ActivationId,
            Payload = payload.RootElement.Clone(),
            Claim = value.Claim,
            Lease = value.Lease,
            Attempt = value.Attempt,
            OccurrenceId = value.Payload.OccurrenceId,
            RequestedDueAt = value.Payload.RequestedDueAt,
            EffectiveDueAt = value.Payload.EffectiveDueAt,
        }, BaseActivationWorkerJsonContext.Default.BaseActivationClaimHttpResult).ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async Task RenewAsync(HttpContext context)
    {
        BaseActivationRenewHttpRequest? wire = await ReadAsync(
            context, BaseActivationWorkerJsonContext.Default.BaseActivationRenewHttpRequest).ConfigureAwait(false);
        Resolved? resolved = wire is null ? null : await ResolveAsync(context, wire.DefinitionId, wire.DefinitionVersion).ConfigureAwait(false);
        if (wire is null || resolved is null)
        { await Problem(context, OperationStatus.PolicyDenied, "base.activation.unauthorized").ConfigureAwait(false); return; }
        OperationResult<BaseActivationRenewResult> result;
        try
        {
            result = await resolved.Worker.RenewAsync(resolved.Session, resolved.Definition,
                wire.Claim, wire.Lease, wire.Identity.ToRuntime(), context.RequestAborted).ConfigureAwait(false);
        }
        catch (ArgumentException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); return; }
        await Write(context, result).ConfigureAwait(false);
    }

    private static async Task CompleteAsync(HttpContext context)
    {
        BaseActivationCompleteHttpRequest? wire = await ReadAsync(
            context, BaseActivationWorkerJsonContext.Default.BaseActivationCompleteHttpRequest).ConfigureAwait(false);
        Resolved? resolved = wire is null ? null : await ResolveAsync(context, wire.DefinitionId, wire.DefinitionVersion).ConfigureAwait(false);
        if (wire is null || resolved is null)
        { await Problem(context, OperationStatus.PolicyDenied, "base.activation.unauthorized").ConfigureAwait(false); return; }
        try
        {
            byte[] resultBytes = JsonSerializer.SerializeToUtf8Bytes(wire.Result, BaseActivationWorkerJsonContext.Default.JsonElement);
            OperationResult<BaseActivationTransitionResult> result = await resolved.Registration.CompleteAsync(
                resolved.Worker, resolved.Session, wire.Claim, resultBytes, wire.Identity.ToRuntime(), context.RequestAborted).ConfigureAwait(false);
            await Write(context, result).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); }
    }

    private static async Task FailAsync(HttpContext context)
    {
        BaseActivationFailHttpRequest? wire = await ReadAsync(
            context, BaseActivationWorkerJsonContext.Default.BaseActivationFailHttpRequest).ConfigureAwait(false);
        Resolved? resolved = wire is null ? null : await ResolveAsync(context, wire.DefinitionId, wire.DefinitionVersion).ConfigureAwait(false);
        if (wire is null || resolved is null || string.IsNullOrWhiteSpace(wire.FailureCode))
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); return; }
        try
        {
            OperationResult<BaseActivationTransitionResult> result = await resolved.Worker.FailAsync(
                resolved.Session, resolved.Definition, wire.Claim, wire.FailureCode,
                wire.Retry, wire.Identity.ToRuntime(), context.RequestAborted).ConfigureAwait(false);
            await Write(context, result).ConfigureAwait(false);
        }
        catch (ArgumentException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); }
    }

    private static async Task ResolveReceiptAsync(HttpContext context)
    {
        BaseActivationReceiptHttpRequest? wire = await ReadAsync(
            context, BaseActivationWorkerJsonContext.Default.BaseActivationReceiptHttpRequest).ConfigureAwait(false);
        Resolved? resolved = wire is null ? null : await ResolveAsync(context, wire.DefinitionId, wire.DefinitionVersion).ConfigureAwait(false);
        if (wire is null || resolved is null)
        { await Problem(context, OperationStatus.PolicyDenied, "base.activation.unauthorized").ConfigureAwait(false); return; }
        try
        {
            OperationResult<BaseActivationReceiptResolution> result = await resolved.Worker.ResolveReceiptAsync(
                resolved.Session, resolved.Definition, resolved.Registration.ResultBindings,
                wire.Identity.ToRuntime(), context.RequestAborted).ConfigureAwait(false);
            await Write(context, result).ConfigureAwait(false);
        }
        catch (ArgumentException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); }
    }

    private static async Task CancelAsync(HttpContext context)
    {
        BaseActivationWorkerCancelHttpRequest? wire = await ReadAsync(
            context, BaseActivationWorkerJsonContext.Default.BaseActivationWorkerCancelHttpRequest).ConfigureAwait(false);
        Resolved? resolved = wire is null ? null : await ResolveAsync(context, wire.DefinitionId, wire.DefinitionVersion).ConfigureAwait(false);
        if (wire is null || resolved is null || !Enum.IsDefined(wire.Propagation))
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); return; }
        try
        {
            OperationResult<BaseActivationTransitionResult> result = await resolved.Worker.CancelAsync(
                resolved.Session, resolved.Definition, wire.ActivationId, wire.ExpectedGeneration,
                wire.Propagation, wire.Identity.ToRuntime(), context.RequestAborted).ConfigureAwait(false);
            await Write(context, result).ConfigureAwait(false);
        }
        catch (ArgumentException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); }
    }

    private static async Task BeginEffectAsync(HttpContext context)
    {
        BaseActivationEffectBeginHttpRequest? wire = await ReadAsync(
            context, BaseActivationWorkerJsonContext.Default.BaseActivationEffectBeginHttpRequest).ConfigureAwait(false);
        Resolved? resolved = wire is null ? null : await ResolveAsync(context, wire.DefinitionId, wire.DefinitionVersion).ConfigureAwait(false);
        if (wire is null || resolved is null)
        { await Problem(context, OperationStatus.PolicyDenied, "base.activation.unauthorized").ConfigureAwait(false); return; }
        try
        {
            OperationResult<BaseActivationTransitionResult> result = await resolved.Worker.BeginEffectAsync(
                resolved.Session, resolved.Definition, wire.Claim, wire.Identity.ToRuntime(), context.RequestAborted).ConfigureAwait(false);
            await Write(context, result).ConfigureAwait(false);
        }
        catch (ArgumentException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); }
    }

    private static async Task HeartbeatEffectAsync(HttpContext context)
    {
        BaseActivationEffectHeartbeatHttpRequest? wire = await ReadAsync(
            context, BaseActivationWorkerJsonContext.Default.BaseActivationEffectHeartbeatHttpRequest).ConfigureAwait(false);
        Resolved? resolved = wire is null ? null : await ResolveAsync(context, wire.DefinitionId, wire.DefinitionVersion).ConfigureAwait(false);
        if (wire is null || resolved is null)
        { await Problem(context, OperationStatus.PolicyDenied, "base.activation.unauthorized").ConfigureAwait(false); return; }
        try
        {
            OperationResult<BaseActivationTransitionResult> result = await resolved.Worker.HeartbeatEffectAsync(
                resolved.Session, resolved.Definition, wire.Effect, wire.Identity.ToRuntime(), context.RequestAborted).ConfigureAwait(false);
            await Write(context, result).ConfigureAwait(false);
        }
        catch (ArgumentException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); }
    }

    private static async Task RegisterExecutorAsync(HttpContext context)
    {
        BaseExecutorRegisterHttpRequest? wire = await ReadAsync(
            context, BaseActivationWorkerJsonContext.Default.BaseExecutorRegisterHttpRequest).ConfigureAwait(false);
        Resolved? resolved = wire is null ? null : await ResolveAsync(context, wire.DefinitionId, wire.DefinitionVersion).ConfigureAwait(false);
        if (wire is null || resolved is null)
        { await Problem(context, OperationStatus.PolicyDenied, "base.activation.unauthorized").ConfigureAwait(false); return; }
        try
        {
            OperationResult<BaseExecutorRegistrationResult> result = await resolved.Worker.RegisterExecutorAsync(
                resolved.Session, resolved.Definition, wire.HostId, wire.ProcessIncarnationId,
                wire.HeartbeatMilliseconds, wire.Identity.ToRuntime(), context.RequestAborted).ConfigureAwait(false);
            await Write(context, result).ConfigureAwait(false);
        }
        catch (ArgumentException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); }
    }

    private static async Task HeartbeatExecutorAsync(HttpContext context)
    {
        BaseExecutorHeartbeatHttpRequest? wire = await ReadAsync(
            context, BaseActivationWorkerJsonContext.Default.BaseExecutorHeartbeatHttpRequest).ConfigureAwait(false);
        Resolved? resolved = wire is null ? null : await ResolveAsync(context, wire.DefinitionId, wire.DefinitionVersion).ConfigureAwait(false);
        if (wire is null || resolved is null)
        { await Problem(context, OperationStatus.PolicyDenied, "base.activation.unauthorized").ConfigureAwait(false); return; }
        try
        {
            OperationResult<BaseExecutorHeartbeatResult> result = await resolved.Worker.HeartbeatExecutorAsync(
                resolved.Session, resolved.Definition, wire.Executor, wire.Heartbeat,
                wire.ExtensionMilliseconds, wire.Identity.ToRuntime(), context.RequestAborted).ConfigureAwait(false);
            await Write(context, result).ConfigureAwait(false);
        }
        catch (ArgumentException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); }
    }

    private static async Task RetireExecutorAsync(HttpContext context)
    {
        BaseExecutorRetireHttpRequest? wire = await ReadAsync(
            context, BaseActivationWorkerJsonContext.Default.BaseExecutorRetireHttpRequest).ConfigureAwait(false);
        Resolved? resolved = wire is null ? null : await ResolveAsync(context, wire.DefinitionId, wire.DefinitionVersion).ConfigureAwait(false);
        if (wire is null || resolved is null)
        { await Problem(context, OperationStatus.PolicyDenied, "base.activation.unauthorized").ConfigureAwait(false); return; }
        try
        {
            OperationResult<BaseExecutorRetirementResult> result = await resolved.Worker.RetireExecutorAsync(
                resolved.Session, resolved.Definition, wire.Executor, wire.Heartbeat,
                wire.Identity.ToRuntime(), context.RequestAborted).ConfigureAwait(false);
            await Write(context, result).ConfigureAwait(false);
        }
        catch (ArgumentException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid").ConfigureAwait(false); }
    }

    private static async ValueTask<Resolved?> ResolveAsync(HttpContext context, string id, int version)
    {
        if (string.IsNullOrWhiteSpace(id) || version <= 0) return null;
        BaseActivationRegistry? registry = context.RequestServices.GetService<BaseActivationRegistry>();
        if (registry is null) return null;
        IBaseActivationRegistration? registration = registry.Registration(id, version);
        if (registration is null) return null;
        PrincipalContext principal = await context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>()
            .CreateAsync(context, context.RequestAborted).ConfigureAwait(false);
        return new Resolved(
            registration,
            registration.Definition,
            context.RequestServices.GetRequiredService<IBaseSessionFactory>().For(principal),
            context.RequestServices.GetRequiredService<IBaseActivationRuntime>(),
            context.RequestServices.GetRequiredService<IBaseActivationWorkerRuntime>());
    }

    private static async ValueTask<T?> ReadAsync<T>(HttpContext context, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        const int maximum = 4 * 1024 * 1024;
        if (context.Request.ContentLength is > maximum) return default;
        try
        {
            await using var bounded = new LimitedRequestBodyStream(context.Request.Body, maximum);
            return await JsonSerializer.DeserializeAsync(bounded, type, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException) { return default; }
    }

    private static async Task Write<T>(HttpContext context, OperationResult<T> result)
    {
        if (!result.IsSuccess() || result.Value is null)
        { await Problem(context, result.Status, result.Error?.Code ?? "base.activation.storeError").ConfigureAwait(false); return; }
        System.Text.Json.Serialization.Metadata.JsonTypeInfo? type =
            HPDBaseJsonSerializerContext.Default.GetTypeInfo(result.Value.GetType());
        if (type is null)
        { await Problem(context, OperationStatus.CapabilityUnavailable, "base.activation.providerContractInvalid").ConfigureAwait(false); return; }
        await Results.Json(result.Value, type).ExecuteAsync(context).ConfigureAwait(false);
    }

    private static Task Problem(HttpContext context, OperationStatus status, string code) => Results.Problem(
        statusCode: BaseHttpStatusCodeMapper.ToStatusCode(status),
        title: "The activation operation failed.",
        extensions: new Dictionary<string, object?> { ["hpd.error.code"] = code }).ExecuteAsync(context);

    private sealed record Resolved(
        IBaseActivationRegistration Registration,
        BaseActivationDefinition Definition,
        BaseSession Session,
        IBaseActivationRuntime Runtime,
        IBaseActivationWorkerRuntime Worker);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseActivationEnqueueHttpRequest
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required JsonElement Payload { get; init; }
    public long? DueAt { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseActivationClaimHttpRequest
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

internal sealed record BaseActivationClaimHttpResult
{
    public required bool Empty { get; init; }
    public string? ActivationId { get; init; }
    public JsonElement? Payload { get; init; }
    public BaseActivationClaimAuthority? Claim { get; init; }
    public BaseActivationLeaseObservation? Lease { get; init; }
    public BaseActivationAttemptEvidence? Attempt { get; init; }
    public string? OccurrenceId { get; init; }
    public long? RequestedDueAt { get; init; }
    public long? EffectiveDueAt { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseActivationRenewHttpRequest
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required BaseActivationClaimAuthority Claim { get; init; }
    public required BaseActivationLeaseObservation Lease { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseActivationCompleteHttpRequest
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required BaseActivationClaimAuthority Claim { get; init; }
    public required JsonElement Result { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseActivationFailHttpRequest
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required BaseActivationClaimAuthority Claim { get; init; }
    public required string FailureCode { get; init; }
    public required bool Retry { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseActivationReceiptHttpRequest
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseActivationWorkerCancelHttpRequest
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required string ActivationId { get; init; }
    public required long ExpectedGeneration { get; init; }
    public required BaseCancellationPropagation Propagation { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseActivationEffectBeginHttpRequest
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required BaseActivationClaimAuthority Claim { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseActivationEffectHeartbeatHttpRequest
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required BaseEffectExecutionAuthority Effect { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseExecutorRegisterHttpRequest
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required string HostId { get; init; }
    public required string ProcessIncarnationId { get; init; }
    public required long HeartbeatMilliseconds { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseExecutorHeartbeatHttpRequest
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    public required BaseExecutorHeartbeatObservation Heartbeat { get; init; }
    public required long ExtensionMilliseconds { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseExecutorRetireHttpRequest
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    public required BaseExecutorHeartbeatObservation Heartbeat { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(BaseActivationEnqueueHttpRequest))]
[JsonSerializable(typeof(BaseActivationClaimHttpRequest))]
[JsonSerializable(typeof(BaseActivationClaimHttpResult))]
[JsonSerializable(typeof(BaseActivationRenewHttpRequest))]
[JsonSerializable(typeof(BaseActivationCompleteHttpRequest))]
[JsonSerializable(typeof(BaseActivationFailHttpRequest))]
[JsonSerializable(typeof(BaseActivationReceiptHttpRequest))]
[JsonSerializable(typeof(BaseActivationWorkerCancelHttpRequest))]
[JsonSerializable(typeof(BaseActivationEffectBeginHttpRequest))]
[JsonSerializable(typeof(BaseActivationEffectHeartbeatHttpRequest))]
[JsonSerializable(typeof(BaseExecutorRegisterHttpRequest))]
[JsonSerializable(typeof(BaseExecutorHeartbeatHttpRequest))]
[JsonSerializable(typeof(BaseExecutorRetireHttpRequest))]
internal partial class BaseActivationWorkerJsonContext : JsonSerializerContext;
