using HPD.Agent.TUI.Models;
using HPD.TUI.Core;
using HPD.TUI.Views;
using HPD.TUI.Rendering;

namespace HPD.Agent.TUI.Composition;

/// <summary>Creates a complete agent shell with mandatory preparation and history-publication ownership.</summary>
public interface IAgentTuiShellLayout
{
    /// <summary>Creates the shell presentation for a conversation and its configured contributions.</summary>
    /// <param name="context">Conversation models, prompt, registry, and chrome shared by the shell.</param>
    IAgentTuiShellView Create(AgentTuiShellLayoutContext context);
}

/// <summary>Owns an agent shell's rendering, frame preparation, and terminal-history lifecycle.</summary>
/// <remarks>Shell wrappers must forward these contracts; history publication cannot be an optional runtime cast.</remarks>
public interface IAgentTuiShellView : IComponent, IAgentTuiFramePreparable, IScrollbackSource
{
}

public sealed class AgentTuiShellLayoutContext
{
    public AgentTuiShellLayoutContext(
        ChatShellModel shell,
        PromptView prompt,
        HpdAgentTuiRegistry registry,
        AgentTuiShellChrome chrome)
        : this(shell, prompt, registry, chrome, new AgentTuiStateBag())
    {
    }

    public AgentTuiShellLayoutContext(
        ChatShellModel shell,
        PromptView prompt,
        HpdAgentTuiRegistry registry,
        AgentTuiShellChrome chrome,
        AgentTuiStateBag state)
    {
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public ChatShellModel Shell { get; }

    public PromptView Prompt { get; }

    public HpdAgentTuiRegistry Registry { get; }

    public AgentTuiShellChrome Chrome { get; }

    public AgentTuiStateBag State { get; }
}
