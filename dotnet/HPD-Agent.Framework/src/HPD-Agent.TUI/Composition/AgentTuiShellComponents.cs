using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Composition;

public sealed class DelegateAgentTuiShellComponent : IAgentTuiShellComponent
{
    private readonly Func<AgentTuiShellContext, IComponent> _render;

    public DelegateAgentTuiShellComponent(Func<AgentTuiShellContext, IComponent> render)
    {
        _render = render ?? throw new ArgumentNullException(nameof(render));
    }

    public IComponent Create(AgentTuiShellContext context)
        => _render(context);
}

public sealed class DefaultHeaderShellComponent : IAgentTuiShellComponent
{
    public IComponent Create(AgentTuiShellContext context)
        => new ShellText(context, static shell => shell.HeaderText);
}

public sealed class DefaultFooterShellComponent : IAgentTuiShellComponent
{
    public IComponent Create(AgentTuiShellContext context)
        => new ShellText(context, static shell => shell.FooterText);
}

/// <summary>Renders the shell's agent-owned status immediately above the prompt.</summary>
public sealed class DefaultPromptStatusShellComponent : IAgentTuiShellComponent
{
    public IComponent Create(AgentTuiShellContext context)
        => new ShellText(context, static shell => shell.PromptStatusText);
}

internal sealed class ShellText : Component
{
    private readonly AgentTuiShellContext _context;
    private readonly Func<Models.ChatShellModel, string> _resolve;

    public ShellText(AgentTuiShellContext context, Func<Models.ChatShellModel, string> resolve)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        var value = _resolve(_context.Shell);
        var width = Math.Min(maxWidth, value.Length);
        return new Measurement(width, width);
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        var value = _resolve(_context.Shell);
        output.Write(value.AsSpan(0, Math.Min(value.Length, maxWidth)), context.Theme.Text);
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        return false;
    }
}
