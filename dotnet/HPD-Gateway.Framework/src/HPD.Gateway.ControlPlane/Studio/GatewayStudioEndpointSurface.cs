using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway.ControlPlane;

/// <summary>Executes the generated Gateway Admin contract through the sealed Studio endpoint surface.</summary>
internal sealed class GatewayStudioEndpointSurface : IBaseStudioFrameworkEndpointSurface
{
    private const long MaximumResponseBytes = 8_388_608;
    private readonly IServiceProvider _services;
    private readonly GatewayControlPlaneRegistration? _registration;
    private readonly GatewayAdminHandlerCatalog? _handlers;

    public GatewayStudioEndpointSurface(IServiceProvider services, GatewayAdminHandlerCatalog? handlers = null,
        GatewayControlPlaneRegistration? registration = null)
    { _services = services; _registration = registration; _handlers = handlers; }

    /// <inheritdoc />
    public string EndpointSurfaceId => "gateway.admin.v1";
    /// <inheritdoc />
    public BaseStudioSha256 OperationInventoryChecksum => GatewayStudioModuleRegistry.OperationInventoryChecksum;
    /// <inheritdoc />
    public ImmutableArray<BaseStudioFrameworkSurfaceOperation> Operations { get; } = CreateOperations();

    /// <inheritdoc />
    public async ValueTask<BaseStudioFrameworkSurfaceResponse?> ExecuteAsync(
        BaseStudioFrameworkSurfaceRequest request, CancellationToken cancellationToken)
    {
        GatewayAdminEndpointDescriptor? endpoint = GatewayAdminEndpointLedger.V1.SingleOrDefault(
            value => StringComparer.Ordinal.Equals(value.Operation, request.OperationId));
        if (endpoint is null || !StringComparer.Ordinal.Equals(endpoint.Capability, request.RequiredCapability) ||
            _handlers is null || !_handlers.TryGet(request.OperationId, out RequestDelegate handler) ||
            !TryMatch(endpoint.Pattern, request.RelativePath, out RouteValueDictionary routeValues)) return null;

        GatewayAdminApiOptions options = _registration?.AdminOptions
            ?? throw new InvalidOperationException("Gateway Studio requires the installed Admin API.");
        if (!options.CapabilityPolicies.TryGetValue(endpoint.Capability, out string? policy))
            throw new InvalidOperationException("The Gateway Studio capability policy is absent.");

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        ClaimsPrincipal principal = request.GetPrincipal();
        AuthorizationResult authorized = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, resource: null, policy).ConfigureAwait(false);
        if (!authorized.Succeeded) return Error(403, "gateway.admin.authorization.denied", "The operation is not authorized.");

        byte[] body = request.GetBody();
        await using var responseBody = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = principal,
            RequestAborted = cancellationToken,
        };
        context.Request.Method = Method(request.Method);
        context.Request.Path = request.RelativePath;
        context.Request.QueryString = new QueryString(request.Query);
        context.Request.RouteValues = routeValues;
        context.Request.Body = new MemoryStream(body, writable: false);
        context.Request.ContentLength = body.LongLength;
        if (body.Length > 0) context.Request.ContentType = request.ContentType;
        foreach (KeyValuePair<string, string> header in request.Headers) context.Request.Headers[header.Key] = header.Value;
        context.Response.Body = responseBody;

        await GatewayAdminEndpointMapper.ExecuteHandlerAsync(context, handler).ConfigureAwait(false);
        byte[] response = responseBody.ToArray();
        return BaseStudioFrameworkSurfaceResponse.Create(context.Response.StatusCode,
            context.Response.ContentType ?? "application/json", response, MaximumResponseBytes,
            context.Response.Headers.Where(static header => !StringComparer.OrdinalIgnoreCase.Equals(header.Key, "Content-Type") &&
                !StringComparer.OrdinalIgnoreCase.Equals(header.Key, "Content-Length"))
                .Select(static header => KeyValuePair.Create(header.Key, header.Value.ToString())));
    }

    internal static ImmutableArray<BaseStudioFrameworkSurfaceOperation> CreateOperations() =>
        [.. GatewayAdminEndpointLedger.V1.OrderBy(static value => value.Operation, StringComparer.Ordinal).Select(endpoint =>
        {
            GatewayAdminClientOperationSemantics semantics = GatewayAdminClientSemanticLedger.For(endpoint.Operation);
            string[] headers = semantics.ParameterConstraints
                .Where(static value => value.Location == GatewayAdminClientParameterLocation.Header)
                .Select(static value => value.Name).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            BaseStudioTransportPurpose purpose = endpoint.Mutation ? BaseStudioTransportPurpose.CommandExecution :
                endpoint.Method == "POST" ? BaseStudioTransportPurpose.CommandPreview : BaseStudioTransportPurpose.Observation;
            string path = endpoint.Pattern.TrimStart('/');
            string[] requestMedia = semantics.RequestBodyPresence == GatewayAdminClientRequestBodyPresence.None
                ? [] : ["application/json"];
            return BaseStudioFrameworkSurfaceOperation.Create(endpoint.Operation, TransportMethod(endpoint.Method), path, purpose,
                endpoint.Capability, semantics.MaximumRequestBodyUtf8Bytes ?? 0, MaximumResponseBytes,
                TimeSpan.FromSeconds(30), requestMedia, ["application/json", "application/json; charset=utf-8"], headers, []);
        })];

    private static BaseStudioTransportMethod TransportMethod(string method) => method switch
    { "GET" => BaseStudioTransportMethod.Get, "POST" => BaseStudioTransportMethod.Post, _ => throw new InvalidOperationException("Unsupported Gateway Admin method.") };
    private static string Method(BaseStudioTransportMethod method) => method switch
    { BaseStudioTransportMethod.Get => "GET", BaseStudioTransportMethod.Post => "POST", _ => throw new InvalidOperationException("Unsupported Gateway Studio method.") };

    private static bool TryMatch(string pattern, string relativePath, out RouteValueDictionary values)
    {
        values = new RouteValueDictionary();
        string[] expected = pattern.Trim('/').Split('/');
        string[] actual = relativePath.Trim('/').Split('/');
        if (expected.Length != actual.Length) return false;
        for (int index = 0; index < expected.Length; index++)
        {
            string target = expected[index]; string supplied = actual[index];
            int open = target.IndexOf('{'); int close = target.IndexOf('}');
            if (open < 0) { if (!StringComparer.Ordinal.Equals(target, supplied)) return false; continue; }
            string prefix = target[..open]; string suffix = target[(close + 1)..];
            if (!supplied.StartsWith(prefix, StringComparison.Ordinal) || !supplied.EndsWith(suffix, StringComparison.Ordinal) ||
                supplied.Length <= prefix.Length + suffix.Length) return false;
            string encoded = supplied[prefix.Length..(supplied.Length - suffix.Length)];
            string decoded;
            try { decoded = Uri.UnescapeDataString(encoded); } catch { return false; }
            if (decoded.Contains('/') || decoded.Any(char.IsControl)) return false;
            values[target[(open + 1)..close]] = decoded;
        }
        return true;
    }

    private static BaseStudioFrameworkSurfaceResponse Error(int status, string code, string title) =>
        BaseStudioFrameworkSurfaceResponse.Create(status, "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new GatewayAdminError(code, title), GatewayAdminJsonContext.Default.GatewayAdminError), MaximumResponseBytes);
}
