using HPD.MultiAgent.Config;
using HPD.Serialization;
using HPD.Agent.Serialization;
using HPD.Agent.Providers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.MultiAgent.Serialization;

/// <summary>Strict workflow serialization using Agent's provider and run-configuration contracts.</summary>
public static class MultiAgentConfigSerializer
{
    public static MultiAgentWorkflowConfig? ReadFile(string path) => ReadFileCore(path, null);
    public static MultiAgentWorkflowConfig? ReadFile(string path, ProviderComposition composition) => ReadFileCore(path, composition);
    private static MultiAgentWorkflowConfig? ReadFileCore(string path, ProviderComposition? composition)
        => ResolveAgentFiles(DeserializeCore(File.ReadAllText(path), composition, Format(path)), Path.GetDirectoryName(Path.GetFullPath(path))!, composition);

    public static ValueTask<MultiAgentWorkflowConfig?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
        => ReadFileCoreAsync(path, null, cancellationToken);
    public static ValueTask<MultiAgentWorkflowConfig?> ReadFileAsync(string path, ProviderComposition composition, CancellationToken cancellationToken = default)
        => ReadFileCoreAsync(path, composition, cancellationToken);
    private static async ValueTask<MultiAgentWorkflowConfig?> ReadFileCoreAsync(string path, ProviderComposition? composition, CancellationToken cancellationToken)
        => ResolveAgentFiles(DeserializeCore(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), composition, Format(path)),
            Path.GetDirectoryName(Path.GetFullPath(path))!, composition);

    private static MultiAgentWorkflowConfig? ResolveAgentFiles(MultiAgentWorkflowConfig? config, string directory, ProviderComposition? composition)
    {
        if (config is null) return null;
        var agents = new Dictionary<string, AgentNodeConfig>(StringComparer.Ordinal);
        foreach (var (id, node) in config.Agents)
        {
            ValidateSource(id, node);
            if (node.AgentFile is null) { agents.Add(id, node); continue; }
            string? path = null;
            try
            {
                path = Path.GetFullPath(node.AgentFile, directory);
                var agent = composition is null ? HpdAgentConfigSerializer.ReadFile(path) : HpdAgentConfigSerializer.ReadFile(path, composition);
                agents.Add(id, node with { Agent = agent ?? throw new JsonException("Agent configuration must not be null."), AgentFile = null });
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException or HPD.Agent.AgentRunConfigurationException)
            {
                throw new JsonException($"Cannot load workflow node '{id}', member 'agentFile', path '{path ?? node.AgentFile}' ({ex.GetType().Name}).", ex);
            }
        }
        return config with { Agents = agents };
    }

    private static void ValidateSource(string id, AgentNodeConfig node)
    {
        if ((node.Agent is null) == (node.AgentFile is null) || node.AgentFile is not null && string.IsNullOrWhiteSpace(node.AgentFile))
            throw new JsonException($"Workflow node '{id}' must specify exactly one nonempty 'agent' or 'agentFile'.");
    }

    public static void WriteFile(string path, MultiAgentWorkflowConfig config) => File.WriteAllText(path, Serialize(config, Format(path)));
    public static void WriteFile(string path, MultiAgentWorkflowConfig config, ProviderComposition composition) => File.WriteAllText(path, Serialize(config, composition, Format(path)));
    public static ValueTask WriteFileAsync(string path, MultiAgentWorkflowConfig config, CancellationToken cancellationToken = default)
        => new(File.WriteAllTextAsync(path, Serialize(config, Format(path)), cancellationToken));
    public static ValueTask WriteFileAsync(string path, MultiAgentWorkflowConfig config, ProviderComposition composition, CancellationToken cancellationToken = default)
        => new(File.WriteAllTextAsync(path, Serialize(config, composition, Format(path)), cancellationToken));
    public static string Serialize(MultiAgentWorkflowConfig config, HpdConfigFormat format = HpdConfigFormat.Json) => SerializeCore(config, null, format);
    public static string Serialize(MultiAgentWorkflowConfig config, ProviderComposition composition, HpdConfigFormat format = HpdConfigFormat.Json) => SerializeCore(config, composition, format);

    private static string SerializeCore(MultiAgentWorkflowConfig config, ProviderComposition? composition, HpdConfigFormat format)
    {
        var nodes = new Dictionary<string, AgentNodeConfig>();
        foreach (var (id, node) in config.Agents)
        {
            ValidateSource(id, node);
            nodes[id] = node with { Agent = null, RunConfig = null };
        }
        var root = JsonSerializer.SerializeToNode(config with { Agents = nodes }, MultiAgentGraphConfigJsonContext.Default.MultiAgentWorkflowConfig)!.AsObject();
        foreach (var (id, node) in config.Agents)
        {
            var target = root["agents"]![id]!.AsObject();
            if (node.Agent is not null)
                target["agent"] = JsonNode.Parse(composition is null ? HpdAgentConfigSerializer.Serialize(node.Agent) : HpdAgentConfigSerializer.Serialize(node.Agent, composition));
            if (node.RunConfig is not null)
                target["runConfig"] = JsonNode.Parse(composition is null ? HpdAgentConfigSerializer.Serialize(node.RunConfig) : HpdAgentConfigSerializer.Serialize(node.RunConfig, composition));
        }
        return format == HpdConfigFormat.Json ? root.ToJsonString(MultiAgentGraphConfigJsonContext.Default.Options)
            : format == HpdConfigFormat.Yaml ? HpdConfigSerializer.WriteYaml(root) : throw new ArgumentOutOfRangeException(nameof(format));
    }

    public static MultiAgentWorkflowConfig? Deserialize(string text, HpdConfigFormat format = HpdConfigFormat.Json) => DeserializeCore(text, null, format);
    public static MultiAgentWorkflowConfig? Deserialize(string text, ProviderComposition composition, HpdConfigFormat format = HpdConfigFormat.Json) => DeserializeCore(text, composition, format);
    private static MultiAgentWorkflowConfig? DeserializeCore(string text, ProviderComposition? composition, HpdConfigFormat format)
    {
        var parsed = format == HpdConfigFormat.Json ? JsonNode.Parse(text)
            : format == HpdConfigFormat.Yaml ? HpdConfigSerializer.ParseYamlToJsonNode(text) : throw new ArgumentOutOfRangeException(nameof(format));
        if (parsed is null) return null;
        var root = parsed as JsonObject ?? throw new JsonException("Workflow configuration must be an object.");
        var extracted = new Dictionary<string, (JsonNode? Agent, JsonNode? Run)>();
        if (root["agents"] is JsonObject agents)
            foreach (var (id, value) in agents)
            {
                var node = value as JsonObject ?? throw new JsonException($"Workflow node '{id}' must be an object.");
                var agent = node["agent"]?.DeepClone();
                var run = node["runConfig"]?.DeepClone();
                node.Remove("agent"); node.Remove("runConfig");
                extracted[id] = (agent, run);
            }
        var config = JsonSerializer.Deserialize(root.ToJsonString(), MultiAgentGraphConfigJsonContext.Default.MultiAgentWorkflowConfig)!;
        var resolved = new Dictionary<string, AgentNodeConfig>();
        foreach (var (id, node) in config.Agents)
        {
            var (agent, run) = extracted[id];
            try
            {
                var result = node with
                {
                    Agent = agent is null ? null : composition is null ? HpdAgentConfigSerializer.Deserialize(agent.ToJsonString()) : HpdAgentConfigSerializer.Deserialize(agent.ToJsonString(), composition),
                    RunConfig = run is null ? null : composition is null ? HpdAgentConfigSerializer.DeserializeRunConfig(run.ToJsonString()) : HpdAgentConfigSerializer.DeserializeRunConfig(run.ToJsonString(), composition)
                };
                ValidateSource(id, result);
                resolved[id] = result;
            }
            catch (Exception ex) when (ex is JsonException or HPD.Agent.AgentRunConfigurationException)
            { throw new JsonException(ex is HPD.Agent.AgentRunConfigurationException configuration
                ? $"Workflow node '{id}', member '{configuration.Path}': {configuration.Code}."
                : $"Workflow node '{id}', member '{((JsonException)ex).Path}': {ex.Message}", ex); }
        }
        return config with { Agents = resolved };
    }
    private static HpdConfigFormat Format(string path) => Path.GetExtension(path).ToLowerInvariant() is ".yaml" or ".yml" ? HpdConfigFormat.Yaml : HpdConfigFormat.Json;
}
