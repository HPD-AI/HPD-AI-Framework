using System.ComponentModel;
using System.Text.Json.Serialization;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>The controller that admitted one child execution, independent of thread creation.</summary>
[DurableEvent]
public sealed record SubAgentExecutionControllerEvent(string ExecutionId, ThreadKey Controller) : AgentEvent
{
    public string? OperationId { get; init; }
}

/// <summary>An explicit, immutable result submitted by one controlled execution.</summary>
[DurableEvent]
public sealed record SubAgentResultSubmittedEvent(
    string ExecutionId, string CallId, ThreadKey Controller, string Report) : AgentEvent;

/// <summary>Closed upward communication actions.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(CompleteParentAction), "complete")]
[JsonDerivedType(typeof(AskParentAction), "ask")]
public abstract record ParentAction;

[AIFunctionAction("ask")]
public sealed record AskParentAction(AgentQuestion[] Questions) : ParentAction;

/// <summary>Submit the final report and end this execution.</summary>
[AIFunctionAction("complete")]
public sealed record CompleteParentAction(
    [property: Description("The final report to the parent, including results and verification.")] string Report) : ParentAction;

/// <summary>Communication with the controller of the current child execution.</summary>
public sealed class ParentToolHarness
{
    internal const string CompletionKey = "hpd.subagent.result";

    [AIFunction(Name = "Parent", InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly)]
    [Description("Ask your immediate parent a question with ask, or submit your final result with complete when your delegated work is done. Only complete ends your execution; ask waits for an answer. Ordinary assistant prose does not submit a result. Call complete alone, without other tool calls in the same batch.")]
    public async Task<string> ParentAsync(ParentAction request, FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (request is AskParentAction ask)
            return await ParentQuestions.AskAsync(ask.Questions, context, cancellationToken).ConfigureAwait(false);
        if (request is not CompleteParentAction complete)
            throw new InvalidOperationException("parent_action_invalid");
        ArgumentException.ThrowIfNullOrWhiteSpace(complete.Report);
        var store = context.GetParentSessionStore() ?? throw new InvalidOperationException("parent_store_required");
        var key = new ThreadKey(context.SessionId ?? throw new InvalidOperationException("parent_session_required"),
            context.ThreadId ?? throw new InvalidOperationException("parent_thread_required"));
        var execution = context.ThreadExecutionId ?? throw new InvalidOperationException("parent_execution_required");
        var result = await SubAgentResults.SubmitAsync(store, key, execution, context.FunctionCallId,
            complete.Report, cancellationToken).ConfigureAwait(false);
        context.ResultMetadata.Set(CompletionKey, result);
        return "Final result accepted.";
    }
}

internal static class SubAgentResults
{
    internal static async Task<List<AgentEvent>> ReadAsync(ISessionStore store, ThreadKey key,
        ThreadJournalCursor cursor, CancellationToken cancellationToken)
    {
        var events = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(key,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(cursor.Generation), cursor.SequenceNumber), cancellationToken)
            .ConfigureAwait(false)) events.AddRange(batch.Events);
        return events;
    }

    internal static async Task<SubAgentExecutionControllerEvent?> ControllerAsync(ISessionStore store,
        ThreadKey key, string execution, CancellationToken cancellationToken)
    {
        var head = await store.GetThreadEventHeadAsync(key, cancellationToken).ConfigureAwait(false);
        if (head is null) return null;
        return (await ReadAsync(store, key, head.Cursor, cancellationToken).ConfigureAwait(false))
            .OfType<SubAgentExecutionControllerEvent>().LastOrDefault(e => e.ExecutionId == execution);
    }

    internal static async Task<SubAgentResultSubmittedEvent> SubmitAsync(ISessionStore store, ThreadKey key,
        string execution, string callId, string report, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var head = await store.GetThreadEventHeadAsync(key, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("parent_thread_missing");
            var events = await ReadAsync(store, key, head.Cursor, cancellationToken).ConfigureAwait(false);
            var existing = events.OfType<SubAgentResultSubmittedEvent>().LastOrDefault(e => e.ExecutionId == execution);
            if (existing is not null)
            {
                if (existing.CallId == callId && existing.Report == report) return existing;
                throw new InvalidOperationException("parent_result_already_submitted");
            }
            if (events.OfType<ThreadExecutionStartedEvent>().LastOrDefault()?.ThreadExecutionId != execution ||
                events.OfType<ThreadExecutionFinishedEvent>().Any(e => e.ThreadExecutionId == execution))
                throw new InvalidOperationException("parent_execution_not_active");
            var controller = events.OfType<SubAgentExecutionControllerEvent>().LastOrDefault(e => e.ExecutionId == execution)
                ?? throw new InvalidOperationException("parent_controller_missing");
            var result = new SubAgentResultSubmittedEvent(execution, callId, controller.Controller, report)
            { SessionId = key.SessionId, ThreadId = key.ThreadId, ThreadExecutionId = execution };
            try
            {
                await store.AppendThreadEventsAsync(key, [result], new ThreadAppendCondition(head.Cursor), cancellationToken)
                    .ConfigureAwait(false);
                return result;
            }
            catch (ThreadAppendConflictException) when (attempt < 15) { }
        }
        throw new InvalidOperationException("parent_result_conflict");
    }

    internal static async Task<string?> ReadReportAsync(ISessionStore store, ThreadKey key, string execution,
        CancellationToken cancellationToken)
    {
        var head = await store.GetThreadEventHeadAsync(key, cancellationToken).ConfigureAwait(false);
        if (head is null) return null;
        return (await ReadAsync(store, key, head.Cursor, cancellationToken).ConfigureAwait(false))
            .OfType<SubAgentResultSubmittedEvent>().LastOrDefault(e => e.ExecutionId == execution)?.Report;
    }
}

internal sealed class ParentCommunicationMiddleware : IAgentMiddleware
{
    public async Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        await ParentQuestions.AddPendingContextAsync(context, cancellationToken).ConfigureAwait(false);
        if (context.Options.Tools is null) return;
        var eligible = context.Config?.SessionStore is { } store && context.ThreadExecutionId is { } execution &&
            context.SessionId is { } session && context.ThreadId is { } thread &&
            await SubAgentResults.ControllerAsync(store, new(session, thread), execution, cancellationToken).ConfigureAwait(false) is not null;
        context.Options.Tools = context.Options.Tools
            .Where(t => (t.Name != "Parent" || eligible) && (t.Name != "AskUser" || context.RunConfig.AllowUserQuestions)).ToList();
    }

    public Task BeforeToolExecutionAsync(BeforeToolExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.ToolCalls.Count > 1 && context.ToolCalls.Any(t => t.Name == "Parent"))
        {
            context.SkipToolExecution = true;
            context.OverrideResponse = new ChatMessage(ChatRole.Assistant,
                "Parent actions must be called alone; no tools in this batch were executed.");
        }
        return Task.CompletedTask;
    }

    public Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
    {
        if (context.Exception is null && context.ResultMetadata.TryGet<SubAgentResultSubmittedEvent>(
            ParentToolHarness.CompletionKey, out _))
            context.UpdateState(s => s with { IsTerminated = true });
        return Task.CompletedTask;
    }
}
