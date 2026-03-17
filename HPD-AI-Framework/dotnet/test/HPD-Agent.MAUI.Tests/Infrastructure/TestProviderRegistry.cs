using Microsoft.Extensions.AI;
using HPD.Agent.Providers;

namespace HPD.Agent.Maui.Tests.Infrastructure;

/// <summary>
/// Test implementation of IProviderRegistry that returns a test provider.
/// </summary>
internal class TestProviderRegistry : IProviderRegistry
{
    private readonly IChatClient? _chatClient;

    public TestProviderRegistry(IChatClient? chatClient = null)
    {
        _chatClient = chatClient;
    }

    public IProviderFeatures? GetProvider(string providerKey)
    {
        if (providerKey == "test")
        {
            return new TestProviderFeatures(_chatClient ?? new FakeChatClient());
        }
        return null;
    }

    public IReadOnlyCollection<string> GetRegisteredProviders()
    {
        return new[] { "test" };
    }

    public void Register(IProviderFeatures provider)
    {
        // No-op for tests
    }

    public bool IsRegistered(string providerKey)
    {
        return providerKey == "test";
    }

    public void Clear()
    {
        // No-op for tests
    }
}

/// <summary>
/// Test implementation of IProviderFeatures that returns the provided chat client.
/// </summary>
internal class TestProviderFeatures : IProviderFeatures
{
    private readonly IChatClient _chatClient;

    public TestProviderFeatures(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public string ProviderKey => "test";
    public string DisplayName => "Test Provider";

    public IChatClient CreateChatClient(ProviderConfig config, IServiceProvider? services = null)
    {
        return _chatClient;
    }

    public HPD.Agent.ErrorHandling.IProviderErrorHandler CreateErrorHandler()
    {
        return new TestErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = "test",
            DisplayName = "Test Provider",
            SupportsStreaming = true,
            SupportsFunctionCalling = true
        };
    }

    public ProviderValidationResult ValidateConfiguration(ProviderConfig config)
    {
        return ProviderValidationResult.Success();
    }
}

/// <summary>
/// Test error handler that does nothing.
/// </summary>
internal class TestErrorHandler : HPD.Agent.ErrorHandling.IProviderErrorHandler
{
    public HPD.Agent.ErrorHandling.ProviderErrorDetails? ParseError(Exception exception)
    {
        return null; // No special error parsing for tests
    }

    public TimeSpan? GetRetryDelay(
        HPD.Agent.ErrorHandling.ProviderErrorDetails details,
        int attempt,
        TimeSpan initialDelay,
        double multiplier,
        TimeSpan maxDelay)
    {
        return null; // No retries in tests by default
    }

    public bool RequiresSpecialHandling(HPD.Agent.ErrorHandling.ProviderErrorDetails details)
    {
        return false; // No special handling needed for tests
    }
}
