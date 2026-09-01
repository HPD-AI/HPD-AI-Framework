using FluentAssertions;
using HPD.Agent.StructuredOutput;
using HPD.Agent.Providers;
using HPD.Agent.Permissions;
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
                Provider = new HPD.Agent.Providers.ProviderReference { Key = "openrouter" },
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
                PermissionOverrides = [new(new("shell"), RequiresPermission: true)]
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
    public void InheritAll_DeepClonesOwnedAudioCompactionAndStructuredOutput()
    {
        var parent = new AgentRunConfig
        {
            Audio = new AudioRunConfig { ContentType = "audio/pcm" },
            Compaction = new CompactionRunPolicy(),
            Collapsing = new CollapsingRunPolicy { EnableErrorRecovery = false },
            StructuredOutput = new StructuredOutputOptions { UnionTypes = [typeof(string)] }
        };

        var child = SubAgentRunConfig
            .InheritOnly(SubAgentRunConfigFields.All)
            .Resolve(parent);

        child.Audio.Should().NotBeSameAs(parent.Audio);
        child.Compaction.Should().NotBeSameAs(parent.Compaction);
        child.Collapsing.Should().NotBeSameAs(parent.Collapsing);
        child.Collapsing!.EnableErrorRecovery.Should().BeFalse();
        child.StructuredOutput.Should().NotBeSameAs(parent.StructuredOutput);
        child.StructuredOutput!.UnionTypes.Should().NotBeSameAs(parent.StructuredOutput!.UnionTypes);
    }

    [Fact]
    public void InheritAll_NeverPropagatesControllerRelativeSubAgentOverrides()
    {
        var parent = new AgentRunConfig
        {
            SubAgents = new SubAgentRunOverrides
            {
                Capabilities = [new SubAgentRunPolicyOverride
                {
                    CapabilityId = CapabilityId.Create("outer:worker"),
                    InheritedFields = SubAgentRunConfigFields.None
                }]
            }
        };

        var child = SubAgentRunConfig.InheritOnly(SubAgentRunConfigFields.All).Resolve(parent);

        child.SubAgents.Capabilities.Should().BeEmpty();
    }

    [Fact]
    public void CompilePolicy_CapturesIsolatedDeclarationWithoutMutableCallback()
    {
        var parent = new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig { Provider = new HPD.Agent.Providers.ProviderReference { Key = "parent-provider" } } },
            SystemInstructions = new SystemInstructionsRunConfig { Override = "Parent persona" }
        };

        var declaration = SubAgentRunConfig.Isolated();
        var policy = declaration.CompilePolicy();
        var child = SubAgentRunConfig.Resolve(policy, parent);

        policy.InheritedFields.Should().Be(SubAgentRunConfigFields.None);
        policy.Clients.Chat.Should().Be(ClientFamilyInheritanceMode.UseOwn);
        child.Clients.Chat.Should().BeNull();
        child.SystemInstructions.Should().BeNull();
    }

    [Fact]
    public void TargetedOverride_RequiresDeclarationAllowanceAndProducesFrozenPolicy()
    {
        var parent = new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig
            {
                Provider = new HPD.Agent.Providers.ProviderReference { Key = "parent-provider" },
                Temperature = 0.8
            } }
        };

        var declaration = SubAgentRunConfig.Inherit().AllowParentRunOverrides(
            new SubAgentRunPolicyOverrideAllowance
            {
                MayEnableInheritedFields = SubAgentRunConfigFields.Instructions,
                Clients = new AgentClientInheritanceOverrideAllowance { Chat = true }
            });
        var runOverride = new SubAgentRunPolicyOverride
        {
            CapabilityId = CapabilityId.Create("test:reviewer"),
            InheritedFields = SubAgentRunConfigFields.Default | SubAgentRunConfigFields.Instructions,
            Clients = new AgentClientInheritancePatch { Chat = ClientFamilyInheritanceMode.UseOwn }
        };
        var policy = declaration.Compile(runOverride);
        var child = SubAgentRunConfig.Resolve(policy, parent);

        policy.Clients.Chat.Should().Be(ClientFamilyInheritanceMode.UseOwn);
        policy.InheritedFields.Should().HaveFlag(SubAgentRunConfigFields.Instructions);
        child.SystemInstructions.Should().NotBeNull();
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
                    Provider = new HPD.Agent.Providers.ProviderReference { Key = "parent-speech" },
                    ModelName = "parent-model",
                    VoiceId = "parent-voice"
                }
            }
        };
        var selection = SubAgentRunConfig.Inherit();

        var child = selection.Resolve(new AgentRunConfig(), parentClients, new AgentConfig());

        child.Clients.TextToSpeech!.Provider?.Key.Should().Be("parent-speech");
        child.Clients.TextToSpeech.ModelName.Should().Be("parent-model");
        child.Clients.TextToSpeech.VoiceId.Should().Be("parent-voice");
        child.Clients.TextToSpeech.Override!.Client.Should().BeSameAs(client);
    }

    [Fact]
    public void UseOwn_NonChatFamily_DoesNotInstallParentBoundClient()
    {
        var parentClients = new AgentClientSet
        {
            TextToSpeech = new FakeTextToSpeechClient(),
            ResolvedConfigs = new Dictionary<ProviderClientFamily, ProviderClientConfig>
            {
                [ProviderClientFamily.TextToSpeech] = new TextToSpeechClientConfig
                {
                    Provider = new HPD.Agent.Providers.ProviderReference { Key = "parent-speech" },
                    ModelName = "parent-model"
                }
            }
        };
        var selection = SubAgentRunConfig.Inherit().WithClients(new AgentClientInheritance
        {
            TextToSpeech = ClientFamilyInheritanceMode.UseOwn
        });

        var child = selection.Resolve(new AgentRunConfig(), parentClients, new AgentConfig());

        child.Clients.TextToSpeech.Should().BeNull();
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
                HostedFiles = new HostedFilesClientConfig
                {
                    Provider = new ProviderReference { Key = "child-files" }
                }
            }
        };

        var child = SubAgentRunConfig.Inherit().Resolve(new AgentRunConfig(), parentClients, childDefaults);

        child.Clients.HostedFiles.Should().BeNull();
    }

    [Fact]
    public async Task ParentClientSet_BorrowedLease_DefersRunOwnedClientDisposal()
    {
        var client = new FakeTextToSpeechClient();
        var clients = new AgentClientSet { TextToSpeech = client };
        clients.SetOwnedClients(new HashSet<object>(ReferenceEqualityComparer.Instance) { client });
        var childLease = clients.AcquireBorrowedLease();

        var disposal = clients.DisposeAsync().AsTask();
        client.DisposeCount.Should().Be(0);

        await childLease.DisposeAsync();
        await disposal;
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
