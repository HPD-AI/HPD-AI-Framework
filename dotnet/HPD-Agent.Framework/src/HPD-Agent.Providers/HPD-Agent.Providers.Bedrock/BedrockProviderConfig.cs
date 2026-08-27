using System.Collections.Generic;
using System.Text.Json.Serialization;
using Amazon.Runtime;

namespace HPD.Agent.Providers.Bedrock;

/// <summary>
/// AWS Bedrock-specific provider configuration using the AWS BedrockRuntime SDK.
/// </summary>
public class BedrockProviderConfig : global::HPD.Agent.IProviderConfig
{
    /// <summary>
    /// AWS Region where the Bedrock service is hosted.
    /// </summary>
    [JsonPropertyName("region")]
    public string? Region { get; set; }

    /// <summary>
    /// Request timeout in milliseconds.
    /// </summary>
    [JsonPropertyName("requestTimeoutMs")]
    public int? RequestTimeoutMs { get; set; }

    /// <summary>
    /// Connection timeout in milliseconds.
    /// </summary>
    [JsonPropertyName("connectTimeoutMs")]
    public int? ConnectTimeoutMs { get; set; }

    /// <summary>
    /// Maximum number of retry attempts for failed requests.
    /// </summary>
    [JsonPropertyName("maxRetryAttempts")]
    public int? MaxRetryAttempts { get; set; }

    /// <summary>
    /// AWS SDK retry mode.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RequestRetryMode>))]
    [JsonPropertyName("retryMode")]
    public RequestRetryMode? RetryMode { get; set; }

    /// <summary>
    /// AWS SDK default configuration mode.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<DefaultConfigurationMode>))]
    [JsonPropertyName("defaultConfigurationMode")]
    public DefaultConfigurationMode? DefaultConfigurationMode { get; set; }

    /// <summary>
    /// Maximum stale connection retries for failed requests.
    /// </summary>
    [JsonPropertyName("maxStaleConnectionRetries")]
    public int? MaxStaleConnectionRetries { get; set; }

    /// <summary>
    /// Use FIPS-compliant endpoints for Bedrock.
    /// </summary>
    [JsonPropertyName("useFipsEndpoint")]
    public bool? UseFipsEndpoint { get; set; }

    /// <summary>
    /// Use dual-stack endpoints for Bedrock when supported by the configured region.
    /// </summary>
    [JsonPropertyName("useDualstackEndpoint")]
    public bool? UseDualstackEndpoint { get; set; }

    /// <summary>
    /// Use HTTP instead of HTTPS.
    /// </summary>
    [JsonPropertyName("useHttp")]
    public bool? UseHttp { get; set; }

    /// <summary>
    /// Custom endpoint URL to use instead of the standard Bedrock endpoint.
    /// </summary>
    [JsonPropertyName("serviceUrl")]
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// AWS signing region to use when it cannot be inferred from a custom service URL.
    /// </summary>
    [JsonPropertyName("authenticationRegion")]
    public string? AuthenticationRegion { get; set; }

    /// <summary>
    /// AWS signing service name.
    /// </summary>
    [JsonPropertyName("authenticationServiceName")]
    public string? AuthenticationServiceName { get; set; }

    /// <summary>
    /// Preferred AWS authentication schemes.
    /// </summary>
    [JsonPropertyName("authSchemePreference")]
    public List<string>? AuthSchemePreference { get; set; }

    /// <summary>
    /// AWS SigV4a signing region set.
    /// </summary>
    [JsonPropertyName("sigV4aSigningRegionSet")]
    public List<string>? SigV4aSigningRegionSet { get; set; }

    /// <summary>
    /// Ignore endpoint URLs configured outside this provider config.
    /// </summary>
    [JsonPropertyName("ignoreConfiguredEndpointUrls")]
    public bool? IgnoreConfiguredEndpointUrls { get; set; }

    /// <summary>
    /// Disables host prefix injection for custom or local endpoint scenarios.
    /// </summary>
    [JsonPropertyName("disableHostPrefixInjection")]
    public bool? DisableHostPrefixInjection { get; set; }

    /// <summary>
    /// Enables AWS endpoint discovery.
    /// </summary>
    [JsonPropertyName("endpointDiscoveryEnabled")]
    public bool? EndpointDiscoveryEnabled { get; set; }

    /// <summary>
    /// Disables request compression.
    /// </summary>
    [JsonPropertyName("disableRequestCompression")]
    public bool? DisableRequestCompression { get; set; }

    /// <summary>
    /// Minimum request size in bytes before compression is considered.
    /// </summary>
    [JsonPropertyName("requestMinCompressionSizeBytes")]
    public long? RequestMinCompressionSizeBytes { get; set; }

    /// <summary>
    /// AWS SDK client application identifier.
    /// </summary>
    [JsonPropertyName("clientAppId")]
    public string? ClientAppId { get; set; }

    /// <summary>
    /// Enables retry throttling.
    /// </summary>
    [JsonPropertyName("throttleRetries")]
    public bool? ThrottleRetries { get; set; }

    /// <summary>
    /// Enables fast-fail behavior when retry capacity is unavailable.
    /// </summary>
    [JsonPropertyName("fastFailRequests")]
    public bool? FastFailRequests { get; set; }

    /// <summary>
    /// Cache HTTP clients created by the AWS SDK.
    /// </summary>
    [JsonPropertyName("cacheHttpClient")]
    public bool? CacheHttpClient { get; set; }

    /// <summary>
    /// AWS SDK HTTP client cache size.
    /// </summary>
    [JsonPropertyName("httpClientCacheSize")]
    public int? HttpClientCacheSize { get; set; }

    /// <summary>
    /// Proxy host used by the AWS SDK.
    /// </summary>
    [JsonPropertyName("proxyHost")]
    public string? ProxyHost { get; set; }

    /// <summary>
    /// Proxy port used by the AWS SDK.
    /// </summary>
    [JsonPropertyName("proxyPort")]
    public int? ProxyPort { get; set; }

    /// <summary>
    /// Maximum connections per server for the AWS SDK HTTP pipeline.
    /// </summary>
    [JsonPropertyName("maxConnectionsPerServer")]
    public int? MaxConnectionsPerServer { get; set; }

    /// <summary>
    /// Log response bodies through AWS SDK logging.
    /// </summary>
    [JsonPropertyName("logResponse")]
    public bool? LogResponse { get; set; }

    /// <summary>
    /// AWS SDK transfer buffer size.
    /// </summary>
    [JsonPropertyName("bufferSize")]
    public int? BufferSize { get; set; }

    /// <summary>
    /// AWS SDK progress update interval in milliseconds.
    /// </summary>
    [JsonPropertyName("progressUpdateIntervalMs")]
    public long? ProgressUpdateIntervalMs { get; set; }

    /// <summary>
    /// Enables request resigning on retry.
    /// </summary>
    [JsonPropertyName("resignRetries")]
    public bool? ResignRetries { get; set; }

    /// <summary>
    /// Allows automatic HTTP redirects.
    /// </summary>
    [JsonPropertyName("allowAutoRedirect")]
    public bool? AllowAutoRedirect { get; set; }

    /// <summary>
    /// Enables AWS SDK metrics logging.
    /// </summary>
    [JsonPropertyName("logMetrics")]
    public bool? LogMetrics { get; set; }

    /// <summary>
    /// Disables AWS SDK logging.
    /// </summary>
    [JsonPropertyName("disableLogging")]
    public bool? DisableLogging { get; set; }
}
