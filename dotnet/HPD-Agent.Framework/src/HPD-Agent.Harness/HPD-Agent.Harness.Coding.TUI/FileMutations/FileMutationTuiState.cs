using HPD.Agent.TUI.Composition;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations;

internal sealed class FileMutationTuiState : IAgentTuiProjectionState
{
    public const string StateKey = "hpd.coding.file-mutations";
    private const int MaxRecent = 50;

    private readonly List<FileMutationTranscriptModel> _recent = [];
    private readonly Dictionary<string, FileMutationTranscriptModel> _byEntryKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileMutationTranscriptModel> _latestByPath = new(StringComparer.Ordinal);
    private readonly HashSet<string> _changedPaths = new(StringComparer.Ordinal);

    public int MutationCount { get; private set; }

    public int ChangedPathCount => _changedPaths.Count;

    public int AddedLines { get; private set; }

    public int RemovedLines { get; private set; }

    public string? LatestPath { get; private set; }

    public IReadOnlyList<FileMutationTranscriptModel> Recent => _recent;

    public FileMutationTranscriptModel Add(FileMutationAppliedEvent evt)
    {
        var model = new FileMutationTranscriptModel(evt);
        if (_byEntryKey.TryGetValue(model.EntryKey, out var existing))
        {
            return existing;
        }

        _byEntryKey[model.EntryKey] = model;
        MutationCount++;
        AddedLines += evt.DiffStat.AddedLines;
        RemovedLines += evt.DiffStat.RemovedLines;
        LatestPath = evt.DisplayPath;
        _changedPaths.Add(NormalizePath(evt.Path));

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

    public object Fork()
    {
        var fork = new FileMutationTuiState
        {
            MutationCount = MutationCount,
            AddedLines = AddedLines,
            RemovedLines = RemovedLines,
            LatestPath = LatestPath
        };
        var copies = new Dictionary<FileMutationTranscriptModel, FileMutationTranscriptModel>();
        foreach (var model in _recent)
        {
            var copy = (FileMutationTranscriptModel)model.Fork();
            copies[model] = copy;
            fork._recent.Add(copy);
        }
        foreach (var pair in _byEntryKey)
            fork._byEntryKey.Add(pair.Key, Copy(pair.Value));
        foreach (var pair in _latestByPath)
            fork._latestByPath.Add(pair.Key, Copy(pair.Value));
        fork._changedPaths.UnionWith(_changedPaths);
        return fork;

        FileMutationTranscriptModel Copy(FileMutationTranscriptModel model)
        {
            if (!copies.TryGetValue(model, out var copy))
            {
                copy = (FileMutationTranscriptModel)model.Fork();
                copies[model] = copy;
            }
            return copy;
        }
    }
}

internal sealed class FileMutationTranscriptModel : IAgentTuiProjectionState
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

    public object Fork()
        => new FileMutationTranscriptModel(Mutation)
        {
            Diagnostics = Diagnostics
        };
}
