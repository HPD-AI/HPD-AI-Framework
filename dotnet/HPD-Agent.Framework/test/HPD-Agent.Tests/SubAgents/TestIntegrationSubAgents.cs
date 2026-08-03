using HPD.Agent;
using HPD.Agent;

/// <summary>
/// Test ToolHarness with sub-agents for integration testing
/// Simulates real-world usage patterns
/// </summary>
public class TestIntegrationSubAgents
{
    [SubAgent]
    public SubAgent WeatherExpert()
    {
        return SubAgent.FromConfig(
            "test/weather-expert",
            "WeatherExpert",
            "Specialized agent for weather forecasts and meteorological analysis",
            new AgentConfig
            {
                Name = "Weather Expert",
                SystemInstructions = "You are a meteorology expert. Provide weather information.",
                MaxAgenticIterations = 10,
                Clients = new AgentClientsConfig { Chat = new ProviderClientConfig {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-2.0-flash-exp:free"
                } }
            });
    }

    [SubAgent]
    public SubAgent MathExpert()
    {
        return SubAgent.FromConfig(
            "test/math-expert",
            "MathExpert",
            "Specialized agent for mathematical calculations and problem-solving",
            new AgentConfig
            {
                Name = "Math Expert",
                SystemInstructions = "You are a mathematics expert. Solve problems step-by-step.",
                MaxAgenticIterations = 15,
                Clients = new AgentClientsConfig { Chat = new ProviderClientConfig {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-2.0-flash-exp:free"
                } }
            });
    }

    [SubAgent]
    public SubAgent CodeReviewer()
    {
        return SubAgent.FromConfig(
            "test/code-reviewer",
            "CodeReviewer",
            "Specialized agent for code review and security analysis",
            new AgentConfig
            {
                Name = "Code Reviewer",
                SystemInstructions = "You are a senior software engineer. Review code for quality and security.",
                MaxAgenticIterations = 20,
                Clients = new AgentClientsConfig { Chat = new ProviderClientConfig {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-2.0-flash-exp:free"
                } }
            });
    }
}
