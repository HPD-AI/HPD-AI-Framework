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
/// app.MapAgentLiveEventsEndpoint("/agent/events/live", sp =>
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
    /// Maps an SSE (Server-Sent Events) endpoint for observing live agent events.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The URL pattern for the endpoint.</param>
    /// <param name="agentFactory">Factory function that creates the agent from services.</param>
    /// <returns>The endpoint convention builder for further configuration.</returns>
    /// <remarks>
    /// <para>
    /// This method creates an observer endpoint that:
    /// - Subscribes to live agent events
    /// - Streams observed events as SSE
    /// - Does not submit input or own agent execution
    /// - Uses standard event serialization format
    /// </para>
    /// <para>
    /// <b>Request format:</b>
    /// <code>
    /// GET /agent/events/live
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
    /// app.MapAgentLiveEventsEndpoint("/agent/events/live", sp =>
    ///     new AgentBuilder()
    ///         .WithProvider("anthropic", "claude-3-sonnet")
    ///         .Build()
    /// );
    ///
    /// // With DI
    /// app.MapAgentLiveEventsEndpoint("/chat/events/live", sp =>
    ///     sp.GetRequiredService&lt;IAgentFactory&gt;().CreateAgent()
    /// );
    /// </code>
    /// </example>
    public static object MapAgentLiveEventsEndpoint(
        this object endpoints,
        string pattern,
        Func<IServiceProvider, Agent> agentFactory)
    {
        // Note: This is a simplified version that returns an object.
        // The actual implementation requires ASP.NET Core references.
        // See the HPD-Agent.AspNetCore package for the full implementation.
        throw new NotImplementedException(
            "This method requires ASP.NET Core. " +
            "Use the HPD-Agent.AspNetCore endpoint mapper or implement submit and observe endpoints manually.");
    }
}

/// <summary>
/// Helper class for manually implementing SSE endpoints.
/// </summary>
/// <remarks>
/// Use this when you need more control over the SSE observation process.
/// </remarks>
public static class SseHelper
{
    /// <summary>
    /// Submits input to the agent runtime.
    /// </summary>
    /// <param name="agent">The agent runtime to submit input to.</param>
    /// <param name="input">The input event to submit.</param>
    /// <param name="cancellationToken">Cancellation token for the short submit/enqueue operation.</param>
    public static async Task SubmitInputAsync(
        Agent agent,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(input);

        await agent.StartAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
        await agent.RunAsync(input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Observes agent events as SSE data lines.
    /// </summary>
    /// <param name="agent">The agent to observe.</param>
    /// <param name="writeAsync">Async function to write SSE data.</param>
    /// <param name="flushAsync">Async function to flush the response.</param>
    /// <param name="cancellationToken">Cancellation token for this observer.</param>
    /// <returns>A task that completes when observation is cancelled.</returns>
    /// <example>
    /// <code>
    /// app.MapGet("/agent/events/live", async (HttpContext context) =>
    /// {
    ///     context.Response.ContentType = "text/event-stream";
    ///     context.Response.Headers.CacheControl = "no-cache";
    ///
    ///     var agent = CreateAgent();
    ///     await SseHelper.ObserveEventsAsync(
    ///         agent,
    ///         data => context.Response.WriteAsync($"data: {data}\n\n"),
    ///         () => context.Response.Body.FlushAsync(),
    ///         context.RequestAborted
    ///     );
    /// });
    /// </code>
    /// </example>
    public static Task ObserveEventsAsync(
        Agent agent,
        Func<string, Task> writeAsync,
        Func<Task> flushAsync,
        CancellationToken cancellationToken = default)
        => ObserveEventsAsync(agent, writeAsync, flushAsync, AgentEventSerializer.ToJson, cancellationToken);

    /// <summary>
    /// Observes agent events as SSE with a custom serializer.
    /// </summary>
    /// <param name="agent">The agent to observe.</param>
    /// <param name="writeAsync">Async function to write SSE data.</param>
    /// <param name="flushAsync">Async function to flush the response.</param>
    /// <param name="eventSerializer">Custom event serializer function.</param>
    /// <param name="cancellationToken">Cancellation token for this observer.</param>
    public static async Task ObserveEventsAsync(
        Agent agent,
        Func<string, Task> writeAsync,
        Func<Task> flushAsync,
        Func<AgentEvent, string> eventSerializer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(writeAsync);
        ArgumentNullException.ThrowIfNull(flushAsync);
        ArgumentNullException.ThrowIfNull(eventSerializer);

        using var subscription = agent.SubscribeAny((Func<AgentEvent, Task>)(async evt =>
        {
            var json = eventSerializer(evt);
            await writeAsync($"data: {json}\n\n");
            await flushAsync();
        }));

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }
}
