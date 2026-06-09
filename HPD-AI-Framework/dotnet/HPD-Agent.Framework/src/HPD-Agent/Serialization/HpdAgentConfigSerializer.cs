using HPD.Serialization;

namespace HPD.Agent.Serialization;

/// <summary>AOT-safe JSON/YAML serialization helpers for HPD agent configuration documents.</summary>
public static class HpdAgentConfigSerializer
{
    public static AgentConfig? ReadFile(string path)
        => HpdConfigSerializer.ReadFile(path, HPDJsonContext.Default.AgentConfig);

    public static ValueTask<AgentConfig?> ReadFileAsync(
        string path,
        CancellationToken cancellationToken = default)
        => HpdConfigSerializer.ReadFileAsync(
            path,
            HPDJsonContext.Default.AgentConfig,
            cancellationToken);

    public static void WriteFile(string path, AgentConfig config)
        => HpdConfigSerializer.WriteFile(path, config, HPDJsonContext.Default.AgentConfig);

    public static ValueTask WriteFileAsync(
        string path,
        AgentConfig config,
        CancellationToken cancellationToken = default)
        => HpdConfigSerializer.WriteFileAsync(
            path,
            config,
            HPDJsonContext.Default.AgentConfig,
            cancellationToken);

    public static string Serialize(AgentConfig config, HpdConfigFormat format = HpdConfigFormat.Json)
        => HpdConfigSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig, format);

    public static AgentConfig? Deserialize(string text, HpdConfigFormat format = HpdConfigFormat.Json)
        => HpdConfigSerializer.Deserialize(text, HPDJsonContext.Default.AgentConfig, format);
}
