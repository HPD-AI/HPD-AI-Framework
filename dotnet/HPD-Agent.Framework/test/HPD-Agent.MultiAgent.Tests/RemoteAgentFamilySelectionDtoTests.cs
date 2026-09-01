using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.MultiAgent;
using Microsoft.Extensions.AI;
using Moq;

namespace HPD.MultiAgent.Tests;

public sealed class RemoteAgentFamilySelectionDtoTests
{
    private static readonly ProviderComposition Composition = ProviderComposition.Create([
        new ProviderManifestFragment([new TestDescriptor()], [], [], [])
    ]);

    [Fact]
    public void CreateAndBind_RoundTripsSafeSelectionAndAbsoluteDeadline()
    {
        var deadline = new AgentInvocationDeadline
        {
            ExpiresAt = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero)
        };
        var transport = RemoteAgentFamilySelectionDto.Create(
            ProviderClientFamily.Chat,
            new ChatClientConfig
            {
                Provider = new ProviderReference
                {
                    Key = "openai",
                    Backend = "platform",
                    Authentication = new ApiKeyProviderAuthentication { SecretKey = "openai:ApiKey" }
                },
                ModelName = "gpt-test",
                Temperature = 0.25f
            },
            Composition,
            deadline);

        var json = JsonSerializer.Serialize(
            transport, MultiAgentGraphConfigJsonContext.Default.RemoteAgentFamilySelectionDto);
        var restored = JsonSerializer.Deserialize(
            json, MultiAgentGraphConfigJsonContext.Default.RemoteAgentFamilySelectionDto)!;
        var bound = restored.Bind(Composition).Should().BeOfType<ChatClientConfig>().Subject;

        bound.Provider!.Key.Should().Be("openai");
        bound.Provider.Backend.Should().Be("platform");
        bound.ModelName.Should().Be("gpt-test");
        bound.Provider.Authentication.Should().BeOfType<ApiKeyProviderAuthentication>()
            .Which.SecretKey.Should().Be("openai:ApiKey");
        bound.Temperature.Should().Be(0.25f);
        restored.Deadline.Should().Be(deadline);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_RejectsProcessLocalLiteralSecretRegistration()
    {
        var action = () => RemoteAgentFamilySelectionDto.Create(
            ProviderClientFamily.Chat,
            new ChatClientConfig
            {
                Provider = new ProviderReference
                {
                    Key = "openai",
                    Backend = "platform",
                    Authentication = new ExplicitApiKeyProviderAuthentication
                        { RuntimeRegistrationName = "runtime-secret" }
                }
            },
            Composition);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Process-local literal provider secrets cannot cross*");
    }

    [Fact]
    public void Create_RejectsRuntimeClientOverride()
    {
        var action = () => RemoteAgentFamilySelectionDto.Create(
            ProviderClientFamily.Chat,
            new ChatClientConfig
            {
                Override = ClientOverride<IChatClient>.Borrow(new Mock<IChatClient>().Object)
            },
            Composition);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Runtime client overrides cannot cross*");
    }

    [Fact]
    public void Bind_RejectsUnknownSchemaVersion()
    {
        var transport = new RemoteAgentFamilySelectionDto
        {
            SchemaVersion = 99,
            Family = ProviderClientFamily.Chat,
            Selection = []
        };

        var action = () => transport.Bind(Composition);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Unsupported remote agent family selection schema version*");
    }

    private sealed class TestDescriptor : IProviderDescriptor
    {
        public string ProviderKey => "openai";
        public string DisplayName => "OpenAI";
        public Uri? DocumentationUri => null;
        public IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; } =
            new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new() { Family = ProviderClientFamily.Chat }
            };
        public IReadOnlyDictionary<string, ProviderBackendDescriptor> Backends { get; } =
            new Dictionary<string, ProviderBackendDescriptor>
            {
                ["platform"] = new()
                {
                    BackendKey = "platform",
                    IsDefault = true,
                    Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
                    {
                        [ProviderClientFamily.Chat] = new() { Family = ProviderClientFamily.Chat }
                    },
                    Authentication =
                    [
                        new ProviderAuthenticationDescriptor
                        {
                            Kind = ProviderAuthenticationKind.ApiKey,
                            IsDefault = true,
                            DefaultSecretKey = "openai:ApiKey",
                            SupportedFamilies = new HashSet<ProviderClientFamily> { ProviderClientFamily.Chat }
                        }
                    ]
                }
            };
        public IReadOnlyList<string> Aliases => [];
    }
}
