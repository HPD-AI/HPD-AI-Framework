using HPD.MultiAgent.Config;
using HPD.Serialization;
using HPD.Agent.Serialization;
using System.Text.Json;

namespace HPD.MultiAgent.Serialization;

/// <summary>AOT-safe JSON/YAML serialization helpers for multi-agent workflow configuration documents.</summary>
public static class MultiAgentConfigSerializer
{
    public static MultiAgentWorkflowConfig? ReadFile(string path)
        => ResolveAgentFiles(
            HpdConfigSerializer.ReadFile(path, MultiAgentGraphConfigJsonContext.Default.MultiAgentWorkflowConfig),
            Path.GetDirectoryName(Path.GetFullPath(path))!);

    public static async ValueTask<MultiAgentWorkflowConfig?> ReadFileAsync(
        string path, CancellationToken cancellationToken = default)
        => ResolveAgentFiles(
            await HpdConfigSerializer.ReadFileAsync(path,
                MultiAgentGraphConfigJsonContext.Default.MultiAgentWorkflowConfig, cancellationToken).ConfigureAwait(false),
            Path.GetDirectoryName(Path.GetFullPath(path))!);

    private static MultiAgentWorkflowConfig? ResolveAgentFiles(MultiAgentWorkflowConfig? config, string directory)
    {
        if (config is null) return null;
        var agents = new Dictionary<string, AgentNodeConfig>(StringComparer.Ordinal);
        foreach (var (id, node) in config.Agents)
        {
            if ((node.Agent is null) == (node.AgentFile is null))
                throw new JsonException($"Agent node '{id}' must specify exactly one of 'agent' or 'agentFile'.");
            if (node.AgentFile is null)
            {
                agents.Add(id, node);
                continue;
            }
            if (string.IsNullOrWhiteSpace(node.AgentFile))
                throw new JsonException($"Agent node '{id}' has an empty 'agentFile'.");
            var path = Path.GetFullPath(node.AgentFile, directory);
            try
            {
                var agent = HpdAgentConfigSerializer.ReadFile(path)
                    ?? throw new JsonException("Agent configuration must not be null.");
                agents.Add(id, node with { Agent = agent, AgentFile = null });
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"Cannot load agent node '{id}' from '{path}': {ex.Message}", ex);
            }
        }
        return config with { Agents = agents };
    }

    public static void WriteFile(string path, MultiAgentWorkflowConfig config)
        => HpdConfigSerializer.WriteFile(path, config, MultiAgentGraphConfigJsonContext.Default.MultiAgentWorkflowConfig);

    public static ValueTask WriteFileAsync(
        string path,
        MultiAgentWorkflowConfig config,
        CancellationToken cancellationToken = default)
        => HpdConfigSerializer.WriteFileAsync(
            path,
            config,
            MultiAgentGraphConfigJsonContext.Default.MultiAgentWorkflowConfig,
            cancellationToken);

    public static string Serialize(MultiAgentWorkflowConfig config, HpdConfigFormat format = HpdConfigFormat.Json)
        => HpdConfigSerializer.Serialize(config, MultiAgentGraphConfigJsonContext.Default.MultiAgentWorkflowConfig, format);

    public static MultiAgentWorkflowConfig? Deserialize(string text, HpdConfigFormat format = HpdConfigFormat.Json)
        => HpdConfigSerializer.Deserialize(text, MultiAgentGraphConfigJsonContext.Default.MultiAgentWorkflowConfig, format);
}
