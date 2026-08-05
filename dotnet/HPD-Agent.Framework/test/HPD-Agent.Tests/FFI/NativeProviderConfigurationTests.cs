using HPD.Agent.FFI;
using HPD.Agent.Providers.Anthropic;

namespace HPD.Agent.Tests.FFI;

public sealed class NativeProviderConfigurationTests
{
    [Fact]
    public void DeserializeAgentConfig_BindsTypedPayloadsThroughFfiComposition()
    {
        const string json = """
            {
              "clients": {
                "chat": {
                  "providerKey": "anthropic",
                  "modelName": "claude-test",
                  "providerConfig": {},
                  "providerOptions": { "thinkingBudgetTokens": 2048 }
                }
              }
            }
            """;

        var config = NativeExports.DeserializeAgentConfig(json);

        Assert.IsType<AnthropicProviderConfig>(config.Clients.Chat!.ProviderConfig);
        Assert.Equal(2048, Assert.IsType<AnthropicChatRequestOptions>(
            config.Clients.Chat.ProviderOptions).ThinkingBudgetTokens);
    }
}
