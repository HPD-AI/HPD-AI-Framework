using HPD.Agent.Serialization;

namespace HPD.Agent.Integrations;

/// <summary>
/// ASP.NET Core integration extensions for agent endpoints.
/// Provides zero-boilerplate methods for common web scenarios.
/// </summary>
/// <remarks>
/// <para>
/// These extensions use the standard event serialization format,
/// ensuring consistent JSON output across all HPD-Agent applications.
/// </para>
/// <para>
/// <b>Usage:</b>
/// <code>
/// app.MapAgentSseEndpoint("/agent/stream", sp =>
///     new AgentBuilder()
///         .WithProvider("openrouter", "gemini")
///         .Build()
/// );
/// </code>
/// </para>
/// </remarks>
public static class AspNetCoreExtensions
{
    /// <summary>
    /// Maps an SSE (Server-Sent Events) endpoint for streaming agent events.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The URL pattern for the endpoint.</param>
    /// <param name="agentFactory">Factory function that creates the agent from services.</param>
    /// <returns>The endpoint convention builder for further configuration.</returns>
    /// <remarks>
    /// <para>
    /// This method creates a POST endpoint that:
    /// - Accepts a JSON agent input event
    /// - Streams agent events as SSE
    /// - Uses standard event serialization format
    /// </para>
    /// <para>
    /// <b>Request format:</b>
    /// <code>
    /// POST /agent/stream
    /// Content-Type: application/json
    ///
    /// {
    ///   "version": "1.0",
    ///   "type": "USER_TEXT_INPUT",
    ///   "text": "Hello, agent!"
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <b>Response format:</b>
    /// <code>
    /// Content-Type: text/event-stream
    ///
    /// data: {"version":"1.0","type":"TEXT_DELTA","text":"Hello",...}
    ///
    /// data: {"version":"1.0","type":"MESSAGE_TURN_FINISHED",...}
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Simple usage
    /// app.MapAgentSseEndpoint("/agent/stream", sp =>
    ///     new AgentBuilder()
    ///         .WithProvider("anthropic", "claude-3-sonnet")
    ///         .Build()
    /// );
    ///
    /// // With DI
    /// app.MapAgentSseEndpoint("/chat", sp =>
    ///     sp.GetRequiredService&lt;IAgentFactory&gt;().CreateAgent()
    /// );
    /// </code>
    /// </example>
    public static object MapAgentSseEndpoint(
        this object endpoints,
        string pattern,
        Func<IServiceProvider, Agent> agentFactory)
    {
        // Note: This is a simplified version that returns an object.
        // The actual implementation requires ASP.NET Core references.
        // See the HPD-Agent.AspNetCore package for the full implementation.
        throw new NotImplementedException(
            "This method requires ASP.NET Core. " +
            "Use the MapAgentSseEndpointCore extension method or implement the endpoint manually.");
    }
}

/// <summary>
/// Helper class for manually implementing SSE endpoints.
/// </summary>
/// <remarks>
/// Use this when you need more control over the SSE streaming process.
/// </remarks>
public static class SseHelper
{
    /// <summary>
    /// Streams agent events as SSE data lines.
    /// </summary>
    /// <param name="agent">The agent to run.</param>
    /// <param name="input">The input event to submit to the agent.</param>
    /// <param name="writeAsync">Async function to write SSE data.</param>
    /// <param name="flushAsync">Async function to flush the response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when streaming is finished.</returns>
    /// <example>
    /// <code>
    /// // In an ASP.NET Core minimal API
    /// app.MapPost("/agent/stream", async (HttpContext context, UserTextInputEvent input) =>
    /// {
    ///     context.Response.ContentType = "text/event-stream";
    ///     context.Response.Headers.CacheControl = "no-cache";
    ///
    ///     var agent = CreateAgent();
    ///     await SseHelper.StreamEventsAsync(
    ///         agent,
    ///         input,
    ///         data => context.Response.WriteAsync($"data: {data}\n\n"),
    ///         () => context.Response.Body.FlushAsync(),
    ///         context.RequestAborted
    ///     );
    /// });
    /// </code>
    /// </example>
    public static async Task StreamEventsAsync(
        Agent agent,
        AgentInputEvent input,
        Func<string, Task> writeAsync,
        Func<Task> flushAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(writeAsync);
        ArgumentNullException.ThrowIfNull(flushAsync);

        using var subscription = agent.SubscribeAny((Func<AgentEvent, Task>)(async evt =>
        {
            var json = AgentEventSerializer.ToJson(evt);
            await writeAsync($"data: {json}\n\n");
            await flushAsync();
        }));

        await agent.RunAsync(input, cancellationToken);
    }

    /// <summary>
    /// Streams agent events as SSE with a custom serializer.
    /// </summary>
    /// <param name="agent">The agent to run.</param>
    /// <param name="input">The input event to submit to the agent.</param>
    /// <param name="writeAsync">Async function to write SSE data.</param>
    /// <param name="flushAsync">Async function to flush the response.</param>
    /// <param name="eventSerializer">Custom event serializer function.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task StreamEventsAsync(
        Agent agent,
        AgentInputEvent input,
        Func<string, Task> writeAsync,
        Func<Task> flushAsync,
        Func<AgentEvent, string> eventSerializer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(writeAsync);
        ArgumentNullException.ThrowIfNull(flushAsync);
        ArgumentNullException.ThrowIfNull(eventSerializer);

        using var subscription = agent.SubscribeAny((Func<AgentEvent, Task>)(async evt =>
        {
            var json = eventSerializer(evt);
            await writeAsync($"data: {json}\n\n");
            await flushAsync();
        }));

        await agent.RunAsync(input, cancellationToken);
    }
}
