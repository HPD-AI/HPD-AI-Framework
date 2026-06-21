using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Composition;

public sealed class AgentTuiPageContext
{
    public AgentTuiPageContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        AgentTuiNavigationModel navigation,
        HpdAgentTuiRegistry registry,
        HpdAgentTuiPageDescriptor page,
        int height)
        : this(scope, shell, navigation, registry, page, height, new AgentTuiStateBag())
    {
    }

    public AgentTuiPageContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        AgentTuiNavigationModel navigation,
        HpdAgentTuiRegistry registry,
        HpdAgentTuiPageDescriptor page,
        int height,
        AgentTuiStateBag state)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Page = page ?? throw new ArgumentNullException(nameof(page));
        Height = height;
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiNavigationModel Navigation { get; }

    public HpdAgentTuiRegistry Registry { get; }

    public HpdAgentTuiPageDescriptor Page { get; }

    public int Height { get; }

    public AgentTuiStateBag State { get; }
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
