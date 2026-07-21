using HPD.Agent;
using HPD.Agent;

/// <summary>
/// Test ToolHarness with various sub-agent patterns for validation
/// Mirrors Microsoft's AsAIFunction() but with HPD-Agent compile-time validation
/// </summary>
public class TestSubAgentTools
{
    [SubAgent]
    public SubAgent ValidSubAgent()
    {
        return SubAgent.FromConfig(
            "test/valid",
            "ValidSubAgent",
            "A valid test sub-agent",
            new AgentConfig
            {
                Name = "Valid Sub-Agent",
                SystemInstructions = "Test instructions",
                MaxAgenticIterations = 10,
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-2.0-flash-exp:free"
                } }
            });
    }

    [SubAgent]
    public SubAgent CategorizedSubAgent()
    {
        return SubAgent.FromConfig(
            "test/categorized",
            "CategorizedSubAgent",
            "Sub-agent with category",
            new AgentConfig
            {
                Name = "Categorized",
                SystemInstructions = "Test",
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "openrouter", ModelName = "test" } }
            });
    }

    [SubAgent]
    public SubAgent PrioritizedSubAgent()
    {
        return SubAgent.FromConfig(
            "test/prioritized",
            "PrioritizedSubAgent",
            "Sub-agent with priority",
            new AgentConfig
            {
                Name = "Prioritized",
                SystemInstructions = "Test",
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "openrouter", ModelName = "test" } }
            });
    }

    [SubAgent]
    public SubAgent DefaultThreadNativeSubAgent()
    {
        return SubAgent.FromConfig(
            "test/default-thread-native",
            "DefaultThreadNativeSubAgent",
            "Default thread-native sub-agent",
            new AgentConfig
            {
                Name = "DefaultThreadNative",
                SystemInstructions = "Test",
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "openrouter", ModelName = "test" } }
            });
    }

    [SubAgent]
    public SubAgent FreshThreadSubAgent()
    {
        return SubAgent.FromConfig(
            "test/fresh-thread",
            "FreshThreadSubAgent",
            "Sub-agent that writes directly into the parent thread",
            new AgentConfig
            {
                Name = "ParentThread",
                SystemInstructions = "Test",
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "openrouter", ModelName = "test" } }
            },
            SubAgentContextPolicy.Fresh);
    }

    [SubAgent]
    public SubAgent SubAgentWithProvider()
    {
        return SubAgent.FromConfig(
            "test/provider",
            "SubAgentWithProvider",
            "Sub-agent with specific provider",
            new AgentConfig
            {
                Name = "With Provider",
                SystemInstructions = "Test",
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-2.0-flash-exp:free"
                } }
            });
    }

    [SubAgent]
    public SubAgent SubAgentWithInstructions()
    {
        return SubAgent.FromConfig(
            "test/instructions",
            "SubAgentWithInstructions",
            "Sub-agent with system instructions",
            new AgentConfig
            {
                Name = "With Instructions",
                SystemInstructions = "You are a test agent. Follow these rules:\n1. Be helpful\n2. Be concise",
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "openrouter", ModelName = "test" } }
            });
    }

    [SubAgent]
    public SubAgent SubAgentWithIterationLimit()
    {
        return SubAgent.FromConfig(
            "test/iteration-limit",
            "SubAgentWithIterationLimit",
            "Sub-agent with custom iteration limit",
            new AgentConfig
            {
                Name = "With Iterations",
                SystemInstructions = "Test",
                MaxAgenticIterations = 15,
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "openrouter", ModelName = "test" } }
            });
    }

    [SubAgent]
    public SubAgent ComplexSubAgent()
    {
        return SubAgent.FromConfig(
            "test/complex",
            "ComplexSubAgent",
            "Sub-agent with full configuration",
            new AgentConfig
            {
                Name = "Complex Sub-Agent",
                SystemInstructions = "You are a complex test agent with multiple configurations.",
                MaxAgenticIterations = 20,
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-2.0-flash-exp:free"
                } }
            });
    }

    [SubAgent]
    public SubAgent SubAgentWithToolss()
    {
        return SubAgent.FromConfig(
            "test/tools",
            "SubAgentWithToolss",
            "Sub-agent with ToolHarnesses registered",
            new AgentConfig
            {
                Name = "With ToolHarnesses",
                SystemInstructions = "Test agent with ToolHarness access",
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "openrouter", ModelName = "test" } }
            },
            contextPolicy: SubAgentContextPolicy.Fork,
            toolharnessTypes: [typeof(HPD.Agent.ToolHarness.FileSystem.FileSystemTools)]);
    }
}
