using HPDOS.Shell.Cli.TUI;
using Spectre.Console;

namespace HPDOS.Shell.Cli.Commands;

public static class VersionCommand
{
    public static int Run()
    {
        SpectreConsoleSession.CreateDefault().MarkupLine("[bold]hpdos[/] v0.1.0");
        return 0;
    }
}
