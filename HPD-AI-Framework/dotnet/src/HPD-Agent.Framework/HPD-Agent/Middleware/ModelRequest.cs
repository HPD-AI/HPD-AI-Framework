using HPD.Events;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>
/// Immutable request object for LLM model calls.
/// </summary>
/// <remarks>
/// <para><b>Design Philosophy:</b></para>
/// <para>
/// ModelRequest is immutable to preserve the original request for debugging,
/// logging, and retry logic. Middleware can create modified copies using the
/// <see cref="Override"/> method without affecting the original request.
/// </para>
/// <para><b>Immutability Benefits:</b></para>
/// <list type="bullet">
/// <item>  Original request preserved for debugging</item>
/// <item>  Safe to pass between middleware layers</item>
/// <item>  Enables request/response comparison for telemetry</item>
/// <item>  Thread-safe (can be shared across async operations)</item>
/// </list>
/// <para><b>Example:</b></para>
/// <code>
/// public async Task&lt;ModelResponse&gt; WrapModelCallAsync(
///     ModelRequest request,
///     Func&lt;ModelRequest, Task&lt;ModelResponse&gt;&gt; handler,
///     CancellationToken ct)
/// {
///     // Modify request immutably
///     var newRequest = request.Override(
///         messages: request.Messages.Append(systemMessage).ToList()
///     );
///
///     // Can compare original vs modified for logging
///     _logger.LogInformation("Added {Count} messages",
///         newRequest.Messages.Count - request.Messages.Count);
///
///     return await handler(newRequest);
/// }
/// </code>
/// </remarks>
public sealed record ModelRequest
{
    /// <summary>
    /// The chat client to use for this LLM call.
    ///   Always available (never NULL)
    /// </summary>
    public required IChatClient Model { get; init; }

    /// <summary>
    /// Messages to send to the LLM.
    ///   Always available (never NULL)
    /// Immutable list - use Override() to create modified copy
    /// </summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>
    /// Chat options for this LLM call.
    ///   Always available (never NULL)
    /// </summary>
    public required ChatOptions Options { get; init; }

    /// <summary>
    /// Current agent state at time of request.
    ///   Always available (never NULL)
    /// </summary>
    public required AgentLoopState State { get; init; }

    /// <summary>
    /// Current iteration number (0-based).
    ///   Always available
    /// </summary>
    public required int Iteration { get; init; }

    //
    // MIDDLEWARE CAPABILITIES
    //

    /// <summary>
    /// Stream registry for creating interruptible audio/video streams.
    /// Used by middleware like AudioPipelineMiddleware for stream management.
    ///   May be NULL if streaming features not available
    /// </summary>
    /// <remarks>
    /// <para>
    /// Middleware can use this to create interruptible streams for audio, video,
    /// or any other content that needs coordinated cancellation support.
    /// </para>
    /// <para><b>Example (AudioPipelineMiddleware):</b></para>
    /// <code>
    /// var stream = request.Streams?.Create();
    /// try
    /// {
    ///     await foreach (var update in handler(request))
    ///     {
    ///         if (stream?.IsInterrupted == true) break;
    ///         // Synthesize audio and emit events...
    ///     }
    /// }
    /// finally
    /// {
    ///     stream?.Complete();
    /// }
    /// </code>
    /// </remarks>
    public IStreamRegistry? Streams { get; init; }

    /// <summary>
    /// The current session. Provides access to session storage (content store, asset store).
    /// Middleware can use this for storing artifacts produced during the model call.
    ///   May be NULL if no session is active
    /// </summary>
    public Session? Session { get; init; }

    /// <summary>
    /// Original run options for this agent turn.
    /// Contains per-request configuration like Audio settings, StructuredOutput, etc.
    ///   May be NULL in test scenarios
    /// </summary>
    /// <remarks>
    /// <para>
    /// Middleware can access per-request overrides through this property.
    /// For example, AudioPipelineMiddleware reads Audio settings:
    /// </para>
    /// <code>
    /// var audioOptions = request.RunConfig?.Audio as AudioRunConfig;
    /// var voice = audioOptions?.Voice ?? "default";
    /// </code>
    /// </remarks>
    public AgentRunConfig? RunConfig { get; init; }

    /// <summary>
    /// Event coordinator for emitting events during streaming.
    /// Middleware can emit progress, audio chunks, metrics, etc.
    ///   May be NULL in test scenarios
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this to emit events during streaming without blocking the pipeline.
    /// Events are automatically routed to the appropriate handlers.
    /// </para>
    /// <para><b>Example:</b></para>
    /// <code>
    /// request.EventCoordinator?.Emit(new AudioChunkEvent(
    ///     Base64Audio: audioData,
    ///     MimeType: "audio/mp3",
    ///     Duration: TimeSpan.FromMilliseconds(100))
    /// {
    ///     Priority = EventPriority.Normal,
    ///     StreamId = stream?.StreamId
    /// });
    /// </code>
    /// </remarks>
    public HPD.Events.IEventCoordinator? EventCoordinator { get; init; }

    /// <summary>
    /// Creates a modified copy of this request.
    /// </summary>
    /// <param name="model">Optional new model (null = keep original)</param>
    /// <param name="messages">Optional new messages (null = keep original)</param>
    /// <param name="options">Optional new options (null = keep original)</param>
    /// <returns>A new ModelRequest with specified properties overridden</returns>
    /// <remarks>
    /// <para>
    /// This method preserves the original request intact while creating a modified copy.
    /// Useful for adding context, transforming messages, or modifying options.
    /// </para>
    /// <para><b>Note:</b></para>
    /// <para>
    /// Streams, RunConfig, and EventCoordinator are NOT overrideable via this method.
    /// These properties come from the agent runtime and should not be modified by middleware.
    /// </para>
    /// <para><b>Example:</b></para>
    /// <code>
    /// // Add a system message
    /// var newRequest = request.Override(
    ///     messages: request.Messages.Prepend(systemMessage).ToList()
    /// );
    ///
    /// // Change temperature
    /// var newRequest = request.Override(
    ///     options: request.Options with { Temperature = 0.7f }
    /// );
    ///
    /// // Multiple overrides
    /// var newRequest = request.Override(
    ///     messages: modifiedMessages,
    ///     options: modifiedOptions
    /// );
    /// </code>
    /// </remarks>
    public ModelRequest Override(
        IChatClient? model = null,
        IReadOnlyList<ChatMessage>? messages = null,
        ChatOptions? options = null)
    {
        return this with
        {
            Model = model ?? Model,
            Messages = messages ?? Messages,
            Options = options ?? Options
        };
    }
}
