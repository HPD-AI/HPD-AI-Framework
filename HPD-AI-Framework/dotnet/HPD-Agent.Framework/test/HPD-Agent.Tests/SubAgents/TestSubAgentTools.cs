using HPD.Agent;
using HPD.Agent;

/// <summary>
/// Test Harness with various sub-agent patterns for validation
/// Mirrors Microsoft's AsAIFunction() but with HPD-Agent compile-time validation
/// </summary>
public class TestSubAgentTools
{
    [SubAgent]
    public SubAgent ValidSubAgent()
    {
        return SubAgent.FromConfig(
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
    public SubAgent DefaultBranchNativeSubAgent()
    {
        return SubAgent.FromConfig(
            "DefaultBranchNativeSubAgent",
            "Default branch-native sub-agent",
            new AgentConfig
            {
                Name = "DefaultBranchNative",
                SystemInstructions = "Test",
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "openrouter", ModelName = "test" } }
            });
    }

    [SubAgent]
    public SubAgent SharedSessionSubAgent()
    {
        return SubAgent.FromConfig(
            "SharedSessionSubAgent",
            "Sub-agent with a shared session",
            new AgentConfig
            {
                Name = "SharedSession",
                SystemInstructions = "Test",
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "openrouter", ModelName = "test" } }
            },
            SubAgentExecutionPolicies.SharedSessionFreshBranch("shared-session-subagent"));
    }

    [SubAgent]
    public SubAgent ParentBranchSubAgent()
    {
        return SubAgent.FromConfig(
            "ParentBranchSubAgent",
            "Sub-agent that writes directly into the parent branch",
            new AgentConfig
            {
                Name = "ParentBranch",
                SystemInstructions = "Test",
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "openrouter", ModelName = "test" } }
            },
            SubAgentExecutionPolicies.ParentBranch());
    }

    [SubAgent]
    public SubAgent SubAgentWithProvider()
    {
        return SubAgent.FromConfig(
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
    public SubAgent SharedSessionExistingBranchSubAgent()
    {
        return SubAgent.FromConfig(
            "SharedSessionExistingBranchSubAgent",
            "Sub-agent pinned to a shared session branch",
            new AgentConfig
            {
                Name = "SharedSessionExistingBranch",
                SystemInstructions = "Test",
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "openrouter", ModelName = "test" } }
            },
            SubAgentExecutionPolicies.SharedSessionExistingBranch("shared-session-with-branch", "review-thread"));
    }

    [SubAgent]
    public SubAgent SubAgentWithToolss()
    {
        return SubAgent.FromConfig(
            "SubAgentWithToolss",
            "Sub-agent with Harneses registered",
            new AgentConfig
            {
                Name = "With Harneses",
                SystemInstructions = "Test agent with Harness access",
                Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "openrouter", ModelName = "test" } }
            },
            null,
            typeof(HPD.Agent.Harness.FileSystem.FileSystemTools));
    }
}
