using HPD.Agent.TUI.Models;
using HPD.TUI.Core;
using HPD.TUI.Views;

namespace HPD.Agent.TUI.Composition;

public interface IAgentTuiShellLayout
{
    IComponent Create(AgentTuiShellLayoutContext context);
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
