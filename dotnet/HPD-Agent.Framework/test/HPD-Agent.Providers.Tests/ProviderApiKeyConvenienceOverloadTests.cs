using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Tests;

public sealed class ProviderApiKeyConvenienceOverloadTests
{
    [Fact]
    public void OpenAI_StringLiteralOverload_RegistersRuntimeOnlyAuthentication()
    {
        var apiKey = "openai-literal-that-must-not-enter-config";
        var builder = HPD.Agent.Providers.OpenAI.AgentBuilderExtensions.WithOpenAI(
            new AgentBuilder(), "gpt-5", apiKey);

        AssertRuntimeOnly(builder, "openai", "platform");
    }

    [Fact]
    public void OpenAICodex_AuthorsPortableOAuthAccountSelection()
    {
        var builder = HPD.Agent.Providers.OpenAI.AgentBuilderExtensions.WithOpenAICodex(
            new AgentBuilder(), "gpt-5-codex", "personal", "desktop");

        var chat = Assert.IsType<ChatClientConfig>(builder.Config.Clients.Chat);
        var provider = Assert.IsType<ProviderReference>(chat.Provider);
        Assert.Equal("openai", provider.Key);
        Assert.Equal("codex", provider.Backend);
        var authentication = Assert.IsType<OAuthProviderAuthentication>(provider.Authentication);
        Assert.Equal("personal", authentication.AccountId);
        Assert.Equal("desktop", authentication.StoreKey);
        Assert.Equal("gpt-5-codex", chat.ModelName);
    }

    [Fact]
    public async Task OpenAICodex_DisconnectedAccountReferenceBuildsWithoutAuthorization()
    {
        var builder = HPD.Agent.Providers.OpenAI.AgentBuilderExtensions.WithOpenAICodex(
            new AgentBuilder(), "gpt-5-codex", "personal");

        await using var agent = await builder.BuildAsync(CancellationToken.None);

        Assert.NotNull(agent);
    }

    [Fact]
    public void Groq_StringLiteralOverload_CannotBindAsEndpoint()
    {
        var apiKey = "groq-literal-that-must-not-be-an-endpoint";
        var builder = HPD.Agent.Providers.Groq.AgentBuilderExtensions.WithGroq(
            new AgentBuilder(), "llama-3.3-70b-versatile", apiKey);

        AssertRuntimeOnly(builder, "groq", null);
        Assert.Null(builder.Config.Clients.Chat!.Endpoint);
    }

    [Fact]
    public void AzureAI_LiteralOverload_PreservesBackendAndEndpoint()
    {
        var builder = HPD.Agent.Providers.AzureAI.AgentBuilderExtensions.WithAzureAI(
            new AgentBuilder(), "https://example.openai.azure.com", "deployment", "azure-literal");

        AssertRuntimeOnly(builder, "azure-ai", "azure");
        Assert.Equal("https://example.openai.azure.com", builder.Config.Clients.Chat!.Endpoint);
    }

    [Fact]
    public void OpenRouter_LiteralOverload_UsesCanonicalSelection()
    {
        var builder = HPD.Agent.Providers.OpenRouter.AgentBuilderExtensions.WithOpenRouter(
            new AgentBuilder(), "deepseek/deepseek-v4-pro", "openrouter-literal");

        AssertRuntimeOnly(builder, "openrouter", "platform");
    }

    [Fact]
    public void GenericProvider_LiteralOverload_RegistersRuntimeOnlyAuthentication()
    {
        var builder = new AgentBuilder()
            .WithProvider("custom-provider", "custom-model", "custom-literal");

        AssertRuntimeOnly(builder, "custom-provider", null);
    }

    [Fact]
    public void GenericProvider_AuthenticationOverload_PreservesAuthenticationSelection()
    {
        var authentication = new OAuthProviderAuthentication
        {
            AccountId = "custom-account",
            Scopes = ["custom.scope"]
        };

        var builder = new AgentBuilder()
            .WithProvider("custom-provider", "custom-model", authentication);

        var selection = Assert.IsType<ProviderReference>(builder.Config.Clients.Chat!.Provider);
        Assert.Same(authentication, selection.Authentication);
    }

    [Fact]
    public void RunConfig_GenericProvider_UsesPortableAuthenticationReference()
    {
        var runConfig = new AgentRunConfig()
            .WithProvider("custom-provider", "custom-model");

        var chat = Assert.IsType<ChatClientConfig>(runConfig.Clients.Chat);
        var selection = Assert.IsType<ProviderReference>(chat.Provider);
        Assert.Equal("custom-provider", selection.Key);
        var authentication = Assert.IsType<ApiKeyProviderAuthentication>(selection.Authentication);
        Assert.Equal("custom-provider:ApiKey", authentication.SecretKey);
    }

    [Fact]
    public void RunConfig_GenericProvider_RejectsRuntimeLiteralRegistration()
    {
        var authentication = new ExplicitApiKeyProviderAuthentication
        {
            RuntimeRegistrationName = "runtime-only"
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            new AgentRunConfig().WithProvider("custom-provider", "custom-model", authentication));

        Assert.Equal("authentication", exception.ParamName);
    }

    private static void AssertRuntimeOnly(AgentBuilder builder, string provider, string? backend)
    {
        var selection = Assert.IsType<ProviderReference>(builder.Config.Clients.Chat!.Provider);
        Assert.Equal(provider, selection.Key);
        Assert.Equal(backend, selection.Backend);
        var authentication = Assert.IsType<ExplicitApiKeyProviderAuthentication>(selection.Authentication);
        Assert.False(string.IsNullOrWhiteSpace(authentication.RuntimeRegistrationName));
    }
}
