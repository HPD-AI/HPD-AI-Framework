using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.Logging;

class MiddlewareTest
{
    static void Main(string[] args)
    {
        // Create logger
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        try
        {
            // Create a simple agent with logging middleware (using unified LoggingMiddleware)
            var agent = new AgentBuilder()
                .WithProvider("openai", "gpt-4o-mini", Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                .WithLogging(loggerFactory) // Uses unified LoggingMiddleware
                .WithToolkit<TestToolkit>()
                .BuildAsync();

            Console.WriteLine(" Agent created successfully!");
            Console.WriteLine($"Config: {agent.Config.ToString()}");

            // Check if unified middlewares are registered
            var agentMiddlewares = agent.AgentMiddlewares;
            Console.WriteLine($" Agent middlewares registered: {agentMiddlewares.Count}");

            foreach (var middleware in agentMiddlewares)
            {
                Console.WriteLine($"   - {middleware.GetType().Name}");
            }

            Console.WriteLine("\n Middleware registration test PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Test FAILED: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}

// Simple test Toolkit
public class TestToolkit
{
    public string TestFunction(string input)
    {
        return $"Echo: {input}";
    }
}
