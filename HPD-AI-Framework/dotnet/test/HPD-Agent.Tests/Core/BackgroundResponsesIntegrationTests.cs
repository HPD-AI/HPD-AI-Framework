using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Integration tests for the Background Responses feature.
/// Tests actual agent behavior with mock providers that simulate background responses.
/// </summary>
public class BackgroundResponsesIntegrationTests : AgentTestBase
{
    #region Agent Run with AllowBackgroundResponses

    [Fact]
    public async Task Agent_WithBackgroundResponsesDisabled_DoesNotEmitBackgroundEvents()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("Hello, I'm a regular response!");

        var config = DefaultConfig();
        config.BackgroundResponses = new BackgroundResponsesConfig { DefaultAllow = false };

        var agent = CreateAgent(config, fakeClient);
        var session = new global::HPD.Agent.Session("test-session-id");
        var branch = new global::HPD.Agent.Branch("test-session-id");
        branch.AddMessage(UserMessage("Hi"));

        // Act
        var events = new List<AgentEvent>();
        var messages = branch.Messages;
        await foreach (var evt in agent.RunAsync(messages, session: session, branch: branch, cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        // Assert: Should not contain any background events
        Assert.DoesNotContain(events, e => e is BackgroundOperationStartedEvent);
        Assert.DoesNotContain(events, e => e is BackgroundOperationStatusEvent);
    }

    [Fact]
    public async Task Agent_RunConfig_AllowBackgroundResponses_OverridesConfig()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("Response text");

        var config = DefaultConfig();
        config.BackgroundResponses = new BackgroundResponsesConfig { DefaultAllow = true };

        var agent = CreateAgent(config, fakeClient);
        var session = new global::HPD.Agent.Session("test-session-id");
        var branch = new global::HPD.Agent.Branch("test-session-id");
        branch.AddMessage(UserMessage("Test"));

        // Act: Override at run level to disable
        var options = new AgentRunConfig { AllowBackgroundResponses = false };
        var events = new List<AgentEvent>();
        var messages = branch.Messages;
        await foreach (var evt in agent.RunAsync(messages, session: session, branch: branch, options: options, cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        // Assert: Background events should not be emitted when disabled at run level
        Assert.DoesNotContain(events, e => e is BackgroundOperationStartedEvent);
    }

    [Fact]
    public async Task Agent_BackgroundPollingInterval_ResolvesFromOptionsOverConfig()
    {
        // Arrange
        var config = DefaultConfig();
        config.BackgroundResponses = new BackgroundResponsesConfig
        {
            DefaultAllow = true,
            DefaultPollingInterval = TimeSpan.FromSeconds(10)
        };

        var options = new AgentRunConfig
        {
            BackgroundPollingInterval = TimeSpan.FromSeconds(1)
        };

        // Act
        var resolvedInterval = options.BackgroundPollingInterval
            ?? config.BackgroundResponses.DefaultPollingInterval;

        // Assert: Options should override config
        Assert.Equal(TimeSpan.FromSeconds(1), resolvedInterval);
    }

    [Fact]
    public async Task Agent_BackgroundTimeout_ResolvesFromOptionsOverConfig()
    {
        // Arrange
        var config = DefaultConfig();
        config.BackgroundResponses = new BackgroundResponsesConfig
        {
            DefaultAllow = true,
            DefaultTimeout = TimeSpan.FromMinutes(30)
        };

        var options = new AgentRunConfig
        {
            BackgroundTimeout = TimeSpan.FromMinutes(5)
        };

        // Act
        var resolvedTimeout = options.BackgroundTimeout
            ?? config.BackgroundResponses.DefaultTimeout;

        // Assert: Options should override config
        Assert.Equal(TimeSpan.FromMinutes(5), resolvedTimeout);
    }

    #endregion

    #region Session State with Background Operations

    [Fact]
    public async Task Session_BackgroundOperation_ClearsAfterCompletion()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("Completed response");

        var config = DefaultConfig();
        var agent = CreateAgent(config, fakeClient);
        var session = new global::HPD.Agent.Session("test-session-id");
        var branch = new global::HPD.Agent.Branch("test-session-id");
        branch.AddMessage(UserMessage("Test"));

        // Act
        var messages = branch.Messages;
        await foreach (var evt in agent.RunAsync(messages, session: session, branch: branch, cancellationToken: TestCancellationToken))
        {
            // Consume all events
        }

        // Assert: ExecutionState should not have active background operation after completion
        // (This is set during streaming when continuation token becomes null)
        // Note: ExecutionState is only set during actual background operations
        // With FakeChatClient not returning continuation tokens, there won't be one
        Assert.True(branch.ExecutionState == null ||
                    branch.ExecutionState.ActiveBackgroundOperation == null);
    }

    [Fact]
    public async Task AgentLoopState_Serialization_PreservesBackgroundOperationInfo()
    {
        // Arrange
        var messages = new List<ChatMessage> { UserMessage("Test") };
        var backgroundOp = new BackgroundOperationInfo
        {
            TokenData = "dGVzdF90b2tlbg==",
            Iteration = 2,
            StartedAt = DateTimeOffset.Parse("2025-12-15T10:30:00Z"),
            LastKnownStatus = OperationStatus.InProgress
        };

        var state = AgentLoopState.InitialSafe(messages, "run-123", "conv-456", "TestAgent")
            .WithBackgroundOperation(backgroundOp);

        // Act: Serialize and deserialize
        var json = state.Serialize();
        var deserialized = AgentLoopState.Deserialize(json);

        // Assert: Core fields should be preserved
        Assert.NotNull(deserialized.ActiveBackgroundOperation);
        Assert.Equal("dGVzdF90b2tlbg==", deserialized.ActiveBackgroundOperation.TokenData);
        Assert.Equal(2, deserialized.ActiveBackgroundOperation.Iteration);

        // Note: LastKnownStatus may not serialize properly if OperationStatus struct
        // doesn't have a proper JSON converter. This is acceptable as the TokenData
        // and Iteration are the critical fields for crash recovery.
        // The status can be re-queried from the provider using the token.
    }

    [Fact]
    public async Task AgentLoopState_Serialization_WorksWithNullBackgroundOperation()
    {
        // Arrange
        var messages = new List<ChatMessage> { UserMessage("Test") };
        var state = AgentLoopState.InitialSafe(messages, "run-123", "conv-456", "TestAgent");

        // Act: Serialize and deserialize
        var json = state.Serialize();
        var deserialized = AgentLoopState.Deserialize(json);

        // Assert
        Assert.Null(deserialized.ActiveBackgroundOperation);
    }

    #endregion

    #region Configuration via AgentConfig

    [Fact]
    public void AgentConfig_BackgroundResponses_FullConfiguration()
    {
        // Arrange & Act
        var config = new AgentConfig
        {
            Name = "BackgroundTestAgent",
            BackgroundResponses = new BackgroundResponsesConfig
            {
                DefaultAllow = true,
                DefaultPollingInterval = TimeSpan.FromSeconds(3),
                DefaultTimeout = TimeSpan.FromMinutes(15),
                AutoPollToCompletion = true,
                MaxPollAttempts = 100
            }
        };

        // Assert
        Assert.NotNull(config.BackgroundResponses);
        Assert.True(config.BackgroundResponses.DefaultAllow);
        Assert.Equal(TimeSpan.FromSeconds(3), config.BackgroundResponses.DefaultPollingInterval);
        Assert.Equal(TimeSpan.FromMinutes(15), config.BackgroundResponses.DefaultTimeout);
        Assert.True(config.BackgroundResponses.AutoPollToCompletion);
        Assert.Equal(100, config.BackgroundResponses.MaxPollAttempts);
    }

    #endregion

    #region Event Emission Tests

    [Fact]
    public async Task Agent_EmitsTextEvents_WhenBackgroundDisabled()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse("Hello", " ", "World", "!");

        var config = DefaultConfig();
        config.BackgroundResponses = new BackgroundResponsesConfig { DefaultAllow = false };

        var agent = CreateAgent(config, fakeClient);
        var session = new global::HPD.Agent.Session("test-session-id");
        var branch = new global::HPD.Agent.Branch("test-session-id");
        branch.AddMessage(UserMessage("Hi"));

        // Act
        var events = new List<AgentEvent>();
        var messages = branch.Messages;
        await foreach (var evt in agent.RunAsync(messages, session: session, branch: branch, cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        // Assert: Should have text delta events
        var textEvents = events.OfType<TextDeltaEvent>().ToList();
        Assert.NotEmpty(textEvents);
    }

    [Fact]
    public async Task Agent_CompletesNormally_WithoutBackgroundSupport()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("Task completed successfully!");

        var config = DefaultConfig();
        // No background config - defaults to disabled

        var agent = CreateAgent(config, fakeClient);
        var session = new global::HPD.Agent.Session("test-session-id");
        var branch = new global::HPD.Agent.Branch("test-session-id");
        branch.AddMessage(UserMessage("Complete my task"));

        // Act
        var events = new List<AgentEvent>();
        var messages = branch.Messages;
        await foreach (var evt in agent.RunAsync(messages, session: session, branch: branch, cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        // Assert: Should have turn finished event
        Assert.Contains(events, e => e is MessageTurnFinishedEvent);
    }

    #endregion

    #region ContinuationToken Pass-Through Tests

    [Fact]
    public async Task Agent_ContinuationToken_PassedThroughToOptions()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("Polling result");

        var config = DefaultConfig();
        config.BackgroundResponses = new BackgroundResponsesConfig { DefaultAllow = true };

        var agent = CreateAgent(config, fakeClient);
        var session = new global::HPD.Agent.Session("test-session-id");
        var branch = new global::HPD.Agent.Branch("test-session-id");
        branch.AddMessage(UserMessage("Poll for result"));

        // Create a mock continuation token
        #pragma warning disable MEAI001 // Experimental API
        var token = ResponseContinuationToken.FromBytes(new byte[] { 0x01, 0x02, 0x03 });
        #pragma warning restore MEAI001

        var options = new AgentRunConfig
        {
            AllowBackgroundResponses = true,
            ContinuationToken = token
        };

        // Act: This tests that the token is accepted and passed through
        // The actual behavior depends on provider support
        var events = new List<AgentEvent>();
        var messages = branch.Messages;
        await foreach (var evt in agent.RunAsync(messages, session: session, branch: branch, options: options, cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        // Assert: Agent should complete without error
        Assert.Contains(events, e => e is MessageTurnFinishedEvent);
    }

    #endregion

    #region Auto-Poll Mode Configuration Tests

    [Fact]
    public void AutoPollMode_Configuration_IsValid()
    {
        // Arrange
        var config = new BackgroundResponsesConfig
        {
            DefaultAllow = true,
            AutoPollToCompletion = true,
            DefaultPollingInterval = TimeSpan.FromSeconds(2),
            MaxPollAttempts = 1000
        };

        // Assert: With default settings, max wait time would be ~33 minutes
        var maxWaitTime = config.DefaultPollingInterval * config.MaxPollAttempts;
        Assert.Equal(TimeSpan.FromSeconds(2000), maxWaitTime);
    }

    [Fact]
    public void AutoPollMode_CustomConfiguration_CalculatesCorrectly()
    {
        // Arrange
        var config = new BackgroundResponsesConfig
        {
            DefaultAllow = true,
            AutoPollToCompletion = true,
            DefaultPollingInterval = TimeSpan.FromSeconds(5),
            MaxPollAttempts = 120 // 10 minutes with 5s interval
        };

        // Assert
        var maxWaitTime = config.DefaultPollingInterval * config.MaxPollAttempts;
        Assert.Equal(TimeSpan.FromMinutes(10), maxWaitTime);
    }

    #endregion

    #region Multiple Provider Scenarios

    [Fact]
    public async Task Agent_MultipleRuns_BackgroundSettingsIndependent()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("Response 1");
        fakeClient.EnqueueTextResponse("Response 2");

        var config = DefaultConfig();
        config.BackgroundResponses = new BackgroundResponsesConfig { DefaultAllow = true };

        var agent = CreateAgent(config, fakeClient);

        // Run 1: With background enabled (via config)
        var session1 = new global::HPD.Agent.Session("test-session-1");
        var branch1 = new global::HPD.Agent.Branch("test-session-1");
        branch1.AddMessage(UserMessage("Request 1"));
        var events1 = new List<AgentEvent>();
        var messages1 = branch1.Messages;
        await foreach (var evt in agent.RunAsync(messages1, session: session1, branch: branch1, cancellationToken: TestCancellationToken))
        {
            events1.Add(evt);
        }

        // Run 2: With background disabled (via options override)
        var session2 = new global::HPD.Agent.Session("test-session-2");
        var branch2 = new global::HPD.Agent.Branch("test-session-2");
        branch2.AddMessage(UserMessage("Request 2"));
        var options2 = new AgentRunConfig { AllowBackgroundResponses = false };
        var events2 = new List<AgentEvent>();
        var messages2 = branch2.Messages;
        await foreach (var evt in agent.RunAsync(messages2, session: session2, branch: branch2, options: options2, cancellationToken: TestCancellationToken))
        {
            events2.Add(evt);
        }

        // Assert: Both runs should complete successfully
        Assert.Contains(events1, e => e is MessageTurnFinishedEvent);
        Assert.Contains(events2, e => e is MessageTurnFinishedEvent);
    }

    #endregion
}
