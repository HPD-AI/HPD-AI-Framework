using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Views;

public sealed class ShellContributionView : IComponent
{
    private readonly IComponent _component;

    public ShellContributionView(ChatShellModel shell, IAgentTuiShellComponent? contribution)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _component = CreateContribution(shell, contribution);
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
        => _component.Measure(in context, maxWidth);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
        => _component.Render(in context, maxWidth, ref output);

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    private static IComponent CreateContribution(ChatShellModel shell, IAgentTuiShellComponent? contribution)
    {
        if (contribution is null)
        {
            return new Text("");
        }

        try
        {
            return contribution.Create(new AgentTuiShellContext(shell.Scope, shell));
        }
        catch (Exception ex)
        {
            return new Text($"shell contribution failed: {ex.GetType().Name}");
        }
    }
}
