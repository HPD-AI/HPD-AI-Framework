using HPD.MultiAgent.Config;
using HPD.MultiAgent.Serialization;
using System.Text.Json;

namespace HPD.MultiAgent;

/// <summary>
/// Builds MultiAgent workflow builders and runtime instances from declarative configuration.
/// </summary>
public interface IMultiAgentFactory
{
    MultiAgent CreateBuilder(MultiAgentWorkflowConfig config);

    MultiAgent CreateBuilderFromFile(string path);

    Task<AgentWorkflowInstance> BuildAsync(
        MultiAgentWorkflowConfig config,
        CancellationToken cancellationToken = default);

    Task<AgentWorkflowInstance> BuildFromFileAsync(
        string path,
        CancellationToken cancellationToken = default);
}

public sealed class MultiAgentFactory : IMultiAgentFactory
{
    private readonly WorkflowHostBinding? _host;
    public MultiAgentFactory(WorkflowHostBinding? host = null) => _host = host;

    public MultiAgent CreateBuilder(MultiAgentWorkflowConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new MultiAgent(config, _host);
    }

    public MultiAgent CreateBuilderFromFile(string path)
        => CreateBuilder(LoadConfigFile(path));

    public Task<AgentWorkflowInstance> BuildAsync(
        MultiAgentWorkflowConfig config,
        CancellationToken cancellationToken = default)
        => CreateBuilder(config).BuildAsync(cancellationToken);

    public Task<AgentWorkflowInstance> BuildFromFileAsync(
        string path,
        CancellationToken cancellationToken = default)
        => CreateBuilderFromFile(path).BuildAsync(cancellationToken);

    private MultiAgentWorkflowConfig LoadConfigFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Multi-agent workflow configuration file path cannot be null or empty.", nameof(path));

        if (!File.Exists(path))
            throw new FileNotFoundException($"Multi-agent workflow configuration file not found: {path}");

        return (_host?.Providers is { } providers ? MultiAgentConfigSerializer.ReadFile(path, providers) : MultiAgentConfigSerializer.ReadFile(path))
            ?? throw new JsonException($"Failed to deserialize MultiAgentWorkflowConfig from '{path}' - result was null.");
    }
}
