using HPD.Agent.Validation;

namespace HPD.Agent.Tests.Configuration;

public sealed class AgentConfigValidationLimitTests
{
    [Fact]
    public void Validate_AllowsUserDefinedOperationalLimits()
    {
        var errors = AgentConfigValidator.Validate(CreateAboveFormerLimitsConfig());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_AppliesHostLimits()
    {
        var errors = AgentConfigValidator.Validate(
            CreateAboveFormerLimitsConfig(),
            new AgentConfigValidationLimits
            {
                MaximumAgentNameLength = 100,
                MaximumAgenticIterations = 50,
                MaximumRetries = 10,
                MaximumConsecutiveCalls = 100,
                MaximumConsecutiveErrors = 100,
                MaximumTotalErrors = 1_000,
                MaximumExtensionNameLength = 128,
                MaximumAudioComponentInstanceLength = 128,
                MaximumAudioSchemaLength = 256
            });

        Assert.Contains(errors, error => error.Contains("host limit of 100", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MaxFunctionCallTurns exceeds host limit of 50", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("retry.modelRetries.MaxRetries exceeds host limit of 10", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("circuitBreaker.maxConsecutiveCalls exceeds host limit of 100", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("consecutiveErrors.maxConsecutiveErrors exceeds host limit of 100", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("totalErrors.maxTotalErrors exceeds host limit of 1000", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("externalDetector exceeds host limit of 128", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ComponentInstance exceeds host limit of 128", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Schema exceeds host limit of 256", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AllowsValuesAtHostLimits()
    {
        var errors = AgentConfigValidator.Validate(
            new AgentConfig
            {
                Name = "Agent",
                MaxAgenticIterations = 50,
                Resilience = new ResilienceConfig
                {
                    Retry = new RetryPolicySection
                    {
                        ModelRetries = new() { MaxRetries = 10 },
                        FunctionRetries = new() { MaxRetries = 10 }
                    },
                    CircuitBreaker = new() { MaxConsecutiveCalls = 100 },
                    ConsecutiveErrors = new() { MaxConsecutiveErrors = 100 },
                    TotalErrors = new() { MaxTotalErrors = 1_000 }
                }
            },
            new AgentConfigValidationLimits
            {
                MaximumAgentNameLength = 100,
                MaximumAgenticIterations = 50,
                MaximumRetries = 10,
                MaximumConsecutiveCalls = 100,
                MaximumConsecutiveErrors = 100,
                MaximumTotalErrors = 1_000
            });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_StillRejectsStructuralInvalidity()
    {
        var errors = AgentConfigValidator.Validate(new AgentConfig
        {
            Name = "Agent",
            MaxAgenticIterations = 0,
            Resilience = new ResilienceConfig
            {
                Retry = new RetryPolicySection
                {
                    ModelRetries = new() { MaxRetries = -1 }
                },
                CircuitBreaker = new() { MaxConsecutiveCalls = 0 },
                ConsecutiveErrors = new() { MaxConsecutiveErrors = 0 },
                TotalErrors = new() { MaxTotalErrors = 0 }
            }
        });

        Assert.Contains(errors, error => error.Contains("MaxFunctionCallTurns must be greater than zero", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("retry.modelRetries.MaxRetries must not be negative", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("circuitBreaker.maxConsecutiveCalls must be greater than zero", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("consecutiveErrors.maxConsecutiveErrors must be greater than zero", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("totalErrors.maxTotalErrors must be greater than zero", StringComparison.Ordinal));
    }

    private static AgentConfig CreateAboveFormerLimitsConfig() => new()
    {
        Name = new string('A', 150),
        MaxAgenticIterations = 500,
        Resilience = new ResilienceConfig
        {
            Retry = new RetryPolicySection
            {
                ModelRetries = new() { MaxRetries = 50 },
                FunctionRetries = new() { MaxRetries = 50 },
                ModelCustomRetryStrategy = new string('M', 200),
                FunctionCustomRetryStrategy = new string('F', 200)
            },
            CircuitBreaker = new() { MaxConsecutiveCalls = 500 },
            ConsecutiveErrors = new() { MaxConsecutiveErrors = 500 },
            TotalErrors = new()
            {
                MaxTotalErrors = 5_000,
                CustomErrorDetector = new string('D', 200)
            }
        },
        Privacy = new PrivacyConfig
        {
            PII = new PIIProtectionPolicy
            {
                ExternalDetector = new string('P', 200)
            }
        },
        Audio = new AudioConfig
        {
            Transport = new AudioTransportConfig
            {
                ComponentInstance = new string('C', 200),
                Endpoint = "https://example.test/audio",
                Schema = new string('S', 400),
                Version = 1
            }
        }
    };
}
