using HPDOS.Shell.Cli.Commands;
using HPDOS.Shell.Shell;
using Spectre.Console;

namespace HPDOS.Shell.Cli;

public static class CliRouter
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
            return ChatCommand.RunAsync([]);

        return args[0] switch
        {
            "version" or "--version" or "-v" => Task.FromResult(VersionCommand.Run()),
            "help"    or "--help"    or "-h" => Task.FromResult(PrintHelp()),
            "setup"                          => SetupCommand.RunAsync(args[1..]),
            "chat"                           => ChatCommand.RunAsync(args[1..]),
            "serve"                          => GUIMode.RunServeAsync(args[1..]),
            "providers"                      => ProvidersCommand.RunAsync(args[1..]),
            _ => Task.FromResult(UnknownCommand(args[0]))
        };
    }

    static int PrintHelp()
    {
        AnsiConsole.MarkupLine("[bold]hpdos[/] — HPDOS shell\n");
        AnsiConsole.MarkupLine("  [cyan]hpdos[/]                Start a TUI agent chat session");
        AnsiConsole.MarkupLine("  [cyan]hpdos gui[/]            Open in system browser");
        AnsiConsole.MarkupLine("  [cyan]hpdos providers[/]      Connect or disconnect AI providers");
        AnsiConsole.MarkupLine("  [cyan]hpdos serve[/]          Run as a public server (auth required)");
        AnsiConsole.MarkupLine("  [cyan]hpdos serve --port N[/] Bind on a specific port (default 5000)");
        AnsiConsole.MarkupLine("  [cyan]hpdos setup[/]          Register hpdos to your PATH");
        AnsiConsole.MarkupLine("  [cyan]hpdos version[/]        Print version");
        AnsiConsole.MarkupLine("  [cyan]hpdos help[/]           Show this help\n");
        return 0;
    }

    static int UnknownCommand(string cmd)
    {
        AnsiConsole.MarkupLine($"[red]Unknown command:[/] {cmd}");
        AnsiConsole.MarkupLine("Run [cyan]hpdos help[/] for usage.");
        return 1;
    }
}
