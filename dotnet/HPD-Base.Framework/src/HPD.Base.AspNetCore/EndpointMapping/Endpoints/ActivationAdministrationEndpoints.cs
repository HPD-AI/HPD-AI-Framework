using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

internal static class ActivationAdministrationEndpoints
{
    internal static void Map(
        RouteGroupBuilder group,
        Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor> convention)
    {
        group.MapPost("/control/activations/query", (RequestDelegate)QueryAsync)
            .WithHPDBaseEndpoint("base.activation.query", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.ActivationQuery, HPDBaseCapabilities.ActivationQuery, convention)
            .WithName("base.activation.query");
        group.MapPost("/control/activations/retry", (RequestDelegate)RetryAsync)
            .WithHPDBaseEndpoint("base.activation.retry", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.ActivationRetry, HPDBaseCapabilities.ActivationRetry, convention)
            .WithName("base.activation.retry");
        group.MapPost("/control/activations/reconcile", (RequestDelegate)ReconcileAsync)
            .WithHPDBaseEndpoint("base.activation.reconcile", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.ActivationReconcile, HPDBaseCapabilities.ActivationReconcile, convention)
            .WithName("base.activation.reconcile");
        group.MapPost("/control/activations/dispose", (RequestDelegate)DisposeAsync)
            .WithHPDBaseEndpoint("base.activation.dispose", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.ActivationDispose, HPDBaseCapabilities.ActivationDispose, convention)
            .WithName("base.activation.dispose");
        group.MapPost("/control/activation-maintenance/advance", (RequestDelegate)AdvanceMaintenanceAsync)
            .WithHPDBaseEndpoint("base.activation.maintenance.advance", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.ActivationMaintenanceAdvance, HPDBaseCapabilities.ActivationMaintenanceAdvance, convention)
            .WithName("base.activation.maintenance.advance");
        group.MapPost("/control/activation-removal/advance", (RequestDelegate)AdvanceRemovalAsync)
            .WithHPDBaseEndpoint("base.activation.removal.advance", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.ActivationRemovalAdvance, HPDBaseCapabilities.ActivationRemovalAdvance, convention)
            .WithName("base.activation.removal.advance");
        group.MapPost("/control/activations/migrate", (RequestDelegate)MigrateAsync)
            .WithHPDBaseEndpoint("base.activation.migrate", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.ActivationMigrate, HPDBaseCapabilities.ActivationMigrate, convention)
            .WithName("base.activation.migrate");
        group.MapPost("/control/activation-repair/execute", (RequestDelegate)RepairAsync)
            .WithHPDBaseEndpoint("base.activation.repair.execute", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.ActivationRepairExecute, HPDBaseCapabilities.ActivationRepairExecute, convention)
            .WithName("base.activation.repair.execute");
    }

    private static async Task QueryAsync(HttpContext context)
    {
        BaseActivationQueryHttpRequest? wire = await ReadAsync(
            context, BaseActivationAdministrationJsonContext.Default.BaseActivationQueryHttpRequest).ConfigureAwait(false);
        if (wire is null) { await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false); return; }
        BaseResult<BaseActivationAdministrationPage> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .ReadActivationsAsync(new BaseActivationAdministrationReadRequest
            {
                StoreId = wire.StoreId, Principal = await Principal(context).ConfigureAwait(false),
                Scope = new BaseOwnedSubjectScopeEvidence { Kind = wire.ScopeKind, Value = wire.ScopeValue },
                DefinitionId = wire.DefinitionId, DefinitionVersion = wire.DefinitionVersion,
                States = wire.States,
                After = wire.After is null ? null : new BaseActivationAdministrationBoundary
                {
                    DefinitionId = wire.After.DefinitionId, DefinitionVersion = wire.After.DefinitionVersion,
                    EffectiveDueAt = wire.After.EffectiveDueAt, ActivationId = wire.After.ActivationId,
                },
                Take = wire.Take,
            }, context.RequestAborted).ConfigureAwait(false);
        if (result is BaseFailure<BaseActivationAdministrationPage> failed)
        {
            int status = failed.Status switch
            {
                OperationStatus.ValidationFailed => 400, OperationStatus.PolicyDenied => 403,
                OperationStatus.NotFound => 404, OperationStatus.Conflict => 409,
                OperationStatus.Unsupported or OperationStatus.CapabilityUnavailable => 424, _ => 500,
            };
            await Problem(context, status, failed.Error.Code).ConfigureAwait(false); return;
        }
        BaseActivationAdministrationPage page = ((BaseSuccess<BaseActivationAdministrationPage>)result).Value;
        var response = new BaseActivationQueryHttpResult
        {
            Items = page.Items.Select(static item => new BaseActivationQueryItemHttpResult
            {
                ActivationId = item.ActivationId, DefinitionId = item.Definition.Id,
                DefinitionVersion = item.Definition.Version, State = item.State,
                Generation = item.Generation, EffectiveDueAt = item.EffectiveDueAt,
                OccurrenceId = item.OccurrenceId, AttemptNumber = item.AttemptNumber,
                ResultRetained = item.ResultRetained, EffectAuthorityRetained = item.EffectAuthorityRetained,
            }).ToImmutableArray(),
            Next = page.Next is null ? null : new BaseActivationQueryBoundaryHttp
            {
                DefinitionId = page.Next.DefinitionId, DefinitionVersion = page.Next.DefinitionVersion,
                EffectiveDueAt = page.Next.EffectiveDueAt, ActivationId = page.Next.ActivationId,
            },
        };
        await Results.Json(response, BaseActivationAdministrationJsonContext.Default.BaseActivationQueryHttpResult)
            .ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async Task RetryAsync(HttpContext context)
    {
        BaseActivationRetryHttpRequest? wire = await ReadAsync(
            context, BaseActivationAdministrationJsonContext.Default.BaseActivationRetryHttpRequest).ConfigureAwait(false);
        if (wire is null) { await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false); return; }
        BaseResult<BaseActivationTransitionResult> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .RetryActivationAsync(new BaseActivationAdministrationRetryRequest
            {
                StoreId = wire.StoreId, Principal = await Principal(context).ConfigureAwait(false),
                DefinitionId = wire.DefinitionId, DefinitionVersion = wire.DefinitionVersion,
                ActivationId = wire.ActivationId, ExpectedGeneration = wire.ExpectedGeneration,
                DueAt = wire.DueAtUnixMilliseconds is long dueAt
                    ? DateTimeOffset.FromUnixTimeMilliseconds(dueAt)
                    : null,
                Identity = wire.Identity.ToRuntime(),
            }, context.RequestAborted).ConfigureAwait(false);
        await Write(context, result).ConfigureAwait(false);
    }


    private static async Task ReconcileAsync(HttpContext context)
    {
        BaseActivationReconcileHttpRequest? wire = await ReadAsync(
            context, BaseActivationAdministrationJsonContext.Default.BaseActivationReconcileHttpRequest).ConfigureAwait(false);
        if (wire is null) { await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false); return; }
        BaseResult<BaseActivationTransitionResult> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .ReconcileActivationAsync(new BaseActivationAdministrationReconcileRequest
            {
                StoreId = wire.StoreId, Principal = await Principal(context).ConfigureAwait(false),
                DefinitionId = wire.DefinitionId, DefinitionVersion = wire.DefinitionVersion,
                ActivationId = wire.ActivationId, ExpectedGeneration = wire.ExpectedGeneration,
                ExpectedEffectStartGeneration = wire.ExpectedEffectStartGeneration,
                ExpectedEffectChecksum = wire.ExpectedEffectChecksum,
                Disposition = wire.Disposition, VerificationEvidence = wire.VerificationEvidence,
                VerificationChecksum = wire.VerificationChecksum, Identity = wire.Identity.ToRuntime(),
            }, context.RequestAborted).ConfigureAwait(false);
        await Write(context, result).ConfigureAwait(false);
    }

    private static async Task DisposeAsync(HttpContext context)
    {
        BaseActivationDisposeHttpRequest? wire = await ReadAsync(
            context, BaseActivationAdministrationJsonContext.Default.BaseActivationDisposeHttpRequest).ConfigureAwait(false);
        if (wire is null) { await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false); return; }
        BaseResult<BaseActivationTransitionResult> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .DisposeActivationAsync(new BaseActivationAdministrationDisposeRequest
            {
                StoreId = wire.StoreId, Principal = await Principal(context).ConfigureAwait(false),
                DefinitionId = wire.DefinitionId, DefinitionVersion = wire.DefinitionVersion,
                ActivationId = wire.ActivationId, ExpectedGeneration = wire.ExpectedGeneration,
                Identity = wire.Identity.ToRuntime(),
            }, context.RequestAborted).ConfigureAwait(false);
        await Write(context, result).ConfigureAwait(false);
    }

    private static async Task AdvanceMaintenanceAsync(HttpContext context)
    {
        BaseActivationMaintenanceHttpRequest? wire = await ReadAsync(
            context, BaseActivationAdministrationJsonContext.Default.BaseActivationMaintenanceHttpRequest).ConfigureAwait(false);
        if (wire is null) { await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false); return; }
        BaseResult<BaseActivationMaintenancePage> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .AdvanceActivationMaintenanceAsync(new BaseActivationAdministrationMaintenanceRequest
            {
                StoreId = wire.StoreId, Principal = await Principal(context).ConfigureAwait(false),
                Scope = new BaseOwnedSubjectScopeEvidence { Kind = wire.ScopeKind, Value = wire.ScopeValue },
                DefinitionId = wire.DefinitionId, DefinitionVersion = wire.DefinitionVersion,
                Kind = wire.Kind, AfterActivationId = wire.AfterActivationId, Take = wire.Take,
                Identity = wire.Identity.ToRuntime(),
            }, context.RequestAborted).ConfigureAwait(false);
        if (!result.TryGetValue(out BaseActivationMaintenancePage? page) || page is null)
        {
            await WriteFailure(context, result.Status, (result as BaseFailure<BaseActivationMaintenancePage>)?.Error).ConfigureAwait(false);
            return;
        }
        await Results.Json(new BaseActivationMaintenanceHttpResult
        {
            Items = page.Items.Select(static item => new BaseActivationMaintenanceItemHttpResult
            {
                ActivationId = item.ActivationId, PreviousGeneration = item.PreviousGeneration,
                ResultingGeneration = item.ResultingGeneration, PreviousState = item.PreviousState,
                ResultingState = item.ResultingState, ControlChecksum = item.ControlChecksum,
            }).ToImmutableArray(),
            NextActivationId = page.NextActivationId, Completed = page.Completed, Disposition = page.Disposition,
        }, BaseActivationAdministrationJsonContext.Default.BaseActivationMaintenanceHttpResult).ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async Task AdvanceRemovalAsync(HttpContext context)
    {
        BaseActivationRemovalHttpRequest? wire = await ReadAsync(
            context, BaseActivationAdministrationJsonContext.Default.BaseActivationRemovalHttpRequest).ConfigureAwait(false);
        if (wire is null) { await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false); return; }
        BaseResult<BaseActivationPrunePage> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .PruneActivationsAsync(new BaseActivationAdministrationPruneRequest
            {
                StoreId = wire.StoreId, Principal = await Principal(context).ConfigureAwait(false),
                Scope = new BaseOwnedSubjectScopeEvidence { Kind = wire.ScopeKind, Value = wire.ScopeValue },
                DefinitionId = wire.DefinitionId, DefinitionVersion = wire.DefinitionVersion,
                AfterActivationId = wire.AfterActivationId, Take = wire.Take, Identity = wire.Identity.ToRuntime(),
            }, context.RequestAborted).ConfigureAwait(false);
        if (!result.TryGetValue(out BaseActivationPrunePage? page) || page is null)
        {
            await WriteFailure(context, result.Status, (result as BaseFailure<BaseActivationPrunePage>)?.Error).ConfigureAwait(false);
            return;
        }
        await Results.Json(new BaseActivationRemovalHttpResult
        {
            ActivationIds = page.Items.Select(static item => item.ActivationId).ToImmutableArray(), NextActivationId = page.NextActivationId,
            Completed = page.Completed, Disposition = page.Disposition,
        }, BaseActivationAdministrationJsonContext.Default.BaseActivationRemovalHttpResult).ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async Task MigrateAsync(HttpContext context)
    {
        BaseActivationMigrationHttpRequest? wire = await ReadAsync(
            context, BaseActivationAdministrationJsonContext.Default.BaseActivationMigrationHttpRequest).ConfigureAwait(false);
        if (wire is null) { await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false); return; }
        BaseResult<BaseActivationMigrationResult> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .MigrateActivationAsync(new BaseActivationAdministrationMigrationRequest
            {
                StoreId = wire.StoreId, Principal = await Principal(context).ConfigureAwait(false),
                Scope = new BaseOwnedSubjectScopeEvidence { Kind = wire.ScopeKind, Value = wire.ScopeValue },
                MigrationId = wire.MigrationId, MigrationVersion = wire.MigrationVersion,
                ActivationId = wire.ActivationId, ExpectedGeneration = wire.ExpectedGeneration,
                DueAt = wire.DueAtUnixMilliseconds is long due ? DateTimeOffset.FromUnixTimeMilliseconds(due) : null,
                Identity = wire.Identity.ToRuntime(),
            }, context.RequestAborted).ConfigureAwait(false);
        if (!result.TryGetValue(out BaseActivationMigrationResult? value) || value is null)
        {
            await WriteFailure(context, result.Status, (result as BaseFailure<BaseActivationMigrationResult>)?.Error).ConfigureAwait(false);
            return;
        }
        await Results.Json(new BaseActivationMigrationHttpResult
        {
            SourceActivationId = value.SourceActivationId, SourceGeneration = value.SourceGeneration,
            ReplacementActivationId = value.ReplacementActivationId, ReplacementGeneration = value.ReplacementGeneration,
            Disposition = value.Disposition,
        }, BaseActivationAdministrationJsonContext.Default.BaseActivationMigrationHttpResult).ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async Task RepairAsync(HttpContext context)
    {
        BaseActivationRepairHttpRequest? wire = await ReadAsync(
            context, BaseActivationAdministrationJsonContext.Default.BaseActivationRepairHttpRequest).ConfigureAwait(false);
        if (wire is null) { await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false); return; }
        BaseResult<BaseActivationQuarantinePage> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .ExecuteActivationRepairAsync(new BaseActivationAdministrationRepairRequest
            {
                StoreId = wire.StoreId, Principal = await Principal(context).ConfigureAwait(false),
                DefinitionId = wire.DefinitionId, DefinitionVersion = wire.DefinitionVersion,
                Kind = wire.Kind, AfterSequence = wire.AfterSequence, Take = wire.Take,
            }, context.RequestAborted).ConfigureAwait(false);
        if (!result.TryGetValue(out BaseActivationQuarantinePage? page) || page is null)
        {
            await WriteFailure(context, result.Status, (result as BaseFailure<BaseActivationQuarantinePage>)?.Error).ConfigureAwait(false);
            return;
        }
        await Results.Json(new BaseActivationRepairHttpResult
        {
            Items = page.Items.Select(static item => new BaseActivationQuarantineItemHttpResult
            { Sequence = item.Sequence, Operation = item.Operation, RetainedAt = item.RetainedAt }).ToImmutableArray(),
            NextSequence = page.NextSequence,
        }, BaseActivationAdministrationJsonContext.Default.BaseActivationRepairHttpResult).ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async ValueTask<T?> ReadAsync<T>(HttpContext context, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        const int maximum = 1024 * 1024;
        if (context.Request.ContentLength is > maximum) return default;
        await using var buffer = new MemoryStream(capacity: 4096);
        byte[] chunk = new byte[8192];
        while (true)
        {
            int read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > maximum) return default;
            await buffer.WriteAsync(chunk.AsMemory(0, read), context.RequestAborted).ConfigureAwait(false);
        }
        if (buffer.Length is <= 0 or > maximum) return default;
        buffer.Position = 0;
        try { return await JsonSerializer.DeserializeAsync(buffer, typeInfo, context.RequestAborted).ConfigureAwait(false); }
        catch (JsonException) { return default; }
    }

    private static ValueTask<PrincipalContext> Principal(HttpContext context) =>
        context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>()
            .CreateAsync(context, context.RequestAborted);

    private static async Task Write(HttpContext context, BaseResult<BaseActivationTransitionResult> result)
    {
        if (!result.TryGetValue(out BaseActivationTransitionResult? value) || value is null)
        {
            BaseError? error = (result as BaseFailure<BaseActivationTransitionResult>)?.Error;
            int status = result.Status switch
            {
                OperationStatus.ValidationFailed => 400, OperationStatus.PolicyDenied => 403,
                OperationStatus.NotFound => 404, OperationStatus.Conflict => 409,
                OperationStatus.Unsupported or OperationStatus.CapabilityUnavailable => 424, _ => 500,
            };
            await Problem(context, status, error?.Code ?? "base.activation.storeError").ConfigureAwait(false);
            return;
        }
        var wire = new BaseActivationControlHttpResult
        {
            Generation = value.Generation,
            State = value.State,
            Disposition = value.Disposition,
        };
        await Results.Json(wire, BaseActivationAdministrationJsonContext.Default.BaseActivationControlHttpResult)
            .ExecuteAsync(context).ConfigureAwait(false);
    }

    private static Task Problem(HttpContext context, int status, string code) =>
        Results.Problem(statusCode: status, title: "The activation operation failed.",
            extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);

    private static Task WriteFailure(HttpContext context, OperationStatus status, BaseError? error) => Problem(context, status switch
    {
        OperationStatus.ValidationFailed => 400, OperationStatus.PolicyDenied => 403,
        OperationStatus.NotFound => 404, OperationStatus.Conflict => 409,
        OperationStatus.Unsupported or OperationStatus.CapabilityUnavailable => 424, _ => 500,
    }, error?.Code ?? "base.activation.storeError");
}

internal sealed record BaseActivationRetryHttpRequest
{
    public required string StoreId { get; init; }
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required string ActivationId { get; init; }
    public required long ExpectedGeneration { get; init; }
    public long? DueAtUnixMilliseconds { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

internal sealed record BaseActivationQueryHttpRequest
{
    public required string StoreId { get; init; }
    public required BaseSubjectScopeKind ScopeKind { get; init; }
    public string? ScopeValue { get; init; }
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required BaseActivationStateSelector States { get; init; }
    public BaseActivationQueryBoundaryHttp? After { get; init; }
    public required int Take { get; init; }
}

internal sealed record BaseActivationQueryBoundaryHttp
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required long EffectiveDueAt { get; init; }
    public required string ActivationId { get; init; }
}

internal sealed record BaseActivationQueryItemHttpResult
{
    public required string ActivationId { get; init; }
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required BaseActivationState State { get; init; }
    public required long Generation { get; init; }
    public required long EffectiveDueAt { get; init; }
    public string? OccurrenceId { get; init; }
    public required int AttemptNumber { get; init; }
    public required bool ResultRetained { get; init; }
    public required bool EffectAuthorityRetained { get; init; }
}

internal sealed record BaseActivationQueryHttpResult
{
    public required ImmutableArray<BaseActivationQueryItemHttpResult> Items { get; init; }
    public BaseActivationQueryBoundaryHttp? Next { get; init; }
}

internal sealed record BaseActivationReconcileHttpRequest
{
    public required string StoreId { get; init; }
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required string ActivationId { get; init; }
    public required long ExpectedGeneration { get; init; }
    public required long ExpectedEffectStartGeneration { get; init; }
    public required ImmutableArray<byte> ExpectedEffectChecksum { get; init; }
    public required BaseEffectReconciliationDisposition Disposition { get; init; }
    public required ImmutableArray<byte> VerificationEvidence { get; init; }
    public required ImmutableArray<byte> VerificationChecksum { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

internal sealed record BaseActivationDisposeHttpRequest
{
    public required string StoreId { get; init; }
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required string ActivationId { get; init; }
    public required long ExpectedGeneration { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

internal sealed record BaseActivationIdentityHttpRequest
{
    public required string Scope { get; init; }
    public required string Operation { get; init; }
    public required string IdempotencyKey { get; init; }
    public required ImmutableArray<byte> Fingerprint { get; init; }

    internal BaseMutationRequestIdentity ToRuntime() => BaseMutationRequestIdentity.Create(
        Scope,
        Operation,
        IdempotencyKey,
        BaseMutationRequestFingerprint.Create(Fingerprint.AsSpan()));
}

internal sealed record BaseActivationControlHttpResult
{
    public required long Generation { get; init; }
    public required BaseActivationState State { get; init; }
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

internal abstract record BaseActivationPageHttpRequest
{
    public required string StoreId { get; init; }
    public required BaseSubjectScopeKind ScopeKind { get; init; }
    public string? ScopeValue { get; init; }
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public string? AfterActivationId { get; init; }
    public required int Take { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

internal sealed record BaseActivationMaintenanceHttpRequest : BaseActivationPageHttpRequest
{
    public required BaseActivationMaintenanceKind Kind { get; init; }
}

internal sealed record BaseActivationRemovalHttpRequest : BaseActivationPageHttpRequest;

internal sealed record BaseActivationMaintenanceItemHttpResult
{
    public required string ActivationId { get; init; }
    public required long PreviousGeneration { get; init; }
    public required long ResultingGeneration { get; init; }
    public required BaseActivationState PreviousState { get; init; }
    public required BaseActivationState ResultingState { get; init; }
    public required ImmutableArray<byte> ControlChecksum { get; init; }
}

internal sealed record BaseActivationMaintenanceHttpResult
{
    public required ImmutableArray<BaseActivationMaintenanceItemHttpResult> Items { get; init; }
    public string? NextActivationId { get; init; }
    public required bool Completed { get; init; }
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

internal sealed record BaseActivationRemovalHttpResult
{
    public required ImmutableArray<string> ActivationIds { get; init; }
    public string? NextActivationId { get; init; }
    public required bool Completed { get; init; }
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

internal sealed record BaseActivationMigrationHttpRequest
{
    public required string StoreId { get; init; }
    public required BaseSubjectScopeKind ScopeKind { get; init; }
    public string? ScopeValue { get; init; }
    public required string MigrationId { get; init; }
    public required int MigrationVersion { get; init; }
    public required string ActivationId { get; init; }
    public required long ExpectedGeneration { get; init; }
    public long? DueAtUnixMilliseconds { get; init; }
    public required BaseActivationIdentityHttpRequest Identity { get; init; }
}

internal sealed record BaseActivationMigrationHttpResult
{
    public required string SourceActivationId { get; init; }
    public required long SourceGeneration { get; init; }
    public required string ReplacementActivationId { get; init; }
    public required long ReplacementGeneration { get; init; }
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

internal sealed record BaseActivationRepairHttpRequest
{
    public required string StoreId { get; init; }
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required BaseActivationRepairKind Kind { get; init; }
    public long? AfterSequence { get; init; }
    public required int Take { get; init; }
}

internal sealed record BaseActivationQuarantineItemHttpResult
{
    public required long Sequence { get; init; }
    public required string Operation { get; init; }
    public required DateTimeOffset RetainedAt { get; init; }
}

internal sealed record BaseActivationRepairHttpResult
{
    public required ImmutableArray<BaseActivationQuarantineItemHttpResult> Items { get; init; }
    public long? NextSequence { get; init; }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationRetryHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationQueryHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationQueryHttpResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationReconcileHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationDisposeHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationControlHttpResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationMaintenanceHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationMaintenanceHttpResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationRemovalHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationRemovalHttpResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationMigrationHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationMigrationHttpResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationRepairHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseActivationRepairHttpResult))]
internal partial class BaseActivationAdministrationJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
