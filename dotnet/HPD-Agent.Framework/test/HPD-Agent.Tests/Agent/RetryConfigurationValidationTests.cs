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
            ErrorHandling = new ErrorHandlingConfig
            {
                ModelRetries = new() { MaxRetries = 11 },
                FunctionRetries = new()
                {
                    RetryDelay = TimeSpan.FromMilliseconds(-1),
                    Exclude = new()
                    {
                        Functions = ["", "Duplicate", "Duplicate"]
                    }
                }
            }
        };

        var errors = AgentConfigValidator.Validate(config);

        Assert.Contains(errors, error =>
            error.Contains("errorHandling.modelRetries.MaxRetries", StringComparison.Ordinal));
        Assert.Contains(errors, error =>
            error.Contains("errorHandling.functionRetries.RetryDelay", StringComparison.Ordinal));
        Assert.Contains(errors, error =>
            error.Contains("errorHandling.functionRetries.exclude.functions", StringComparison.Ordinal) &&
            error.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsEmptyAndDuplicateActionSelectors()
    {
        var config = new AgentConfig
        {
            Name = "Retry Validation",
            ErrorHandling = new ErrorHandlingConfig
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
}
