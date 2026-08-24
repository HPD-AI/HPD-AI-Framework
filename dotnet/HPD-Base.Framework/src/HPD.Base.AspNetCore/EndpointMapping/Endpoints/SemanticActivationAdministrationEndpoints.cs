using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

internal static class SemanticActivationAdministrationEndpoints
{
    internal static void Map(RouteGroupBuilder group,
        Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor> convention)
    {
        group.MapPost("/control/stores/{storeId}/semantic-activations/query", (RequestDelegate)InspectAsync)
            .WithHPDBaseEndpoint("base.semanticActivation.inspect", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.SemanticActivationInspect, HPDBaseCapabilities.SemanticActivationInspect, convention)
            .WithName("base.semanticActivation.inspect");
        group.MapPost("/control/stores/{storeId}/semantic-activations/{definitionId}/control", (RequestDelegate)ReadControlAsync)
            .WithHPDBaseEndpoint("base.semanticActivation.control.read", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.SemanticActivationControlRead, HPDBaseCapabilities.SemanticActivationMaintenance, convention)
            .WithName("base.semanticActivation.control.read");
        group.MapPost("/control/stores/{storeId}/semantic-activations:compact", (RequestDelegate)CompactAsync)
            .WithHPDBaseEndpoint("base.semanticActivation.compact", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.SemanticActivationCompact, HPDBaseCapabilities.SemanticActivationMaintenance, convention)
            .WithName("base.semanticActivation.compact");
        group.MapPost("/control/stores/{storeId}/semantic-activations/maintenance:resume", (RequestDelegate)ResumeAsync)
            .WithHPDBaseEndpoint("base.semanticActivation.maintenance.resume", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.SemanticActivationMaintenanceResume, HPDBaseCapabilities.SemanticActivationMaintenance, convention)
            .WithName("base.semanticActivation.maintenance.resume");
        group.MapPost("/control/stores/{storeId}/semantic-activations/maintenance:resolve", (RequestDelegate)ResolveAsync)
            .WithHPDBaseEndpoint("base.semanticActivation.maintenance.resolve", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.SemanticActivationMaintenanceResolve, HPDBaseCapabilities.SemanticActivationMaintenance, convention)
            .WithName("base.semanticActivation.maintenance.resolve");
        group.MapPost("/control/stores/{storeId}/semantic-activations:remove", (RequestDelegate)RemoveAsync)
            .WithHPDBaseEndpoint("base.semanticActivation.remove", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.SemanticActivationRemove, HPDBaseCapabilities.SemanticActivationMaintenance, convention)
            .WithName("base.semanticActivation.remove");
    }

    private static async Task InspectAsync(HttpContext context)
    {
        BaseSemanticActivationInspectionRequestWire? wire = await ReadAsync(context,
            BaseSemanticActivationAdministrationJsonContext.Default.BaseSemanticActivationInspectionRequestWire).ConfigureAwait(false);
        if (wire is null) { await Problem(context, 400, BaseSemanticActivationErrorCodes.Invalid).ConfigureAwait(false); return; }
        BaseSemanticActivationInspectionToken? after;
        try { after = wire.After is null ? null : BaseSemanticActivationInspectionToken.FromWire(wire.After); }
        catch (FormatException) { await Problem(context, 400, BaseSemanticActivationErrorCodes.Invalid).ConfigureAwait(false); return; }
        var request = new BaseSemanticActivationInspectionRequest
        {
            Definition = new() { Id = wire.DefinitionId, Version = wire.DefinitionVersion, Checksum = wire.DefinitionChecksum.ToImmutableArray() },
            State = wire.State is null ? null : Enum.IsDefined(typeof(BaseSemanticActivationSlotState), wire.State.Value)
                ? (BaseSemanticActivationSlotState)wire.State.Value : (BaseSemanticActivationSlotState)(-1),
            After = after, Take = wire.Take,
            // The Runtime replaces this closed minimum request shape with the installed/provider/platform intersection.
            Limits = SemanticInspectionWireLimits,
        };
        BaseResult<BaseSemanticActivationInspectionPage> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .InspectSemanticActivationsAsync(context.Request.RouteValues["storeId"] as string ?? string.Empty,
                await Principal(context).ConfigureAwait(false), request,
                context.RequestAborted).ConfigureAwait(false);
        if (result is BaseFailure<BaseSemanticActivationInspectionPage> failure)
        { await WriteFailure(context, failure.Status, failure.Error).ConfigureAwait(false); return; }
        BaseSemanticActivationInspectionPage page = ((BaseSuccess<BaseSemanticActivationInspectionPage>)result).Value;
        await Results.Json(new BaseSemanticActivationInspectionPageWire
        {
            Items = page.Items.Select(static item => new BaseSemanticActivationInspectionItemWire
            {
                State = (int)item.State, SlotGeneration = item.SlotGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ItemToken = item.ItemToken.Value, RetirementPosition = item.RetirementPosition?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SanitizedChecksum = item.SanitizedChecksum.ToArray(),
            }).ToImmutableArray(),
            Next = page.Next?.Value,
            CapturedAuthorityGeneration = page.CapturedAuthorityGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Checksum = page.Checksum.ToArray(),
        }, BaseSemanticActivationAdministrationJsonContext.Default.BaseSemanticActivationInspectionPageWire)
            .ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async Task ReadControlAsync(HttpContext context)
    {
        BaseSemanticActivationControlReadWire? wire = await ReadAsync(context,
            BaseSemanticActivationAdministrationJsonContext.Default.BaseSemanticActivationControlReadWire).ConfigureAwait(false);
        string storeId = context.Request.RouteValues["storeId"] as string ?? string.Empty;
        string definitionId = context.Request.RouteValues["definitionId"] as string ?? string.Empty;
        if (wire is null) { await Problem(context, 400, BaseSemanticActivationErrorCodes.Invalid).ConfigureAwait(false); return; }
        BaseResult<BaseSemanticActivationControlDescriptor> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .ReadSemanticActivationControlAsync(storeId, await Principal(context).ConfigureAwait(false), new()
            { Id = definitionId, Version = wire.DefinitionVersion, Checksum = wire.DefinitionChecksum.ToImmutableArray() }, context.RequestAborted).ConfigureAwait(false);
        if (result is BaseFailure<BaseSemanticActivationControlDescriptor> failure)
        { await WriteFailure(context, failure.Status, failure.Error).ConfigureAwait(false); return; }
        BaseSemanticActivationControlDescriptor value = ((BaseSuccess<BaseSemanticActivationControlDescriptor>)result).Value;
        await Results.Json(new BaseSemanticActivationControlDescriptorWire
        {
            DefinitionId = value.DefinitionId, DefinitionVersion = value.DefinitionVersion,
            AuthorityGeneration = value.AuthorityGeneration?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LiveCount = value.LiveCount?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RetiredCount = value.RetiredCount?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            AbsenceCount = value.AbsenceCount?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Ready = value.Ready, Quarantined = value.Quarantined, CompactToken = value.Compact?.Value,
            RemoveToken = value.Remove?.Value,
        }, BaseSemanticActivationAdministrationJsonContext.Default.BaseSemanticActivationControlDescriptorWire).ExecuteAsync(context).ConfigureAwait(false);
    }

    private static Task CompactAsync(HttpContext context) => ExecuteControlAsync(context, "compact-retired-semantic-authority");
    private static Task RemoveAsync(HttpContext context) => ExecuteControlAsync(context, "remove-semantic-definition");
    private static Task ResumeAsync(HttpContext context) => ExecuteControlAsync(context, "resume-semantic-maintenance");

    private static async Task ExecuteControlAsync(HttpContext context, string expectedConfirmation)
    {
        BaseSemanticActivationControlCommandWire? wire = await ReadAsync(context,
            BaseSemanticActivationAdministrationJsonContext.Default.BaseSemanticActivationControlCommandWire).ConfigureAwait(false);
        if (wire is null || wire.Confirmation != expectedConfirmation) { await Problem(context, 400, BaseSemanticActivationErrorCodes.Invalid).ConfigureAwait(false); return; }
        BaseSemanticActivationControlToken token;
        try { token = BaseSemanticActivationControlToken.FromWire(wire.CommandToken); }
        catch (FormatException) { await Problem(context, 400, BaseSemanticActivationErrorCodes.Invalid).ConfigureAwait(false); return; }
        BaseResult<BaseSemanticActivationControlResult> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .ExecuteSemanticActivationControlAsync(context.Request.RouteValues["storeId"] as string ?? string.Empty,
                await Principal(context).ConfigureAwait(false), new() { Token = token, IdempotencyKey = wire.IdempotencyKey, Confirmation = wire.Confirmation },
                context.RequestAborted).ConfigureAwait(false);
        await WriteControl(context, result).ConfigureAwait(false);
    }

    private static async Task ResolveAsync(HttpContext context)
    {
        BaseSemanticActivationControlResolutionWire? wire = await ReadAsync(context,
            BaseSemanticActivationAdministrationJsonContext.Default.BaseSemanticActivationControlResolutionWire).ConfigureAwait(false);
        if (wire is null) { await Problem(context, 400, BaseSemanticActivationErrorCodes.Invalid).ConfigureAwait(false); return; }
        BaseSemanticActivationControlToken token;
        try { token = BaseSemanticActivationControlToken.FromWire(wire.ResolutionToken); }
        catch (FormatException) { await Problem(context, 400, BaseSemanticActivationErrorCodes.Invalid).ConfigureAwait(false); return; }
        BaseResult<BaseSemanticActivationControlResult> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .ResolveSemanticActivationControlAsync(context.Request.RouteValues["storeId"] as string ?? string.Empty,
                await Principal(context).ConfigureAwait(false), new() { Token = token }, context.RequestAborted).ConfigureAwait(false);
        await WriteControl(context, result).ConfigureAwait(false);
    }

    private static async Task WriteControl(HttpContext context, BaseResult<BaseSemanticActivationControlResult> result)
    {
        if (result is BaseFailure<BaseSemanticActivationControlResult> failure)
        { await WriteFailure(context, failure.Status, failure.Error).ConfigureAwait(false); return; }
        BaseSemanticActivationControlResult value = ((BaseSuccess<BaseSemanticActivationControlResult>)result).Value;
        await Results.Json(new BaseSemanticActivationControlResultWire
        {
            Disposition = value.Disposition.ToString(), AuthorityGeneration = value.AuthorityGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ExaminedRows = value.ExaminedRows.ToString(System.Globalization.CultureInfo.InvariantCulture), ChangedRows = value.ChangedRows.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CanonicalBytes = value.CanonicalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), ReceiptDisposition = value.ReceiptDisposition?.ToString(),
            ResumeToken = value.Resume?.Value, ResolutionToken = value.Resolution?.Value, SanitizedChecksum = value.SanitizedChecksum.ToArray(),
        }, BaseSemanticActivationAdministrationJsonContext.Default.BaseSemanticActivationControlResultWire).ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async ValueTask<T?> ReadAsync<T>(HttpContext context,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        const int maximum = 1024 * 1024;
        if (context.Request.ContentLength is > maximum) return default;
        await using var buffer = new MemoryStream(4096); byte[] chunk = new byte[8192];
        while (true)
        {
            int read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > maximum) return default;
            await buffer.WriteAsync(chunk.AsMemory(0, read), context.RequestAborted).ConfigureAwait(false);
        }
        if (buffer.Length == 0) return default;
        buffer.Position = 0;
        try { return await JsonSerializer.DeserializeAsync(buffer, typeInfo, context.RequestAborted).ConfigureAwait(false); }
        catch (JsonException) { return default; }
    }

    private static ValueTask<PrincipalContext> Principal(HttpContext context) =>
        context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>().CreateAsync(context, context.RequestAborted);

    private static Task WriteFailure(HttpContext context, OperationStatus status, BaseError error)
    {
        (int httpStatus, string code, string message) = FailureMapping(status, error);
        return Problem(context, httpStatus, code, message);
    }

    internal static (int HttpStatus, string Code, string Message) FailureMapping(OperationStatus status, BaseError error) =>
        (status, error.Category, error.Code) switch
    {
        (OperationStatus.PolicyDenied, ErrorCategory.Authorization, BaseSemanticActivationErrorCodes.Unauthorized) => (404, error.Code, "The requested operation is unavailable."),
        (OperationStatus.ValidationFailed, ErrorCategory.Validation, BaseSemanticActivationErrorCodes.NotInstalled) => (400, error.Code, "The semantic activation contract is unavailable."),
        (OperationStatus.ValidationFailed, ErrorCategory.Validation, BaseSemanticActivationErrorCodes.Invalid) => (400, error.Code, "The semantic activation request is invalid."),
        (OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.FingerprintConflict) => (409, error.Code, "The semantic identity was used with different activation semantics."),
        (OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.ActivationNotTerminal) => (409, error.Code, "The semantic activation is not terminal."),
        (OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.GuardLost) => (409, error.Code, "The activation child authority is no longer current."),
        (OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.RestoreConflict) => (409, error.Code, "The semantic activation restore authority changed."),
        (OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.GraphChanged) => (409, error.Code, "The semantic activation contract changed."),
        (OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.CapacityUnavailable) => (409, error.Code, "Semantic activation capacity is unavailable."),
        (OperationStatus.ValidationFailed, ErrorCategory.Validation, BaseSemanticActivationErrorCodes.BudgetExceeded) => (413, error.Code, "The semantic activation operation exceeded its installed limits."),
        (OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.CancelledBeforeInfluence) => (408, error.Code, "The semantic activation operation was cancelled."),
        (OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.CancelledRolledBack) => (408, error.Code, "The semantic activation operation was cancelled and rolled back."),
        (OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.AcquisitionTimeout) => (503, error.Code, "Semantic activation authority acquisition timed out."),
        (OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.TransactionTimeout) => (503, error.Code, "The semantic activation transaction timed out and was rolled back."),
        (OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.CommitIndeterminate) => (503, error.Code, "The semantic activation commit outcome requires reconciliation."),
        (OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.ReceiptResolutionTimeout) => (503, error.Code, "The semantic activation receipt could not be resolved in time."),
        (OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.ExternalPublicationPending) => (503, error.Code, "Semantic activation recovery publication requires reconciliation."),
        (OperationStatus.CapabilityUnavailable, ErrorCategory.Capability, BaseSemanticActivationErrorCodes.ExternalAuthorityUnavailable) => (503, error.Code, "Semantic activation recovery authority is unavailable."),
        (OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.MaintenanceTimeout) => (503, error.Code, "Semantic activation maintenance did not complete in time."),
        (OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.MaintenanceIndeterminate) => (503, error.Code, "Semantic activation maintenance requires reconciliation."),
        (OperationStatus.CapabilityUnavailable, ErrorCategory.Capability, BaseSemanticActivationErrorCodes.RecoveryProofUnavailable) => (503, error.Code, "Semantic activation recovery proof is unavailable."),
        (OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.RecoveryProofInvalid) => (503, error.Code, "Semantic activation recovery proof is invalid."),
        (OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.CompactionBlocked) => (409, error.Code, "Semantic activation compaction is not currently permitted."),
        (OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.MigrationBlocked) => (409, error.Code, "Semantic activation migration requirements are not satisfied."),
        (OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.RemovalBlocked) => (409, error.Code, "The semantic activation definition cannot be removed."),
        (OperationStatus.CapabilityUnavailable, ErrorCategory.Capability, BaseSemanticActivationErrorCodes.CapabilityUnavailable) => (424, error.Code, "The semantic activation capability is unavailable."),
        (OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.Quarantined) => (503, error.Code, "Semantic activation authority is quarantined pending recovery."),
        (OperationStatus.CapabilityUnavailable, ErrorCategory.Capability, BaseSemanticActivationErrorCodes.ProviderContractInvalid) => (424, error.Code, "The semantic activation provider returned invalid evidence."),
        (OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.Corrupt) => (503, error.Code, "Semantic activation authority requires operator attention."),
        _ => (424, BaseSemanticActivationErrorCodes.ProviderContractInvalid, "The semantic activation provider returned invalid evidence."),
    };

    private static Task Problem(HttpContext context, int status, string code, string message = "The semantic activation operation failed.") =>
        Results.Problem(statusCode: status, title: message,
            extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);

    private static readonly BaseSemanticActivationExecutionLimits SemanticInspectionWireLimits = new()
    {
        MaximumOperations = 1, MaximumScopeDirectoryReads = 1, MaximumSlotReads = 256,
        MaximumActivationReads = 1, MaximumReadIntervals = 4096, MaximumIndexOperations = 8192,
        MaximumActivationBytes = 1_048_576, MaximumScopeDirectoryBytes = 65_536, MaximumEvidenceBytes = 1_048_576,
        MaximumReceiptBytes = 1_048_576, MaximumTransientBytes = 8_388_608,
    };
}

/// <summary>Strict ControlPlane wire request for semantic activation inspection.</summary>
public sealed record BaseSemanticActivationInspectionRequestWire
{
    /// <summary>Gets the installed semantic definition identifier.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets the positive definition version.</summary>
    public required int DefinitionVersion { get; init; }
    /// <summary>Gets the exact definition checksum.</summary>
    public required byte[] DefinitionChecksum { get; init; }
    /// <summary>Gets the optional integer slot-state filter.</summary>
    public int? State { get; init; }
    /// <summary>Gets the optional opaque continuation.</summary>
    public string? After { get; init; }
    /// <summary>Gets the requested page size.</summary>
    public required int Take { get; init; }
}

/// <summary>Strict sanitized ControlPlane wire item.</summary>
public sealed record BaseSemanticActivationInspectionItemWire
{
    /// <summary>Gets the integer slot state.</summary>
    public required int State { get; init; }
    /// <summary>Gets the positive slot generation as a canonical integer string.</summary>
    public required string SlotGeneration { get; init; }
    /// <summary>Gets the opaque sanitized item token.</summary>
    public required string ItemToken { get; init; }
    /// <summary>Gets the optional retirement position as a canonical integer string.</summary>
    public string? RetirementPosition { get; init; }
    /// <summary>Gets the sanitized item checksum.</summary>
    public required byte[] SanitizedChecksum { get; init; }
}

/// <summary>Strict sanitized ControlPlane wire page.</summary>
public sealed record BaseSemanticActivationInspectionPageWire
{
    /// <summary>Gets ordered sanitized items.</summary>
    public required ImmutableArray<BaseSemanticActivationInspectionItemWire> Items { get; init; }
    /// <summary>Gets the optional opaque next continuation.</summary>
    public string? Next { get; init; }
    /// <summary>Gets captured authority generation as a canonical integer string.</summary>
    public required string CapturedAuthorityGeneration { get; init; }
    /// <summary>Gets the sanitized page checksum.</summary>
    public required byte[] Checksum { get; init; }
}

internal sealed record BaseSemanticActivationControlReadWire
{
    public required int DefinitionVersion { get; init; }
    public required byte[] DefinitionChecksum { get; init; }
}

internal sealed record BaseSemanticActivationControlDescriptorWire
{
    public required string DefinitionId { get; init; }
    public required int DefinitionVersion { get; init; }
    public required string? AuthorityGeneration { get; init; }
    public required string? LiveCount { get; init; }
    public required string? RetiredCount { get; init; }
    public required string? AbsenceCount { get; init; }
    public required bool Ready { get; init; }
    public required bool Quarantined { get; init; }
    public string? CompactToken { get; init; }
    public string? RemoveToken { get; init; }
}

internal sealed record BaseSemanticActivationControlCommandWire
{
    public required string CommandToken { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string Confirmation { get; init; }
}

internal sealed record BaseSemanticActivationControlResolutionWire
{
    public required string ResolutionToken { get; init; }
}

internal sealed record BaseSemanticActivationControlResultWire
{
    public required string Disposition { get; init; }
    public required string AuthorityGeneration { get; init; }
    public required string ExaminedRows { get; init; }
    public required string ChangedRows { get; init; }
    public required string CanonicalBytes { get; init; }
    public string? ReceiptDisposition { get; init; }
    public string? ResumeToken { get; init; }
    public string? ResolutionToken { get; init; }
    public required byte[] SanitizedChecksum { get; init; }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSemanticActivationInspectionRequestWire))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSemanticActivationInspectionPageWire))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSemanticActivationControlReadWire))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSemanticActivationControlDescriptorWire))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSemanticActivationControlCommandWire))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSemanticActivationControlResolutionWire))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSemanticActivationControlResultWire))]
internal partial class BaseSemanticActivationAdministrationJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
