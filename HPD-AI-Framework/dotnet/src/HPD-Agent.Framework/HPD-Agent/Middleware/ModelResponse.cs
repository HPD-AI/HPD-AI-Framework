using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>
/// Immutable response object from LLM model calls.
/// Contains the assistant message and any tool calls requested.
/// </summary>
/// <remarks>
/// <para><b>Design Philosophy:</b></para>
/// <para>
/// ModelResponse is immutable to maintain consistency between the message
/// and tool calls. If middleware needs to modify the response, it should
/// create a new instance with updated values.
/// </para>
/// <para><b>Example:</b></para>
/// <code>
/// public async Task&lt;ModelResponse&gt; WrapModelCallAsync(
///     ModelRequest request,
///     Func&lt;ModelRequest, Task&lt;ModelResponse&gt;&gt; handler,
///     CancellationToken ct)
/// {
///     var response = await handler(request);
///
///     // Transform response if needed
///     if (ShouldModifyResponse(response))
///     {
///         return new ModelResponse
///         {
///             Message = TransformMessage(response.Message),
///             ToolCalls = response.ToolCalls,
///             Error = response.Error
///         };
///     }
///
///     return response;
/// }
/// </code>
/// </remarks>
public sealed record ModelResponse
{
    /// <summary>
    /// The assistant message returned by the LLM.
    ///   Always available when successful (NULL only if Error is set)
    /// </summary>
    public required ChatMessage Message { get; init; }

    /// <summary>
    /// Tool calls requested by the LLM in this response.
    ///   Always available (never NULL, but may be empty)
    /// </summary>
    public required IReadOnlyList<FunctionCallContent> ToolCalls { get; init; }

    /// <summary>
    /// Exception that occurred during LLM call (if any).
    /// NULL if call succeeded.
    /// </summary>
    public Exception? Error { get; init; }

    //
    // HELPERS
    //

    /// <summary>
    /// True if the LLM call succeeded (no error).
    /// </summary>
    public bool IsSuccess => Error == null;

    /// <summary>
    /// True if the LLM call failed (has error).
    /// </summary>
    public bool IsFailure => Error != null;

    /// <summary>
    /// True if the LLM requested tool calls.
    /// </summary>
    public bool HasToolCalls => ToolCalls.Count > 0;

    /// <summary>
    /// True if this is likely the final response (no tool calls).
    /// </summary>
    public bool IsFinalResponse => ToolCalls.Count == 0 && Error == null;
}
