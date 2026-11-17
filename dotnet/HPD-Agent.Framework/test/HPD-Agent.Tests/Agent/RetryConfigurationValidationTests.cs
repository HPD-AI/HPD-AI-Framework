using HPD.Agent.Validation;

namespace HPD.Agent.Tests.Configuration;

public sealed class RetryConfigurationValidationTests
{
    [Fact]
    public void Validate_UsesScopeSpecificRetryPaths()
    {
        var config = new AgentConfig
        {
            Name = "Retry Validation",
            Resilience = new ResilienceConfig
            {
                Retry = new RetryPolicySection
                {
                    ModelRetries = new() { MaxRetries = -1 },
                    FunctionRetries = new()
                    {
                        RetryDelay = TimeSpan.FromMilliseconds(-1),
                        Exclude = new()
                        {
                            Functions = ["", "Duplicate", "Duplicate"]
                        }
                    }
                }
            }
        };

        var errors = AgentConfigValidator.Validate(config);

        Assert.Contains(errors, error =>
            error.Contains("resilience.retry.modelRetries.MaxRetries", StringComparison.Ordinal));
        Assert.Contains(errors, error =>
            error.Contains("resilience.retry.functionRetries.RetryDelay", StringComparison.Ordinal));
        Assert.Contains(errors, error =>
            error.Contains("resilience.retry.functionRetries.exclude.functions", StringComparison.Ordinal) &&
            error.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsEmptyAndDuplicateActionSelectors()
    {
        var config = new AgentConfig
        {
            Name = "Retry Validation",
            Resilience = new ResilienceConfig
            {
                Retry = new RetryPolicySection
                {
                    FunctionRetries = new()
                    {
                        Exclude = new()
                        {
                            Actions =
                            [
                                new ActionRetryExclusion(),
                                new ActionRetryExclusion
                                {
                                    Function = "Files",
                                    Action = "delete"
                                },
                                new ActionRetryExclusion
                                {
                                    Function = "Files",
                                    Action = "delete"
                                }
                            ]
                        }
                    }
                }
            }
        };

        var errors = AgentConfigValidator.Validate(config);

        Assert.Contains(errors, error =>
            error.Contains("actions[0].function", StringComparison.Ordinal));
        Assert.Contains(errors, error =>
            error.Contains("actions[0].action", StringComparison.Ordinal));
        Assert.Contains(errors, error =>
            error.Contains("actions[2]", StringComparison.Ordinal) &&
            error.Contains("duplicates", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsInvalidPortableExtensionNames()
    {
        var config = new AgentConfig
        {
            Name = "Extension Validation",
            Resilience = new ResilienceConfig
            {
                Retry = new RetryPolicySection
                {
                    ModelCustomRetryStrategy = " ",
                    FunctionCustomRetryStrategy = "retry\nstrategy"
                },
                TotalErrors = new TotalErrorPolicy
                {
                    CustomErrorDetector = "detector\nname"
                }
            },
            Privacy = new PrivacyConfig
            {
                PII = new PIIProtectionPolicy
                {
                    ExternalDetector = ""
                }
            }
        };

        var errors = AgentConfigValidator.Validate(config);

        Assert.Contains(errors, error =>
            error.Contains("resilience.retry.modelCustomRetryStrategy", StringComparison.Ordinal));
        Assert.Contains(errors, error =>
            error.Contains("resilience.retry.functionCustomRetryStrategy", StringComparison.Ordinal) &&
            error.Contains("control characters", StringComparison.Ordinal));
        Assert.Contains(errors, error =>
            error.Contains("resilience.totalErrors.customErrorDetector", StringComparison.Ordinal) &&
            error.Contains("control characters", StringComparison.Ordinal));
        Assert.Contains(errors, error =>
            error.Contains("privacy.pii.externalDetector", StringComparison.Ordinal));
    }
}
