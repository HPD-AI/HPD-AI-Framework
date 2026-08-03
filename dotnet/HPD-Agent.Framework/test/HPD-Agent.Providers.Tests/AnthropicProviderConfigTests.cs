using System.Text.Json;
using Anthropic.Models.Messages;
using FluentAssertions;
using HPD.Agent.Providers.Anthropic;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Tests;

public class AnthropicProviderConfigTests
{
    [Fact]
    public void AnthropicProviderConfig_ShouldBeConstructionOnly()
    {
        typeof(AnthropicProviderConfig).GetProperties()
            .Should().BeEmpty();
    }

    [Fact]
    public void AnthropicChatRequestOptions_ShouldSerializeRuntimeOptions()
    {
        var options = new AnthropicChatRequestOptions
        {
            ServiceTier = AnthropicServiceTier.Auto,
            ThinkingBudgetTokens = 4096,
            ThinkingDisplay = AnthropicThinkingDisplay.Summarized,
            CacheControl = new AnthropicCacheControlConfig
            {
                SystemMessages = AnthropicCacheTtl.OneHour,
                LastUserMessage = AnthropicCacheTtl.FiveMinutes
            }
        };

        var json = JsonSerializer.Serialize(options, AnthropicJsonContext.Default.AnthropicChatRequestOptions);

        json.Should().Contain("\"serviceTier\":\"auto\"");
        json.Should().Contain("\"thinkingBudgetTokens\":4096");
        json.Should().Contain("\"thinkingDisplay\":\"summarized\"");
        json.Should().Contain("\"systemMessages\":\"1h\"");
        json.Should().Contain("\"lastUserMessage\":\"5m\"");

        var deserialized = JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicChatRequestOptions);

        deserialized.Should().NotBeNull();
        deserialized!.ServiceTier.Should().Be(AnthropicServiceTier.Auto);
        deserialized.ThinkingBudgetTokens.Should().Be(4096);
        deserialized.ThinkingDisplay.Should().Be(AnthropicThinkingDisplay.Summarized);
        deserialized.CacheControl!.SystemMessages.Should().Be(AnthropicCacheTtl.OneHour);
        deserialized.CacheControl.LastUserMessage.Should().Be(AnthropicCacheTtl.FiveMinutes);
    }

    [Fact]
    public void WithAnthropicChatRequestOptions_ShouldStoreOptionsInChatDefaults()
    {
        var builder = new AgentBuilder()
            .WithAnthropic("claude-sonnet-4-5-20250929", "key")
            .WithAnthropicChatRequestOptions(opts =>
            {
                opts.ServiceTier = AnthropicServiceTier.StandardOnly;
                opts.ThinkingBudgetTokens = 4096;
            });

        var chatConfig = builder.Config.EnsureChatClientConfig();

        chatConfig.Should().NotBeNull();
        var providerOptions = chatConfig!.ProviderOptions.Should().BeOfType<AnthropicChatRequestOptions>().Subject;
        providerOptions.ServiceTier.Should().Be(AnthropicServiceTier.StandardOnly);
        providerOptions.ThinkingBudgetTokens.Should().Be(4096L);
    }

    [Fact]
    public async Task AnthropicConfiguredChatClient_ShouldApplyRequestOptionsWithoutOverwritingCallerRawValues()
    {
        var inner = new CaptureChatClient();
        using var client = new AnthropicConfiguredChatClient(inner, "claude-test", 1024);

        var explicitRaw = new MessageCreateParams
        {
            MaxTokens = 9000,
            Model = "claude-raw",
            Messages = [],
            ServiceTier = ServiceTier.StandardOnly,
            Thinking = new ThinkingConfigParam(new ThinkingConfigEnabled(5000))
        };
        var options = new ChatOptions
        {
            ModelId = "claude-options",
            MaxOutputTokens = 2048,
            RawRepresentationFactory = _ => explicitRaw
        }.UseAnthropicChatRequestOptions(new AnthropicChatRequestOptions
        {
            ServiceTier = AnthropicServiceTier.Auto,
            ThinkingBudgetTokens = 4096,
            ThinkingDisplay = AnthropicThinkingDisplay.Omitted
        });

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            options,
            CancellationToken.None);

        var raw = inner.LastOptions!.RawRepresentationFactory!(inner)
            .Should().BeOfType<MessageCreateParams>().Subject;

        raw.ServiceTier!.Value().Should().Be(ServiceTier.StandardOnly);
        raw.Thinking.Should().BeSameAs(explicitRaw.Thinking);
        raw.MaxTokens.Should().Be(9000);
        raw.Model.Raw().Should().Be("claude-raw");
    }

    [Fact]
    public async Task AnthropicConfiguredChatClient_ShouldApplyRequestOptionsWhenCallerHasNoRawFactory()
    {
        var inner = new CaptureChatClient();
        using var client = new AnthropicConfiguredChatClient(inner, "claude-default", 1024);

        var options = new ChatOptions { ModelId = "claude-options", MaxOutputTokens = 2048 }
            .UseAnthropicChatRequestOptions(new AnthropicChatRequestOptions
            {
                ServiceTier = AnthropicServiceTier.StandardOnly,
                ThinkingBudgetTokens = 4096,
                ThinkingDisplay = AnthropicThinkingDisplay.Summarized
            });

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            options,
            CancellationToken.None);

        var raw = inner.LastOptions!.RawRepresentationFactory!(inner)
            .Should().BeOfType<MessageCreateParams>().Subject;

        raw.ServiceTier!.Value().Should().Be(ServiceTier.StandardOnly);
        raw.MaxTokens.Should().Be(5120);
        raw.Model.Raw().Should().Be("claude-options");
        var thinking = raw.Thinking!.Value.Should().BeOfType<ThinkingConfigEnabled>().Subject;
        thinking.BudgetTokens.Should().Be(4096);
        thinking.Display!.Value().Should().Be(ThinkingConfigEnabledDisplay.Summarized);
    }

    [Fact]
    public async Task AnthropicConfiguredChatClient_ShouldApplyCachePolicyFromRequestOptionsToClonedMessages()
    {
        var inner = new CaptureChatClient();
        using var client = new AnthropicConfiguredChatClient(inner, "claude-default", 1024);
        var systemContent = new TextContent("system");
        var firstUserContent = new TextContent("first");
        var lastUserContent = new TextContent("last");
        var options = new ChatOptions()
            .UseAnthropicChatRequestOptions(new AnthropicChatRequestOptions
            {
                CacheControl = new AnthropicCacheControlConfig
                {
                    SystemMessages = AnthropicCacheTtl.OneHour,
                    LastUserMessage = AnthropicCacheTtl.FiveMinutes
                }
            });

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, [systemContent]),
                new ChatMessage(ChatRole.User, [firstUserContent]),
                new ChatMessage(ChatRole.User, [lastUserContent])
            ],
            options,
            cancellationToken: CancellationToken.None);

        systemContent.AdditionalProperties.Should().BeNull();
        firstUserContent.AdditionalProperties.Should().BeNull();
        lastUserContent.AdditionalProperties.Should().BeNull();

        var clonedSystem = inner.LastMessages![0].Contents[0];
        var clonedFirstUser = inner.LastMessages[1].Contents[0];
        var clonedLastUser = inner.LastMessages[2].Contents[0];

        clonedSystem.Should().NotBeSameAs(systemContent);
        clonedLastUser.Should().NotBeSameAs(lastUserContent);
        GetTtl(clonedSystem).Should().Be(Ttl.Ttl1h);
        GetTtl(clonedFirstUser).Should().BeNull();
        GetTtl(clonedLastUser).Should().Be(Ttl.Ttl5m);
    }

    private static Ttl? GetTtl(AIContent content)
    {
        if (content.AdditionalProperties?.TryGetValue("anthropic:cache_control", out var value) != true)
            return null;

        return value.Should().BeOfType<CacheControlEphemeral>().Subject.Ttl!.Value();
    }

    private sealed class CaptureChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

        public ChatOptions? LastOptions { get; private set; }

        public void Dispose()
        {
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            LastOptions = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            LastOptions = options;
            await Task.CompletedTask;
            yield break;
        }
    }
}
