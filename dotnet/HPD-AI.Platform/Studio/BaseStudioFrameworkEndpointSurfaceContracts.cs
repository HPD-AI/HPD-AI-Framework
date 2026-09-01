using System.Collections.Immutable;
using System.Security.Claims;

namespace HPD.AI.Platform.Studio;

/// <summary>Contains one bounded request admitted through a shell-owned framework-client endpoint surface.</summary>
public sealed class BaseStudioFrameworkSurfaceRequest
{
    internal BaseStudioFrameworkSurfaceRequest(string operationId, string requiredCapability, string relativePath, string query,
        BaseStudioTransportMethod method, string? contentType, byte[] body, ImmutableSortedDictionary<string, string> headers,
        BaseStudioResponseAuthority authority, ClaimsPrincipal principal)
    { OperationId = operationId; RequiredCapability = requiredCapability; RelativePath = relativePath; Query = query; Method = method;
      ContentType = contentType; _body = body; Headers = headers; Authority = authority; _principal = Clone(principal); }
    private readonly byte[] _body;
    private readonly ClaimsPrincipal _principal;
    /// <summary>Gets the generated operation identity.</summary>
    public string OperationId { get; }
    /// <summary>Gets the exact registered framework capability that the executor must reauthorize.</summary>
    public string RequiredCapability { get; }
    /// <summary>Gets the bounded operation-relative path.</summary>
    public string RelativePath { get; }
    /// <summary>Gets the bounded query string including its leading question mark.</summary>
    public string Query { get; }
    /// <summary>Gets the exact transport method.</summary>
    public BaseStudioTransportMethod Method { get; }
    /// <summary>Gets the exact registered request media type, or <see langword="null"/> for a bodyless request.</summary>
    public string? ContentType { get; }
    /// <summary>Gets the exact generated semantic request headers, excluding authentication and shell protocol headers.</summary>
    public ImmutableSortedDictionary<string, string> Headers { get; }
    /// <summary>Gets the current principal-filtered Studio response authority.</summary>
    public BaseStudioResponseAuthority Authority { get; }
    /// <summary>Returns a defensive copy of the bounded request body.</summary>
    public byte[] GetBody() => _body.ToArray();
    /// <summary>Returns a defensive server-only principal snapshot for framework operation-specific authorization.</summary>
    public ClaimsPrincipal GetPrincipal() => Clone(_principal);
    private static ClaimsPrincipal Clone(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal); return new ClaimsPrincipal(principal.Identities.Select(identity =>
        {
            var copy = new ClaimsIdentity(identity.AuthenticationType, identity.NameClaimType, identity.RoleClaimType);
            copy.AddClaims(identity.Claims.Select(claim => claim.Clone(copy))); return copy;
        }));
    }
}

/// <summary>Contains one bounded response from a registered framework-client endpoint surface.</summary>
public sealed class BaseStudioFrameworkSurfaceResponse
{
    private readonly byte[] _body;
    private BaseStudioFrameworkSurfaceResponse(int statusCode, string contentType, byte[] body, ImmutableSortedDictionary<string, string> headers)
    { StatusCode = statusCode; ContentType = contentType; _body = body; Headers = headers; }
    /// <summary>Gets the safe HTTP status.</summary>
    public int StatusCode { get; }
    /// <summary>Gets the exact registered response content type.</summary>
    public string ContentType { get; }
    /// <summary>Gets the exact generated semantic response headers.</summary>
    public ImmutableSortedDictionary<string, string> Headers { get; }
    /// <summary>Returns a defensive copy of the bounded response bytes.</summary>
    public byte[] GetBody() => _body.ToArray();
    /// <summary>Creates a deeply owned response.</summary>
    public static BaseStudioFrameworkSurfaceResponse Create(int statusCode, string contentType, ReadOnlySpan<byte> body, long maximumBytes,
        IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        if (statusCode is < 200 or > 599 || maximumBytes < 0 || body.Length > maximumBytes)
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        if (!SafeMediaType(contentType)) throw new ArgumentException("Framework Studio response media type is invalid.", nameof(contentType));
        return new(statusCode, new string(contentType.AsSpan()), body.ToArray(), OwnHeaders(headers ?? []));
    }
    private static ImmutableSortedDictionary<string, string> OwnHeaders(IEnumerable<KeyValuePair<string, string>> headers)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in headers)
            if (builder.Count == 32 || !SafeHeader(pair.Key) || string.IsNullOrEmpty(pair.Value) || pair.Value.Length > 4_096 || pair.Value.Any(char.IsControl) || !builder.TryAdd(pair.Key, pair.Value))
                throw new ArgumentException("A framework Studio response header is invalid.", nameof(headers));
        return builder.ToImmutable();
    }
    internal static bool SafeHeader(string name) => name.Length is > 0 and <= 128 && name.All(static value => char.IsAsciiLetterOrDigit(value) || value == '-') &&
        name is not ("Authorization" or "Cookie" or "Set-Cookie" or "Connection" or "Transfer-Encoding" or "Proxy-Authorization" or "Proxy-Authenticate" or "Upgrade");
    internal static bool SafeMediaType(string value) => value.Length is > 2 and <= 128 && !value.Any(char.IsControl) && value.Count(static character => character == '/') == 1;
}

/// <summary>Describes one frozen operation admitted by a framework endpoint surface.</summary>
public sealed record BaseStudioFrameworkSurfaceOperation(string OperationId, BaseStudioTransportMethod Method, string RelativePathTemplate,
    BaseStudioTransportPurpose Purpose, string RequiredCapability, long MaximumRequestBytes, long MaximumResponseBytes, TimeSpan Deadline,
    ImmutableArray<string> RequestMediaTypes, ImmutableArray<string> ResponseMediaTypes,
    ImmutableArray<string> RequestHeaderNames, ImmutableArray<string> ResponseHeaderNames)
{
    /// <summary>Creates and validates one exact operation descriptor.</summary>
    public static BaseStudioFrameworkSurfaceOperation Create(string operationId, BaseStudioTransportMethod method,
        string relativePathTemplate, BaseStudioTransportPurpose purpose, string requiredCapability, long maximumRequestBytes, long maximumResponseBytes, TimeSpan deadline)
        => Create(operationId, method, relativePathTemplate, purpose, requiredCapability, maximumRequestBytes, maximumResponseBytes, deadline,
            ["application/json", "application/json; charset=utf-8"], ["application/json", "application/json; charset=utf-8"], [], []);
    /// <summary>Creates and validates one exact operation descriptor including its generated semantic headers.</summary>
    public static BaseStudioFrameworkSurfaceOperation Create(string operationId, BaseStudioTransportMethod method,
        string relativePathTemplate, BaseStudioTransportPurpose purpose, string requiredCapability, long maximumRequestBytes, long maximumResponseBytes, TimeSpan deadline,
        IEnumerable<string> requestMediaTypes, IEnumerable<string> responseMediaTypes,
        IEnumerable<string> requestHeaderNames, IEnumerable<string> responseHeaderNames)
    {
        StudioContractValidation.Id(operationId); StudioContractValidation.Enum(method); StudioContractValidation.Enum(purpose);
        string[] segments = relativePathTemplate.Split('/'); var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        if (relativePathTemplate.Length is < 1 or > 2_048 || relativePathTemplate.StartsWith('/') ||
            segments.Any(segment => !ValidTemplateSegment(segment, parameterNames)))
            throw new ArgumentException("Framework Studio relative path template is invalid.", nameof(relativePathTemplate));
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredCapability);
        if (requiredCapability.Length > 256 || requiredCapability.Any(static value => char.IsControl(value) || char.IsWhiteSpace(value)))
            throw new ArgumentException("Framework Studio capability identity is invalid.", nameof(requiredCapability));
        if (maximumRequestBytes is < 0 or > 64 * 1024 * 1024 || maximumResponseBytes is < 1 or > 64 * 1024 * 1024 ||
            deadline <= TimeSpan.Zero || deadline > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(maximumRequestBytes));
        ImmutableArray<string> requests = Headers(requestHeaderNames, nameof(requestHeaderNames));
        ImmutableArray<string> responses = Headers(responseHeaderNames, nameof(responseHeaderNames));
        ImmutableArray<string> requestTypes = Media(requestMediaTypes, maximumRequestBytes == 0, nameof(requestMediaTypes));
        ImmutableArray<string> responseTypes = Media(responseMediaTypes, false, nameof(responseMediaTypes));
        return new(operationId, method, new string(relativePathTemplate.AsSpan()), purpose, new string(requiredCapability.AsSpan()), maximumRequestBytes, maximumResponseBytes, deadline,
            requestTypes, responseTypes, requests, responses);
        static ImmutableArray<string> Headers(IEnumerable<string> values, string parameter)
        {
            ImmutableArray<string> result = StudioContractValidation.Materialize(values, 32, true, parameter);
            if (result.Any(static value => !BaseStudioFrameworkSurfaceResponse.SafeHeader(value)) ||
                !result.SequenceEqual(result.Order(StringComparer.OrdinalIgnoreCase)) || result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
                throw new ArgumentException("Framework Studio header names are not canonical.", parameter);
            return result;
        }
        static ImmutableArray<string> Media(IEnumerable<string> values, bool allowEmpty, string parameter)
        {
            ImmutableArray<string> result = StudioContractValidation.Materialize(values, 8, allowEmpty, parameter);
            if (result.Any(static value => !BaseStudioFrameworkSurfaceResponse.SafeMediaType(value)) ||
                !result.SequenceEqual(result.Order(StringComparer.OrdinalIgnoreCase)) || result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
                throw new ArgumentException("Framework Studio media types are not canonical.", parameter);
            return result;
        }
    }

    internal bool Matches(string relativePath)
    {
        string[] expected = RelativePathTemplate.Split('/'); string[] actual = relativePath.Split('/'); if (expected.Length != actual.Length) return false;
        for (int index = 0; index < expected.Length; index++)
            if (!MatchSegment(expected[index], actual[index])) return false;
        return true;
    }

    private static bool ValidTemplateSegment(string segment, HashSet<string> parameterNames)
    {
        if (segment.Length is < 1 or > 512 || segment is "." or ".." || segment.Any(char.IsControl)) return false;
        int open = segment.IndexOf('{'); int close = segment.IndexOf('}');
        if (open < 0) return close < 0 && !segment.Contains('{') && !segment.Contains('}');
        if (close <= open + 1 || segment.IndexOf('{', open + 1) >= 0 || segment.IndexOf('}', close + 1) >= 0) return false;
        string name = segment[(open + 1)..close];
        return name.All(static value => char.IsAsciiLetterOrDigit(value) || value is '.' or '-' or '_') && parameterNames.Add(name);
    }

    private static bool MatchSegment(string template, string actual)
    {
        int open = template.IndexOf('{'); if (open < 0) return StringComparer.Ordinal.Equals(template, actual);
        int close = template.IndexOf('}', open + 1); string prefix = template[..open]; string suffix = template[(close + 1)..];
        if (!actual.StartsWith(prefix, StringComparison.Ordinal) || !actual.EndsWith(suffix, StringComparison.Ordinal) ||
            actual.Length <= prefix.Length + suffix.Length) return false;
        string parameter = actual[prefix.Length..(actual.Length - suffix.Length)];
        return parameter.Length <= 512 && !parameter.Any(char.IsControl) && !parameter.Contains('/');
    }

    /// <summary>Computes the frozen generated operation-inventory checksum used by graph, surface, bootstrap, and client activation.</summary>
    public static BaseStudioSha256 ComputeInventoryChecksum(string endpointSurfaceId, IEnumerable<BaseStudioFrameworkSurfaceOperation> operations)
    {
        StudioContractValidation.Id(endpointSurfaceId); ImmutableArray<BaseStudioFrameworkSurfaceOperation> owned =
            StudioContractValidation.Materialize(operations, 1_024, false, nameof(operations));
        if (!owned.Select(static value => value.OperationId).SequenceEqual(owned.Select(static value => value.OperationId).Order(StringComparer.Ordinal)) ||
            owned.Select(static value => value.OperationId).Distinct(StringComparer.Ordinal).Count() != owned.Length)
            throw new ArgumentException("Framework Studio operations are not canonical.", nameof(operations));
        return StudioCanonicalEncoding.Hash("base.studio.framework-operation-inventory.v1", writer =>
        {
            writer.String(endpointSurfaceId); writer.Count(owned.Length); foreach (BaseStudioFrameworkSurfaceOperation value in owned)
            { writer.String(value.OperationId); writer.Enum(value.Method); writer.String(value.RelativePathTemplate); writer.Enum(value.Purpose);
              writer.String(value.RequiredCapability); writer.Int64(value.MaximumRequestBytes); writer.Int64(value.MaximumResponseBytes);
              writer.Int64(checked((long)value.Deadline.TotalMilliseconds)); writer.Count(value.RequestMediaTypes.Length);
              foreach (string item in value.RequestMediaTypes) writer.String(item); writer.Count(value.ResponseMediaTypes.Length);
              foreach (string item in value.ResponseMediaTypes) writer.String(item); writer.Count(value.RequestHeaderNames.Length);
              foreach (string item in value.RequestHeaderNames) writer.String(item); writer.Count(value.ResponseHeaderNames.Length);
              foreach (string item in value.ResponseHeaderNames) writer.String(item); }
        });
    }
}

/// <summary>Executes one frozen generated-client endpoint surface without exposing its route prefix or credentials.</summary>
public interface IBaseStudioFrameworkEndpointSurface
{
    /// <summary>Gets the exact endpoint-surface identity installed in the Studio graph.</summary>
    string EndpointSurfaceId { get; }
    /// <summary>Gets the generated operation-inventory checksum.</summary>
    BaseStudioSha256 OperationInventoryChecksum { get; }
    /// <summary>Gets the exact generated operation identities accepted by this surface.</summary>
    ImmutableArray<BaseStudioFrameworkSurfaceOperation> Operations { get; }
    /// <summary>Executes one already-authorized bounded request through framework-owned semantic handlers.</summary>
    ValueTask<BaseStudioFrameworkSurfaceResponse?> ExecuteAsync(BaseStudioFrameworkSurfaceRequest request, CancellationToken cancellationToken);
}

internal sealed class BaseStudioFrameworkEndpointSurfaceCatalog
{
    private readonly ImmutableDictionary<string, IBaseStudioFrameworkEndpointSurface> _surfaces;
    public BaseStudioFrameworkEndpointSurfaceCatalog(IEnumerable<IBaseStudioFrameworkEndpointSurface> surfaces)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, IBaseStudioFrameworkEndpointSurface>(StringComparer.Ordinal);
        foreach (IBaseStudioFrameworkEndpointSurface surface in surfaces)
        {
            StudioContractValidation.Id(surface.EndpointSurfaceId); ArgumentNullException.ThrowIfNull(surface.OperationInventoryChecksum);
            if (surface.Operations.IsDefaultOrEmpty || surface.Operations.Length > 1_024 ||
                !surface.Operations.Select(static value => value.OperationId).SequenceEqual(surface.Operations.Select(static value => value.OperationId).Order(StringComparer.Ordinal)) ||
                surface.Operations.Select(static value => value.OperationId).Distinct(StringComparer.Ordinal).Count() != surface.Operations.Length)
                throw new InvalidOperationException("A framework Studio endpoint surface operation inventory is invalid.");
            if (!BaseStudioSha256.FixedTimeEquals(surface.OperationInventoryChecksum,
                    BaseStudioFrameworkSurfaceOperation.ComputeInventoryChecksum(surface.EndpointSurfaceId, surface.Operations)))
                throw new InvalidOperationException("A framework Studio endpoint surface inventory checksum is invalid.");
            if (!builder.TryAdd(surface.EndpointSurfaceId, surface)) throw new InvalidOperationException("A framework Studio endpoint surface is duplicated.");
        }
        _surfaces = builder.ToImmutable();
    }
    internal bool TryGet(string id, out IBaseStudioFrameworkEndpointSurface surface) => _surfaces.TryGetValue(id, out surface!);
}
