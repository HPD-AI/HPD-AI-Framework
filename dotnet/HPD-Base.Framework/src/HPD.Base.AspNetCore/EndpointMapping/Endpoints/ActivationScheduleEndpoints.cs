using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

internal static class ActivationScheduleEndpoints
{
    internal static void Map(
        RouteGroupBuilder group,
        Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor> convention)
    {
        group.MapPost("/control/schedules/query", (RequestDelegate)ReadAsync)
            .WithHPDBaseEndpoint("base.activation.schedule.read", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.ActivationScheduleRead, HPDBaseCapabilities.ActivationScheduleRead, convention)
            .WithName("base.activation.schedule.read");
        group.MapPost("/control/schedules/mutate", (RequestDelegate)MutateAsync)
            .WithHPDBaseEndpoint("base.activation.schedule.mutate", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.ActivationScheduleMutate, HPDBaseCapabilities.ActivationScheduleMutate, convention)
            .WithName("base.activation.schedule.mutate");
    }

    private static async Task ReadAsync(HttpContext context)
    {
        BaseScheduleReadHttpRequest? request = await ReadBodyAsync(
            context, BaseActivationScheduleJsonContext.Default.BaseScheduleReadHttpRequest).ConfigureAwait(false);
        if (request is null) { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid"); return; }
        BaseInstalledScheduleHandle? handle = await ResolveAsync(context, request.ScheduleId, request.ScheduleVersion).ConfigureAwait(false);
        if (handle is null) { await Problem(context, OperationStatus.PolicyDenied, "base.activation.unauthorized"); return; }
        OperationResult<BaseScheduleAuthority> result = await handle.ReadAsync(context.RequestAborted).ConfigureAwait(false);
        if (!result.IsSuccess() || result.Value is null)
        { await Problem(context, result.Status, result.Error?.Code ?? "base.activation.storeError"); return; }
        await Results.Json(Project(result.Value), BaseActivationScheduleJsonContext.Default.BaseScheduleHttpResult)
            .ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async Task MutateAsync(HttpContext context)
    {
        BaseScheduleMutationHttpRequest? request = await ReadBodyAsync(
            context, BaseActivationScheduleJsonContext.Default.BaseScheduleMutationHttpRequest).ConfigureAwait(false);
        if (request is null || !Enum.IsDefined(request.Kind)
            || request.Kind != BaseScheduleMutationKind.Create && request.ExpectedGeneration is null
            || request.Kind == BaseScheduleMutationKind.Create && request.ExpectedGeneration is not null
            || !context.Request.Headers.TryGetValue(BaseHttpHeaders.IdempotencyKey, out var keys) || keys.Count != 1)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid"); return; }
        BaseInstalledScheduleHandle? handle = await ResolveAsync(context, request.ScheduleId, request.ScheduleVersion).ConfigureAwait(false);
        if (handle is null) { await Problem(context, OperationStatus.PolicyDenied, "base.activation.unauthorized"); return; }
        byte[] fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.activation.schedule.http.v1\0{request.ScheduleId}\n{request.ScheduleVersion}\n{(int)request.Kind}\n{request.ExpectedGeneration?.ToString() ?? "none"}"));
        BaseMutationRequestIdentity identity;
        try
        {
            identity = BaseMutationRequestIdentity.Create(
                $"schedule:{request.ScheduleId}:{request.ScheduleVersion}",
                "base.activation.schedule.mutate", keys[0]!, BaseMutationRequestFingerprint.Create(fingerprint));
        }
        catch (ArgumentException)
        { await Problem(context, OperationStatus.ValidationFailed, "base.activation.invalid"); return; }
        OperationResult<BaseScheduleMutationResult> result = request.Kind switch
        {
            BaseScheduleMutationKind.Create => await handle.CreateAsync(identity, context.RequestAborted).ConfigureAwait(false),
            BaseScheduleMutationKind.Update => await handle.UpdateAsync(request.ExpectedGeneration!.Value, identity, context.RequestAborted).ConfigureAwait(false),
            BaseScheduleMutationKind.Enable => await handle.EnableAsync(request.ExpectedGeneration!.Value, identity, context.RequestAborted).ConfigureAwait(false),
            BaseScheduleMutationKind.Disable => await handle.DisableAsync(request.ExpectedGeneration!.Value, identity, context.RequestAborted).ConfigureAwait(false),
            BaseScheduleMutationKind.Remove => await handle.RemoveAsync(request.ExpectedGeneration!.Value, identity, context.RequestAborted).ConfigureAwait(false),
            _ => throw new InvalidOperationException("base.activation.invalid"),
        };
        if (!result.IsSuccess() || result.Value is null)
        { await Problem(context, result.Status, result.Error?.Code ?? "base.activation.storeError"); return; }
        BaseScheduleMutationResult value = result.Value;
        await Results.Json(new BaseScheduleMutationHttpResult
        {
            Disposition = value.Disposition,
            Authority = value.Authority is null ? null : Project(value.Authority),
        }, BaseActivationScheduleJsonContext.Default.BaseScheduleMutationHttpResult).ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async ValueTask<BaseInstalledScheduleHandle?> ResolveAsync(
        HttpContext context, string scheduleId, int scheduleVersion)
    {
        if (string.IsNullOrWhiteSpace(scheduleId) || scheduleVersion <= 0) return null;
        BaseScheduleDefinition? definition = context.RequestServices.GetRequiredService<BaseScheduleRegistry>()
            .Find(scheduleId, scheduleVersion);
        if (definition is null) return null;
        PrincipalContext principal = await context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>()
            .CreateAsync(context, context.RequestAborted).ConfigureAwait(false);
        BaseSession session = context.RequestServices.GetRequiredService<IBaseSessionFactory>().For(principal);
        return session.Activations.GetSchedule(BaseScheduleRegistration.Create(definition));
    }

    private static BaseScheduleHttpResult Project(BaseScheduleAuthority value) => new()
    {
        ScheduleId = value.Definition.Id, ScheduleVersion = value.Definition.Version,
        Generation = value.DefinitionGeneration, Enabled = value.Enabled,
        Epoch = value.ScheduleEpoch, LastConsideredNominal = value.LastConsideredNominal,
        NextNominal = value.NextNominal,
    };

    private static async ValueTask<T?> ReadBodyAsync<T>(HttpContext context, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        if (context.Request.ContentLength is > 65_536) return default;
        try { return await System.Text.Json.JsonSerializer.DeserializeAsync(context.Request.Body, type, context.RequestAborted).ConfigureAwait(false); }
        catch (System.Text.Json.JsonException) { return default; }
    }

    private static Task Problem(HttpContext context, OperationStatus status, string code) => Results.Problem(
        statusCode: BaseHttpStatusCodeMapper.ToStatusCode(status), title: "BASE activation schedule request failed.",
        extensions: new Dictionary<string, object?> { ["hpd.error.code"] = code }).ExecuteAsync(context);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseScheduleReadHttpRequest
{
    public required string ScheduleId { get; init; }
    public required int ScheduleVersion { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BaseScheduleMutationHttpRequest
{
    public required string ScheduleId { get; init; }
    public required int ScheduleVersion { get; init; }
    public required BaseScheduleMutationKind Kind { get; init; }
    public long? ExpectedGeneration { get; init; }
}

internal sealed record BaseScheduleHttpResult
{
    public required string ScheduleId { get; init; }
    public required int ScheduleVersion { get; init; }
    public required long Generation { get; init; }
    public required bool Enabled { get; init; }
    public required long Epoch { get; init; }
    public long? LastConsideredNominal { get; init; }
    public long? NextNominal { get; init; }
}

internal sealed record BaseScheduleMutationHttpResult
{
    public required BaseMutationRequestDisposition Disposition { get; init; }
    public BaseScheduleHttpResult? Authority { get; init; }
}

[JsonSerializable(typeof(BaseScheduleReadHttpRequest))]
[JsonSerializable(typeof(BaseScheduleMutationHttpRequest))]
[JsonSerializable(typeof(BaseScheduleHttpResult))]
[JsonSerializable(typeof(BaseScheduleMutationHttpResult))]
internal partial class BaseActivationScheduleJsonContext : JsonSerializerContext;
