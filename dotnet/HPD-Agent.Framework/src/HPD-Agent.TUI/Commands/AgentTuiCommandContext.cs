using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Commands;

public sealed class AgentTuiCommandContext
{
    public AgentTuiCommandContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        AgentTuiNavigationModel navigation,
        IHpdAgentTuiRuntime runtime,
        IAgentTuiDialogService dialogs,
        Func<AgentTuiRuntimeScope, CancellationToken, ValueTask> switchScopeAsync,
        HpdAgentTuiCommandDescriptor command,
        string arguments)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        SwitchScopeAsync = switchScopeAsync ?? throw new ArgumentNullException(nameof(switchScopeAsync));
        Command = command ?? throw new ArgumentNullException(nameof(command));
        Arguments = arguments ?? "";
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiNavigationModel Navigation { get; }

    public IHpdAgentTuiRuntime Runtime { get; }

    public IAgentTuiDialogService Dialogs { get; }

    public Func<AgentTuiRuntimeScope, CancellationToken, ValueTask> SwitchScopeAsync { get; }

    public HpdAgentTuiCommandDescriptor Command { get; }

    public string Arguments { get; }
}
