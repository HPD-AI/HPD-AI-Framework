using HPD.Agent.TUI.Views;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Composition;

public sealed class DefaultAgentTuiShellLayout : IAgentTuiShellLayout
{
    /// <inheritdoc />
    public IAgentTuiShellView Create(AgentTuiShellLayoutContext context)
        => new DefaultAgentTuiShellView(context);
}
