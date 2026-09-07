using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Commands;

public sealed class AgentTuiCommandContext
{
    public AgentTuiCommandContext(
        AgentTuiExecutionTarget target,
        ChatShellModel shell,
        AgentTuiNavigationModel navigation,
        IHpdAgentTuiRuntime runtime,
        IAgentTuiDialogService dialogs,
        Func<AgentTuiExecutionTarget, CancellationToken, ValueTask> switchTargetAsync,
        HpdAgentTuiCommandDescriptor command,
        string arguments)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Scope = target.Scope;
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        SwitchTargetAsync = switchTargetAsync ?? throw new ArgumentNullException(nameof(switchTargetAsync));
        Command = command ?? throw new ArgumentNullException(nameof(command));
        Arguments = arguments ?? "";
    }

    public AgentTuiExecutionTarget Target { get; }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiNavigationModel Navigation { get; }

    public IHpdAgentTuiRuntime Runtime { get; }

    public IAgentTuiDialogService Dialogs { get; }

    public Func<AgentTuiExecutionTarget, CancellationToken, ValueTask> SwitchTargetAsync { get; }

    // Compatibility bridge for console commands authored before execution targets
    // replaced direct runtime scopes.
    public Func<AgentTuiRuntimeScope, CancellationToken, ValueTask> SwitchScopeAsync
        => (scope, cancellationToken) =>
            SwitchTargetAsync(new DirectAgentTuiExecutionTarget(scope), cancellationToken);

    public HpdAgentTuiCommandDescriptor Command { get; }

    public string Arguments { get; }
}
