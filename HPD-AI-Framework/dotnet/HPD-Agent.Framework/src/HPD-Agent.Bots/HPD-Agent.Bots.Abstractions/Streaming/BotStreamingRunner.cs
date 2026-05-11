using System.Text;
using HPD.Agent;
using HPD.Agent.Bots.Cards;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Events;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Bots.Streaming;

/// <summary>Describes one platform adapter stream into an HPD agent.</summary>
/// <param name="AgentId">Agent definition ID to run.</param>
/// <param name="SessionId">Session scope for the user input.</param>
/// <param name="BranchId">Branch scope for the user input.</param>
/// <param name="Text">Plain user input text sent to the agent.</param>
/// <param name="Context">Platform-specific state used by callbacks.</param>
/// <param name="Strategy">How streamed agent output is delivered to the platform.</param>
/// <param name="DebounceMs">Minimum time between streaming text update callbacks.</param>
/// <param name="Attachments">Optional binary user attachments for the agent turn.</param>
/// <param name="RunConfig">Optional runtime configuration for this agent turn.</param>
public sealed record BotStreamingRequest<TContext>(
    string AgentId,
    string SessionId,
    string BranchId,
    string Text,
    TContext Context,
    StreamingStrategy Strategy,
    int DebounceMs,
    IReadOnlyList<DataContent>? Attachments = null,
    AgentRunConfig? RunConfig = null);

/// <summary>
/// Platform-specific operations used by <see cref="BotStreamingRunner"/> while
/// consuming the shared agent event stream.
/// </summary>
public sealed class BotStreamingCallbacks<TContext>
{
    /// <summary>Runs after the stream lock is acquired and before the agent starts.</summary>
    public Func<TContext, CancellationToken, Task>? InitializeAsync { get; init; }

    /// <summary>Applies a debounced text update to the platform message.</summary>
    public required Func<TContext, string, CancellationToken, Task> UpdateTextAsync { get; init; }

    /// <summary>Applies the final text update. Defaults to <see cref="UpdateTextAsync"/>.</summary>
    public Func<TContext, string, CancellationToken, Task>? CompleteTextAsync { get; init; }

    /// <summary>Applies the final structured card representation.</summary>
    public required Func<TContext, CardElement, CancellationToken, Task> CompleteCardAsync { get; init; }

    /// <summary>Handles an agent permission request, if the platform supports it.</summary>
    public Func<TContext, Agent, PermissionRequestEvent, CancellationToken, Task>? HandlePermissionAsync { get; init; }
}

/// <summary>
/// Runs the shared adapter streaming loop while platform adapters supply the
/// transport-specific message operations.
/// </summary>
public sealed class BotStreamingRunner(
    SessionManager sessionManager,
    AgentManager agentManager)
{
    /// <summary>
    /// Runs the agent stream and returns <c>false</c> when another stream already
    /// holds the same session/branch lock.
    /// </summary>
    public async Task<bool> RunAsync<TContext>(
        BotStreamingRequest<TContext> request,
        BotStreamingCallbacks<TContext> callbacks,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(callbacks);

        if (!sessionManager.TryAcquireStreamLock(request.SessionId, request.BranchId))
            return false;

        try
        {
            if (callbacks.InitializeAsync is not null)
                await callbacks.InitializeAsync(request.Context, ct);

            var agent = await agentManager.GetOrBuildAgentAsync(request.AgentId, ct);
            var buffer = new StringBuilder();
            using var debounce = new BotDebounceTimer(request.DebounceMs);

            await using var subscription = agent.EventCoordinator.SubscribeStream<AgentEvent>();

            async Task HandleEventAsync(AgentEvent evt)
            {
                switch (evt)
                {
                    case TextDeltaEvent delta:
                        buffer.Append(delta.Text);
                        if (request.Strategy != StreamingStrategy.BufferAndPost)
                        {
                            debounce.Schedule(async () =>
                                await callbacks.UpdateTextAsync(request.Context, buffer.ToString(), ct));
                        }
                        break;

                    case TextMessageEndEvent:
                        debounce.Cancel();
                        if (callbacks.CompleteTextAsync is not null)
                            await callbacks.CompleteTextAsync(request.Context, buffer.ToString(), ct);
                        else
                            await callbacks.UpdateTextAsync(request.Context, buffer.ToString(), ct);
                        buffer.Clear();
                        break;

                    case CardContentEvent card:
                        debounce.Cancel();
                        await callbacks.CompleteCardAsync(request.Context, card.Card, ct);
                        break;

                    case PermissionRequestEvent permission:
                        if (callbacks.HandlePermissionAsync is not null)
                            await callbacks.HandlePermissionAsync(request.Context, agent, permission, ct);
                        break;
                }
            }

            var runTask = RunAgentAsync(agent, request, ct);
            await DrainEventsUntilRunCompletesAsync(subscription, runTask, HandleEventAsync, ct);
            await runTask.ConfigureAwait(false);

            return true;
        }
        finally
        {
            sessionManager.ReleaseStreamLock(request.SessionId, request.BranchId);
        }
    }

    private static async Task RunAgentAsync<TContext>(
        Agent agent,
        BotStreamingRequest<TContext> request,
        CancellationToken ct)
    {
        if (request.Attachments is { Count: > 0 })
        {
            var contents = new List<AIContent>();
            if (!string.IsNullOrWhiteSpace(request.Text))
                contents.Add(new TextContent(request.Text));
            contents.AddRange(request.Attachments);

            var runConfig = request.RunConfig ?? new AgentRunConfig();
            runConfig.UserMessage ??= request.Text;
            runConfig.Attachments ??= request.Attachments;

            await agent.RunAsync(new UserMessagesInputEvent([new ChatMessage(ChatRole.User, contents)])
            {
                AgentId = request.AgentId,
                SessionId = request.SessionId,
                BranchId = request.BranchId,
                RunConfig = runConfig,
            }, ct).ConfigureAwait(false);
        }
        else
        {
            await agent.RunAsync(new UserTextInputEvent(request.Text)
            {
                AgentId = request.AgentId,
                SessionId = request.SessionId,
                BranchId = request.BranchId,
                RunConfig = request.RunConfig,
            }, ct).ConfigureAwait(false);
        }
    }

    private static async Task DrainEventsUntilRunCompletesAsync(
        EventStreamSubscription<AgentEvent> subscription,
        Task runTask,
        Func<AgentEvent, Task> handleEventAsync,
        CancellationToken ct)
    {
        while (true)
        {
            while (subscription.Reader.TryRead(out var evt))
                await handleEventAsync(evt).ConfigureAwait(false);

            if (runTask.IsCompleted)
                return;

            var waitForEventTask = subscription.Reader.WaitToReadAsync(ct).AsTask();
            var completed = await Task.WhenAny(runTask, waitForEventTask).ConfigureAwait(false);
            if (completed == runTask)
            {
                await runTask.ConfigureAwait(false);
                continue;
            }

            if (!await waitForEventTask.ConfigureAwait(false))
                return;
        }
    }
}
