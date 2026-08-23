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
        group.MapPost("/control/semantic-activations/maintenance", (RequestDelegate)ExecuteAsync)
            .WithHPDBaseEndpoint("base.semanticActivation.maintenance.execute", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.SemanticActivationMaintenanceExecute, HPDBaseCapabilities.SemanticActivationMaintenance, convention)
            .WithName("base.semanticActivation.maintenance.execute");
        group.MapPost("/control/semantic-activations/maintenance/resolve", (RequestDelegate)ResolveAsync)
            .WithHPDBaseEndpoint("base.semanticActivation.maintenance.resolve", HPDBaseEndpointAudience.ControlPlane,
                HPDBaseEndpointOperation.SemanticActivationMaintenanceResolve, HPDBaseCapabilities.SemanticActivationMaintenance, convention)
            .WithName("base.semanticActivation.maintenance.resolve");
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

    private static async Task ExecuteAsync(HttpContext context)
    {
        BaseSemanticActivationMaintenanceHttpRequest? wire = await ReadAsync(context,
            BaseSemanticActivationAdministrationJsonContext.Default.BaseSemanticActivationMaintenanceHttpRequest).ConfigureAwait(false);
        if (wire?.Request is null) { await Problem(context, 400, BaseSemanticActivationErrorCodes.Invalid).ConfigureAwait(false); return; }
        BaseResult<BaseSemanticActivationMaintenanceResult> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .ExecuteSemanticActivationMaintenanceAsync(wire.StoreId, await Principal(context).ConfigureAwait(false), wire.Request,
                context.RequestAborted).ConfigureAwait(false);
        await WriteMaintenance(context, result).ConfigureAwait(false);
    }

    private static async Task ResolveAsync(HttpContext context)
    {
        BaseSemanticActivationMaintenanceResolutionHttpRequest? wire = await ReadAsync(context,
            BaseSemanticActivationAdministrationJsonContext.Default.BaseSemanticActivationMaintenanceResolutionHttpRequest).ConfigureAwait(false);
        if (wire?.Request is null) { await Problem(context, 400, BaseSemanticActivationErrorCodes.Invalid).ConfigureAwait(false); return; }
        BaseResult<BaseSemanticActivationMaintenanceResult> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>()
            .ResolveSemanticActivationMaintenanceAsync(wire.StoreId, await Principal(context).ConfigureAwait(false), wire.Request,
                context.RequestAborted).ConfigureAwait(false);
        await WriteMaintenance(context, result).ConfigureAwait(false);
    }

    private static async Task WriteMaintenance(HttpContext context, BaseResult<BaseSemanticActivationMaintenanceResult> result)
    {
        if (result is BaseFailure<BaseSemanticActivationMaintenanceResult> failure)
        { await WriteFailure(context, failure.Status, failure.Error).ConfigureAwait(false); return; }
        await Results.Json(((BaseSuccess<BaseSemanticActivationMaintenanceResult>)result).Value,
            BaseSemanticActivationAdministrationJsonContext.Default.BaseSemanticActivationMaintenanceResult)
            .ExecuteAsync(context).ConfigureAwait(false);
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

    private static Task WriteFailure(HttpContext context, OperationStatus status, BaseError error) =>
        Problem(context, status switch
        {
            OperationStatus.ValidationFailed => 400, OperationStatus.PolicyDenied or OperationStatus.Unauthorized => 403,
            OperationStatus.NotFound => 404, OperationStatus.Conflict => 409,
            OperationStatus.Unsupported or OperationStatus.CapabilityUnavailable => 424, _ => 500,
        }, error.Code);

    private static Task Problem(HttpContext context, int status, string code) =>
        Results.Problem(statusCode: status, title: "The semantic activation operation failed.",
            extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);

    private static readonly BaseSemanticActivationExecutionLimits SemanticInspectionWireLimits = new()
    {
        MaximumOperations = 1, MaximumScopeDirectoryReads = 1, MaximumSlotReads = 1,
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

internal sealed record BaseSemanticActivationMaintenanceHttpRequest
{
    public required string StoreId { get; init; }
    public required BaseSemanticActivationMaintenanceRequest Request { get; init; }
}

internal sealed record BaseSemanticActivationMaintenanceResolutionHttpRequest
{
    public required string StoreId { get; init; }
    public required BaseSemanticActivationMaintenanceResolutionRequest Request { get; init; }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSemanticActivationInspectionRequestWire))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSemanticActivationInspectionPageWire))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSemanticActivationMaintenanceHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSemanticActivationMaintenanceResolutionHttpRequest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseSemanticActivationMaintenanceResult))]
internal partial class BaseSemanticActivationAdministrationJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
