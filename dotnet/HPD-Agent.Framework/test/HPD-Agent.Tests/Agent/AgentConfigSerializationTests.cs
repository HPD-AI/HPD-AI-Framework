using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.Serialization;
using System.Text.Json;

namespace HPD.Agent.Tests.Serialization;

public sealed class AgentConfigSerializationTests
{
    private sealed class ProviderTestOptions
    {
        public int budget { get; set; }
        public bool enabled { get; set; }
    }

    [Fact]
    public void ReadFile_YamlExtension_LoadsAgentConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-agent-config-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
        name: YAML Agent
        systemInstructions: Answer carefully.
        maxAgenticIterations: 4
        continuationExtensionAmount: 2
        """);

        try
        {
            var config = HpdAgentConfigSerializer.ReadFile(path);

            config.Should().NotBeNull();
            config!.Name.Should().Be("YAML Agent");
            config.SystemInstructions.Should().Be("Answer carefully.");
            config.MaxAgenticIterations.Should().Be(4);
            config.ContinuationExtensionAmount.Should().Be(2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AgentBuilder_FromFile_YamlExtension_LoadsAgentConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-agent-builder-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
        name: Builder YAML Agent
        systemInstructions: Build from the obvious API.
        maxAgenticIterations: 3
        """);

        try
        {
            var builder = AgentBuilder.FromFile(path);

            builder.Config.Name.Should().Be("Builder YAML Agent");
            builder.Config.SystemInstructions.Should().Be("Build from the obvious API.");
            builder.Config.MaxAgenticIterations.Should().Be(3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Serialize_YamlFormat_EmitsYaml()
    {
        var config = new AgentConfig
        {
            Name = "Exported Agent",
            SystemInstructions = "Export me."
        };

        var yaml = HpdAgentConfigSerializer.Serialize(config, HpdConfigFormat.Yaml);

        yaml.Should().Contain("name: Exported Agent");
        yaml.Should().Contain("systemInstructions: Export me.");
    }

    [Fact]
    public void Deserialize_RejectsUnknownAgentConfigProperties()
    {
        var json = """
        {
          "name": "Strict Agent",
          "systemInstructions": "No stale config.",
          "oldProvider": "openai"
        }
        """;

        var act = () => HpdAgentConfigSerializer.Deserialize(json);

        act.Should().Throw<JsonException>()
            .WithMessage("*oldProvider*");
    }

    [Fact]
    public void Deserialize_RejectsStringNumbers()
    {
        var json = """
        {
          "name": "Strict Agent",
          "maxAgenticIterations": "4"
        }
        """;

        var act = () => HpdAgentConfigSerializer.Deserialize(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void ProviderOptions_ObjectFeedsRegisteredProviderDeserializer()
    {
        var providerKey = $"provider-options-{Guid.NewGuid():N}";
        ProviderContributionRegistry.RegisterProviderConfigType<ProviderTestOptions>(
            providerKey,
            json => JsonSerializer.Deserialize<ProviderTestOptions>(json),
            config => JsonSerializer.Serialize(config));

        var config = new ClientProviderConfig
        {
            ProviderKey = providerKey,
            ProviderOptions = JsonDocument.Parse("""{"budget":512,"enabled":true}""").RootElement.Clone()
        };

        var options = config.GetProviderConfig<ProviderTestOptions>();

        options.Should().NotBeNull();
        options!.budget.Should().Be(512);
        options.enabled.Should().BeTrue();
    }

    [Fact]
    public void ProviderOptions_YamlObjectMergesWithOverrides()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-agent-options-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
        name: YAML Agent
        clients:
          providers:
            openai:
              providerKey: openai
              providerOptions:
                organizationId: org_1
                projectId: proj_agent
          chat:
            providerKey: openai
            modelName: gpt-agent
            providerOptions:
              projectId: proj_chat
              reasoningEffort: medium
        """);

        try
        {
            var config = HpdAgentConfigSerializer.ReadFile(path);

            var resolved = config!.ResolveClientConfig(ProviderClientFamily.Chat);

            resolved.Should().NotBeNull();
            using var json = JsonDocument.Parse(resolved!.GetProviderOptionsRawJson()!);
            var root = json.RootElement;
            root.GetProperty("organizationId").GetString().Should().Be("org_1");
            root.GetProperty("projectId").GetString().Should().Be("proj_chat");
            root.GetProperty("reasoningEffort").GetString().Should().Be("medium");
            resolved.ProviderOptions.Should().NotBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
