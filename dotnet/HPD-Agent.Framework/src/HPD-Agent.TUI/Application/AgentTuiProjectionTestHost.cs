using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Testing;

/// <summary>Drives TUI contributions without starting a terminal or runtime observer.</summary>
public sealed class AgentTuiProjectionTestHost
{
    private readonly Application.AgentTuiSessionState _session;

    public AgentTuiProjectionTestHost(AgentTuiRuntimeScope scope, HpdAgentTuiRegistry registry,
        Action? requestRender = null)
    {
        _session = new(scope, registry, requestRender);
    }

    public ChatShellModel Shell => _session.Shell;
    public IAgentTuiStateStore State => _session.State;
    public IReadOnlyList<string> FailedContributionKeys =>
        _session.ProjectionFailures.Select(failure => failure.ContributionKey).ToArray();

    public ValueTask ApplyEventAsync(AgentEvent evt,
        CancellationToken cancellationToken = default,
        AgentTuiEventDeliveryMode deliveryMode = AgentTuiEventDeliveryMode.Live) =>
        _session.ApplyEventAsync(evt, cancellationToken, deliveryMode);
    public static AgentTuiProjectionContext CreateContext(
        AgentTuiRuntimeScope scope, ChatShellModel shell, HpdAgentTuiRegistry registry,
        IAgentTuiStateStore? state = null, AgentTuiEventDeliveryMode deliveryMode = AgentTuiEventDeliveryMode.Live) =>
        new(scope, shell, registry, state is null ? new AgentTuiStateBag() : (AgentTuiStateBag)state, deliveryMode);

    public static IAgentTuiStateStore CreateState() => new AgentTuiStateBag();

    public static void SetRuntime(ChatShellModel shell, IHpdAgentTuiRuntime runtime) => shell.Runtime = runtime;

    public static void SetTargetSwitch(ChatShellModel shell, Func<AgentTuiExecutionTarget, CancellationToken, ValueTask> callback) =>
        shell.SwitchTargetAsync = callback;

    public static HPD.TUI.Core.IFocusable NextFocus(ChatShellModel shell, HPD.TUI.Core.IFocusable current, HPD.TUI.Core.IFocusable prompt) =>
        shell.WidgetFocus.Next(current, prompt, []);

    public static HPD.TUI.Core.IComponent CreateAboveEditor(ChatShellModel shell, IAgentTuiStateStore state, HpdAgentTuiRegistry registry) =>
        new Views.ContributionWidgetSlotView(TuiSlot.AboveEditor, shell, (AgentTuiStateBag)state, registry.AboveEditorWidgets);

    public AgentTuiShellLayoutContext CreateLayoutContext(HPD.TUI.Views.PromptView prompt, HpdAgentTuiRegistry registry) =>
        new(Shell, prompt, registry, registry.ShellChrome, _session.State);
    public static HPD.TUI.Core.IComponent CreateTranscript(TranscriptModel model, HpdAgentTuiRegistry registry, int height) =>
        new Views.TranscriptView(model, registry.TranscriptRenderers, height);
}
