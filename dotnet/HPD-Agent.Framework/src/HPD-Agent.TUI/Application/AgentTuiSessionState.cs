using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;

namespace HPD.Agent.TUI.Application;

public sealed class AgentTuiSessionState
{
    private readonly HpdAgentTuiRegistry _registry;
    private readonly AgentTuiStateBag _state = new();

    public AgentTuiSessionState(
        AgentTuiRuntimeScope scope,
        HpdAgentTuiRegistry registry)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Shell = new ChatShellModel(scope);
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var context = new AgentTuiEventContext(Scope, Shell, Shell.Navigation, _registry, _state);
        foreach (var handler in _registry.FindEventHandlers(evt))
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
