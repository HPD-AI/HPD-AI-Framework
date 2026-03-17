using HPD.Agent;
using HPD.Agent;

/// <summary>
/// Test Toolkit with sub-agents for integration testing
/// Simulates real-world usage patterns
/// </summary>
public class TestIntegrationSubAgents
{
    [SubAgent]
    public SubAgent WeatherExpert()
    {
        return SubAgentFactory.Create(
            "WeatherExpert",
            "Specialized agent for weather forecasts and meteorological analysis",
            new AgentConfig
            {
                Name = "Weather Expert",
                SystemInstructions = "You are a meteorology expert. Provide weather information.",
                MaxAgenticIterations = 10,
                Provider = new ProviderConfig
                {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-2.0-flash-exp:free"
                }
            });
    }

    [SubAgent]
    public SubAgent MathExpert()
    {
        return SubAgentFactory.CreateStateful(
            "MathExpert",
            "Specialized agent for mathematical calculations and problem-solving",
            new AgentConfig
            {
                Name = "Math Expert",
                SystemInstructions = "You are a mathematics expert. Solve problems step-by-step.",
                MaxAgenticIterations = 15,
                Provider = new ProviderConfig
                {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-2.0-flash-exp:free"
                }
            });
    }

    [SubAgent]
    public SubAgent CodeReviewer()
    {
        return SubAgentFactory.Create(
            "CodeReviewer",
            "Specialized agent for code review and security analysis",
            new AgentConfig
            {
                Name = "Code Reviewer",
                SystemInstructions = "You are a senior software engineer. Review code for quality and security.",
                MaxAgenticIterations = 20,
                Provider = new ProviderConfig
                {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-2.0-flash-exp:free"
                }
            });
    }
}
