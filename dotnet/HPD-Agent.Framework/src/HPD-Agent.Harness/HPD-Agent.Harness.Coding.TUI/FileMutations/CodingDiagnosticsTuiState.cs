using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations;

internal sealed class CodingDiagnosticsTuiState
{
    public const string StateKey = "hpd.coding.diagnostics";

    private readonly Dictionary<string, LanguageServerDiagnosticsReceivedEvent> _latestByPath = new(StringComparer.Ordinal);

    public int ErrorCount { get; private set; }

    public int WarningCount { get; private set; }

    public int InformationCount { get; private set; }

    public int HintCount { get; private set; }

    public string? LatestPath { get; private set; }

    public IReadOnlyDictionary<string, LanguageServerDiagnosticsReceivedEvent> LatestByPath => _latestByPath;

    public void Update(LanguageServerDiagnosticsReceivedEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        ErrorCount = evt.ErrorCount;
        WarningCount = evt.WarningCount;
        InformationCount = evt.InformationCount;
        HintCount = evt.HintCount;
        LatestPath = evt.Path;
        _latestByPath[FileMutationTuiState.NormalizePath(evt.Path)] = evt;
    }
}
