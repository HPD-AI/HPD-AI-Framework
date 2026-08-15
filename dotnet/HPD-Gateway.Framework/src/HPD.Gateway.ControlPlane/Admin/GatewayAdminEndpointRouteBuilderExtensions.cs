using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway.ControlPlane;

internal static partial class GatewayAdminEndpointMapper
{
    private const int MaximumBodyBytes = 4 * 1024 * 1024;

    internal static RouteGroupBuilder MapGatewayAdminCore(
        this IEndpointRouteBuilder endpoints,
        GatewayAdminApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(endpoints, options);
        endpoints.ServiceProvider.GetRequiredService<GatewayAdminOpenApiContract>()
            .Seal(options.OpenApiSecurityScheme);

        var authenticated = new AuthorizationPolicyBuilder(options.AuthenticationScheme)
            .RequireAuthenticatedUser().Build();
        RouteGroupBuilder group = endpoints.MapGroup(options.RoutePrefix);
        group.RequireAuthorization(authenticated);
        group.RequireRateLimiting(options.RateLimitPolicy);
        group.WithRequestTimeout(options.RequestTimeoutPolicy);
        group.WithTags("HPD Gateway Management v1");
        IGatewayAdminSecurityMetadataProvider? security = endpoints.ServiceProvider.GetService<IGatewayAdminSecurityMetadataProvider>();
        security?.Validate(options);
        security?.ApplyGroup(group);

        Map(group, options, security, "capabilities", context => Write(context, TypedResults.Json(
            new GatewayCapabilityCatalog(GatewayAdminCapabilities.All, "v1"),
            GatewayAdminJsonContext.Default.GatewayCapabilityCatalog)));

        Map(group, options, security, "host-capabilities", context =>
        {
            HostCapabilitySnapshot capabilities = context.RequestServices.GetRequiredService<HostCapabilitySnapshot>();
            return Write(context, TypedResults.Json(
                GatewayHostCapabilityProjector.Project(capabilities),
                GatewayAdminJsonContext.Default.GatewayHostCapabilitySnapshotResponse));
        });

        Map(group, options, security, "validate", async context =>
        {
            IGatewayAdminActorProjector projector = context.RequestServices.GetRequiredService<IGatewayAdminActorProjector>();
            GatewayAdminRequestAttribution attribution = await projector.ProjectAsync(
                context, GatewayAdminCapabilities.RevisionValidate, context.RequestAborted).ConfigureAwait(false);
            HostCapabilitySnapshot capabilities = context.RequestServices.GetRequiredService<HostCapabilitySnapshot>();
            GatewayHostCapabilitySnapshotResponse host = GatewayHostCapabilityProjector.Project(capabilities);
            byte[] body = await ReadBoundedBodyAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
            GatewayCandidateReadResult candidate = GatewayCandidateReader.Read(body, capabilities);
            var response = new GatewayValidationResponse(
                candidate.IsAccepted,
                candidate.Errors.Select(static error => new GatewayAdminDiagnostic(
                    error.Code.ToString(), error.Path, error.Message)).ToImmutableArray(),
                candidate.Configuration is null
                    ? null
                    : $"{candidate.Configuration.SchemaVersion.Major}.{candidate.Configuration.SchemaVersion.Minor}",
                candidate.Configuration?.CanonicalizationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                candidate.CanonicalDocument?.ContentHash.Algorithm,
                candidate.CanonicalDocument?.ContentHash.Value,
                host.SnapshotAlgorithm,
                host.SnapshotValue,
                attribution.CorrelationId,
                DateTimeOffset.UtcNow);
            await Write(context, TypedResults.Json(response, GatewayAdminJsonContext.Default.GatewayValidationResponse)).ConfigureAwait(false);
        });

        Map(group, options, security, "provision", async context =>
        {
            string ns = Route(context, "ns");
            string target = Route(context, "target");
            IAuthorizationService authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
            if (!ValidComponent(ns) || !ValidComponent(target)) { await Write(context, Invalid(context)); return; }
            if (!await AuthorizeResource(context, authorization, ns, target, GatewayAdminResourceKind.Target).ConfigureAwait(false))
            { await Write(context, NotFound(context)); return; }
            if (!TryMutationHeaders(context, allowIfMatch: false, out string key, out _, out IResult? failure))
            { await Write(context, failure!); return; }
            IGatewayAdminActorProjector projector = context.RequestServices.GetRequiredService<IGatewayAdminActorProjector>();
            IGatewayManagementCommandCoordinator commands = context.RequestServices.GetRequiredService<IGatewayManagementCommandCoordinator>();
            GatewayAdminRequestAttribution attribution = await projector.ProjectAsync(
                context, GatewayAdminCapabilities.TargetProvision, context.RequestAborted).ConfigureAwait(false);
            GatewayManagementCommandResult result = await commands.ProvisionLocalTargetAsync(new(
                ns, target, key, attribution.ToActor(), attribution.CorrelationId), context.RequestAborted).ConfigureAwait(false);
            await Write(context, ProjectCommand(context, result, CommandProjection.Provision)).ConfigureAwait(false);
        });

        Map(group, options, security, "desired", async context =>
        {
            string ns = Route(context, "ns");
            string target = Route(context, "target");
            if (!await AdmitTarget(context, ns, target).ConfigureAwait(false)) return;
            IGatewayManagementReader reader = context.RequestServices.GetRequiredService<IGatewayManagementReader>();
            GatewayDesiredProjection? desired = await reader.GetDesiredProjectionAsync(ns, target, context.RequestAborted).ConfigureAwait(false);
            IResult result = desired is not null
                ? TypedResults.Json(desired, GatewayAdminJsonContext.Default.GatewayDesiredProjection)
                : NotFound(context);
            await Write(context, result).ConfigureAwait(false);
        });

        Map(group, options, security, "status", async context =>
        {
            string ns = Route(context, "ns");
            string target = Route(context, "target");
            if (!await AdmitTarget(context, ns, target).ConfigureAwait(false)) return;
            IGatewayManagementStatusReader status = context.RequestServices.GetRequiredService<IGatewayManagementStatusReader>();
            GatewayAppliedRuntimeObservation? effective = context.RequestServices.GetRequiredService<IGatewayNodeAppliedRuntimeReader>().GetCurrent();
            GatewayDesiredProjection? desired = await context.RequestServices.GetRequiredService<IGatewayManagementReader>()
                .GetDesiredProjectionAsync(ns, target, context.RequestAborted).ConfigureAwait(false);
            GatewayManagementStatusSnapshot snapshot = await status.GetCurrentAsync(
                ns, target, desired?.ActivationIntentId, context.RequestAborted).ConfigureAwait(false);
            bool nodeObserved = effective is not null &&
                StringComparer.Ordinal.Equals(effective.NamespaceId, ns) &&
                StringComparer.Ordinal.Equals(effective.TargetNodeId, target) &&
                desired is not null &&
                StringComparer.Ordinal.Equals(effective.Snapshot.CandidateId.Value, desired.CandidateId);
            GatewayStatusSnapshot? node = nodeObserved
                ? context.RequestServices.GetRequiredService<IGatewayStatusReader>().GetCurrent()
                : null;
            GatewayNodeObservationState observation = nodeObserved
                ? GatewayNodeObservationState.Observed
                : snapshot.LatestNodeOutcome == GatewayNodeOutcomeKind.PublicationIndeterminate
                    ? GatewayNodeObservationState.Indeterminate
                    : snapshot.LatestNodeOutcome is not null
                        ? GatewayNodeObservationState.ObservedWithoutEffectiveProjection
                        : snapshot.NodeAttemptStarted
                            ? GatewayNodeObservationState.NotObserved
                            : GatewayNodeObservationState.NotAttempted;
            await Write(context, TypedResults.Json(new GatewayTargetStatusResponse(
                snapshot,
                observation,
                node,
                node?.GeneratedAt ?? DateTimeOffset.UtcNow,
                node?.DetailsTruncated ?? false),
                GatewayAdminJsonContext.Default.GatewayTargetStatusResponse)).ConfigureAwait(false);
        });

        Map(group, options, security, "submit", context => Submit(context, activate: false, GatewayAdminCapabilities.RevisionWrite));

        Map(group, options, security, "submit-and-activate", context => Submit(context, activate: true, GatewayAdminCapabilities.RevisionSubmitAndActivate));

        Map(group, options, security, "revisions", async context =>
        {
            string ns = Route(context, "ns");
            string target = Route(context, "target");
            if (!await AdmitTarget(context, ns, target).ConfigureAwait(false)) return;
            IGatewayManagementReader reader = context.RequestServices.GetRequiredService<IGatewayManagementReader>();
            if (!TryPage(context, GatewayAdminClientSemanticLedger.For("revisions"),
                out int maximum, out string? cursor, out IResult? pageFailure))
            { await Write(context, pageFailure!); return; }
            GatewayManagedPage<GatewayAcceptedRevision> page = await reader.ListRevisionsAsync(
                ns, target, maximum, cursor, context.RequestAborted).ConfigureAwait(false);
            var projected = new GatewayAdminPage<GatewayRevisionProjection>(
                page.Items.Select(ProjectRevision).ToImmutableArray(), page.ContinuationToken, page.HasMore);
            await Write(context, TypedResults.Json(projected, GatewayAdminJsonContext.Default.GatewayAdminPageGatewayRevisionProjection)).ConfigureAwait(false);
        });

        Map(group, options, security, "audit", async context =>
        {
            string ns = Route(context, "ns");
            IAuthorizationService authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
            if (!ValidComponent(ns)) { await Write(context, Invalid(context)); return; }
            if (!await AuthorizeResource(context, authorization, ns, null, GatewayAdminResourceKind.Namespace).ConfigureAwait(false))
            { await Write(context, NotFound(context)); return; }
            IGatewayManagementReader reader = context.RequestServices.GetRequiredService<IGatewayManagementReader>();
            if (!TryPage(context, GatewayAdminClientSemanticLedger.For("audit"),
                out int maximum, out string? cursor, out IResult? pageFailure))
            { await Write(context, pageFailure!); return; }
            GatewayManagedPage<GatewayAdministrativeAuditRecord> page = await reader.ListAuditAsync(
                ns, maximum, cursor, context.RequestAborted).ConfigureAwait(false);
            var projected = new GatewayAdminPage<GatewayAuditProjection>(page.Items.Select(static item => new GatewayAuditProjection(
                item.Id, item.Value.ActorId, item.Value.Operation, item.Value.ResultCode,
                item.Value.CorrelationId, item.Value.SubjectId, item.CreatedAt)).ToImmutableArray(),
                page.ContinuationToken, page.HasMore);
            await Write(context, TypedResults.Json(projected, GatewayAdminJsonContext.Default.GatewayAdminPageGatewayAuditProjection)).ConfigureAwait(false);
        });

        MapAdditional(group, options, security);

        return group;
    }

    private static IEndpointConventionBuilder Map(
        RouteGroupBuilder group,
        GatewayAdminApiOptions options,
        IGatewayAdminSecurityMetadataProvider? security,
        string operation,
        RequestDelegate handler)
    {
        GatewayAdminEndpointDescriptor descriptor = GatewayAdminEndpointLedger.V1.Single(value => value.Operation == operation);
        IEndpointConventionBuilder mapped = group.MapMethods(
                descriptor.Pattern, [descriptor.Method], (RequestDelegate)Dispatch)
            .WithName("HpdGatewayAdmin." + descriptor.Operation)
            .WithMetadata(descriptor)
            .WithMetadata(new GatewayAdminHandlerMetadata(handler))
            .WithHpdGatewayEndpointRole(GatewayListenerRole.Management, options.EndpointSurfaceId, options.RequireManagementListener)
            .RequireAuthorization(options.CapabilityPolicies[descriptor.Capability]);
        mapped.Add(endpoint => GatewayAdminOpenApiMetadata.Apply(endpoint, descriptor));
        security?.ApplyEndpoint(mapped, descriptor.Capability);
        return mapped;
    }

    private static async Task Dispatch(HttpContext context)
    {
        RequestDelegate handler = context.GetEndpoint()!.Metadata
            .GetRequiredMetadata<GatewayAdminHandlerMetadata>().Handler;
        try { await handler(context).ConfigureAwait(false); }
        catch (GatewayAdminRequestException exception) when (!context.Response.HasStarted)
        {
            await Write(context, Error(context, exception.StatusCode, exception.Code, exception.SafeTitle)).ConfigureAwait(false);
        }
    }

    private sealed record GatewayAdminHandlerMetadata(RequestDelegate Handler);

    private static async Task Submit(
        HttpContext context, bool activate, string capability)
    {
        string ns = Route(context, "ns");
        string target = Route(context, "target");
        if (!await AdmitTarget(context, ns, target).ConfigureAwait(false)) return;
        if (!TryMutationHeaders(context, allowIfMatch: activate, out string key, out string? expected, out IResult? failure))
        { await Write(context, failure!); return; }
        byte[] body = await ReadBoundedBodyAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
        GatewayRevisionRequest? request;
        try { request = JsonSerializer.Deserialize(body, GatewayAdminJsonContext.Default.GatewayRevisionRequest); }
        catch (JsonException) { await Write(context, Invalid(context)); return; }
        if (request is null || !ValidComponent(request.SourceKind) || !ValidComponent(request.SourceId) ||
            request.Description is { Length: > 1024 }) { await Write(context, Invalid(context)); return; }
        IGatewayAdminActorProjector projector = context.RequestServices.GetRequiredService<IGatewayAdminActorProjector>();
        IGatewayManagementCommandCoordinator commands = context.RequestServices.GetRequiredService<IGatewayManagementCommandCoordinator>();
        if (Encoding.UTF8.GetByteCount(request.ConfigurationJson) > MaximumBodyBytes)
        { await Write(context, Error(context, 413, "gateway.admin.request.tooLarge", "The request is too large.")); return; }
        byte[] configuration = Encoding.UTF8.GetBytes(request.ConfigurationJson);
        GatewayAdminRequestAttribution attribution = await projector.ProjectAsync(
            context, capability, context.RequestAborted).ConfigureAwait(false);
        GatewayManagementCommandResult result = await commands.SubmitAsync(new GatewaySubmitCommand(
            ns, target, key, attribution.ToActor(), attribution.CorrelationId,
            request.SourceKind, request.SourceId, request.Description,
            ImmutableArray.Create(configuration), expected, activate), context.RequestAborted).ConfigureAwait(false);
        await Write(context, ProjectCommand(context, result, activate ? CommandProjection.Activation : CommandProjection.Revision)).ConfigureAwait(false);
    }

    private static async ValueTask<bool> AuthorizeResource(
        HttpContext context, IAuthorizationService authorization,
        string ns, string? target, GatewayAdminResourceKind kind)
    {
        string policy = kind switch
        {
            GatewayAdminResourceKind.Namespace => GatewayAdminResourcePolicies.Namespace,
            GatewayAdminResourceKind.Target => GatewayAdminResourcePolicies.Target,
            GatewayAdminResourceKind.Administration => GatewayAdminResourcePolicies.Administration,
            _ => throw new InvalidOperationException("Unsupported Gateway Admin resource kind."),
        };
        AuthorizationResult result = await authorization.AuthorizeAsync(
            context.User, new GatewayAdminResource(ns, target, kind), policy).ConfigureAwait(false);
        return result.Succeeded;
    }

    private static bool TryMutationHeaders(
        HttpContext context, bool allowIfMatch,
        out string idempotencyKey, out string? desiredToken, out IResult? failure)
    {
        idempotencyKey = string.Empty;
        desiredToken = null;
        failure = null;
        var keys = context.Request.Headers["Idempotency-Key"];
        if (keys.Count != 1 || !ValidVisibleAscii(keys[0], 128)) { failure = Invalid(context); return false; }
        idempotencyKey = keys[0]!;
        var matches = context.Request.Headers.IfMatch;
        if (!allowIfMatch && matches.Count != 0) { failure = Invalid(context); return false; }
        if (matches.Count == 0) return true;
        if (matches.Count != 1 || matches[0] is not { Length: >= 3 and <= 514 } value ||
            value.StartsWith("W/", StringComparison.Ordinal) || value[0] != '"' || value[^1] != '"' ||
            value.AsSpan(1, value.Length - 2).Contains('"') || value.AsSpan().Contains(','))
        { failure = Invalid(context); return false; }
        desiredToken = value[1..^1];
        return ValidVisibleAscii(desiredToken, 512);
    }

    private enum CommandProjection : byte { Provision, Revision, Activation }

    private static IResult ProjectCommand(HttpContext context, GatewayManagementCommandResult result, CommandProjection projection)
    {
        if (result.State is GatewayManagementCommandState.Accepted or GatewayManagementCommandState.Duplicate)
        {
            if (projection == CommandProjection.Provision)
                return TypedResults.Json(new GatewayProvisionResponse(result.OperationId!, result.State == GatewayManagementCommandState.Duplicate),
                    GatewayAdminJsonContext.Default.GatewayProvisionResponse, statusCode: 201);
            return TypedResults.Json(new GatewayRevisionResponse(
                result.OperationId!, result.RevisionId, result.ActivationIntentId,
                result.DesiredStateToken, result.State == GatewayManagementCommandState.Duplicate),
                GatewayAdminJsonContext.Default.GatewayRevisionResponse,
                statusCode: projection == CommandProjection.Revision ? 201 : 202);
        }
        if (result.Code is "management.target.not-owned" or "management.revision.not-found")
            return NotFound(context);
        int status = result.State switch
        {
            GatewayManagementCommandState.Invalid => 422,
            GatewayManagementCommandState.Conflict => 409,
            GatewayManagementCommandState.OutcomeUnknown or GatewayManagementCommandState.Unavailable => 503,
            _ => 500,
        };
        return Error(context, status, result.Code, "The Gateway management operation was not accepted.");
    }

    private static async ValueTask<byte[]> ReadBoundedBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Headers.ContentEncoding.Count != 0)
            throw GatewayAdminRequestException.UnsupportedMedia();
        if (request.ContentType is null)
            throw GatewayAdminRequestException.UnsupportedMedia();
        string mediaType = request.ContentType.Split(';', 2)[0].Trim();
        if (!StringComparer.OrdinalIgnoreCase.Equals(mediaType, "application/json") &&
            !StringComparer.OrdinalIgnoreCase.Equals(mediaType, "application/hpd.gateway+json"))
            throw GatewayAdminRequestException.UnsupportedMedia();
        if (request.ContentLength is > MaximumBodyBytes) throw GatewayAdminRequestException.TooLarge();
        using var stream = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            int remaining = MaximumBodyBytes + 1 - total;
            int read = await request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > MaximumBodyBytes) throw GatewayAdminRequestException.TooLarge();
            stream.Write(buffer, 0, read);
        }
        return stream.ToArray();
    }

    private static IResult Invalid(HttpContext context) =>
        Error(context, 400, "gateway.admin.request.invalid", "The request is invalid.");

    private static IResult NotFound(HttpContext context) =>
        Error(context, 404, "gateway.admin.resource.notFound", "The resource was not found.");

    private static GatewayRevisionProjection ProjectRevision(GatewayManagedRecord<GatewayAcceptedRevision> item) =>
        new(item.Id, item.Value.ContentHashAlgorithm, item.Value.ContentHashValue,
            item.Value.SchemaVersion, item.Value.CanonicalizationVersion,
            item.Value.ParentRevisionId, item.Value.DerivedFromRevisionId,
            item.Value.ValidationId, item.Value.SourceKind, item.Value.SourceId,
            item.Value.Description, item.CreatedAt);

    private static IResult Error(HttpContext context, int status, string code, string title) =>
        TypedResults.Json(new GatewayAdminError(code, title), GatewayAdminJsonContext.Default.GatewayAdminError, statusCode: status);

    private static bool ValidComponent(string? value) => value is { Length: > 0 and <= 128 }
        && value.IsNormalized(NormalizationForm.FormC)
        && Encoding.UTF8.GetByteCount(value) <= 128
        && !value.Any(char.IsControl);

    private static bool ValidVisibleAscii(string? value, int maximum) =>
        value is { Length: > 0 } && value.Length <= maximum && value.All(static c => c is >= '!' and <= '~');

    private static string Route(HttpContext context, string name) =>
        context.Request.RouteValues[name]?.ToString() ?? string.Empty;

    private static Task Write(HttpContext context, IResult result) => result.ExecuteAsync(context);

    private sealed class GatewayAdminRequestException(
        int statusCode, string code, string safeTitle) : Exception
    {
        internal int StatusCode { get; } = statusCode;
        internal string Code { get; } = code;
        internal string SafeTitle { get; } = safeTitle;
        internal static GatewayAdminRequestException UnsupportedMedia() =>
            new(415, "gateway.admin.media.unsupported", "The request media type is unsupported.");
        internal static GatewayAdminRequestException TooLarge() =>
            new(413, "gateway.admin.request.tooLarge", "The request is too large.");
    }

    private static void ValidateOptions(IEndpointRouteBuilder endpoints, GatewayAdminApiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RoutePrefix) || options.RoutePrefix.Length > 128 || options.RoutePrefix[0] != '/')
            throw new InvalidOperationException("The Gateway Admin route prefix is invalid.");
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AuthenticationScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RateLimitPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RequestTimeoutPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OpenApiSecurityScheme);
        if (!GatewayIdentifier.IsCanonical(options.EndpointSurfaceId))
            throw new InvalidOperationException("The Gateway Admin endpoint surface ID is invalid.");
        if (!endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>()
                .IsService(typeof(IGatewayAdminActorProjector)))
            throw new InvalidOperationException("The Gateway Admin actor projector is not registered.");
        _ = endpoints.ServiceProvider.GetRequiredService<HostCapabilitySnapshot>();
        _ = endpoints.ServiceProvider.GetRequiredService<IGatewayManagementCommandCoordinator>();
        _ = endpoints.ServiceProvider.GetRequiredService<IGatewayManagementApplication>();
        _ = endpoints.ServiceProvider.GetRequiredService<IGatewayManagementReader>();
        _ = endpoints.ServiceProvider.GetRequiredService<IGatewayManagementAdministration>();
        _ = endpoints.ServiceProvider.GetRequiredService<IGatewayManagementStatusReader>();
        _ = endpoints.ServiceProvider.GetRequiredService<IGatewayStatusReader>();
        _ = endpoints.ServiceProvider.GetRequiredService<IGatewayNodeAppliedRuntimeReader>();
        _ = endpoints.ServiceProvider.GetRequiredService<GatewayBackupSinkRegistry>();
        if (options.CapabilityPolicies.Count != GatewayAdminCapabilities.All.Length ||
            options.CapabilityPolicies.Keys.Except(GatewayAdminCapabilities.All, StringComparer.Ordinal).Any())
            throw new InvalidOperationException("The Gateway Admin capability-policy catalog is not the exact v1 catalog.");
        foreach (string capability in GatewayAdminCapabilities.All)
            if (!options.CapabilityPolicies.TryGetValue(capability, out string? policy) || string.IsNullOrWhiteSpace(policy))
                throw new InvalidOperationException($"The Gateway Admin capability '{capability}' has no policy mapping.");
    }
}
