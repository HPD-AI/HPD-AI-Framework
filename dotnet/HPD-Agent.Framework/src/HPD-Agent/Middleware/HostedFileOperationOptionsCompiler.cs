using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

internal static class HostedFileOperationOptionsCompiler
{
    internal static HostedFileClientOptions Compile(
        AgentRunConfig runConfig,
        AgentClientSet? clientSet,
        string? omittedPurposeFallback = null)
    {
        var config = clientSet?.GetResolvedConfig(Providers.ProviderClientFamily.HostedFiles)
            as HostedFilesClientConfig
            ?? runConfig.Clients.HostedFiles;

        return new HostedFileClientOptions
        {
            Scope = config?.Scope,
            Purpose = config?.Purpose ?? omittedPurposeFallback,
            Limit = config?.Limit
        };
    }
}
