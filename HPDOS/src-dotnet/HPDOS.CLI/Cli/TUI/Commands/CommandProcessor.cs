using Spectre.Console;

namespace HPDOS.Shell.Cli.TUI.Commands;

/// <summary>
/// Processes and executes slash commands.
/// </summary>
public class CommandProcessor
{
    private readonly CommandRegistry _registry;
    private readonly AgentUIRenderer _renderer;
    private readonly Dictionary<string, object> _contextData;

    public CommandProcessor(CommandRegistry registry, AgentUIRenderer renderer, Dictionary<string, object>? contextData = null)
    {
        _registry = registry;
        _renderer = renderer;
        _contextData = contextData ?? new Dictionary<string, object>();
    }

    public static bool IsCommand(string input) =>
        !string.IsNullOrWhiteSpace(input) && input.TrimStart().StartsWith('/');

    public async Task<CommandResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        input = input.TrimStart();
        if (!input.StartsWith('/'))
            return CommandResult.Error("Not a valid command");

        var parts = input[1..].Split([' '], 2, StringSplitOptions.None);
        var commandName = parts[0].ToLowerInvariant();
        var arguments = parts.Length > 1 ? parts[1] : "";

        var command = _registry.FindExact(commandName);

        if (command == null)
        {
            AnsiConsole.MarkupLine($"[red]Unknown command:[/] [yellow]{Markup.Escape(commandName)}[/]");
            AnsiConsole.MarkupLine("[dim]Type /help to see available commands[/]");
            return CommandResult.Error($"Unknown command: {commandName}");
        }

        var context = new CommandContext
        {
            RawInput = input,
            CommandName = commandName,
            Arguments = arguments,
            UIRenderer = _renderer,
            Data = _contextData,
            CancellationToken = ct
        };

        try
        {
            if (command.Action == null)
                return CommandResult.Error($"Command '{commandName}' has no action defined");

            var result = await command.Action(context);

            // If Ctrl+C fired during the command, always exit regardless of what the action returned.
            if (ct.IsCancellationRequested)
                return CommandResult.Exit();

            if (!string.IsNullOrEmpty(result.Message))
            {
                if (result.Success)
                    AnsiConsole.MarkupLine($"[green]{Markup.Escape(result.Message)}[/]");
                else
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(result.Message)}[/]");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return CommandResult.Exit();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error executing command:[/] {Markup.Escape(ex.Message)}");
            return CommandResult.Error($"Command execution failed: {ex.Message}");
        }
    }

    public SuggestionManager CreateSuggestionManager() => new(_registry);
}
