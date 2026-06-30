namespace HPD.Base.AspNetCore.Configuration;

/// <summary>
/// Configures how HTTP request metadata is copied into HPD.BASE operation contexts.
/// </summary>
public sealed class HPDBaseHttpRequestContextOptions
{
    /// <summary>
    /// Gets or sets whether the remote IP address may be copied into request context.
    /// </summary>
    public bool IncludeIpAddress { get; set; }

    /// <summary>
    /// Gets or sets whether the user agent may be copied into request context.
    /// </summary>
    public bool IncludeUserAgent { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum length of copied client metadata values.
    /// </summary>
    public int MaxClientMetadataLength { get; set; } = 128;

    /// <summary>
    /// Gets or sets the header that carries a caller supplied correlation id.
    /// </summary>
    public string CorrelationIdHeaderName { get; set; } = "X-Correlation-ID";

    /// <summary>
    /// Gets or sets the header that carries the HPD client name.
    /// </summary>
    public string ClientNameHeaderName { get; set; } = "X-HPD-Client";

    /// <summary>
    /// Gets or sets the header that carries the HPD client version.
    /// </summary>
    public string ClientVersionHeaderName { get; set; } = "X-HPD-Client-Version";
}
