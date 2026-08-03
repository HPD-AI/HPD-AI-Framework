using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.MultiAgent;
using Microsoft.Extensions.AI;
using Moq;

namespace HPD.MultiAgent.Tests;

public sealed class RemoteAgentFamilySelectionDtoTests
{
    private static readonly ProviderComposition EmptyComposition = ProviderComposition.Create([]);

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
                ProviderKey = "openai",
                ModelName = "gpt-test",
                AuthenticationKey = "tenant-a",
                Temperature = 0.25f
            },
            EmptyComposition,
            deadline);

        var json = JsonSerializer.Serialize(
            transport, MultiAgentGraphConfigJsonContext.Default.RemoteAgentFamilySelectionDto);
        var restored = JsonSerializer.Deserialize(
            json, MultiAgentGraphConfigJsonContext.Default.RemoteAgentFamilySelectionDto)!;
        var bound = restored.Bind(EmptyComposition).Should().BeOfType<ChatClientConfig>().Subject;

        bound.ProviderKey.Should().Be("openai");
        bound.ModelName.Should().Be("gpt-test");
        bound.AuthenticationKey.Should().Be("tenant-a");
        bound.Temperature.Should().Be(0.25f);
        restored.Deadline.Should().Be(deadline);
        json.Should().NotContain("apiKey", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_RejectsRawApiKey()
    {
        var action = () => RemoteAgentFamilySelectionDto.Create(
            ProviderClientFamily.Chat,
            new ChatClientConfig { ApiKey = "secret" },
            EmptyComposition);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Raw API keys cannot cross*");
    }

    [Fact]
    public void Create_RejectsRuntimeClientOverride()
    {
        var action = () => RemoteAgentFamilySelectionDto.Create(
            ProviderClientFamily.Chat,
            new ChatClientConfig
            {
                Override = ClientOverride<IChatClient>.Borrowed(new Mock<IChatClient>().Object)
            },
            EmptyComposition);

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

        var action = () => transport.Bind(EmptyComposition);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Unsupported remote agent family selection schema version*");
    }
}
