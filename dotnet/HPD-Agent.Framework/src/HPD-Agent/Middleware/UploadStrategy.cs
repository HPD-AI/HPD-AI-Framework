namespace HPD.Agent.Middleware;

/// <summary>
/// Controls how DataContent attachments are uploaded in ContentLifecycleMiddleware.
/// </summary>
public enum UploadStrategy
{
    /// <summary>
    /// Use provider's HostedFileClient if available, otherwise fall back to IContentStore.
    /// This is the recommended default for maximum compatibility and automatic adaptation
    /// to provider capabilities.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Force upload through provider's HostedFileClient (provider-native).
    /// Throws InvalidOperationException if the current provider does not implement
    /// IHostedFileClientProvider.
    /// </summary>
    Hosted = 1,

    /// <summary>
    /// Force upload to IContentStore (framework-managed local/ephemeral storage).
    /// Ignores provider capabilities and always uses ContentStore.
    /// Emits a bounded upload failure and keeps the original DataContent when no store is configured.
    /// </summary>
    Local = 2
}
