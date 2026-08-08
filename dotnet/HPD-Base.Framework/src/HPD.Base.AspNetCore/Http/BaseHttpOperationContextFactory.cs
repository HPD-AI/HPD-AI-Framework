using HPD.Base;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HPD.Base.AspNetCore;

internal sealed class BaseHttpOperationContextFactory : IBaseHttpOperationContextFactory
{
    private readonly HPDBaseAspNetCoreSnapshot _options;
    private readonly TimeProvider _timeProvider;
    private readonly IBaseHttpCorrelationProvider _correlation;

    /// <summary>Initializes a new instance.</summary>
    public BaseHttpOperationContextFactory(
        HPDBaseAspNetCoreSnapshot options,
        TimeProvider timeProvider,
        IBaseHttpCorrelationProvider correlation)
    {
        _options = options;
        _timeProvider = timeProvider;
        _correlation = correlation;
    }

    /// <summary>Executes the create operation.</summary>
    public OperationContext Create(
        HttpContext httpContext,
        PrincipalContext principal,
        BaseOperationKind operation,
        string collectionId,
        string? recordId = null,
        OperationMode mode = OperationMode.User)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(principal);

        var requestOptions = _options.RequestContext;
        var correlationId = _correlation.GetCorrelationId(httpContext);

        return new OperationContext
        {
            Operation = operation,
            CollectionId = collectionId,
            RecordId = recordId,
            TenantId = principal.CurrentTenantId,
            Mode = mode,
            CorrelationId = correlationId,
            Now = _timeProvider.GetUtcNow(),
            Request = new RequestContext
            {
                Method = httpContext.Request.Method,
                Route = RoutePattern(httpContext),
                ClientName = SafeHeader(httpContext, requestOptions.ClientNameHeaderName, requestOptions.MaxClientMetadataLength),
                ClientVersion = SafeHeader(httpContext, requestOptions.ClientVersionHeaderName, requestOptions.MaxClientMetadataLength),
                IpAddress = requestOptions.IncludeIpAddress ? httpContext.Connection.RemoteIpAddress?.ToString() : null,
                UserAgent = requestOptions.IncludeUserAgent ? SafeHeader(httpContext, "User-Agent", requestOptions.MaxClientMetadataLength) : null,
                QueryKeys = httpContext.Request.Query.Keys.Order(StringComparer.Ordinal).ToArray(),
                Redacted = true
            }
        };
    }

    private static string? RoutePattern(HttpContext httpContext) =>
        (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;

    private static string? SafeHeader(HttpContext httpContext, string headerName, int maxLength)
    {
        if (!httpContext.Request.Headers.TryGetValue(headerName, out var values))
            return null;

        var value = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
