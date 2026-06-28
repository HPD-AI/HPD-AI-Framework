using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Interactions;

public sealed class AgentTuiInteractionContext
{
    public AgentTuiInteractionContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        AgentTuiNavigationModel navigation,
        IHpdAgentTuiRuntime runtime,
        IAgentTuiDialogService dialogs,
        IAgentTuiSessionUi sessionUi,
        AgentEvent request)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        SessionUi = sessionUi ?? throw new ArgumentNullException(nameof(sessionUi));
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiNavigationModel Navigation { get; }

    public IHpdAgentTuiRuntime Runtime { get; }

    public IAgentTuiDialogService Dialogs { get; }

    public IAgentTuiSessionUi SessionUi { get; }

    public AgentEvent Request { get; }
}

public sealed class AgentTuiInteractionContext<TRequest>
    where TRequest : AgentEvent
{
    public AgentTuiInteractionContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        AgentTuiNavigationModel navigation,
        IHpdAgentTuiRuntime runtime,
        IAgentTuiDialogService dialogs,
        IAgentTuiSessionUi sessionUi,
        TRequest request)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        SessionUi = sessionUi ?? throw new ArgumentNullException(nameof(sessionUi));
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiNavigationModel Navigation { get; }

    public IHpdAgentTuiRuntime Runtime { get; }

    public IAgentTuiDialogService Dialogs { get; }

    public IAgentTuiSessionUi SessionUi { get; }

    public TRequest Request { get; }
}
