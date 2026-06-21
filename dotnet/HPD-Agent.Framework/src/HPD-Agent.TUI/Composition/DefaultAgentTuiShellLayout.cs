using HPD.Agent.TUI.Views;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Composition;

public sealed class DefaultAgentTuiShellLayout : IAgentTuiShellLayout
{
    public IComponent Create(AgentTuiShellLayoutContext context)
        => new DefaultAgentTuiShellView(context);
}
