using HPD.Agent;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Composition;

public interface IAgentTuiSessionUi
{
    void SetStatus(string key, string? text);

    void SetTemporaryWidget(
        string key,
        IAgentTuiWidget? widget,
        TuiSlot slot = TuiSlot.AboveEditor);

    Task<bool> ConfirmAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default);

    Task<string?> PromptAsync(
        string title,
        string? placeholder = null,
        CancellationToken cancellationToken = default);

    void Notify(
        string message,
        TranscriptSeverity severity = TranscriptSeverity.Info);

    void SetWorkingMessage(string? message);

    ValueTask SetPromptDraftAsync(
        string text,
        CancellationToken cancellationToken = default);

    void RequestRender();
}

public sealed class AgentTuiSessionUiContext
{
    public required HpdContributionOwner Owner { get; init; }

    public required AgentTuiRuntimeScope Scope { get; init; }

    public required ChatShellModel Shell { get; init; }

    public required AgentTuiStateBag State { get; init; }

    public required HpdAgentTuiRegistry Registry { get; init; }
}
