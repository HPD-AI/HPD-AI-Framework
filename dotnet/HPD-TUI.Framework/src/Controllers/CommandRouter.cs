using HPD.TUI.Core;
using HPD.TUI.Models;

namespace HPD.TUI.Controllers;

public sealed class CommandRouter
{
    private readonly CommandModel _model;

    public CommandRouter(CommandModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public bool TryExecute(ReadOnlySpan<char> commandLine)
    {
        var trimmed = commandLine.Trim();
        if (trimmed.IsEmpty)
        {
            return false;
        }

        if (trimmed[0] == '/')
        {
            trimmed = trimmed[1..];
        }

        var split = trimmed.IndexOf(' ');
        var name = split < 0 ? trimmed : trimmed[..split];
        var args = split < 0 ? ReadOnlySpan<char>.Empty : trimmed[(split + 1)..].Trim();

        if (!TryFind(name, includeHidden: true, out var command))
        {
            return false;
        }

        command.Execute(new CommandContext(command, args.ToString().AsMemory()));
        return true;
    }

    public bool TryHandleKey(in KeyEvent key)
    {
        var gesture = new KeyGesture(key.Key, key.Modifiers);
        if (!_model.KeyBindings.TryGetValue(gesture, out var commandName))
        {
            return false;
        }

        if (!TryFind(commandName.AsSpan(), includeHidden: true, out var command))
        {
            return false;
        }

        command.Execute(new CommandContext(command, ReadOnlyMemory<char>.Empty));
        return true;
    }

    public int Complete(ReadOnlySpan<char> prefix, Span<CommandDescriptor> destination)
    {
        var count = 0;
        var normalized = prefix.TrimStart('/');

        foreach (var command in _model.Commands)
        {
            if (count >= destination.Length)
            {
                break;
            }

            if (command.Hidden)
            {
                continue;
            }

            if (MatchesPrefix(command, normalized))
            {
                destination[count++] = command;
            }
        }

        return count;
    }

    private bool TryFind(ReadOnlySpan<char> name, bool includeHidden, out CommandDescriptor command)
    {
        foreach (var candidate in _model.Commands)
        {
            if (!includeHidden && candidate.Hidden)
            {
                continue;
            }

            if (candidate.Name.AsSpan().Equals(name, StringComparison.OrdinalIgnoreCase) ||
                candidate.SlashName.AsSpan().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                command = candidate;
                return true;
            }

            foreach (var alias in candidate.Aliases)
            {
                if (alias.AsSpan().Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    command = candidate;
                    return true;
                }
            }
        }

        command = null!;
        return false;
    }

    private static bool MatchesPrefix(CommandDescriptor command, ReadOnlySpan<char> prefix)
    {
        if (command.Name.AsSpan().StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            command.SlashName.AsSpan().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var alias in command.Aliases)
        {
            if (alias.AsSpan().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
