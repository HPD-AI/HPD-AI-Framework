using FluentAssertions;
using HPD.Agent.StructuredOutput;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using Xunit;

#pragma warning disable MEAI001

namespace HPD.Agent.Tests.SubAgents;

public sealed class SubAgentRunConfigTests
{
    [Fact]
    public void SubAgent_DefaultsToRunEnvironmentInheritance()
    {
        var definition = SubAgent.FromConfig(
            "test/reviewer",
            "reviewer",
            "Reviews a change.",
            new AgentConfig { Name = "Reviewer" });

        definition.RunConfig.InheritedFields.Should().Be(SubAgentRunConfigFields.Default);
        definition.RunConfig.Clients.Chat.Should().Be(ClientFamilyInheritanceMode.InheritResolved);
        definition.RunConfig.Clients.ImageGeneration.Should().Be(ClientFamilyInheritanceMode.UseOwn);
        definition.RunConfig.Clients.HostedFiles.Should().Be(ClientFamilyInheritanceMode.FallbackToParent);
    }

    [Fact]
    public void WithRunConfig_ReturnsNewDeclarationWithoutMutatingOriginal()
    {
        var definition = SubAgent.FromConfig(
            "test/reviewer",
            "reviewer",
            "Reviews a change.",
            new AgentConfig { Name = "Reviewer" });

        var isolated = definition.WithRunConfig(SubAgentRunConfig.Isolated());

        isolated.Should().NotBeSameAs(definition);
        isolated.RunConfig.InheritedFields.Should().Be(SubAgentRunConfigFields.None);
        isolated.RunConfig.Clients.Chat.Should().Be(ClientFamilyInheritanceMode.UseOwn);
        definition.RunConfig.InheritedFields.Should().Be(SubAgentRunConfigFields.Default);
    }

    [Fact]
    public void DefaultSelection_InheritsEnvironmentWithoutParentBehavior()
    {
        var parent = new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig
            {
                ProviderKey = "openrouter",
                ModelName = "parent-model",
                Temperature = 0.25
            } },
            Security = new AgentSecurityRunConfig
            {
                Approval = AgentApprovalPolicy.AutoApprove,
                Sandbox = new AgentSandboxRunConfig
                {
                    Mode = AgentSandboxPolicy.Disabled,
                    Escape = AgentSandboxEscapePolicy.Deny
                }
            },
            Streaming = new StreamingRunConfig { CoalesceDeltas = true },
            SystemInstructions = new SystemInstructionsRunConfig { Override = "Parent persona" },
            StructuredOutput = new StructuredOutputOptions()
        };

        var child = SubAgentRunConfig.Inherit().Resolve(parent);

        child.Clients.Chat.Should().BeNull();
        child.Security.Should().Be(parent.Security);
        child.Security.Should().NotBeSameAs(parent.Security);
        child.Streaming!.CoalesceDeltas.Should().BeTrue();
        child.SystemInstructions.Should().BeNull();
        child.StructuredOutput.Should().BeNull();
    }

    [Fact]
    public void IncludeAndExclude_ReturnIndependentSelections()
    {
        var defaults = SubAgentRunConfig.Inherit();
        var instructions = defaults.Include(SubAgentRunConfigFields.Instructions);
        var withoutContext = instructions.Exclude(SubAgentRunConfigFields.Context);

        defaults.InheritedFields.Should().Be(SubAgentRunConfigFields.Default);
        instructions.InheritedFields.Should().HaveFlag(SubAgentRunConfigFields.Instructions);
        withoutContext.InheritedFields.Should().NotHaveFlag(SubAgentRunConfigFields.Context);
        withoutContext.InheritedFields.Should().HaveFlag(SubAgentRunConfigFields.Instructions);
    }

    [Fact]
    public void InheritAll_CopiesParentBehaviorIntoIndependentRootSnapshot()
    {
        var parent = new AgentRunConfig
        {
            SystemInstructions = new SystemInstructionsRunConfig { Override = "Parent persona" },
            Context = new AgentContextRunConfig
            {
                Properties = new Dictionary<string, object> { ["tenant"] = "one" }
            },
            Security = new AgentSecurityRunConfig
            {
                PermissionOverrides = new Dictionary<string, bool> { ["shell"] = true }
            }
        };

        var child = SubAgentRunConfig
            .InheritOnly(SubAgentRunConfigFields.All)
            .Resolve(parent);

        child.Should().NotBeSameAs(parent);
        child.SystemInstructions!.Override.Should().Be("Parent persona");
        child.Context!.Properties.Should().NotBeSameAs(parent.Context!.Properties);
        child.Security.PermissionOverrides.Should().NotBeSameAs(parent.Security.PermissionOverrides);
    }

    [Fact]
    public void IsolatedWithOverride_UsesChildOnlyConfiguration()
    {
        var parent = new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig { ProviderKey = "parent-provider" } },
            SystemInstructions = new SystemInstructionsRunConfig { Override = "Parent persona" }
        };

        var child = SubAgentRunConfig
            .Isolated()
            .Override(config =>
            {
                config.Clients.Chat ??= new ChatClientConfig();
                config.Clients.Chat.ProviderKey = "child-provider";
                config.SystemInstructions = new SystemInstructionsRunConfig { Override = "Child override" };
            })
            .Resolve(parent);

        child.Clients.Chat!.ProviderKey.Should().Be("child-provider");
        child.SystemInstructions!.Override.Should().Be("Child override");
    }

    [Fact]
    public void Override_RunsAfterInheritance()
    {
        var parent = new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig
            {
                ProviderKey = "parent-provider",
                Temperature = 0.8
            } }
        };

        var child = SubAgentRunConfig
            .Inherit()
            .Override(config => config.Clients.Chat = new ChatClientConfig
            {
                ProviderKey = "child-provider",
                Temperature = 0.1
            })
            .Resolve(parent);

        child.Clients.Chat!.ProviderKey.Should().Be("child-provider");
        child.Clients.Chat.Temperature.Should().Be(0.1);
        parent.Clients.Chat!.Temperature.Should().Be(0.8);
    }

    [Fact]
    public void InheritResolved_NonChatFamily_UsesCurrentParentPlanAndClient()
    {
        var client = new FakeTextToSpeechClient();
        var parentClients = new AgentClientSet
        {
            TextToSpeech = client,
            ResolvedConfigs = new Dictionary<ProviderClientFamily, ProviderClientConfig>
            {
                [ProviderClientFamily.TextToSpeech] = new TextToSpeechClientConfig
                {
                    ProviderKey = "parent-speech",
                    ModelName = "parent-model",
                    VoiceId = "parent-voice"
                }
            }
        };
        var selection = SubAgentRunConfig.Inherit().Override(config =>
            config.Clients.TextToSpeech = new TextToSpeechClientConfig { Speed = 1.25f });

        var child = selection.Resolve(new AgentRunConfig(), parentClients, new AgentConfig());

        child.Clients.TextToSpeech!.ProviderKey.Should().Be("parent-speech");
        child.Clients.TextToSpeech.ModelName.Should().Be("parent-model");
        child.Clients.TextToSpeech.VoiceId.Should().Be("parent-voice");
        child.Clients.TextToSpeech.Speed.Should().Be(1.25f);
        child.Clients.TextToSpeech.Override!.Client.Should().BeSameAs(client);
    }

    [Fact]
    public void InheritResolved_ExplicitProviderSwitch_DiscardsParentBoundClient()
    {
        var parentClients = new AgentClientSet
        {
            TextToSpeech = new FakeTextToSpeechClient(),
            ResolvedConfigs = new Dictionary<ProviderClientFamily, ProviderClientConfig>
            {
                [ProviderClientFamily.TextToSpeech] = new TextToSpeechClientConfig
                {
                    ProviderKey = "parent-speech",
                    ModelName = "parent-model"
                }
            }
        };
        var selection = SubAgentRunConfig.Inherit().Override(config =>
            config.Clients.TextToSpeech = new TextToSpeechClientConfig
            {
                ProviderKey = "child-speech",
                ModelName = "child-model"
            });

        var child = selection.Resolve(new AgentRunConfig(), parentClients, new AgentConfig());

        child.Clients.TextToSpeech!.ProviderKey.Should().Be("child-speech");
        child.Clients.TextToSpeech.Override.Should().BeNull();
    }

    [Fact]
    public void FallbackToParent_NonChatFamily_PreservesUsableChildDefault()
    {
        var parentClients = new AgentClientSet
        {
            HostedFiles = null,
            ResolvedConfigs = new Dictionary<ProviderClientFamily, ProviderClientConfig>()
        };
        var childDefaults = new AgentConfig
        {
            Clients = new AgentClientsConfig
            {
                HostedFiles = new HostedFilesClientConfig { ProviderKey = "child-files" }
            }
        };

        var child = SubAgentRunConfig.Inherit().Resolve(new AgentRunConfig(), parentClients, childDefaults);

        child.Clients.HostedFiles.Should().BeNull();
    }

    [Fact]
    public void ParentClientSet_BorrowedLease_DefersRunOwnedClientDisposal()
    {
        var client = new FakeTextToSpeechClient();
        var clients = new AgentClientSet { TextToSpeech = client };
        clients.SetOwnedClients(new HashSet<object>(ReferenceEqualityComparer.Instance) { client });
        var childLease = clients.AcquireBorrowedLease();

        clients.Dispose();
        client.DisposeCount.Should().Be(0);

        childLease.Dispose();
        client.DisposeCount.Should().Be(1);
    }

    private sealed class FakeTextToSpeechClient : ITextToSpeechClient
    {
        public int DisposeCount { get; private set; }
        public Task<TextToSpeechResponse> GetAudioAsync(string text, TextToSpeechOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextToSpeechResponse([]));
        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(string text, TextToSpeechOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => DisposeCount++;
    }
}
