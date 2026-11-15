using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using HPD.Agent.Tests.Infrastructure;
using HPD.Agent.Validation;
using HPD.Serialization;

namespace HPD.Agent.Tests.Middleware;

public sealed class DurableResiliencePolicyTests
{
    [Fact]
    public async Task BuilderPolicies_MaterializeAndRebuildFromPortableConfig()
    {
        var config = new AgentConfig
        {
            Name = "Durable policies"
        };
        var builder = new AgentBuilder(config)
            .WithRetry(retry => retry.FunctionRetries.MaxRetries = 7)
            .WithCircuitBreaker(4)
            .WithErrorTracking(5)
            .WithTotalErrorThreshold(6)
            .WithFunctionTimeout(TimeSpan.FromSeconds(8))
            .WithErrorFormatting(includeDetailedErrors: false)
            .WithPIIProtection(pii =>
            {
                pii.ApplyToOutput = true;
                pii.AddCustomDetector(
                    "EmployeeId",
                    @"EMP-\d{6}",
                    PIIStrategy.Redact,
                    "[employee-id]");
            })
            .WithLogging(options: new LoggingMiddlewareOptions
            {
                LogMessageTurn = false,
                LogIteration = false,
                LogFunction = true,
                IncludeArguments = false,
                IncludeResults = false
            });

        Assert.Equal(7, config.Resilience!.Retry!.FunctionRetries.MaxRetries);
        Assert.Equal(4, config.Resilience.CircuitBreaker!.MaxConsecutiveCalls);
        Assert.Equal(5, config.Resilience.ConsecutiveErrors!.MaxConsecutiveErrors);
        Assert.Equal(6, config.Resilience.TotalErrors!.MaxTotalErrors);
        Assert.Equal(TimeSpan.FromSeconds(8), config.Resilience.FunctionTimeout);
        Assert.False(config.ErrorHandling!.IncludeDetailedErrorsInChat);
        Assert.True(config.Privacy!.PII!.ApplyToOutput);
        Assert.Equal("EMP-\\d{6}", Assert.Single(config.Privacy.PII.CustomDetectors).Pattern);
        Assert.Equal("[employee-id]", Assert.Single(config.Privacy.PII.CustomDetectors).Replacement);
        Assert.NotNull(config.Observability!.Logging);
        Assert.Contains(builder.Middlewares, middleware => middleware is PIIMiddleware);

        var document = HpdAgentConfigSerializer.Serialize(config, HpdConfigFormat.Json);
        var restored = HpdAgentConfigSerializer.Deserialize(document) ??
            throw new InvalidOperationException("Agent configuration did not round-trip.");
        await using var agent = await new AgentBuilder(restored)
            .WithChatClient(new FakeChatClient())
            .WithEventComposition(TestEventApplication.Composition)
            .BuildAsync();

        Assert.Single(agent.Middlewares.OfType<HPD.Agent.Middleware.Function.RetryMiddleware>());
        Assert.Single(agent.Middlewares.OfType<CircuitBreakerMiddleware>());
        Assert.Single(agent.Middlewares.OfType<ErrorTrackingMiddleware>());
        Assert.Single(agent.Middlewares.OfType<TotalErrorThresholdMiddleware>());
        Assert.Single(agent.Middlewares.OfType<HPD.Agent.Middleware.Function.FunctionTimeoutMiddleware>());
        Assert.Single(agent.Middlewares.OfType<HPD.Agent.Middleware.Function.ErrorFormattingMiddleware>());
        var pii = Assert.Single(agent.Middlewares.OfType<PIIMiddleware>());
        Assert.Equal("[employee-id]", Assert.Single(pii.CustomDetectors).Replacement);
        Assert.Single(agent.Middlewares.OfType<LoggingMiddleware>());
    }

    [Fact]
    public void InvalidDeclarativePiiPattern_FailsValidationAtItsPortablePath()
    {
        var config = new AgentConfig
        {
            Name = "Invalid PII",
            Privacy = new PrivacyConfig
            {
                PII = new PIIProtectionPolicy
                {
                    CustomDetectors =
                    [
                        new DeclarativePIIDetector
                        {
                            Name = "Broken",
                            Pattern = "[",
                            Strategy = PIIStrategy.Redact
                        }
                    ]
                }
            }
        };

        var errors = AgentConfigValidator.Validate(config);

        Assert.Contains(errors, error =>
            error.Contains("privacy.pii.customDetectors[0].pattern", StringComparison.Ordinal));
    }
}
