using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Tests for PIIMiddleware - PII detection and handling.
/// Covers all strategies (Block, Redact, Mask, Hash), all PII types,
/// and edge cases like Luhn validation and custom detectors.
/// </summary>
public class PIIMiddlewareTests
{
    //      
    // EMAIL DETECTION TESTS
    //      

    [Fact]
    public async Task DetectsEmail_WithRedactStrategy_ReplacesWithPlaceholder()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            EmailStrategy = PIIStrategy.Redact
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Contact me at john.doe@example.com")
        };

        var context = CreateContext(messages);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var processed = context.ThreadHistory.Single();
        Assert.Contains("[EMAIL_REDACTED]", processed.Text);
        Assert.DoesNotContain("john.doe@example.com", processed.Text);
    }

    [Fact]
    public async Task DetectsEmail_WithMaskStrategy_PartiallyMasks()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            EmailStrategy = PIIStrategy.Mask
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Email: john@example.com")
        };

        var context = CreateContext(messages);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var processed = context.ThreadHistory.Single();
        // Should show first char, mask middle, keep @domain
        Assert.Contains("j***@example.com", processed.Text);
    }

    [Fact]
    public async Task DetectsEmail_WithHashStrategy_CreatesDeterministicHash()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            EmailStrategy = PIIStrategy.Hash
        };

        var messages1 = new List<ChatMessage>
        {
            new(ChatRole.User, "Email: test@test.com")
        };

        var context1 = CreateContext(messages1);

        // Act - run twice to verify determinism
        await middleware.BeforeMessageTurnAsync(context1, CancellationToken.None);

        var messages2 = new List<ChatMessage>
        {
            new(ChatRole.User, "Email: test@test.com")
        };
        var context2 = CreateContext(messages2);
        await middleware.BeforeMessageTurnAsync(context2, CancellationToken.None);

        // Assert - same email should produce same hash
        var hash1 = context1.ThreadHistory.Single().Text;
        var hash2 = context2.ThreadHistory.Single().Text;
        Assert.Equal(hash1, hash2);
        Assert.Contains("<email_hash:", hash1);
    }

    [Fact]
    public async Task DetectsMultipleEmails_ReplacesAll()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            EmailStrategy = PIIStrategy.Redact
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "CC: a@b.com and b@c.com")
        };

        var context = CreateContext(messages);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var processed = context.ThreadHistory.Single().Text!;
        Assert.DoesNotContain("@", processed);
        Assert.Equal(2, CountOccurrences(processed, "[EMAIL_REDACTED]"));
    }

    //      
    // CREDIT CARD DETECTION TESTS (WITH LUHN VALIDATION)
    //      

    [Fact]
    public async Task DetectsCreditCard_ValidLuhn_BlocksMessage()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            CreditCardStrategy = PIIStrategy.Block
        };

        // 4111111111111111 is a valid Luhn test card number
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "My card is 4111111111111111")
        };

        var context = CreateContext(messages);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<PIIBlockedException>(
            () => middleware.BeforeMessageTurnAsync(context, CancellationToken.None));

        Assert.Equal("CreditCard", ex.PIIType);
    }

    [Fact]
    public async Task DetectsCreditCard_ValidLuhn_WithMaskStrategy()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            CreditCardStrategy = PIIStrategy.Mask
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Card: 4111111111111111")
        };

        var context = CreateContext(messages);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var processed = context.ThreadHistory.Single().Text!;
        Assert.Contains("***", processed);
        Assert.Contains("1111", processed); // Should keep last 4 digits
    }

    [Fact]
    public async Task DetectsCreditCard_InvalidLuhn_DoesNotBlock()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            CreditCardStrategy = PIIStrategy.Block
        };

        // Invalid Luhn number (random digits)
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Number: 1234567890123456")
        };

        var context = CreateContext(messages);

        // Act - should NOT throw
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert - message should pass through unchanged
        var processed = context.ThreadHistory.Single().Text!;
        Assert.Contains("1234567890123456", processed);
    }

    //      
    // SSN DETECTION TESTS
    //      

    [Fact]
    public async Task DetectsSSN_WithBlockStrategy_ThrowsException()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            SSNStrategy = PIIStrategy.Block
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "My SSN is 123-45-6789")
        };

        var context = CreateContext(messages);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<PIIBlockedException>(
            () => middleware.BeforeMessageTurnAsync(context, CancellationToken.None));

        Assert.Equal("SSN", ex.PIIType);
    }

    [Fact]
    public async Task DetectsSSN_WithRedactStrategy_Redacts()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            SSNStrategy = PIIStrategy.Redact
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "SSN: 123-45-6789")
        };

        var context = CreateContext(messages);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var processed = context.ThreadHistory.Single().Text!;
        Assert.Contains("[SSN_REDACTED]", processed);
        Assert.DoesNotContain("123-45-6789", processed);
    }

    //      
    // PHONE NUMBER DETECTION TESTS
    //      

    [Fact]
    public async Task DetectsPhone_WithMaskStrategy_PartiallyMasks()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            PhoneStrategy = PIIStrategy.Mask
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Call me at 555-123-4567")
        };

        var context = CreateContext(messages);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var processed = context.ThreadHistory.Single().Text!;
        Assert.Contains("***", processed);
        Assert.Contains("4567", processed); // Should keep last 4 digits
    }

    //      
    // IP ADDRESS DETECTION TESTS
    //      

    [Fact]
    public async Task DetectsIPAddress_WithHashStrategy_HashesIP()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            IPAddressStrategy = PIIStrategy.Hash
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Server IP: 192.168.1.1")
        };

        var context = CreateContext(messages);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var processed = context.ThreadHistory.Single().Text!;
        Assert.DoesNotContain("192.168.1.1", processed);
        Assert.Contains("<ipaddress_hash:", processed);
    }

    //      
    // ALLOW STRATEGY TESTS
    //      

    [Fact]
    public async Task AllowStrategy_PassesThrough()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            EmailStrategy = PIIStrategy.Allow
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Email: test@test.com")
        };

        var context = CreateContext(messages);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var processed = context.ThreadHistory.Single().Text!;
        Assert.Contains("test@test.com", processed);
    }

    //      
    // CUSTOM DETECTOR TESTS
    //      

    [Fact]
    public async Task CustomDetector_DetectsCustomPattern()
    {
        // Arrange
        var middleware = new PIIMiddleware();
        middleware.AddCustomDetector(
            name: "EmployeeId",
            pattern: @"EMP-\d{6}",
            strategy: PIIStrategy.Redact);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Employee EMP-123456 needs access")
        };

        var context = CreateContext(messages);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var processed = context.ThreadHistory.Single().Text!;
        Assert.Contains("[EMPLOYEEID_REDACTED]", processed);
        Assert.DoesNotContain("EMP-123456", processed);
    }

    //      
    // CONFIGURATION TESTS
    //      

    [Fact]
    public void DefaultConfiguration_UsesReasonableDefaults()
    {
        // Arrange & Act
        var middleware = new PIIMiddleware();

        // Assert
        Assert.Equal(PIIStrategy.Redact, middleware.EmailStrategy);
        Assert.Equal(PIIStrategy.Block, middleware.CreditCardStrategy);
        Assert.Equal(PIIStrategy.Block, middleware.SSNStrategy);
        Assert.Equal(PIIStrategy.Mask, middleware.PhoneStrategy);
        Assert.Equal(PIIStrategy.Hash, middleware.IPAddressStrategy);
        Assert.True(middleware.ApplyToInput);
        Assert.False(middleware.ApplyToOutput);
    }

    [Fact]
    public async Task ApplyToInput_WhenFalse_SkipsUserMessages()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            ApplyToInput = false,
            EmailStrategy = PIIStrategy.Block
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Email: test@test.com")
        };

        var context = CreateContext(messages);

        // Act - should NOT throw even with Block strategy
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert - message should pass through
        var processed = context.ThreadHistory.Single().Text!;
        Assert.Contains("test@test.com", processed);
    }

    [Fact]
    public async Task ApplyToOutput_WhenTrue_RedactsDurableHistoryWithoutMutatingPublishedFinalResponse()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            ApplyToOutput = true,
            EmailStrategy = PIIStrategy.Redact
        };

        var assistantMessage = new ChatMessage(ChatRole.Assistant, "Send it to agent@example.com");
        var turnHistoryMessage = new ChatMessage(ChatRole.Assistant, "Logged agent@example.com");
        var context = CreateAfterMessageTurnContext(
            finalResponse: new ChatResponse(assistantMessage),
            turnHistory: [turnHistoryMessage]);

        // Act
        await middleware.AfterMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.Contains("agent@example.com", context.FinalResponse.Text);
        Assert.Contains("[EMAIL_REDACTED]", context.TurnHistory.Single().Text);
        Assert.DoesNotContain("agent@example.com", context.TurnHistory.Single().Text);
    }

    [Fact]
    public async Task ApplyToOutput_RedactsStreamingTextBeforeItIsPublished()
    {
        var middleware = new PIIMiddleware { ApplyToOutput = true, EmailStrategy = PIIStrategy.Redact };
        var request = V2.MiddlewareTestHelpers.CreateAgentModelTurnRequest();

        async IAsyncEnumerable<AgentModelUpdate> Next(AgentModelTurnRequest _)
        {
            yield return new AgentTextDeltaUpdate("agent@");
            await Task.Yield();
            yield return new AgentTextDeltaUpdate("example.com", IsFinal: true);
        }

        var stream = middleware.WrapModelTurnStreamingAsync(request, Next, CancellationToken.None);
        Assert.NotNull(stream);
        var output = new List<AgentModelUpdate>();
        await foreach (var update in stream!) output.Add(update);

        Assert.Equal("[EMAIL_REDACTED]", string.Concat(output.OfType<AgentTextDeltaUpdate>().Select(update => update.Text)));
    }

    [Fact]
    public async Task ApplyToOutput_UsesBoundedLookbehindWithoutWaitingForTheFinalChunk()
    {
        var middleware = new PIIMiddleware { ApplyToOutput = true };
        var request = V2.MiddlewareTestHelpers.CreateAgentModelTurnRequest();

        async IAsyncEnumerable<AgentModelUpdate> Next(AgentModelTurnRequest _)
        {
            yield return new AgentTextDeltaUpdate(new string('a', 430));
            await Task.Yield();
            yield return new AgentTextDeltaUpdate("world", IsFinal: true);
        }

        await using var enumerator = middleware.WrapModelTurnStreamingAsync(request, Next, CancellationToken.None)!.GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(new string('a', 46), Assert.IsType<AgentTextDeltaUpdate>(enumerator.Current).Text);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(new string('a', 384) + "world", Assert.IsType<AgentTextDeltaUpdate>(enumerator.Current).Text);
    }

    [Fact]
    public async Task ApplyToOutput_UnboundedCustomDetectorDoesNotLeakAcrossLargeChunkBoundary()
    {
        var middleware = new PIIMiddleware { ApplyToOutput = true };
        middleware.AddCustomDetector("LongSecret", @"BEGIN-[a-z]{300}-END", PIIStrategy.Redact);
        var request = V2.MiddlewareTestHelpers.CreateAgentModelTurnRequest();

        async IAsyncEnumerable<AgentModelUpdate> Next(AgentModelTurnRequest _)
        {
            yield return new AgentTextDeltaUpdate("BEGIN-" + new string('a', 270));
            await Task.Yield();
            yield return new AgentTextDeltaUpdate(new string('a', 30) + "-END", IsFinal: true);
        }

        await using var enumerator = middleware.WrapModelTurnStreamingAsync(request, Next, CancellationToken.None)!.GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(string.Empty, Assert.IsType<AgentTextDeltaUpdate>(enumerator.Current).Text);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("[LONGSECRET_REDACTED]", Assert.IsType<AgentTextDeltaUpdate>(enumerator.Current).Text);
    }

    [Fact]
    public async Task ApplyToOutput_WhenFalse_LeavesAssistantOutputUnchanged()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            ApplyToOutput = false,
            EmailStrategy = PIIStrategy.Block
        };

        var context = CreateAfterMessageTurnContext(
            finalResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, "Email agent@example.com")),
            turnHistory: [new ChatMessage(ChatRole.Assistant, "Email agent@example.com")]);

        // Act
        await middleware.AfterMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.Contains("agent@example.com", context.FinalResponse.Text);
        Assert.Contains("agent@example.com", context.TurnHistory.Single().Text);
    }

    [Fact]
    public async Task ApplyToToolResults_WhenTrue_RedactsFunctionResult()
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            ApplyToToolResults = true,
            EmailStrategy = PIIStrategy.Redact
        };

        var result = new FunctionResultContent("call-1", "Customer email is tool@example.com");
        var context = CreateAfterIterationContext([result]);

        // Act
        await middleware.AfterIterationAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal("Customer email is [EMAIL_REDACTED]", result.Result);
    }

    //      
    // LUHN VALIDATION TESTS
    //      

    [Theory]
    [InlineData("4111111111111111", true)]   // Visa test card
    [InlineData("5500000000000004", true)]   // MasterCard test card
    [InlineData("340000000000009", true)]    // Amex test card
    [InlineData("1234567890123456", false)]  // Random invalid
    [InlineData("0000000000000000", true)]   // All zeros passes Luhn
    public async Task LuhnValidation_ValidatesCorrectly(string number, bool expectedValid)
    {
        // Arrange
        var middleware = new PIIMiddleware
        {
            CreditCardStrategy = PIIStrategy.Redact
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, $"Number: {number}")
        };

        var context = CreateContext(messages);

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var processed = context.ThreadHistory.Single().Text!;
        if (expectedValid)
        {
            Assert.Contains("[CREDITCARD_REDACTED]", processed);
        }
        else
        {
            Assert.Contains(number, processed);
        }
    }

    //      
    // HELPER METHODS
    //      

    private static BeforeMessageTurnContext CreateContext(List<ChatMessage> messages)
    {
        var state = AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");

        var agentContext = new AgentContext(
            agentName: "TestAgent",
            conversationId: "test-conversation",
            state,
            new HPD.Events.Core.EventCoordinator(),
            new global::HPD.Agent.Session("test-session"),
            new global::HPD.Agent.Thread("test-session", "test-agent"),
            CancellationToken.None);

        // PIIMiddleware uses BeforeMessageTurnAsync, so create the appropriate context
        var userMessage = new ChatMessage(ChatRole.User, "test");
        return agentContext.AsBeforeMessageTurn(
            userMessage,
            new List<ChatMessage>(messages),
            new AgentRunConfig());
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    private static AgentContext CreateAgentContext(AgentLoopState? state = null)
    {
        var agentState = state ?? AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");

        return new AgentContext(
            "TestAgent",
            "test-conversation",
            agentState,
            new HPD.Events.Core.EventCoordinator(),
            new global::HPD.Agent.Session("test-session"),
            new global::HPD.Agent.Thread("test-session", "test-agent"),
            CancellationToken.None);
    }

    private static BeforeToolExecutionContext CreateBeforeToolExecutionContext(
        ChatMessage? response = null,
        List<FunctionCallContent>? toolCalls = null,
        AgentLoopState? state = null)
    {
        var agentContext = CreateAgentContext(state);
        response ??= new ChatMessage(ChatRole.Assistant, []);
        toolCalls ??= new List<FunctionCallContent>();
        return agentContext.AsBeforeToolExecution(response, toolCalls, new AgentRunConfig());
    }

    private static AfterMessageTurnContext CreateAfterMessageTurnContext(
        AgentLoopState? state = null,
        ChatResponse? finalResponse = null,
        List<ChatMessage>? turnHistory = null)
    {
        var agentContext = CreateAgentContext(state);
        finalResponse ??= new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test response"));
        turnHistory ??= new List<ChatMessage>();
        return agentContext.AsAfterMessageTurn(finalResponse, turnHistory, new AgentRunConfig());
    }

    private static AfterIterationContext CreateAfterIterationContext(
        IReadOnlyList<FunctionResultContent> toolResults,
        AgentLoopState? state = null)
    {
        var agentContext = CreateAgentContext(state);
        return agentContext.AsAfterIteration(0, toolResults, new AgentRunConfig());
    }

}
