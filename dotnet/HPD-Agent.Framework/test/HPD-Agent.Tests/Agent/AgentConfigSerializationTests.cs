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

    [Theory]
    [InlineData(HpdConfigFormat.Json)]
    [InlineData(HpdConfigFormat.Yaml)]
    public void CommunicationCapabilities_RoundTrip(HpdConfigFormat format)
    {
        var config = new AgentConfig
        {
            Name = "Communication Agent",
            EnableParentCommunication = true,
            AllowUserQuestions = true
        };

        var restored = HpdAgentConfigSerializer.Deserialize(
            HpdAgentConfigSerializer.Serialize(config, format),
            format);

        restored.Should().NotBeNull();
        restored!.EnableParentCommunication.Should().BeTrue();
        restored.AllowUserQuestions.Should().BeTrue();
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

    [Theory]
    [InlineData(HpdConfigFormat.Json)]
    [InlineData(HpdConfigFormat.Yaml)]
    public void ErrorHandling_SplitRetryPolicies_RoundTrip(HpdConfigFormat format)
    {
        var config = new AgentConfig
        {
            Name = "Retry Agent",
            ErrorHandling = new ErrorHandlingConfig
            {
                ModelRetries = new()
                {
                    MaxRetries = 4,
                    RetryDelay = TimeSpan.FromMilliseconds(25)
                },
                FunctionRetries = new()
                {
                    MaxRetries = 2,
                    RetryDelay = TimeSpan.FromMilliseconds(50),
                    Exclude = new()
                    {
                        Harnesses = ["ComputerHarness"],
                        Functions = ["SendPayment"],
                        Actions =
                        [
                            new ActionRetryExclusion
                            {
                                Function = "Files",
                                Action = "delete"
                            }
                        ]
                    }
                }
            }
        };

        var serialized = HpdAgentConfigSerializer.Serialize(config, format);
        var restored = HpdAgentConfigSerializer.Deserialize(serialized, format);

        restored.Should().NotBeNull();
        restored!.ErrorHandling.Should().NotBeNull();
        restored.ErrorHandling!.ModelRetries.MaxRetries.Should().Be(4);
        restored.ErrorHandling.FunctionRetries.MaxRetries.Should().Be(2);
        restored.ErrorHandling.FunctionRetries.Exclude.Harnesses.Should()
            .BeEquivalentTo("ComputerHarness");
        restored.ErrorHandling.FunctionRetries.Exclude.Functions.Should()
            .BeEquivalentTo("SendPayment");
        restored.ErrorHandling.FunctionRetries.Exclude.Actions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ActionRetryExclusion
            {
                Function = "Files",
                Action = "delete"
            });
    }

    [Theory]
    [InlineData(HpdConfigFormat.Json)]
    [InlineData(HpdConfigFormat.Yaml)]
    public void Deserialize_RejectsRetiredCombinedRetryConfiguration(HpdConfigFormat format)
    {
        var text = format == HpdConfigFormat.Json
            ? """
              {
                "name": "Legacy Retry Agent",
                "errorHandling": {
                  "maxRetries": 3
                }
              }
              """
            : """
              name: Legacy Retry Agent
              errorHandling:
                maxRetries: 3
              """;

        var act = () => HpdAgentConfigSerializer.Deserialize(text, format);

        act.Should().Throw<JsonException>()
            .WithMessage("*maxRetries*");
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
    public void Deserialize_ProviderPayloadWithoutComposition_FailsExplicitly()
    {
        var json = """
        {
          "clients": {
            "chat": {
              "providerKey": "openai",
              "providerOptions": { "serviceTier": "auto" }
            }
          }
        }
        """;

        var exception = Assert.Throws<AgentRunConfigurationException>(
            () => HpdAgentConfigSerializer.Deserialize(json));

        exception.Code.Should().Be("ProviderCompositionNotInstalled");
        exception.Path.Should().Be("clients.chat");
    }

    [Fact]
    public void ProviderConfig_IsStoredAsTypedPayload()
    {
        var config = new ProviderClientConfig
        {
            Provider = new HPD.Agent.Providers.ProviderReference { Key = "provider-options" },
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
