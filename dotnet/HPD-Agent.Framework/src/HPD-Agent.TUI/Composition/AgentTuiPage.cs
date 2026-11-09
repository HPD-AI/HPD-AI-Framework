using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.Agent.TUI.Composition;

public sealed class AgentTuiPageContext
{
    private readonly AgentTuiStateBag _state;

    public AgentTuiPageContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        AgentTuiNavigationModel navigation,
        HpdAgentTuiRegistry registry,
        HpdAgentTuiPageDescriptor page,
        int height,
        int width = 80,
        Theme? theme = null,
        ColorSystem colorSystem = ColorSystem.TrueColor)
        : this(scope, shell, navigation, registry, page, height, new AgentTuiStateBag(), width, theme, colorSystem)
    {
    }

    internal AgentTuiPageContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        AgentTuiNavigationModel navigation,
        HpdAgentTuiRegistry registry,
        HpdAgentTuiPageDescriptor page,
        int height,
        AgentTuiStateBag state,
        int width = 80,
        Theme? theme = null,
        ColorSystem colorSystem = ColorSystem.TrueColor)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Page = page ?? throw new ArgumentNullException(nameof(page));
        Height = height;
        _state = state ?? throw new ArgumentNullException(nameof(state));
        Width = Math.Max(1, width);
        Theme = theme ?? HPD.TUI.Core.Theme.Default;
        ColorSystem = colorSystem;
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiNavigationModel Navigation { get; }

    public HpdAgentTuiRegistry Registry { get; }

    /// <summary>Gets the runtime available to host-authored page actions.</summary>
    public IHpdAgentTuiRuntime? Runtime => Shell.Runtime;

    /// <summary>Gets the supported target-switching operation, when the host provides one.</summary>
    public Func<AgentTuiExecutionTarget, CancellationToken, ValueTask>? SwitchTargetAsync => Shell.SwitchTargetAsync;

    /// <summary>Gets the direct-scope switching convenience operation, when supported.</summary>
    public Func<AgentTuiRuntimeScope, CancellationToken, ValueTask>? SwitchScopeAsync => Shell.SwitchScopeAsync;

    public HpdAgentTuiPageDescriptor Page { get; }

    public int Height { get; }

    public IAgentTuiStateStore State => _state;

    internal AgentTuiStateBag StateBag => _state;

    /// <summary>Gets the exact page width prepared for the next frame.</summary>
    public int Width { get; }
    /// <summary>Gets the immutable theme prepared for the next frame.</summary>
    public Theme Theme { get; }
    /// <summary>Gets the terminal color system prepared for the next frame.</summary>
    public ColorSystem ColorSystem { get; }
}

/// <summary>Prepares width- and theme-dependent agent TUI projections before component measurement.</summary>
public interface IAgentTuiFramePreparable
{
    /// <summary>Prepares immutable projections for the next dirty frame.</summary>
    void PrepareFrame(TerminalSize size, Theme theme, ColorSystem colorSystem);
}

public sealed class HpdAgentTuiPageDescriptor
{
    public HpdAgentTuiPageDescriptor(
        string id,
        Func<AgentTuiPageContext, IComponent> render)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Title = id;
        Render = render ?? throw new ArgumentNullException(nameof(render));
    }

    public string Id { get; }

    public string Title { get; init; }

    public string? Description { get; init; }

    public bool Hidden { get; init; }

    public Func<AgentTuiPageContext, IComponent> Render { get; }

    public Func<AgentTuiPageContext, KeyEvent, bool>? HandleInput { get; init; }
}
