namespace HPD.Agent.Middleware;

/// <summary>
/// Controls how DataContent attachments are uploaded in ContentUploadMiddleware.
/// </summary>
public enum UploadStrategy
{
    /// <summary>
    /// Use provider's HostedFileClient if available, otherwise fall back to workspace store.
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
    /// Force upload to the configured workspace store facade.
    /// Ignores provider capabilities and always writes through the workspace.
    /// Throws InvalidOperationException if no workspace store is configured.
    /// </summary>
    Local = 2
}
