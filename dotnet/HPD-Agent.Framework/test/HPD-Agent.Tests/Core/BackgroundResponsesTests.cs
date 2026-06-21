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
        var options = new AgentRunConfig { AllowBackgroundResponses = true };
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
        var options = new AgentRunConfig { AllowBackgroundResponses = null };
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
        var options = new AgentRunConfig { AllowBackgroundResponses = false };
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
        return options?.AllowBackgroundResponses
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
        Assert.Null(options.AllowBackgroundResponses);
        Assert.Null(options.ContinuationToken);
        Assert.Null(options.BackgroundPollingInterval);
        Assert.Null(options.BackgroundTimeout);
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
            AllowBackgroundResponses = true,
            ContinuationToken = token,
            BackgroundPollingInterval = TimeSpan.FromSeconds(3),
            BackgroundTimeout = TimeSpan.FromMinutes(5)
        };

        // Assert
        Assert.True(options.AllowBackgroundResponses);
        Assert.NotNull(options.ContinuationToken);
        Assert.Equal(TimeSpan.FromSeconds(3), options.BackgroundPollingInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), options.BackgroundTimeout);
    }

    #endregion

    #region OperationStatus Tests

    [Fact]
    public void OperationStatus_HasExpectedStaticValues()
    {
        // Assert
        Assert.Equal("Queued", OperationStatus.Queued.Value);
        Assert.Equal("InProgress", OperationStatus.InProgress.Value);
        Assert.Equal("Completed", OperationStatus.Completed.Value);
        Assert.Equal("Failed", OperationStatus.Failed.Value);
        Assert.Equal("Cancelled", OperationStatus.Cancelled.Value);
    }

    [Fact]
    public void OperationStatus_IsTerminal_ReturnsTrueForTerminalStates()
    {
        // Assert
        Assert.True(OperationStatus.Completed.IsTerminal);
        Assert.True(OperationStatus.Failed.IsTerminal);
        Assert.True(OperationStatus.Cancelled.IsTerminal);
    }

    [Fact]
    public void OperationStatus_IsTerminal_ReturnsFalseForNonTerminalStates()
    {
        // Assert
        Assert.False(OperationStatus.Queued.IsTerminal);
        Assert.False(OperationStatus.InProgress.IsTerminal);
    }

    [Fact]
    public void OperationStatus_Equality_WorksCorrectly()
    {
        // Arrange
        var status1 = OperationStatus.InProgress;
        var status2 = OperationStatus.InProgress;
        var status3 = OperationStatus.Completed;

        // Assert
        Assert.Equal(status1, status2);
        Assert.NotEqual(status1, status3);
        Assert.True(status1 == status2);
        Assert.False(status1 == status3);
    }

    [Fact]
    public void OperationStatus_CustomValue_CanBeCreated()
    {
        // Act
        var customStatus = new OperationStatus("CustomState");

        // Assert
        Assert.Equal("CustomState", customStatus.Value);
        Assert.False(customStatus.IsTerminal);
    }

    #endregion

    #region BackgroundOperationInfo Tests

    [Fact]
    public void BackgroundOperationInfo_CanBeCreated()
    {
        // Arrange & Act
        var info = new BackgroundOperationInfo
        {
            TokenData = "dGVzdF90b2tlbl9kYXRh", // Base64 encoded
            Iteration = 3,
            StartedAt = DateTimeOffset.UtcNow,
            LastKnownStatus = OperationStatus.InProgress
        };

        // Assert
        Assert.Equal("dGVzdF90b2tlbl9kYXRh", info.TokenData);
        Assert.Equal(3, info.Iteration);
        Assert.NotEqual(default, info.StartedAt);
        Assert.Equal(OperationStatus.InProgress, info.LastKnownStatus);
    }

    [Fact]
    public void BackgroundOperationInfo_TokenData_CanBeDeserializedToToken()
    {
        // Arrange
        var originalBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var base64 = Convert.ToBase64String(originalBytes);

        var info = new BackgroundOperationInfo
        {
            TokenData = base64,
            Iteration = 1,
            StartedAt = DateTimeOffset.UtcNow,
            LastKnownStatus = OperationStatus.Queued
        };

        // Act
        var decodedBytes = Convert.FromBase64String(info.TokenData);
        #pragma warning disable MEAI001 // Experimental API
        var token = ResponseContinuationToken.FromBytes(decodedBytes);
        #pragma warning restore MEAI001

        // Assert
        Assert.Equal(originalBytes, token.ToBytes().ToArray());
    }

    #endregion

    #region AgentLoopState Background Operation Tests

    [Fact]
    public void AgentLoopState_Initial_HasNoBackgroundOperation()
    {
        // Arrange
        var messages = new List<ChatMessage> { UserMessage("Test") };

        // Act
        var state = AgentLoopState.InitialSafe(messages, "run-123", "conv-456", "TestAgent");

        // Assert
        Assert.Null(state.ActiveBackgroundOperation);
    }

    [Fact]
    public void AgentLoopState_WithBackgroundOperation_SetsOperation()
    {
        // Arrange
        var messages = new List<ChatMessage> { UserMessage("Test") };
        var state = AgentLoopState.InitialSafe(messages, "run-123", "conv-456", "TestAgent");
        var operation = new BackgroundOperationInfo
        {
            TokenData = "dGVzdA==",
            Iteration = 1,
            StartedAt = DateTimeOffset.UtcNow,
            LastKnownStatus = OperationStatus.InProgress
        };

        // Act
        var newState = state.WithBackgroundOperation(operation);

        // Assert
        Assert.NotNull(newState.ActiveBackgroundOperation);
        Assert.Equal("dGVzdA==", newState.ActiveBackgroundOperation.TokenData);
        Assert.Equal(1, newState.ActiveBackgroundOperation.Iteration);
    }

    [Fact]
    public void AgentLoopState_WithBackgroundOperation_Null_ClearsOperation()
    {
        // Arrange
        var messages = new List<ChatMessage> { UserMessage("Test") };
        var state = AgentLoopState.InitialSafe(messages, "run-123", "conv-456", "TestAgent")
            .WithBackgroundOperation(new BackgroundOperationInfo
            {
                TokenData = "dGVzdA==",
                Iteration = 1,
                StartedAt = DateTimeOffset.UtcNow,
                LastKnownStatus = OperationStatus.InProgress
            });

        // Act
        var clearedState = state.WithBackgroundOperation(null);

        // Assert
        Assert.Null(clearedState.ActiveBackgroundOperation);
    }

    [Fact]
    public void AgentLoopState_BackgroundOperation_PersistedThroughIterations()
    {
        // Arrange
        var messages = new List<ChatMessage> { UserMessage("Test") };
        var operation = new BackgroundOperationInfo
        {
            TokenData = "cGVyc2lzdGVk",
            Iteration = 0,
            StartedAt = DateTimeOffset.UtcNow,
            LastKnownStatus = OperationStatus.InProgress
        };

        // Act
        var state = AgentLoopState.InitialSafe(messages, "run-123", "conv-456", "TestAgent")
            .WithBackgroundOperation(operation)
            .NextIteration()
            .NextIteration();

        // Assert: Background operation should persist through iterations
        Assert.NotNull(state.ActiveBackgroundOperation);
        Assert.Equal("cGVyc2lzdGVk", state.ActiveBackgroundOperation.TokenData);
        Assert.Equal(2, state.Iteration);
    }

    #endregion

    #region BackgroundOperationStartedEvent Tests

    [Fact]
    public void BackgroundOperationStartedEvent_CanBeCreated()
    {
        // Arrange
        #pragma warning disable MEAI001 // Experimental API
        var token = ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3 });
        #pragma warning restore MEAI001

        // Act
        var evt = new BackgroundOperationStartedEvent(
            ContinuationToken: token,
            Status: OperationStatus.InProgress,
            OperationId: "op-123");

        // Assert
        Assert.NotNull(evt.ContinuationToken);
        Assert.Equal(OperationStatus.InProgress, evt.Status);
        Assert.Equal("op-123", evt.OperationId);
    }

    [Fact]
    public void BackgroundOperationStartedEvent_OperationId_IsOptional()
    {
        // Arrange
        #pragma warning disable MEAI001 // Experimental API
        var token = ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3 });
        #pragma warning restore MEAI001

        // Act
        var evt = new BackgroundOperationStartedEvent(
            ContinuationToken: token,
            Status: OperationStatus.Queued);

        // Assert
        Assert.Null(evt.OperationId);
    }

    #endregion

    #region BackgroundOperationStatusEvent Tests

    [Fact]
    public void BackgroundOperationStatusEvent_CanBeCreated()
    {
        // Arrange
        #pragma warning disable MEAI001 // Experimental API
        var token = ResponseContinuationToken.FromBytes(new byte[] { 4, 5, 6 });
        #pragma warning restore MEAI001

        // Act
        var evt = new BackgroundOperationStatusEvent(
            ContinuationToken: token,
            Status: OperationStatus.InProgress,
            StatusMessage: "Still processing...");

        // Assert
        Assert.NotNull(evt.ContinuationToken);
        Assert.Equal(OperationStatus.InProgress, evt.Status);
        Assert.Equal("Still processing...", evt.StatusMessage);
    }

    [Fact]
    public void BackgroundOperationStatusEvent_Completed_HasNullToken()
    {
        // Act - When operation completes, token becomes null
        var evt = new BackgroundOperationStatusEvent(
            ContinuationToken: null!,
            Status: OperationStatus.Completed,
            StatusMessage: "Background operation completed successfully");

        // Assert
        Assert.Equal(OperationStatus.Completed, evt.Status);
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
            BackgroundPollingInterval = TimeSpan.FromSeconds(10)
        };
        var config = new BackgroundResponsesConfig
        {
            DefaultPollingInterval = TimeSpan.FromSeconds(2)
        };

        // Act
        var resolved = options.BackgroundPollingInterval ?? config.DefaultPollingInterval;

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
        var resolved = options.BackgroundPollingInterval ?? config.DefaultPollingInterval;

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
            BackgroundTimeout = TimeSpan.FromMinutes(15)
        };
        var config = new BackgroundResponsesConfig
        {
            DefaultTimeout = TimeSpan.FromMinutes(10)
        };

        // Act
        var resolved = options.BackgroundTimeout ?? config.DefaultTimeout;

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
        var resolved = options.BackgroundTimeout ?? config.DefaultTimeout;

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
        var resolved = options.BackgroundTimeout ?? config.DefaultTimeout;

        // Assert
        Assert.Null(resolved);
    }

    #endregion
}
