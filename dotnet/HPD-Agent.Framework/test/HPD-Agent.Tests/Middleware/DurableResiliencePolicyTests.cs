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

    [Fact]
    public async Task NamedExecutablePolicies_ResolveOnlyFromHostRegistry()
    {
        var config = new AgentConfig
        {
            Name = "Named executable policies",
            Resilience = new ResilienceConfig
            {
                Retry = new RetryPolicySection
                {
                    FunctionCustomRetryStrategy = "production-function-retry",
                    ModelCustomRetryStrategy = "production-model-retry"
                },
                TotalErrors = new TotalErrorPolicy
                {
                    CustomErrorDetector = "application-error-detector"
                }
            },
            Privacy = new PrivacyConfig
            {
                PII = new PIIProtectionPolicy
                {
                    ExternalDetector = "company-pii"
                }
            }
        };
        var restored = HpdAgentConfigSerializer.Deserialize(
            HpdAgentConfigSerializer.Serialize(config))!;
        var functionRetryCalls = 0;
        var modelRetryCalls = 0;
        var piiCalls = 0;

        await using var agent = await new AgentBuilder(restored)
            .WithChatClient(new FakeChatClient())
            .WithEventComposition(TestEventApplication.Composition)
            .WithFunctionRetryStrategy(
                "production-function-retry",
                (_, _, _) =>
                {
                    functionRetryCalls++;
                    return Task.FromResult<TimeSpan?>(TimeSpan.FromSeconds(2));
                })
            .WithModelRetryStrategy(
                "production-model-retry",
                (_, _, _) =>
                {
                    modelRetryCalls++;
                    return Task.FromResult<TimeSpan?>(TimeSpan.FromSeconds(3));
                })
            .WithErrorDetector(
                "application-error-detector",
                static result => result.Exception is not null)
            .WithExternalPIIDetector(
                "company-pii",
                (_, _) =>
                {
                    piiCalls++;
                    return Task.FromResult<IEnumerable<PIIMatch>>([]);
                })
            .BuildAsync();

        var retry = Assert.Single(agent.Middlewares.OfType<HPD.Agent.Middleware.Function.RetryMiddleware>());
        Assert.Equal(
            TimeSpan.FromSeconds(2),
            await retry.FunctionCustomRetryStrategy!(
                new InvalidOperationException(),
                1,
                CancellationToken.None));
        Assert.Equal(
            TimeSpan.FromSeconds(3),
            await retry.ModelCustomRetryStrategy!(
                new InvalidOperationException(),
                1,
                CancellationToken.None));
        Assert.Equal(1, functionRetryCalls);
        Assert.Equal(1, modelRetryCalls);

        var totalErrors = Assert.Single(agent.Middlewares.OfType<TotalErrorThresholdMiddleware>());
        Assert.NotNull(totalErrors.CustomErrorDetector);

        var pii = Assert.Single(agent.Middlewares.OfType<PIIMiddleware>());
        Assert.Empty(await pii.ExternalDetector!("no matches", CancellationToken.None));
        Assert.Equal(1, piiCalls);
    }

    [Fact]
    public async Task MissingNamedExecutablePolicy_FailsAtPortablePath()
    {
        var config = new AgentConfig
        {
            Name = "Missing executable policy",
            Resilience = new ResilienceConfig
            {
                TotalErrors = new TotalErrorPolicy
                {
                    CustomErrorDetector = "missing-detector"
                }
            }
        };

        var exception = await Assert.ThrowsAsync<AgentRunConfigurationException>(async () =>
            await new AgentBuilder(config)
                .WithChatClient(new FakeChatClient())
                .WithEventComposition(TestEventApplication.Composition)
                .BuildAsync());

        Assert.Equal("error_detector_not_found", exception.Code);
        Assert.Equal("resilience.totalErrors.customErrorDetector", exception.Path);
    }

    [Fact]
    public void DuplicateNamedExecutablePolicy_IsRejectedAtRegistration()
    {
        var builder = new AgentBuilder()
            .WithFunctionRetryStrategy("same-name", (_, _, _) => Task.FromResult<TimeSpan?>(null));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.WithFunctionRetryStrategy(
                "same-name",
                (_, _, _) => Task.FromResult<TimeSpan?>(null)));

        Assert.Contains("already registered", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChildBuilder_ResolvesNamedPolicyFromParentRuntimeRegistry()
    {
        var config = new AgentConfig
        {
            Name = "Child policy",
            Resilience = new ResilienceConfig
            {
                Retry = new RetryPolicySection
                {
                    FunctionCustomRetryStrategy = "parent-retry"
                }
            }
        };
        var parentRegistry = new AgentPolicyExtensionRegistry();
        parentRegistry.AddFunctionRetryStrategy(
            "parent-retry",
            (_, _, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromSeconds(4)));

        await using var agent = await new AgentBuilder(config)
            .WithServiceProvider(new SingleServiceProvider(parentRegistry))
            .WithChatClient(new FakeChatClient())
            .WithEventComposition(TestEventApplication.Composition)
            .BuildAsync();

        var retry = Assert.Single(agent.Middlewares.OfType<HPD.Agent.Middleware.Function.RetryMiddleware>());
        Assert.NotNull(retry.FunctionCustomRetryStrategy);
    }

    private sealed class SingleServiceProvider(object service) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType.IsInstanceOfType(service))
                return service;
            if (serviceType.IsGenericType &&
                serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return Array.CreateInstance(serviceType.GenericTypeArguments[0], 0);
            }
            return null;
        }
    }
}
