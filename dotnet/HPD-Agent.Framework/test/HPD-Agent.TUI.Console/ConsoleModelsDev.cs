namespace HPD.Agent.TUI.Console;

internal static class ConsoleModelsDev
{
    public static HpdAgentTuiBuilder AddConsoleModelsDevModelSelection(
        this HpdAgentTuiBuilder tui,
        ConsoleProviderContext providers)
        => tui.AddModelSelection(
            providers.ModelCatalog,
            providers.ModelSelection);
}
