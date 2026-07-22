using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.LanguageServers;

internal sealed class CodingLanguageServerTuiState
{
    public const string StateKey = "hpd.coding.language-servers";

    public IReadOnlyList<LanguageServerStatusSnapshot> Servers { get; private set; } = [];

    public void Replace(IReadOnlyList<LanguageServerStatusSnapshot> servers)
        => Servers = servers
            .OrderBy(static server => server.ServerId, StringComparer.Ordinal)
            .ThenBy(static server => server.Root, StringComparer.Ordinal)
            .ToArray();
}
