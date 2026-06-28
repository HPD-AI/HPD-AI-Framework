using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;

namespace HPD.Agent.TUI.Application;

public sealed class AgentTuiSessionState
{
    private readonly AgentTuiStateBag _state;

    public AgentTuiSessionState(AgentTuiRuntimeScope scope)
        : this(scope, new ChatShellModel(scope), new AgentTuiStateBag())
    {
    }

    public AgentTuiSessionState(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        AgentTuiStateBag state)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiStateBag State => _state;

    public void AppendUserInput(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Shell.Transcript.AddFinal(new TranscriptEntry(
            Id: $"user-{Guid.NewGuid():N}",
            EntryKey: null,
            Cell: new UserMessageCell(new Text(text)),
            Metadata: new TranscriptEntryMetadata(
                AgentId: Scope.AgentId,
                AgentName: "user")));
    }

    public async ValueTask ApplyEventAsync(
        AgentEvent evt,
        HpdAgentTuiRegistry registry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(registry);

        var context = new AgentTuiEventContext(Scope, Shell, Shell.Navigation, registry, _state);
        foreach (var handler in registry.FindEventHandlers(evt))
        {
            try
            {
                await handler.Value.HandleAsync(evt, context, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Shell.Transcript.AddFinal(new TranscriptEntry(
                    Id: $"event-handler-error-{Guid.NewGuid():N}",
                    EntryKey: null,
                    Cell: new NoticeCell(
                        $"Event handler '{handler.Key}' failed",
                        new Text(ex.Message),
                        TranscriptSeverity.Error),
                    Metadata: new TranscriptEntryMetadata(
                        AgentId: Scope.AgentId,
                        AgentName: "tui",
                        AgentChain: ["tui"])));
            }
        }
    }
}
