using HPD.MultiAgent.Config;
using HPD.Serialization;

namespace HPD.MultiAgent.Serialization;

/// <summary>AOT-safe JSON/YAML serialization helpers for multi-agent workflow configuration documents.</summary>
public static class MultiAgentConfigSerializer
{
    public static MultiAgentWorkflowConfig? ReadFile(string path)
        => HpdConfigSerializer.ReadFile(path, MultiAgentGraphConfigJsonContext.Default.MultiAgentWorkflowConfig);

    public static ValueTask<MultiAgentWorkflowConfig?> ReadFileAsync(
        string path,
        CancellationToken cancellationToken = default)
        => HpdConfigSerializer.ReadFileAsync(
            path,
            MultiAgentGraphConfigJsonContext.Default.MultiAgentWorkflowConfig,
            cancellationToken);

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
