using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Controllers;
using HPD.TUI.Views;

namespace HPD.Agent.TUI.Composition;

public sealed class AgentTuiPromptContext
{
    public AgentTuiPromptContext(AgentTuiRuntimeScope scope, ChatShellModel shell)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }
}

public interface IAgentTuiPromptFactory
{
    PromptView Create(
        AgentTuiPromptContext context,
        Action<ReadOnlyMemory<char>> submitted,
        AutocompleteController autocomplete);
}

public sealed class DefaultAgentTuiPromptFactory : IAgentTuiPromptFactory
{
    public string Placeholder { get; init; } = "Ask HPD...";

    public bool Multiline { get; init; }

    public PromptView Create(
        AgentTuiPromptContext context,
        Action<ReadOnlyMemory<char>> submitted,
        AutocompleteController autocomplete)
        => PromptView.Create(
            placeholder: Placeholder,
            submitted: submitted,
            autocomplete: autocomplete,
            multiline: Multiline,
            visualCursor: true);
}

public sealed class DelegateAgentTuiPromptFactory : IAgentTuiPromptFactory
{
    private readonly Func<AgentTuiPromptContext, Action<ReadOnlyMemory<char>>, AutocompleteController, PromptView> _create;

    public DelegateAgentTuiPromptFactory(
        Func<AgentTuiPromptContext, Action<ReadOnlyMemory<char>>, AutocompleteController, PromptView> create)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
    }

    public PromptView Create(
        AgentTuiPromptContext context,
        Action<ReadOnlyMemory<char>> submitted,
        AutocompleteController autocomplete)
        => _create(context, submitted, autocomplete);
}
