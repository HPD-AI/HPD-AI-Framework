using HPD.TUI.Core;

namespace HPD.TUI.Models;

public sealed class CommandModel
{
    private readonly List<CommandDescriptor> _commands = [];
    private readonly Dictionary<KeyGesture, string> _keyBindings = [];

    public IReadOnlyList<CommandDescriptor> Commands => _commands;

    public IReadOnlyDictionary<KeyGesture, string> KeyBindings => _keyBindings;

    public CommandModel Register(CommandDescriptor command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Add(command);
        return this;
    }

    public CommandModel Bind(KeyGesture gesture, string commandName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        _keyBindings[gesture] = commandName;
        return this;
    }
}

public sealed class CommandDescriptor
{
    public CommandDescriptor(string name, CommandExecute execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(execute);

        Name = name;
        Execute = execute;
        Title = name;
        SlashName = name;
    }

    public string Name { get; }

    public string Title { get; init; }

    public string? Description { get; init; }

    public string? Category { get; init; }

    public string SlashName { get; init; }

    public IReadOnlyList<string> Aliases { get; init; } = [];

    public bool Hidden { get; init; }

    public bool Suggested { get; init; }

    public CommandExecute Execute { get; }
}

public readonly record struct CommandContext(CommandDescriptor Command, ReadOnlyMemory<char> Arguments);

public delegate void CommandExecute(CommandContext context);

public readonly record struct KeyGesture(KeyCode Key, KeyModifiers Modifiers = KeyModifiers.None);
