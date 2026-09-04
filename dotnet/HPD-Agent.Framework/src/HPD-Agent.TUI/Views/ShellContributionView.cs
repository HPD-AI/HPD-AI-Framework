using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Views;

public sealed class ShellContributionView : Component
{
    private readonly IComponent _component;

    public ShellContributionView(ChatShellModel shell, IAgentTuiShellComponent? contribution)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _component = CreateContribution(shell, contribution);
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
        => _component.Measure(in context, constraints);

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
        => output.Render(_component, in context, output.MaxWidth);

    public override bool HandleInput(in TuiInputEvent key)
    {
        return _component.HandleInput(in key);
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
