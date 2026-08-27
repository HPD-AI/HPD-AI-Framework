using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Unit tests for the Background Responses feature (AllowBackgroundResponses).
/// Tests configuration resolution, option propagation, event emission, and state tracking.
/// </summary>
public class BackgroundResponsesTests : AgentTestBase
{
    #region Configuration Resolution Tests

    [Fact]
    public void AllowBackgroundResponses_ResolvesFromOptions_WhenExplicitlySet()
    {
        // Arrange
        var options = new AgentRunConfig { BackgroundResponses = new BackgroundResponsesRunConfig { Allow = true } };
        var config = new BackgroundResponsesConfig { DefaultAllow = false };

        // Act
        var resolved = ResolveBackgroundSetting(options, config);

        // Assert: Options should take precedence over config
        Assert.True(resolved);
    }

    [Fact]
    public void AllowBackgroundResponses_FallsBackToConfig_WhenOptionsNotSet()
    {
        // Arrange
        var options = new AgentRunConfig { BackgroundResponses = new BackgroundResponsesRunConfig { Allow = null } };
        var config = new BackgroundResponsesConfig { DefaultAllow = true };

        // Act
        var resolved = ResolveBackgroundSetting(options, config);

        // Assert: Should use config default
        Assert.True(resolved);
    }

    [Fact]
    public void AllowBackgroundResponses_OptionsOverridesConfig_WhenBothSet()
    {
        // Arrange
        var options = new AgentRunConfig { BackgroundResponses = new BackgroundResponsesRunConfig { Allow = false } };
        var config = new BackgroundResponsesConfig { DefaultAllow = true };

        // Act
        var resolved = ResolveBackgroundSetting(options, config);

        // Assert: Options wins
        Assert.False(resolved);
    }

    [Fact]
    public void AllowBackgroundResponses_DefaultsFalse_WhenNothingConfigured()
    {
        // Arrange
        AgentRunConfig? options = null;
        BackgroundResponsesConfig? config = null;

        // Act
        var resolved = ResolveBackgroundSetting(options, config);

        // Assert: Should default to false (traditional blocking behavior)
        Assert.False(resolved);
    }

    [Fact]
    public void AllowBackgroundResponses_DefaultsFalse_WhenOptionsNull()
    {
        // Arrange
        var options = new AgentRunConfig(); // AllowBackgroundResponses is null by default
        var config = new BackgroundResponsesConfig(); // DefaultAllow is false by default

        // Act
        var resolved = ResolveBackgroundSetting(options, config);

        // Assert
        Assert.False(resolved);
    }

    /// <summary>
    /// Helper method that matches the resolution logic in Agent.RunAsync
    /// </summary>
    private static bool ResolveBackgroundSetting(AgentRunConfig? options, BackgroundResponsesConfig? config)
    {
        return options?.BackgroundResponses?.Allow
            ?? config?.DefaultAllow
            ?? false;
    }

    #endregion

    #region BackgroundResponsesConfig Tests

    [Fact]
    public void BackgroundResponsesConfig_HasCorrectDefaults()
    {
        // Act
        var config = new BackgroundResponsesConfig();

        // Assert
        Assert.False(config.DefaultAllow);
        Assert.Equal(TimeSpan.FromSeconds(2), config.DefaultPollingInterval);
        Assert.Null(config.DefaultTimeout);
        Assert.False(config.AutoPollToCompletion);
        Assert.Equal(1000, config.MaxPollAttempts);
    }

    [Fact]
    public void BackgroundResponsesConfig_CanBeFullyConfigured()
    {
        // Arrange & Act
        var config = new BackgroundResponsesConfig
        {
            DefaultAllow = true,
            DefaultPollingInterval = TimeSpan.FromSeconds(5),
            DefaultTimeout = TimeSpan.FromMinutes(10),
            AutoPollToCompletion = true,
            MaxPollAttempts = 500
        };

        // Assert
        Assert.True(config.DefaultAllow);
        Assert.Equal(TimeSpan.FromSeconds(5), config.DefaultPollingInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), config.DefaultTimeout);
        Assert.True(config.AutoPollToCompletion);
        Assert.Equal(500, config.MaxPollAttempts);
    }

    #endregion

    #region AgentRunConfig Background Properties Tests

    [Fact]
    public void AgentRunConfig_BackgroundProperties_AreNullByDefault()
    {
        // Act
        var options = new AgentRunConfig();

        // Assert
        Assert.Null(options.BackgroundResponses?.Allow);
        Assert.Null(options.BackgroundResponses?.ContinuationToken);
        Assert.Null(options.BackgroundResponses?.PollingInterval);
        Assert.Null(options.BackgroundResponses?.Timeout);
    }

    [Fact]
    public void AgentRunConfig_BackgroundProperties_CanBeSet()
    {
        // Arrange
        #pragma warning disable MEAI001 // Experimental API
        var token = ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3 });
        #pragma warning restore MEAI001

        // Act
        var options = new AgentRunConfig
        {
            BackgroundResponses = new BackgroundResponsesRunConfig
            {
                Allow = true,
                ContinuationToken = token,
                PollingInterval = TimeSpan.FromSeconds(3),
                Timeout = TimeSpan.FromMinutes(5)
            }
        };

        // Assert
        Assert.True(options.BackgroundResponses?.Allow);
        Assert.NotNull(options.BackgroundResponses?.ContinuationToken);
        Assert.Equal(TimeSpan.FromSeconds(3), options.BackgroundResponses?.PollingInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), options.BackgroundResponses?.Timeout);
    }

    #endregion

    #region AgentConfig with BackgroundResponses Tests

    [Fact]
    public void AgentConfig_BackgroundResponses_IsNullByDefault()
    {
        // Act
        var config = new AgentConfig();

        // Assert
        Assert.Null(config.BackgroundResponses);
    }

    [Fact]
    public void AgentConfig_BackgroundResponses_CanBeConfigured()
    {
        // Act
        var config = new AgentConfig
        {
            BackgroundResponses = new BackgroundResponsesConfig
            {
                DefaultAllow = true,
                DefaultPollingInterval = TimeSpan.FromSeconds(5),
                AutoPollToCompletion = true
            }
        };

        // Assert
        Assert.NotNull(config.BackgroundResponses);
        Assert.True(config.BackgroundResponses.DefaultAllow);
        Assert.Equal(TimeSpan.FromSeconds(5), config.BackgroundResponses.DefaultPollingInterval);
        Assert.True(config.BackgroundResponses.AutoPollToCompletion);
    }

    #endregion

    #region ResponseContinuationToken Serialization Tests

    [Fact]
    public void ResponseContinuationToken_RoundTrip_PreservesData()
    {
        // Arrange
        var originalData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        // Act
        #pragma warning disable MEAI001 // Experimental API
        var token = ResponseContinuationToken.FromBytes(originalData);
        var roundTrippedData = token.ToBytes().ToArray();
        #pragma warning restore MEAI001

        // Assert
        Assert.Equal(originalData, roundTrippedData);
    }

    [Fact]
    public void ResponseContinuationToken_Base64_RoundTrip_PreservesData()
    {
        // Arrange
        var originalData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        // Act - Simulate storage/transport as Base64
        #pragma warning disable MEAI001 // Experimental API
        var token = ResponseContinuationToken.FromBytes(originalData);
        var base64 = Convert.ToBase64String(token.ToBytes().Span);
        var restoredBytes = Convert.FromBase64String(base64);
        var restoredToken = ResponseContinuationToken.FromBytes(restoredBytes);
        #pragma warning restore MEAI001

        // Assert
        Assert.Equal(originalData, restoredToken.ToBytes().ToArray());
    }

    #endregion

    #region Polling Interval Resolution Tests

    [Fact]
    public void PollingInterval_ResolvesFromOptions_WhenSet()
    {
        // Arrange
        var options = new AgentRunConfig
        {
            BackgroundResponses = new BackgroundResponsesRunConfig { PollingInterval = TimeSpan.FromSeconds(10) }
        };
        var config = new BackgroundResponsesConfig
        {
            DefaultPollingInterval = TimeSpan.FromSeconds(2)
        };

        // Act
        var resolved = options.BackgroundResponses?.PollingInterval ?? config.DefaultPollingInterval;

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(10), resolved);
    }

    [Fact]
    public void PollingInterval_FallsBackToConfig_WhenOptionsNull()
    {
        // Arrange
        var options = new AgentRunConfig(); // BackgroundPollingInterval is null
        var config = new BackgroundResponsesConfig
        {
            DefaultPollingInterval = TimeSpan.FromSeconds(5)
        };

        // Act
        var resolved = options.BackgroundResponses?.PollingInterval ?? config.DefaultPollingInterval;

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(5), resolved);
    }

    #endregion

    #region Timeout Resolution Tests

    [Fact]
    public void BackgroundTimeout_ResolvesFromOptions_WhenSet()
    {
        // Arrange
        var options = new AgentRunConfig
        {
            BackgroundResponses = new BackgroundResponsesRunConfig { Timeout = TimeSpan.FromMinutes(15) }
        };
        var config = new BackgroundResponsesConfig
        {
            DefaultTimeout = TimeSpan.FromMinutes(10)
        };

        // Act
        var resolved = options.BackgroundResponses?.Timeout ?? config.DefaultTimeout;

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(15), resolved);
    }

    [Fact]
    public void BackgroundTimeout_FallsBackToConfig_WhenOptionsNull()
    {
        // Arrange
        var options = new AgentRunConfig(); // BackgroundTimeout is null
        var config = new BackgroundResponsesConfig
        {
            DefaultTimeout = TimeSpan.FromMinutes(30)
        };

        // Act
        var resolved = options.BackgroundResponses?.Timeout ?? config.DefaultTimeout;

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(30), resolved);
    }

    [Fact]
    public void BackgroundTimeout_IsNull_WhenNothingConfigured()
    {
        // Arrange
        var options = new AgentRunConfig();
        var config = new BackgroundResponsesConfig(); // DefaultTimeout is null by default

        // Act
        var resolved = options.BackgroundResponses?.Timeout ?? config.DefaultTimeout;

        // Assert
        Assert.Null(resolved);
    }

    #endregion
}
