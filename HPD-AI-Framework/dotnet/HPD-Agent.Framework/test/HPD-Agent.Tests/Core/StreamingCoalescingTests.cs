using Microsoft.Extensions.AI;
using Xunit;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Tests to ensure streaming text chunks are coalesced into single TextContent items.
/// Prevents regression where threads show individual stream chunks as separate content.
/// </summary>
public class StreamingCoalescingTests
{
    /// <summary>
    /// Tests that ConstructChatResponseFromUpdates properly coalesces consecutive TextContent items.
    /// This is critical to prevent threads from showing individual streaming chunks.
    /// </summary>
    [Fact]
    public void ConstructChatResponseFromUpdates_CoalescesConsecutiveTextContent()
    {
        // Arrange: Simulate streaming chunks (multiple TextContent items)
        var updates = new List<ChatResponseUpdate>
        {
            new ChatResponseUpdate
            {
                Contents = [new TextContent("Hello")],
                ResponseId = "msg-123"
            },
            new ChatResponseUpdate
            {
                Contents = [new TextContent(" ")],
            },
            new ChatResponseUpdate
            {
                Contents = [new TextContent("World")],
            },
            new ChatResponseUpdate
            {
                Contents = [new TextContent("!")],
                FinishReason = ChatFinishReason.Stop
            }
        };

        // Act: Call private method via reflection
        var method = typeof(Agent).GetMethod(
            "ConstructChatResponseFromUpdates",
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(method);
        
        var response = (ChatResponse)method.Invoke(null, [updates, false])!;

        // Assert: Should have exactly ONE TextContent with combined text
        Assert.Single(response.Messages);
        var message = response.Messages[0];
        
        Assert.Single(message.Contents);
        var content = message.Contents[0];
        
        Assert.IsType<TextContent>(content);
        var textContent = (TextContent)content;
        Assert.Equal("Hello World!", textContent.Text);
    }

    /// <summary>
    /// Tests that CoalesceTextContents preserves non-text content as boundaries.
    /// Function calls should not be merged with surrounding text.
    /// </summary>
    [Fact]
    public void CoalesceTextContents_PreservesNonTextContentAsBoundaries()
    {
        // Arrange: Mix of text and function call content
        var contents = new List<AIContent>
        {
            new TextContent("Before "),
            new TextContent("function"),
            new FunctionCallContent("call-123", "GetWeather", new Dictionary<string, object?> { ["city"] = "Seattle" }),
            new TextContent("After "),
            new TextContent("function")
        };

        // Act: Call private method via reflection
        var method = typeof(Agent).GetMethod(
            "CoalesceTextContents",
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(method);
        
        var result = (List<AIContent>)method.Invoke(null, [contents])!;

        // Assert: Should have 3 items: coalesced text, function call, coalesced text
        Assert.Equal(3, result.Count);
        
        // First item: "Before function"
        Assert.IsType<TextContent>(result[0]);
        Assert.Equal("Before function", ((TextContent)result[0]).Text);
        
        // Second item: Function call (preserved)
        Assert.IsType<FunctionCallContent>(result[1]);
        Assert.Equal("GetWeather", ((FunctionCallContent)result[1]).Name);
        
        // Third item: "After function"
        Assert.IsType<TextContent>(result[2]);
        Assert.Equal("After function", ((TextContent)result[2]).Text);
    }

    /// <summary>
    /// Tests that CoalesceTextContents handles single item lists efficiently.
    /// </summary>
    [Fact]
    public void CoalesceTextContents_HandlesSingleItemList()
    {
        // Arrange
        var contents = new List<AIContent>
        {
            new TextContent("Single item")
        };

        // Act
        var method = typeof(Agent).GetMethod(
            "CoalesceTextContents",
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(method);
        
        var result = (List<AIContent>)method.Invoke(null, [contents])!;

        // Assert: Should return the same list (optimization)
        Assert.Same(contents, result);
    }

    /// <summary>
    /// Tests that CoalesceTextContents handles empty lists.
    /// </summary>
    [Fact]
    public void CoalesceTextContents_HandlesEmptyList()
    {
        // Arrange
        var contents = new List<AIContent>();

        // Act
        var method = typeof(Agent).GetMethod(
            "CoalesceTextContents",
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(method);
        
        var result = (List<AIContent>)method.Invoke(null, [contents])!;

        // Assert: Should return the same empty list
        Assert.Same(contents, result);
        Assert.Empty(result);
    }

    /// <summary>
    /// Tests that ConstructChatResponseFromUpdates excludes TextReasoningContent by default.
    /// </summary>
    [Fact]
    public void ConstructChatResponseFromUpdates_ExcludesReasoningByDefault()
    {
        // Arrange: Updates with reasoning content
        var updates = new List<ChatResponseUpdate>
        {
            new ChatResponseUpdate
            {
                Contents = [new TextContent("Regular text")],
            },
            new ChatResponseUpdate
            {
                Contents = [new TextReasoningContent("Internal reasoning")],
            },
            new ChatResponseUpdate
            {
                Contents = [new TextContent("More text")],
                FinishReason = ChatFinishReason.Stop
            }
        };

        // Act
        var method = typeof(Agent).GetMethod(
            "ConstructChatResponseFromUpdates",
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(method);
        
        var response = (ChatResponse)method.Invoke(null, [updates, false])!;

        // Assert: Should only have regular text (reasoning excluded)
        Assert.Single(response.Messages);
        var message = response.Messages[0];
        
        Assert.Single(message.Contents);
        var content = (TextContent)message.Contents[0];
        Assert.Equal("Regular textMore text", content.Text);
    }

    /// <summary>
    /// Tests that ConstructChatResponseFromUpdates preserves reasoning when configured.
    /// </summary>
    [Fact]
    public void ConstructChatResponseFromUpdates_PreservesReasoningWhenConfigured()
    {
        // Arrange: Updates with reasoning content (multiple chunks)
        var updates = new List<ChatResponseUpdate>
        {
            new ChatResponseUpdate
            {
                Contents = [new TextContent("Regular ")],
            },
            new ChatResponseUpdate
            {
                Contents = [new TextContent("text")],
            },
            new ChatResponseUpdate
            {
                Contents = [new TextReasoningContent("Internal ")],
            },
            new ChatResponseUpdate
            {
                Contents = [new TextReasoningContent("reasoning")],
            },
            new ChatResponseUpdate
            {
                FinishReason = ChatFinishReason.Stop
            }
        };

        // Act: Pass true to preserve reasoning
        var method = typeof(Agent).GetMethod(
            "ConstructChatResponseFromUpdates",
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(method);
        
        var response = (ChatResponse)method.Invoke(null, [updates, true])!;

        // Assert: Should have both regular and reasoning content preserved and coalesced
        Assert.Single(response.Messages);
        var message = response.Messages[0];
        
        // TextContent and TextReasoningContent are coalesced separately
        Assert.Equal(2, message.Contents.Count);
        
        var regularText = Assert.IsType<TextContent>(message.Contents[0]);
        Assert.Equal("Regular text", regularText.Text);
        
        var reasoningText = Assert.IsType<TextReasoningContent>(message.Contents[1]);
        Assert.Equal("Internal reasoning", reasoningText.Text);
    }

    /// <summary>
    /// Tests that ConstructChatResponseFromUpdates handles UsageContent correctly.
    /// </summary>
    [Fact]
    public void ConstructChatResponseFromUpdates_ExtractsUsageFromContent()
    {
        // Arrange: Updates with usage content
        var usageDetails = new UsageDetails
        {
            InputTokenCount = 100,
            OutputTokenCount = 50,
            TotalTokenCount = 150
        };

        var updates = new List<ChatResponseUpdate>
        {
            new ChatResponseUpdate
            {
                Contents = [new TextContent("Response")],
            },
            new ChatResponseUpdate
            {
                Contents = [new UsageContent(usageDetails)],
                FinishReason = ChatFinishReason.Stop
            }
        };

        // Act
        var method = typeof(Agent).GetMethod(
            "ConstructChatResponseFromUpdates",
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(method);
        
        var response = (ChatResponse)method.Invoke(null, [updates, false])!;

        // Assert: Should extract usage but not include it in message contents
        Assert.NotNull(response.Usage);
        Assert.Equal(100, response.Usage.InputTokenCount);
        Assert.Equal(50, response.Usage.OutputTokenCount);
        Assert.Equal(150, response.Usage.TotalTokenCount);
        
        // Message should only have text content (UsageContent is metadata)
        Assert.Single(response.Messages);
        var message = response.Messages[0];
        Assert.Single(message.Contents);
        Assert.IsType<TextContent>(message.Contents[0]);
    }

    /// <summary>
    /// Tests that CoalesceTextContents handles lists with only non-text content.
    /// </summary>
    [Fact]
    public void CoalesceTextContents_HandlesOnlyNonTextContent()
    {
        // Arrange: Only function calls, no text
        var contents = new List<AIContent>
        {
            new FunctionCallContent("call-1", "Function1", new Dictionary<string, object?>()),
            new FunctionCallContent("call-2", "Function2", new Dictionary<string, object?>())
        };

        // Act
        var method = typeof(Agent).GetMethod(
            "CoalesceTextContents",
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(method);
        
        var result = (List<AIContent>)method.Invoke(null, [contents])!;

        // Assert: Should preserve all non-text content unchanged
        Assert.Equal(2, result.Count);
        Assert.IsType<FunctionCallContent>(result[0]);
        Assert.IsType<FunctionCallContent>(result[1]);
        Assert.Equal("Function1", ((FunctionCallContent)result[0]).Name);
        Assert.Equal("Function2", ((FunctionCallContent)result[1]).Name);
    }

    /// <summary>
    /// Regression test: Ensures that streaming chunks are coalesced in realistic scenario.
    /// This simulates what happens when an LLM streams a response token by token.
    /// </summary>
    [Fact]
    public void RegressionTest_StreamingChunksAreCoalesced()
    {
        // Arrange: Simulate realistic token-by-token streaming
        var updates = new List<ChatResponseUpdate>
        {
            new ChatResponseUpdate { Contents = [new TextContent("The")] },
            new ChatResponseUpdate { Contents = [new TextContent(" quick")] },
            new ChatResponseUpdate { Contents = [new TextContent(" brown")] },
            new ChatResponseUpdate { Contents = [new TextContent(" fox")] },
            new ChatResponseUpdate { Contents = [new TextContent(" jumps")] },
            new ChatResponseUpdate { Contents = [new TextContent(" over")] },
            new ChatResponseUpdate { Contents = [new TextContent(" the")] },
            new ChatResponseUpdate { Contents = [new TextContent(" lazy")] },
            new ChatResponseUpdate { Contents = [new TextContent(" dog")] },
            new ChatResponseUpdate { FinishReason = ChatFinishReason.Stop }
        };

        // Act
        var method = typeof(Agent).GetMethod(
            "ConstructChatResponseFromUpdates",
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(method);
        
        var response = (ChatResponse)method.Invoke(null, [updates, false])!;

        // Assert: Should produce exactly ONE message with ONE TextContent
        Assert.Single(response.Messages);
        var message = response.Messages[0];
        Assert.Single(message.Contents);
        
        var textContent = Assert.IsType<TextContent>(message.Contents[0]);
        Assert.Equal("The quick brown fox jumps over the lazy dog", textContent.Text);
        
        // This is the critical assertion: NOT 9 separate TextContent items
        Assert.NotEqual(9, message.Contents.Count);
    }
}
