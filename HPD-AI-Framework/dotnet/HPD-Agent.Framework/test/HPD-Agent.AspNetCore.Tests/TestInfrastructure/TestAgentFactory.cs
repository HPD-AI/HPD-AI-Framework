using Microsoft.Extensions.AI;
using HPD.Agent.Providers;
using HPD.Agent;

namespace HPD.Agent.AspNetCore.Tests.TestInfrastructure;

/// <summary>
/// Factory for creating Agent instances in tests with minimal setup.
/// Uses AgentBuilder with a test-friendly provider registry.
/// </summary>
public static class TestAgentFactory
{
    /// <summary>
    /// Creates an agent with minimal configuration for testing.
    /// </summary>
    /// <param name="config">Optional agent configuration (uses defaults if not provided)</param>
    /// <param name="chatClient">Optional chat client (uses FakeChatClient if not provided)</param>
    /// <param name="circuitBreakerThreshold">Circuit breaker threshold (null = disabled, default = 5)</param>
    /// <param name="tools">Optional tools to register</param>
    /// <returns>Configured Agent instance ready for testing</returns>
    internal static Agent Create(
        AgentConfig? config = null,
        IChatClient? chatClient = null,
        int? circuitBreakerThreshold = 5,
        params AIFunction[] tools)
    {
        // Use defaults if not provided
        config ??= CreateDefaultConfig();
        chatClient ??= new FakeChatClient();

        // Create builder with test provider registry that knows about our chat client
        var builder = new AgentBuilder(config, new TestProviderRegistry(chatClient));

        // Add tools to config if provided
        if (tools.Length > 0)
        {
            config.Provider ??= new ProviderConfig();
            config.Provider.DefaultChatOptions ??= new Microsoft.Extensions.AI.ChatOptions();
            config.Provider.DefaultChatOptions.Tools = tools.Cast<Microsoft.Extensions.AI.AITool>().ToList();
        }

        // Register standard iteration middlewares for loop protection
        // These are essential for preventing infinite loops in tests
        if (circuitBreakerThreshold.HasValue && circuitBreakerThreshold.Value > 0)
        {
            builder.WithCircuitBreaker(circuitBreakerThreshold.Value);
        }
        builder.WithErrorTracking(maxConsecutiveErrors: 3);

        // Build and return core agent (not protocol-wrapped)
        // Use the internal BuildCoreAsync method to get the core agent directly
        return builder.BuildAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Creates a default test configuration.
    /// </summary>
    private static AgentConfig CreateDefaultConfig()
    {
        return new AgentConfig
        {
            Name = "TestAgent",
            MaxAgenticIterations = 50,
            SystemInstructions = "You are a helpful test agent.",
            Provider = new ProviderConfig
            {
                ProviderKey = "test",  // Required by validation
                ModelName = "test-model"
            },
            AgenticLoop = new AgenticLoopConfig
            {
                MaxTurnDuration = TimeSpan.FromMinutes(1)
            },
            ErrorHandling = new ErrorHandlingConfig
            {
                MaxRetries = 3,
                NormalizeErrors = true
            }
        };
    }
}

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
