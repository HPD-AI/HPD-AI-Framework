using HPD.Agent;
using HPD.Agent;

/// <summary>
/// Test Toolkit with various sub-agent patterns for validation
/// Mirrors Microsoft's AsAIFunction() but with HPD-Agent compile-time validation
/// </summary>
public class TestSubAgentTools
{
    [SubAgent]
    public SubAgent ValidSubAgent()
    {
        return SubAgentFactory.Create(
            "ValidSubAgent",
            "A valid test sub-agent",
            new AgentConfig
            {
                Name = "Valid Sub-Agent",
                SystemInstructions = "Test instructions",
                MaxAgenticIterations = 10,
                Provider = new ProviderConfig
                {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-2.0-flash-exp:free"
                }
            });
    }

    [SubAgent]
    public SubAgent CategorizedSubAgent()
    {
        return SubAgentFactory.Create(
            "CategorizedSubAgent",
            "Sub-agent with category",
            new AgentConfig
            {
                Name = "Categorized",
                SystemInstructions = "Test",
                Provider = new ProviderConfig { ProviderKey = "openrouter", ModelName = "test" }
            });
    }

    [SubAgent]
    public SubAgent PrioritizedSubAgent()
    {
        return SubAgentFactory.Create(
            "PrioritizedSubAgent",
            "Sub-agent with priority",
            new AgentConfig
            {
                Name = "Prioritized",
                SystemInstructions = "Test",
                Provider = new ProviderConfig { ProviderKey = "openrouter", ModelName = "test" }
            });
    }

    [SubAgent]
    public SubAgent StatelessSubAgent()
    {
        return SubAgentFactory.Create(
            "StatelessSubAgent",
            "Stateless sub-agent (default)",
            new AgentConfig
            {
                Name = "Stateless",
                SystemInstructions = "Test",
                Provider = new ProviderConfig { ProviderKey = "openrouter", ModelName = "test" }
            });
    }

    [SubAgent]
    public SubAgent StatefulSubAgent()
    {
        return SubAgentFactory.CreateStateful(
            "StatefulSubAgent",
            "Stateful sub-agent with shared thread",
            new AgentConfig
            {
                Name = "Stateful",
                SystemInstructions = "Test",
                Provider = new ProviderConfig { ProviderKey = "openrouter", ModelName = "test" }
            });
    }

    [SubAgent]
    public SubAgent PerSessionSubAgent()
    {
        return SubAgentFactory.CreatePerSession(
            "PerSessionSubAgent",
            "Per-session sub-agent",
            new AgentConfig
            {
                Name = "PerSession",
                SystemInstructions = "Test",
                Provider = new ProviderConfig { ProviderKey = "openrouter", ModelName = "test" }
            });
    }

    [SubAgent]
    public SubAgent SubAgentWithProvider()
    {
        return SubAgentFactory.Create(
            "SubAgentWithProvider",
            "Sub-agent with specific provider",
            new AgentConfig
            {
                Name = "With Provider",
                SystemInstructions = "Test",
                Provider = new ProviderConfig
                {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-2.0-flash-exp:free"
                }
            });
    }

    [SubAgent]
    public SubAgent SubAgentWithInstructions()
    {
        return SubAgentFactory.Create(
            "SubAgentWithInstructions",
            "Sub-agent with system instructions",
            new AgentConfig
            {
                Name = "With Instructions",
                SystemInstructions = "You are a test agent. Follow these rules:\n1. Be helpful\n2. Be concise",
                Provider = new ProviderConfig { ProviderKey = "openrouter", ModelName = "test" }
            });
    }

    [SubAgent]
    public SubAgent SubAgentWithIterationLimit()
    {
        return SubAgentFactory.Create(
            "SubAgentWithIterationLimit",
            "Sub-agent with custom iteration limit",
            new AgentConfig
            {
                Name = "With Iterations",
                SystemInstructions = "Test",
                MaxAgenticIterations = 15,
                Provider = new ProviderConfig { ProviderKey = "openrouter", ModelName = "test" }
            });
    }

    [SubAgent]
    public SubAgent ComplexSubAgent()
    {
        return SubAgentFactory.Create(
            "ComplexSubAgent",
            "Sub-agent with full configuration",
            new AgentConfig
            {
                Name = "Complex Sub-Agent",
                SystemInstructions = "You are a complex test agent with multiple configurations.",
                MaxAgenticIterations = 20,
                Provider = new ProviderConfig
                {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-2.0-flash-exp:free"
                }
            });
    }

    [SubAgent]
    public SubAgent StatefulSubAgentWithBranch()
    {
        var subAgent = SubAgentFactory.CreateStateful(
            "StatefulSubAgentWithBranch",
            "Stateful sub-agent pinned to a specific branch",
            new AgentConfig
            {
                Name = "StatefulWithBranch",
                SystemInstructions = "Test",
                Provider = new ProviderConfig { ProviderKey = "openrouter", ModelName = "test" }
            });
        subAgent.SharedBranchId = "review-thread";
        return subAgent;
    }

    [SubAgent]
    public SubAgent SubAgentWithToolss()
    {
        return SubAgentFactory.Create(
            "SubAgentWithToolss",
            "Sub-agent with Toolkits registered",
            new AgentConfig
            {
                Name = "With Toolkits",
                SystemInstructions = "Test agent with Toolkit access",
                Provider = new ProviderConfig { ProviderKey = "openrouter", ModelName = "test" }
            },
            typeof(HPD.Agent.Toolkit.FileSystem.FileSystemTools));
    }
}
