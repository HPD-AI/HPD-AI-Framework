namespace HPDOS.Apps.AppRecorder.Intelligence;

/// <summary>Phase 2 — stub. Full implementation in Phase 2 (AI intelligence).</summary>
public sealed record SmartEditOptions(
    bool ApplyZoom = true,
    bool ApplyTrim = true,
    bool ApplyHighlights = false,
    bool ApplyCursorSignals = true
);

public sealed record SmartEditResult(string Summary);

public sealed record SmartEditPreviewResult(
    IReadOnlyList<SmartEditCandidate> Candidates,
    string Summary
);

public sealed record SmartEditCandidate(
    string Kind,           // "zoom" | "trim" | "highlight"
    long StartMs,
    long EndMs,
    double Confidence,
    string Reason
);

/// <summary>Phase 2 — stub. Full implementation in Phase 2 (AI intelligence).</summary>
public sealed class SmartEditAgent(AppRecorderApp app)
{
    public Task<SmartEditResult> RunAsync(string projectId, SmartEditOptions options, CancellationToken ct = default)
        => throw new NotImplementedException("SmartEdit is a Phase 2 feature.");

    public Task<SmartEditPreviewResult> PreviewAsync(string projectId, SmartEditOptions options, CancellationToken ct = default)
        => throw new NotImplementedException("SmartEdit preview is a Phase 2 feature.");
}
