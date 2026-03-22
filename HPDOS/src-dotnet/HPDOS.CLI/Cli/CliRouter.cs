using HPDOS.Shell.Cli.Commands;
using HPDOS.Shell.Cli.TUI;
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
        var session = SpectreConsoleSession.CreateDefault();
        session.MarkupLine("[bold]hpdos[/] — HPDOS shell\n");
        session.MarkupLine("  [cyan]hpdos[/]                Start a TUI agent chat session");
        session.MarkupLine("  [cyan]hpdos gui[/]            Open in system browser");
        session.MarkupLine("  [cyan]hpdos providers[/]      Connect or disconnect AI providers");
        session.MarkupLine("  [cyan]hpdos serve[/]          Run as a public server (auth required)");
        session.MarkupLine("  [cyan]hpdos serve --port N[/] Bind on a specific port (default 5000)");
        session.MarkupLine("  [cyan]hpdos setup[/]          Register hpdos to your PATH");
        session.MarkupLine("  [cyan]hpdos version[/]        Print version");
        session.MarkupLine("  [cyan]hpdos help[/]           Show this help\n");
        return 0;
    }

    static int UnknownCommand(string cmd)
    {
        var session = SpectreConsoleSession.CreateDefault();
        session.MarkupLine($"[red]Unknown command:[/] {Markup.Escape(cmd)}");
        session.MarkupLine("Run [cyan]hpdos help[/] for usage.");
        return 1;
    }
}
