using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Core;
using System.Text;

namespace HPD.Agent.TUI.Composition;

public readonly record struct KeyGesture(
    KeyCode Key,
    KeyModifiers Modifiers = KeyModifiers.None,
    Rune Character = default)
{
    public bool Matches(in KeyEvent key)
        => key.Key == Key &&
           key.Modifiers == Modifiers &&
           (Key != KeyCode.Character || key.Character.Equals(Character));
}

public sealed class AgentTuiShortcutContext
{
    public AgentTuiShortcutContext(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        AgentTuiNavigationModel navigation,
        HpdAgentTuiShortcutDescriptor shortcut)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Shortcut = shortcut ?? throw new ArgumentNullException(nameof(shortcut));
    }

    public AgentTuiRuntimeScope Scope { get; }

    public ChatShellModel Shell { get; }

    public AgentTuiNavigationModel Navigation { get; }

    public HpdAgentTuiShortcutDescriptor Shortcut { get; }
}

public sealed class HpdAgentTuiShortcutDescriptor
{
    public HpdAgentTuiShortcutDescriptor(
        string key,
        KeyGesture gesture,
        Action<AgentTuiShortcutContext> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Gesture = gesture;
        Execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public string Key { get; }

    public KeyGesture Gesture { get; init; }

    public string? Description { get; init; }

    public Action<AgentTuiShortcutContext> Execute { get; }
}
