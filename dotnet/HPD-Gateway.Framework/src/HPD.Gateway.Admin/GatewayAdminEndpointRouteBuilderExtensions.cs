using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HPD.Gateway.Abstractions.Serialization;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Hosting;
using HPD.Gateway.Core;
using HPD.Gateway.Management;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway.Admin;

public static class GatewayAdminEndpointRouteBuilderExtensions
{
    private const int MaximumBodyBytes = 4 * 1024 * 1024;

    public static RouteGroupBuilder MapHpdGatewayAdmin(
        this IEndpointRouteBuilder endpoints,
        GatewayAdminEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(endpoints, options);

        var authenticated = new AuthorizationPolicyBuilder(options.AuthenticationScheme)
            .RequireAuthenticatedUser().Build();
        RouteGroupBuilder group = endpoints.MapGroup(options.RoutePrefix);
        group.RequireAuthorization(authenticated);
        group.RequireRateLimiting(options.RateLimitPolicy);
        group.WithRequestTimeout(options.RequestTimeoutPolicy);
        group.WithTags("HPD Gateway Management v1");
        IGatewayAdminSecurityMetadataProvider? security = endpoints.ServiceProvider.GetService<IGatewayAdminSecurityMetadataProvider>();
        security?.ApplyGroup(group);

        Map(group, options, security, "capabilities", context => Write(context, TypedResults.Json(
            new GatewayCapabilityCatalog(GatewayAdminCapabilities.All, "v1"),
            GatewayAdminJsonContext.Default.GatewayCapabilityCatalog)));

        Map(group, options, security, "validate", async context =>
        {
            HostCapabilitySnapshot capabilities = context.RequestServices.GetRequiredService<HostCapabilitySnapshot>();
            byte[] body = await ReadBoundedBodyAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
            GatewayCandidateReadResult candidate = GatewayCandidateReader.Read(body, capabilities);
            var response = new GatewayValidationResponse(candidate.IsAccepted,
                candidate.Errors.Select(static error => new GatewayAdminDiagnostic(
                    error.Code.ToString(), error.Path, error.Message)).ToImmutableArray());
            await Write(context, TypedResults.Json(response, GatewayAdminJsonContext.Default.GatewayValidationResponse)).ConfigureAwait(false);
        });

        Map(group, options, security, "provision", async context =>
        {
            string ns = Route(context, "ns");
            string target = Route(context, "target");
            IAuthorizationService authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
            IGatewayAdminActorProjector projector = context.RequestServices.GetRequiredService<IGatewayAdminActorProjector>();
            IGatewayManagementCommandCoordinator commands = context.RequestServices.GetRequiredService<IGatewayManagementCommandCoordinator>();
            if (!ValidComponent(ns) || !ValidComponent(target)) { await Write(context, Invalid(context)); return; }
            if (!await AuthorizeResource(context, authorization, ns, target, GatewayAdminResourceKind.Target).ConfigureAwait(false))
            { await Write(context, NotFound(context)); return; }
            if (!TryMutationHeaders(context, allowIfMatch: false, out string key, out _, out IResult? failure))
            { await Write(context, failure!); return; }
            GatewayAdminRequestAttribution attribution = await projector.ProjectAsync(
                context, GatewayAdminCapabilities.TargetProvision, context.RequestAborted).ConfigureAwait(false);
            GatewayManagementCommandResult result = await commands.ProvisionLocalTargetAsync(new(
                ns, target, key, attribution.ToActor(), attribution.CorrelationId), context.RequestAborted).ConfigureAwait(false);
            await Write(context, ProjectCommand(context, result, created: true)).ConfigureAwait(false);
        });

        Map(group, options, security, "desired", async context =>
        {
            string ns = Route(context, "ns");
            string target = Route(context, "target");
            IAuthorizationService authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
            IGatewayManagementReader reader = context.RequestServices.GetRequiredService<IGatewayManagementReader>();
            if (!ValidComponent(ns) || !ValidComponent(target)) { await Write(context, Invalid(context)); return; }
            if (!await AuthorizeResource(context, authorization, ns, target, GatewayAdminResourceKind.Target).ConfigureAwait(false))
            { await Write(context, NotFound(context)); return; }
            GatewayManagedRecord<GatewayDesiredState>? desired = await reader.GetDesiredAsync(target, context.RequestAborted).ConfigureAwait(false);
            IResult result = desired is not null && StringComparer.Ordinal.Equals(desired.Value.NamespaceId, ns)
                ? TypedResults.Json(desired, GatewayAdminJsonContext.Default.GatewayManagedRecordGatewayDesiredState)
                : NotFound(context);
            await Write(context, result).ConfigureAwait(false);
        });

        Map(group, options, security, "status", async context =>
        {
            string ns = Route(context, "ns");
            string target = Route(context, "target");
            IAuthorizationService authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
            IGatewayManagementStatusReader status = context.RequestServices.GetRequiredService<IGatewayManagementStatusReader>();
            if (!ValidComponent(ns) || !ValidComponent(target)) { await Write(context, Invalid(context)); return; }
            if (!await AuthorizeResource(context, authorization, ns, target, GatewayAdminResourceKind.Target).ConfigureAwait(false))
            { await Write(context, NotFound(context)); return; }
            GatewayManagementStatusSnapshot snapshot = await status.GetCurrentAsync(context.RequestAborted).ConfigureAwait(false);
            await Write(context, TypedResults.Json(snapshot, GatewayAdminJsonContext.Default.GatewayManagementStatusSnapshot)).ConfigureAwait(false);
        });

        Map(group, options, security, "submit", context => Submit(context, activate: false, GatewayAdminCapabilities.RevisionWrite));

        Map(group, options, security, "submit-and-activate", context => Submit(context, activate: true, GatewayAdminCapabilities.RevisionSubmitAndActivate));

        Map(group, options, security, "revisions", async context =>
        {
            string ns = Route(context, "ns");
            string target = Route(context, "target");
            IAuthorizationService authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
            IGatewayManagementReader reader = context.RequestServices.GetRequiredService<IGatewayManagementReader>();
            if (!ValidComponent(ns) || !ValidComponent(target)) { await Write(context, Invalid(context)); return; }
            if (!await AuthorizeResource(context, authorization, ns, target, GatewayAdminResourceKind.Target).ConfigureAwait(false))
            { await Write(context, NotFound(context)); return; }
            int maximum = QueryMaximum(context);
            string? cursor = context.Request.Query["cursor"].Count == 1 ? context.Request.Query["cursor"][0] : null;
            GatewayManagedPage<GatewayAcceptedRevision> page = await reader.ListRevisionsAsync(
                ns, maximum, cursor, context.RequestAborted).ConfigureAwait(false);
            await Write(context, TypedResults.Json(page, GatewayAdminJsonContext.Default.GatewayManagedPageGatewayAcceptedRevision)).ConfigureAwait(false);
        });

        Map(group, options, security, "audit", async context =>
        {
            string ns = Route(context, "ns");
            IAuthorizationService authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
            IGatewayManagementReader reader = context.RequestServices.GetRequiredService<IGatewayManagementReader>();
            if (!ValidComponent(ns)) { await Write(context, Invalid(context)); return; }
            if (!await AuthorizeResource(context, authorization, ns, null, GatewayAdminResourceKind.Namespace).ConfigureAwait(false))
            { await Write(context, NotFound(context)); return; }
            int maximum = QueryMaximum(context);
            string? cursor = context.Request.Query["cursor"].Count == 1 ? context.Request.Query["cursor"][0] : null;
            GatewayManagedPage<GatewayAdministrativeAuditRecord> page = await reader.ListAuditAsync(
                ns, maximum, cursor, context.RequestAborted).ConfigureAwait(false);
            await Write(context, TypedResults.Json(page, GatewayAdminJsonContext.Default.GatewayManagedPageGatewayAdministrativeAuditRecord)).ConfigureAwait(false);
        });

        return group;
    }

    private static IEndpointConventionBuilder Map(
        RouteGroupBuilder group,
        GatewayAdminEndpointOptions options,
        IGatewayAdminSecurityMetadataProvider? security,
        string operation,
        RequestDelegate handler)
    {
        GatewayAdminEndpointDescriptor descriptor = GatewayAdminEndpointLedger.V1.Single(value => value.Operation == operation);
        IEndpointConventionBuilder mapped = group.MapMethods(descriptor.Pattern, [descriptor.Method], handler)
            .WithName("HpdGatewayAdmin." + descriptor.Operation)
            .WithMetadata(descriptor)
            .WithHpdGatewayEndpointRole(GatewayListenerRole.Management, options.EndpointSurfaceId, options.RequireManagementListener)
            .RequireAuthorization(options.CapabilityPolicies[descriptor.Capability]);
        security?.ApplyEndpoint(mapped, descriptor.Capability);
        return mapped;
    }

    private static async Task Submit(
        HttpContext context, bool activate, string capability)
    {
        string ns = Route(context, "ns");
        string target = Route(context, "target");
        IAuthorizationService authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
        IGatewayAdminActorProjector projector = context.RequestServices.GetRequiredService<IGatewayAdminActorProjector>();
        IGatewayManagementCommandCoordinator commands = context.RequestServices.GetRequiredService<IGatewayManagementCommandCoordinator>();
        if (!ValidComponent(ns) || !ValidComponent(target)) { await Write(context, Invalid(context)); return; }
        if (!await AuthorizeResource(context, authorization, ns, target, GatewayAdminResourceKind.Target).ConfigureAwait(false))
        { await Write(context, NotFound(context)); return; }
        if (!TryMutationHeaders(context, allowIfMatch: activate, out string key, out string? expected, out IResult? failure))
        { await Write(context, failure!); return; }
        byte[] body = await ReadBoundedBodyAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
        GatewayRevisionRequest? request;
        try { request = JsonSerializer.Deserialize(body, GatewayAdminJsonContext.Default.GatewayRevisionRequest); }
        catch (JsonException) { await Write(context, Invalid(context)); return; }
        if (request is null || !ValidComponent(request.SourceKind) || !ValidComponent(request.SourceId) ||
            request.Description is { Length: > 1024 }) { await Write(context, Invalid(context)); return; }
        byte[] configuration = JsonSerializer.SerializeToUtf8Bytes(
            request.Configuration, GatewayAdminJsonContext.Default.JsonElement);
        GatewayAdminRequestAttribution attribution = await projector.ProjectAsync(
            context, capability, context.RequestAborted).ConfigureAwait(false);
        GatewayManagementCommandResult result = await commands.SubmitAsync(new GatewaySubmitCommand(
            ns, target, key, attribution.ToActor(), attribution.CorrelationId,
            request.SourceKind, request.SourceId, request.Description,
            ImmutableArray.Create(configuration), expected, activate), context.RequestAborted).ConfigureAwait(false);
        await Write(context, ProjectCommand(context, result, created: !activate)).ConfigureAwait(false);
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

    private static IResult ProjectCommand(HttpContext context, GatewayManagementCommandResult result, bool created)
    {
        if (result.State is GatewayManagementCommandState.Accepted or GatewayManagementCommandState.Duplicate)
        {
            if (result.DesiredStateToken is null)
                return TypedResults.Json(new GatewayProvisionResponse(result.OperationId!, result.State == GatewayManagementCommandState.Duplicate),
                    GatewayAdminJsonContext.Default.GatewayProvisionResponse, statusCode: created ? 201 : 202);
            return TypedResults.Json(new GatewayRevisionResponse(result.OperationId!, result.DesiredStateToken,
                result.State == GatewayManagementCommandState.Duplicate),
                GatewayAdminJsonContext.Default.GatewayRevisionResponse, statusCode: created ? 201 : 202);
        }
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
        if (request.ContentLength is > MaximumBodyBytes) throw new BadHttpRequestException("Request body too large.", 413);
        using var stream = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            int remaining = MaximumBodyBytes + 1 - total;
            int read = await request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > MaximumBodyBytes) throw new BadHttpRequestException("Request body too large.", 413);
            stream.Write(buffer, 0, read);
        }
        return stream.ToArray();
    }

    private static IResult Invalid(HttpContext context) =>
        Error(context, 400, "gateway.admin.request.invalid", "The request is invalid.");

    private static IResult NotFound(HttpContext context) =>
        Error(context, 404, "gateway.admin.resource.notFound", "The resource was not found.");

    private static IResult Error(HttpContext context, int status, string code, string title) =>
        TypedResults.Json(new GatewayAdminError(code, title), GatewayAdminJsonContext.Default.GatewayAdminError, statusCode: status);

    private static bool ValidComponent(string? value) => value is { Length: > 0 and <= 128 }
        && value.IsNormalized(NormalizationForm.FormC)
        && !value.Any(char.IsControl);

    private static bool ValidVisibleAscii(string? value, int maximum) =>
        value is { Length: > 0 } && value.Length <= maximum && value.All(static c => c is >= '!' and <= '~');

    private static string Route(HttpContext context, string name) =>
        context.Request.RouteValues[name]?.ToString() ?? string.Empty;

    private static int QueryMaximum(HttpContext context) =>
        context.Request.Query["maximum"].Count == 1 &&
        int.TryParse(context.Request.Query["maximum"][0], out int value) && value is >= 1 and <= 256
            ? value
            : 64;

    private static Task Write(HttpContext context, IResult result) => result.ExecuteAsync(context);

    private static void ValidateOptions(IEndpointRouteBuilder endpoints, GatewayAdminEndpointOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RoutePrefix) || options.RoutePrefix.Length > 128 || options.RoutePrefix[0] != '/')
            throw new InvalidOperationException("The Gateway Admin route prefix is invalid.");
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AuthenticationScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RateLimitPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RequestTimeoutPolicy);
        if (!GatewayIdentifier.IsCanonical(options.EndpointSurfaceId))
            throw new InvalidOperationException("The Gateway Admin endpoint surface ID is invalid.");
        _ = endpoints.ServiceProvider.GetRequiredService<IGatewayAdminActorProjector>();
        foreach (string capability in GatewayAdminCapabilities.All)
            if (!options.CapabilityPolicies.TryGetValue(capability, out string? policy) || string.IsNullOrWhiteSpace(policy))
                throw new InvalidOperationException($"The Gateway Admin capability '{capability}' has no policy mapping.");
    }
}
