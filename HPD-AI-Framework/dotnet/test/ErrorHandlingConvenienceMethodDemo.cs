using HPD.Agent;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Demo;

/// <summary>
/// Demonstrates the new WithErrorHandling() convenience method.
/// This shows all the different ways to use it.
/// </summary>
public class ErrorHandlingConvenienceMethodDemo
{
    public static void DemoSimpleUsage()
    {
        //  Simple usage with defaults
        var agent = new AgentBuilder()
            .WithErrorHandling()  // One call, all middleware registered in correct order
            .BuildAsync();

        Console.WriteLine("✓ Simple usage: All error handling middleware registered");
        Console.WriteLine($"  Middleware count: {GetMiddlewareCount(agent)}");
    }

    public static void DemoCustomThresholds()
    {
        //  Custom thresholds
        var agent = new AgentBuilder()
            .WithErrorHandling(
                maxConsecutiveCalls: 3,      // Circuit breaker triggers after 3 identical calls
                maxConsecutiveErrors: 5,      // Terminate after 5 consecutive errors
                maxTotalErrors: 15)           // Terminate after 15 total errors
            .BuildAsync();

        Console.WriteLine("✓ Custom thresholds: Error handling configured with custom limits");
    }

    public static void DemoAdvancedConfiguration()
    {
        //  Advanced configuration with per-middleware control
        var agent = new AgentBuilder()
            .WithErrorHandling(
                configureCircuitBreaker: cb =>
                {
                    cb.MaxConsecutiveCalls = 3;
                    cb.TerminationMessageTemplate = "Loop detected for {toolName}!";
                },
                configureFunctionRetry: retry =>
                {
                    retry.MaxRetries = 5;
                    retry.RetryDelay = TimeSpan.FromSeconds(2);
                    retry.MaxRetriesByCategory = new Dictionary<ErrorCategory, int>
                    {
                        [ErrorCategory.RateLimitRetryable] = 10,  // More patient with rate limits
                        [ErrorCategory.ServerError] = 3            // Less patient with server errors
                    };
                },
                configureFunctionTimeout: TimeSpan.FromMinutes(2))
            .BuildAsync();

        Console.WriteLine("✓ Advanced configuration: Fine-grained control over each middleware");
    }

    public static void DemoStandaloneMiddleware()
    {
        //  Using individual middleware methods
        var config = new AgentConfig
        {
            ErrorHandling = new ErrorHandlingConfig
            {
                MaxRetries = 5,
                RetryDelay = TimeSpan.FromSeconds(2),
                SingleFunctionTimeout = TimeSpan.FromMinutes(2)
            }
        };

        var agent = new AgentBuilder(config)
            .WithFunctionRetry()      // Uses config.ErrorHandling settings
            .WithFunctionTimeout()     // Uses config.ErrorHandling.SingleFunctionTimeout
            .WithCircuitBreaker(3)
            .WithErrorTracking(5)
            .BuildAsync();

        Console.WriteLine("✓ Standalone middleware: Individual methods for granular control");
    }

    public static void DemoMiddlewareOrder()
    {
        Console.WriteLine("\n📋 Middleware Registration Order:");
        Console.WriteLine("   WithErrorHandling() registers middleware in this order:");
        Console.WriteLine("   1. CircuitBreakerMiddleware (iteration-level)");
        Console.WriteLine("   2. ErrorTrackingMiddleware (iteration-level)");
        Console.WriteLine("   3. TotalErrorThresholdMiddleware (iteration-level)");
        Console.WriteLine("   4. FunctionRetryMiddleware (function-level, outermost)");
        Console.WriteLine("   5. FunctionTimeoutMiddleware (function-level, inner)");
        Console.WriteLine("\n   This creates an onion pattern where:");
        Console.WriteLine("   - Retry wraps Timeout (so timeout applies to each retry attempt)");
        Console.WriteLine("   - Circuit breaker and error tracking run at iteration level");
    }

    public static void RunAllDemos()
    {
        Console.WriteLine("=== WithErrorHandling() Convenience Method Demo ===\n");

        DemoSimpleUsage();
        Console.WriteLine();

        DemoCustomThresholds();
        Console.WriteLine();

        DemoAdvancedConfiguration();
        Console.WriteLine();

        DemoStandaloneMiddleware();

        DemoMiddlewareOrder();

        Console.WriteLine("\n All demos completed successfully!");
    }

    private static int GetMiddlewareCount(HPD.Agent.Agent agent)
    {
        // This is just for demo purposes - in real code you'd use the builder
        return 5; // CB + ErrorTracking + TotalThreshold + Retry + Timeout
    }
}
