namespace HPDOS.Apps.AppRecorder.Intelligence;

// ── Signal type taxonomy ──────────────────────────────────────────────────────

public enum SignalKind
{
    /// Cursor dwelt in a region — good zoom candidate.
    CursorDwell,

    /// Rapid cursor movement burst — attention moment, potential zoom/highlight.
    ClickBurst,

    /// Audio silence region — good trim candidate.
    AudioSilence,
}

public enum CandidateAction
{
    /// Add a zoom region over this time range.
    AddZoom,

    /// Trim (remove) this time range.
    Trim,

    /// Mark as a highlight (keyframe/split) — used by HighlightDetector.
    Highlight,
}

// ── Candidate ─────────────────────────────────────────────────────────────────

/// <summary>
/// A ranked edit candidate produced by a signal detector.
/// Multiple detectors emit candidates; Task 19 (ConfidenceScorer) merges them.
/// </summary>
public sealed record SignalCandidate(
    /// <summary>Milliseconds from start of recording.</summary>
    long StartMs,
    long EndMs,

    /// <summary>What kind of signal produced this candidate.</summary>
    SignalKind Kind,

    /// <summary>The suggested edit action.</summary>
    CandidateAction Action,

    /// <summary>Raw confidence score in [0.0, 1.0] before merging.</summary>
    double Confidence,

    /// <summary>
    /// For zoom candidates: normalised focus point (cx, cy).
    /// For trim/highlight: null.
    /// </summary>
    double? FocusCx = null,
    double? FocusCy = null,

    /// <summary>Human-readable reason string for debugging / agent explanation.</summary>
    string? Reason = null
)
{
    public long DurationMs => EndMs - StartMs;
}

// ── Merged result from Task 19 ────────────────────────────────────────────────

/// <summary>
/// A scored, merged candidate ready for <c>SmartEdit</c> to apply.
/// Combined from one or more raw <see cref="SignalCandidate"/>s covering the same window.
/// </summary>
public sealed record MergedCandidate(
    long StartMs,
    long EndMs,
    CandidateAction Action,

    /// <summary>Final combined confidence after weighting and signal-overlap boost.</summary>
    double Score,

    /// <summary>Normalised focus point for zoom actions. Null for trim/highlight.</summary>
    double? FocusCx,
    double? FocusCy,

    /// <summary>All raw candidates that were merged into this result.</summary>
    IReadOnlyList<SignalCandidate> Sources
)
{
    public long DurationMs => EndMs - StartMs;
}
