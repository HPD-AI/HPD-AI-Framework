using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HPD.Gateway.Management;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway.Admin;

public static partial class GatewayAdminEndpointRouteBuilderExtensions
{
    private static void MapAdditional(
        RouteGroupBuilder group, GatewayAdminEndpointOptions options,
        IGatewayAdminSecurityMetadataProvider? security)
    {
        Map(group, options, security, "revision", context => ReadRecord(context, true));
        Map(group, options, security, "validation", context => ReadRecord(context, false));
        Map(group, options, security, "activate", Activate);
        Map(group, options, security, "rollback", Rollback);
        Map(group, options, security, "activations", Activations);
        Map(group, options, security, "compare", Compare);
        Map(group, options, security, "export", Export);
        Map(group, options, security, "import", context => Import(context, false));
        Map(group, options, security, "import-and-activate", context => Import(context, true));
        Map(group, options, security, "operation", Operation);
        Map(group, options, security, "backup", Backup);
        Map(group, options, security, "purge", Purge);
        Map(group, options, security, "effective", Effective);
    }

    private static async Task ReadRecord(HttpContext context, bool revision)
    {
        string ns = Route(context, "ns");
        string target = Route(context, "target");
        string id = Route(context, revision ? "revision" : "validation");
        if (!await AdmitTarget(context, ns, target, id).ConfigureAwait(false)) return;
        IGatewayManagementReader reader = context.RequestServices.GetRequiredService<IGatewayManagementReader>();
        if (revision)
        {
            GatewayManagedRecord<GatewayAcceptedRevision>? value = await reader.GetRevisionAsync(ns, id, context.RequestAborted).ConfigureAwait(false);
            await Write(context, value is null ? NotFound(context) : TypedResults.Json(value,
                GatewayAdminJsonContext.Default.GatewayManagedRecordGatewayAcceptedRevision)).ConfigureAwait(false);
        }
        else
        {
            GatewayManagedRecord<GatewayValidationRecord>? value = await reader.GetValidationAsync(ns, id, context.RequestAborted).ConfigureAwait(false);
            await Write(context, value is null ? NotFound(context) : TypedResults.Json(value,
                GatewayAdminJsonContext.Default.GatewayManagedRecordGatewayValidationRecord)).ConfigureAwait(false);
        }
    }

    private static async Task Activate(HttpContext context)
    {
        string ns = Route(context, "ns"), target = Route(context, "target"), revision = Route(context, "revision");
        if (!await AdmitTarget(context, ns, target, revision).ConfigureAwait(false)) return;
        if (!TryMutationHeaders(context, true, out string key, out string? expected, out IResult? failure))
        { await Write(context, failure!); return; }
        GatewayAdminRequestAttribution attribution = await Attribution(context, GatewayAdminCapabilities.ActivationWrite).ConfigureAwait(false);
        GatewayManagementCommandResult result = await context.RequestServices.GetRequiredService<IGatewayManagementCommandCoordinator>()
            .ActivateRevisionAsync(new(ns, target, revision, key, attribution.ToActor(), attribution.CorrelationId, expected), context.RequestAborted)
            .ConfigureAwait(false);
        await Write(context, ProjectCommand(context, result, CommandProjection.Activation)).ConfigureAwait(false);
    }

    private static async Task Rollback(HttpContext context)
    {
        string ns = Route(context, "ns"), target = Route(context, "target"), revision = Route(context, "revision");
        if (!await AdmitTarget(context, ns, target, revision).ConfigureAwait(false)) return;
        if (!TryMutationHeaders(context, true, out string key, out string? expected, out IResult? failure))
        { await Write(context, failure!); return; }
        GatewayActivationRequest? request = await ReadOptionalActivation(context).ConfigureAwait(false);
        if (request?.Description is { Length: > 1024 }) { await Write(context, Invalid(context)); return; }
        GatewayAdminRequestAttribution attribution = await Attribution(context, GatewayAdminCapabilities.ActivationWrite).ConfigureAwait(false);
        GatewayManagementCommandResult result = await context.RequestServices.GetRequiredService<IGatewayManagementApplication>()
            .RollbackAsync(new(ns, target, revision, key, attribution.ToActor(), attribution.CorrelationId,
                request?.Description, expected), context.RequestAborted).ConfigureAwait(false);
        await Write(context, ProjectCommand(context, result, CommandProjection.Activation)).ConfigureAwait(false);
    }

    private static async Task Activations(HttpContext context)
    {
        string ns = Route(context, "ns"), target = Route(context, "target");
        if (!await AdmitTarget(context, ns, target).ConfigureAwait(false)) return;
        if (!TryPage(context, out int maximum, out string? cursor, out IResult? failure))
        { await Write(context, failure!); return; }
        IGatewayManagementReader reader = context.RequestServices.GetRequiredService<IGatewayManagementReader>();
        var intents = await reader.ListActivationsAsync(ns, target, maximum, cursor, context.RequestAborted).ConfigureAwait(false);
        var outcomes = await reader.ListOutcomesAsync(ns, target, maximum, cursor, context.RequestAborted).ConfigureAwait(false);
        await Write(context, TypedResults.Json(new GatewayActivationHistoryResponse(intents, outcomes),
            GatewayAdminJsonContext.Default.GatewayActivationHistoryResponse)).ConfigureAwait(false);
    }

    private static async Task Compare(HttpContext context)
    {
        string ns = Route(context, "ns"), target = Route(context, "target");
        if (!await AdmitTarget(context, ns, target).ConfigureAwait(false)) return;
        GatewayCompareRequest? request;
        try { request = JsonSerializer.Deserialize(await ReadBoundedBodyAsync(context.Request, context.RequestAborted), GatewayAdminJsonContext.Default.GatewayCompareRequest); }
        catch (JsonException) { await Write(context, Invalid(context)); return; }
        if (request is null || !ValidComponent(request.LeftRevisionId) || !ValidComponent(request.RightRevisionId))
        { await Write(context, Invalid(context)); return; }
        var result = await context.RequestServices.GetRequiredService<IGatewayManagementApplication>()
            .CompareAsync(ns, request.LeftRevisionId, request.RightRevisionId, context.RequestAborted).ConfigureAwait(false);
        await Write(context, result.State == GatewayApplicationReadState.Found
            ? TypedResults.Json(result.Value!, GatewayAdminJsonContext.Default.GatewayRevisionComparison)
            : NotFound(context)).ConfigureAwait(false);
    }

    private static async Task Export(HttpContext context)
    {
        string ns = Route(context, "ns"), target = Route(context, "target"), revision = Route(context, "revision");
        if (!await AdmitTarget(context, ns, target, revision).ConfigureAwait(false)) return;
        var result = await context.RequestServices.GetRequiredService<IGatewayManagementApplication>()
            .ExportAsync(ns, revision, context.RequestAborted).ConfigureAwait(false);
        if (result.State == GatewayApplicationReadState.Gone)
        { await Write(context, Error(context, 410, "gateway.admin.content.gone", "The retained content is unavailable.")); return; }
        if (result.State != GatewayApplicationReadState.Found) { await Write(context, NotFound(context)); return; }
        GatewayRevisionExport value = result.Value!;
        await Write(context, TypedResults.Json(new GatewayExportResponse(value.SchemaVersion, value.RevisionId,
            value.ContentHashAlgorithm, value.ContentHashValue, Encoding.UTF8.GetString(value.Utf8Configuration.AsSpan())),
            GatewayAdminJsonContext.Default.GatewayExportResponse)).ConfigureAwait(false);
    }

    private static async Task Import(HttpContext context, bool activate)
    {
        string ns = Route(context, "ns"), target = Route(context, "target");
        if (!await AdmitTarget(context, ns, target).ConfigureAwait(false)) return;
        if (!TryMutationHeaders(context, activate, out string key, out string? expected, out IResult? failure))
        { await Write(context, failure!); return; }
        GatewayImportRequest? request;
        try { request = JsonSerializer.Deserialize(await ReadBoundedBodyAsync(context.Request, context.RequestAborted), GatewayAdminJsonContext.Default.GatewayImportRequest); }
        catch (JsonException) { await Write(context, Invalid(context)); return; }
        if (request is null || !ValidComponent(request.SourceId) || request.Description is { Length: > 1024 } ||
            Encoding.UTF8.GetByteCount(request.ConfigurationJson) > MaximumBodyBytes)
        { await Write(context, Invalid(context)); return; }
        string capability = activate ? GatewayAdminCapabilities.ImportAndActivate : GatewayAdminCapabilities.ImportWrite;
        GatewayAdminRequestAttribution attribution = await Attribution(context, capability).ConfigureAwait(false);
        GatewayManagementCommandResult result = await context.RequestServices.GetRequiredService<IGatewayManagementApplication>()
            .ImportAsync(new(ns, target, key, attribution.ToActor(), attribution.CorrelationId,
                ImmutableArray.Create(Encoding.UTF8.GetBytes(request.ConfigurationJson)), request.Description,
                expected, activate, "import", request.SourceId), context.RequestAborted).ConfigureAwait(false);
        await Write(context, ProjectCommand(context, result, activate ? CommandProjection.Activation : CommandProjection.Revision)).ConfigureAwait(false);
    }

    private static async Task Operation(HttpContext context)
    {
        string ns = Route(context, "ns"), operation = Route(context, "operation");
        if (!await AdmitNamespace(context, ns, operation, false).ConfigureAwait(false)) return;
        GatewayManagedRecord<GatewayCommandReceipt>? value = await context.RequestServices.GetRequiredService<IGatewayManagementReader>()
            .GetOperationAsync(ns, operation, context.RequestAborted).ConfigureAwait(false);
        await Write(context, value is null ? NotFound(context) : TypedResults.Json(value,
            GatewayAdminJsonContext.Default.GatewayManagedRecordGatewayCommandReceipt)).ConfigureAwait(false);
    }

    private static async Task Backup(HttpContext context)
    {
        string ns = Route(context, "ns");
        if (!await AdmitNamespace(context, ns, null, true).ConfigureAwait(false)) return;
        if (!TryMutationHeaders(context, false, out string key, out _, out IResult? failure)) { await Write(context, failure!); return; }
        GatewayBackupRequest? request;
        try { request = JsonSerializer.Deserialize(await ReadBoundedBodyAsync(context.Request, context.RequestAborted), GatewayAdminJsonContext.Default.GatewayBackupRequest); }
        catch (JsonException) { await Write(context, Invalid(context)); return; }
        if (request is null || !GatewayBackupSinkRegistry.ValidName(request.SinkName) || !ValidLabel(request.ArtifactLabel) ||
            !context.RequestServices.GetRequiredService<GatewayBackupSinkRegistry>().TryGet(request.SinkName, out IGatewayBackupSink? sink))
        { await Write(context, Invalid(context)); return; }
        GatewayAdminRequestAttribution attribution = await Attribution(context, GatewayAdminCapabilities.BackupWrite).ConfigureAwait(false);
        GatewayBackupArtifact artifact = await sink!.OpenAsync(request.ArtifactLabel, context.RequestAborted).ConfigureAwait(false);
        await using (artifact.Destination.ConfigureAwait(false))
        {
            GatewayAdministrativeResult result = await context.RequestServices.GetRequiredService<IGatewayManagementAdministration>()
                .CreateBackupAsync(ns, key, attribution.ToActor(), artifact.Destination, context.RequestAborted).ConfigureAwait(false);
            await Write(context, ProjectAdministration(result, artifact.PublicReference)).ConfigureAwait(false);
        }
    }

    private static async Task Purge(HttpContext context)
    {
        string ns = Route(context, "ns");
        if (!await AdmitNamespace(context, ns, null, true).ConfigureAwait(false)) return;
        if (!TryMutationHeaders(context, false, out string key, out _, out IResult? failure)) { await Write(context, failure!); return; }
        GatewayPurgeRequest? request;
        try { request = JsonSerializer.Deserialize(await ReadBoundedBodyAsync(context.Request, context.RequestAborted), GatewayAdminJsonContext.Default.GatewayPurgeRequest); }
        catch (JsonException) { await Write(context, Invalid(context)); return; }
        if (request is null || request.ResourceIds.IsDefault || request.ResourceIds.Length is < 1 or > 256 ||
            request.ResourceIds.Any(static value => !ValidComponent(value)) ||
            !request.ResourceIds.SequenceEqual(request.ResourceIds.Order(StringComparer.Ordinal)) ||
            request.ResourceIds.Distinct(StringComparer.Ordinal).Count() != request.ResourceIds.Length)
        { await Write(context, Invalid(context)); return; }
        GatewayAdminRequestAttribution attribution = await Attribution(context, GatewayAdminCapabilities.PurgeWrite).ConfigureAwait(false);
        GatewayAdministrativeResult result = await context.RequestServices.GetRequiredService<IGatewayManagementAdministration>()
            .RequestPurgeAsync(ns, key, attribution.ToActor(), (GatewayManagementPurgeCategory)request.Category,
                request.ResourceIds, context.RequestAborted).ConfigureAwait(false);
        await Write(context, ProjectAdministration(result, null)).ConfigureAwait(false);
    }

    private static async Task Effective(HttpContext context)
    {
        string ns = Route(context, "ns"), target = Route(context, "target");
        if (!await AdmitTarget(context, ns, target).ConfigureAwait(false)) return;
        GatewayNodeEffectiveObservation? observation = context.RequestServices.GetRequiredService<IGatewayNodeEffectiveReader>().GetCurrent();
        await Write(context, observation is null ? NotFound(context) : TypedResults.Json(observation.Snapshot,
            GatewayAdminJsonContext.Default.GatewayEffectiveSnapshot)).ConfigureAwait(false);
    }

    private static async ValueTask<bool> AdmitTarget(HttpContext context, string ns, string target, string? component = null)
    {
        if (!ValidComponent(ns) || !ValidComponent(target) || component is not null && !ValidComponent(component))
        { await Write(context, Invalid(context)); return false; }
        return await AuthorizeOrNotFound(context, ns, target, GatewayAdminResourceKind.Target).ConfigureAwait(false);
    }

    private static async ValueTask<bool> AdmitNamespace(HttpContext context, string ns, string? component, bool administration)
    {
        if (!ValidComponent(ns) || component is not null && !ValidComponent(component))
        { await Write(context, Invalid(context)); return false; }
        return await AuthorizeOrNotFound(context, ns, null,
            administration ? GatewayAdminResourceKind.Administration : GatewayAdminResourceKind.Namespace).ConfigureAwait(false);
    }

    private static async ValueTask<bool> AuthorizeOrNotFound(HttpContext context, string ns, string? target, GatewayAdminResourceKind kind)
    {
        bool allowed = await AuthorizeResource(context, context.RequestServices.GetRequiredService<IAuthorizationService>(), ns, target, kind).ConfigureAwait(false);
        if (!allowed) await Write(context, NotFound(context));
        return allowed;
    }

    private static ValueTask<GatewayAdminRequestAttribution> Attribution(HttpContext context, string capability) =>
        context.RequestServices.GetRequiredService<IGatewayAdminActorProjector>()
            .ProjectAsync(context, capability, context.RequestAborted);

    private static async ValueTask<GatewayActivationRequest?> ReadOptionalActivation(HttpContext context)
    {
        if (context.Request.ContentLength is null or 0) return null;
        try { return JsonSerializer.Deserialize(await ReadBoundedBodyAsync(context.Request, context.RequestAborted), GatewayAdminJsonContext.Default.GatewayActivationRequest); }
        catch (JsonException) { return new GatewayActivationRequest(new string('x', 1025)); }
    }

    private static bool TryPage(HttpContext context, out int maximum, out string? cursor, out IResult? failure)
    {
        maximum = 64; cursor = null; failure = null;
        if (context.Request.Query.Keys.Any(static key => key is not ("maximum" or "cursor"))) { failure = Invalid(context); return false; }
        if (context.Request.Query["maximum"].Count > 1 || context.Request.Query["cursor"].Count > 1) { failure = Invalid(context); return false; }
        if (context.Request.Query["maximum"].Count == 1 && (!int.TryParse(context.Request.Query["maximum"][0], out maximum) || maximum is < 1 or > 256))
        { failure = Invalid(context); return false; }
        cursor = context.Request.Query["cursor"].Count == 1 ? context.Request.Query["cursor"][0] : null;
        if (cursor is { Length: > 4096 }) { failure = Invalid(context); return false; }
        return true;
    }

    private static bool ValidLabel(string? value) => value is null || value is { Length: >= 1 and <= 128 }
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static IResult ProjectAdministration(GatewayAdministrativeResult result, string? artifactReference) =>
        TypedResults.Json(new GatewayAdministrativeResponse(result.OperationId, result.State, result.Code, artifactReference),
            GatewayAdminJsonContext.Default.GatewayAdministrativeResponse, statusCode: 202);
}
