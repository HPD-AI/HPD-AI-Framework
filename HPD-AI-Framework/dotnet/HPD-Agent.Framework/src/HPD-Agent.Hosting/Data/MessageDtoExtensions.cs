using Microsoft.Extensions.AI;

namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Extension methods for <see cref="MessageDto"/> that let consumers work with message
/// contents without needing to import or pattern-match on Microsoft.Extensions.AI types directly.
/// </summary>
public static class MessageDtoExtensions
{
    /// <summary>
    /// Returns the concatenated text of all <see cref="TextContent"/> items in the message.
    /// Returns an empty string if there is no text content.
    /// </summary>
    public static string GetText(this MessageDto message)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in message.Contents)
        {
            if (c is TextContent text && !string.IsNullOrEmpty(text.Text))
                sb.Append(text.Text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Enumerates each tool call in the message as a <see cref="ToolCallInfo"/>.
    /// </summary>
    public static IEnumerable<ToolCallInfo> GetToolCalls(this MessageDto message)
    {
        foreach (var c in message.Contents)
        {
            if (c is FunctionCallContent call)
                yield return new ToolCallInfo(call.CallId, call.Name ?? string.Empty, call.Arguments);
        }
    }

    /// <summary>
    /// Enumerates each tool result in the message as a <see cref="ToolResultInfo"/>.
    /// </summary>
    public static IEnumerable<ToolResultInfo> GetToolResults(this MessageDto message)
    {
        foreach (var c in message.Contents)
        {
            if (c is FunctionResultContent result)
                yield return new ToolResultInfo(result.CallId, result.Result);
        }
    }

    /// <summary>Returns true if the message contains any text content.</summary>
    public static bool HasText(this MessageDto message) =>
        message.Contents.Any(c => c is TextContent t && !string.IsNullOrEmpty(t.Text));

    /// <summary>Returns true if the message contains any tool calls.</summary>
    public static bool HasToolCalls(this MessageDto message) =>
        message.Contents.Any(c => c is FunctionCallContent);
}

/// <summary>A tool call extracted from a <see cref="MessageDto"/>.</summary>
/// <param name="CallId">The unique call identifier used to match results.</param>
/// <param name="Name">The tool/function name.</param>
/// <param name="Arguments">The arguments as a JSON-serializable object (may be null).</param>
public record ToolCallInfo(string CallId, string Name, object? Arguments);

/// <summary>A tool result extracted from a <see cref="MessageDto"/>.</summary>
/// <param name="CallId">The call identifier this result corresponds to.</param>
/// <param name="Result">The result value (may be null).</param>
public record ToolResultInfo(string CallId, object? Result);
