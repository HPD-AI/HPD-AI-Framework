using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.Serialization;
using System.Text.Json;

namespace HPD.Agent.Tests.Serialization;

public sealed class AgentConfigSerializationTests
{
    private sealed class ProviderTestOptions : IProviderConfig
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
    public void ProviderConfig_IsStoredAsTypedPayload()
    {
        var config = new ProviderClientConfig
        {
            ProviderKey = "provider-options",
            ProviderConfig = new ProviderTestOptions { budget = 512, enabled = true }
        };

        var options = config.ProviderConfig as ProviderTestOptions;

        options.Should().NotBeNull();
        options!.budget.Should().Be(512);
        options.enabled.Should().BeTrue();
    }

    [Fact]
    public void LegacyProviderMapAndConstructionOptions_AreRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-agent-options-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
        name: YAML Agent
        clients:
          providers:
            openai:
              providerKey: openai
              constructionOptions:
                organizationId: org_1
                projectId: proj_agent
          chat:
            providerKey: openai
            modelName: gpt-agent
            constructionOptions:
              projectId: proj_chat
              reasoningEffort: medium
        """);

        try
        {
            var act = () => HpdAgentConfigSerializer.ReadFile(path);

            act.Should().Throw<JsonException>();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
