using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations;

internal sealed class FileMutationTuiState
{
    public const string StateKey = "hpd.coding.file-mutations";
    private const int MaxRecent = 50;

    private readonly List<FileMutationTranscriptModel> _recent = [];
    private readonly Dictionary<string, FileMutationTranscriptModel> _latestByPath = new(StringComparer.Ordinal);

    public int MutationCount { get; private set; }

    public int AddedLines { get; private set; }

    public int RemovedLines { get; private set; }

    public string? LatestPath { get; private set; }

    public IReadOnlyList<FileMutationTranscriptModel> Recent => _recent;

    public FileMutationTranscriptModel Add(FileMutationAppliedEvent evt)
    {
        var model = new FileMutationTranscriptModel(evt);
        MutationCount++;
        AddedLines += evt.DiffStat.AddedLines;
        RemovedLines += evt.DiffStat.RemovedLines;
        LatestPath = evt.DisplayPath;

        _recent.Insert(0, model);
        while (_recent.Count > MaxRecent)
        {
            _recent.RemoveAt(_recent.Count - 1);
        }

        _latestByPath[NormalizePath(evt.Path)] = model;
        _latestByPath[NormalizePath(evt.DisplayPath)] = model;
        return model;
    }

    public bool TryGetLatestByPath(string path, out FileMutationTranscriptModel model)
        => _latestByPath.TryGetValue(NormalizePath(path), out model!);

    public static string NormalizePath(string path)
        => string.IsNullOrWhiteSpace(path)
            ? ""
            : path.Replace('\\', '/').Trim();
}

internal sealed class FileMutationTranscriptModel
{
    public FileMutationTranscriptModel(FileMutationAppliedEvent mutation)
    {
        Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
        EntryKey = FileMutationTranscriptEntryFactory.EntryKey(mutation);
    }

    public FileMutationAppliedEvent Mutation { get; }

    public string EntryKey { get; }

    public LanguageServerDiagnosticsReceivedEvent? Diagnostics { get; private set; }

    public void SetDiagnostics(LanguageServerDiagnosticsReceivedEvent diagnostics)
    {
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }
}
