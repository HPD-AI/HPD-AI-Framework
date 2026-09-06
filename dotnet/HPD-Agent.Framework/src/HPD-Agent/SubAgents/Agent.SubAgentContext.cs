using Microsoft.Extensions.AI;

namespace HPD.Agent;

public sealed partial class Agent
{
    internal async Task<SubAgentContextReceivedEvent> PrepareSubAgentContextAsync(
        ThreadKey parent, CompactionSpecification? specification, CancellationToken cancellationToken,
        ThreadJournalCursor? sourceCursor = null)
    {
        var store = Config.SessionStore ?? throw new InvalidOperationException("Session store required.");
        var descriptor = await store.GetThreadAsync(parent, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("subagent_parent_missing");
        var cursor = sourceCursor ?? new ThreadJournalCursor(descriptor.Generation, descriptor.Head);
        var source = new Thread(parent.SessionId, parent.ThreadId, descriptor.DefaultAgent.AgentId);
        await foreach (var batch in store.ReadThreadEventsAsync(parent,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(cursor.Generation), Through: cursor.SequenceNumber),
            cancellationToken).ConfigureAwait(false))
            ThreadProjector.Apply(source, batch.Events, ThreadProjectionPurpose.ModelContext);

        SubAgentCompactionConfiguration.Validate(specification);
        IReadOnlyList<ChatMessage> messages = source.Messages;
        if (specification is not null)
        {
            await using var lease = specification.Strategy is SummarizingCompaction summarizing
                ? await _chatClientResolver.ResolveAsync(new AgentChatClientResolutionRequest
                {
                    AgentConfig = Config,
                    SpecializedChat = summarizing.Summarizer
                }, cancellationToken).ConfigureAwait(false)
                : null;
            var options = (lease?.Handle.ResolvedConfig as ChatClientConfig)?.ToMicrosoftChatOptions() ?? new ChatOptions();
            var prepared = await new ThreadCompactionEngine().PrepareAsync(
                new ThreadCompactionContext(source, messages, Publisher: null,
                    SummarizerClient: lease?.Client, SummarizerOptions: options,
                    SummarizerIdentity: lease?.Handle.ExecutionIdentity), specification, cancellationToken).ConfigureAwait(false);
            if (prepared is not null) messages = prepared.ResultingMessages;
        }
        return new SubAgentContextReceivedEvent(Guid.NewGuid().ToString("N"),
            CompactionEvidence.Serialize(messages), parent, cursor);
    }
}
