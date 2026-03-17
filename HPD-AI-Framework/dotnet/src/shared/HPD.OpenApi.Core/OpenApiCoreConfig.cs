namespace HPD.OpenApi.Core;

/// <summary>
/// Configuration for OpenAPI spec parsing and operation execution.
/// Framework-agnostic: zero references to HPD-Agent, Microsoft.Extensions.AI, or HPD.Graph.
///
/// HPD-Agent.OpenApi extends this with OpenApiConfig, adding agent-specific fields
/// (RequiresPermission, CollapseWithinToolkit, SchemaTransformOptions, ResponseOptimization).
/// </summary>
public class OpenApiCoreConfig
{
    /// <summary>
    /// Path to OpenAPI spec file (JSON).
    /// Mutually exclusive with SpecUri — exactly one must be set.
    /// </summary>
    public string? SpecPath { get; set; }

    /// <summary>
    /// URI to fetch OpenAPI spec from.
    /// Mutually exclusive with SpecPath — exactly one must be set.
    /// </summary>
    public Uri? SpecUri { get; set; }

    /// <summary>
    /// Override base URL for all operations.
    /// If not set, uses server URL from spec.
    /// Analogous to SK's ServerUrlOverride.
    /// </summary>
    public Uri? ServerUrlOverride { get; set; }

    /// <summary>
    /// HTTP client to use for both fetching specs from URI and executing operations.
    /// Never disposed by HPD.OpenApi.Core — caller owns the lifecycle.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Authentication callback invoked before each API request is sent.
    /// Signature matches SK's AuthenticateRequestAsyncCallback.
    /// </summary>
    public Func<HttpRequestMessage, CancellationToken, Task>? AuthCallback { get; set; }

    /// <summary>
    /// Optional callback to detect API-specific error patterns in responses that
    /// return HTTP 200 but indicate failure (e.g., Slack's { "ok": false } pattern).
    ///
    /// Called after every successful HTTP response. Receives the HttpResponseMessage
    /// and the response body as a string. Return a non-null OpenApiErrorResponse
    /// to treat the response as an error.
    ///
    /// Most APIs don't need this — standard HTTP error codes are handled automatically.
    /// Only set this for APIs with non-standard error shapes.
    ///
    /// Example (Slack):
    ///   ErrorDetector = (response, body) =>
    ///   {
    ///       if (body != null
    ///           &amp;&amp; JsonDocument.Parse(body).RootElement.TryGetProperty("ok", out var ok)
    ///           &amp;&amp; !ok.GetBoolean())
    ///           return new OpenApiErrorResponse { StatusCode = 200, Body = body };
    ///       return null;
    ///   }
    /// </summary>
    public Func<HttpResponseMessage, string?, OpenApiErrorResponse?>? ErrorDetector { get; set; }

    /// <summary>
    /// Predicate to filter which operations to import.
    /// Return true to include the operation.
    /// Mutually exclusive with OperationsToExclude.
    /// </summary>
    public Func<OperationSelectionContext, bool>? OperationSelectionPredicate { get; set; }

    /// <summary>
    /// List of operation IDs to exclude from import.
    /// Mutually exclusive with OperationSelectionPredicate.
    /// </summary>
    public IList<string>? OperationsToExclude { get; set; }

    /// <summary>
    /// When true (default), nested body properties are flattened into separate parameters.
    /// When false, the entire body is passed as a single "payload" string parameter.
    /// Matches SK's EnableDynamicPayload.
    /// </summary>
    public bool EnableDynamicPayload { get; set; } = true;

    /// <summary>
    /// When true, payload parameter names are prefixed with parent property names
    /// to avoid collisions in deeply nested objects.
    /// Only relevant when EnableDynamicPayload is true.
    /// Matches SK's EnablePayloadNamespacing.
    /// </summary>
    public bool EnablePayloadNamespacing { get; set; } = false;

    /// <summary>
    /// When true, parsing continues with warnings instead of throwing on non-compliant specs.
    /// </summary>
    public bool IgnoreNonCompliantErrors { get; set; } = false;

    /// <summary>User-Agent header for API requests.</summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Timeout for API requests on internally-created HttpClient instances.
    /// Ignored if HttpClient is user-provided (user controls timeout on their own client).
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
