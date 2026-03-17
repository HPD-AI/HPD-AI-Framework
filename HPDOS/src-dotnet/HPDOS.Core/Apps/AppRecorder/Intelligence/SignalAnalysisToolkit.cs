using HPD.Agent;
using HPDOS.Apps.AppRecorder.Export;
using HPDOS.Apps.AppRecorder.Recording;

namespace HPDOS.Apps.AppRecorder.Intelligence;

/// <summary>
/// Toolkit given to the SmartEdit sub-agent.
/// Exposes signal detectors as AIFunctions so the sub-agent can read
/// cursor + audio signals, then decide which edits to apply via VideoEditorToolkit.
/// </summary>
[Collapse("Signal analysis — read cursor dwell, click bursts, and audio silence from a recording")]
public sealed class SignalAnalysisToolkit(AppRecorderApp app)
{
    public SignalAnalysisToolkit() : this(null!) => throw new InvalidOperationException(
        "SignalAnalysisToolkit requires an AppRecorderApp instance.");

    [AIFunction]
    [AIDescription(
        "Detect cursor dwell regions from the project's cursor telemetry sidecar. " +
        "Returns zoom candidates: time ranges where the cursor stayed in one area long enough to suggest the user was focused there. " +
        "Only available for screen recordings (SourceType = Screen). " +
        "Returns an empty list for camera or import sources.")]
    public async Task<IReadOnlyList<SignalCandidate>> GetDwellCandidates(
        [AIDescription("Project id.")] string projectId,
        CancellationToken ct = default)
    {
        var project = app.GetProject(projectId);
        if (project.SourceType != HPDOS.Apps.AppRecorder.Project.SourceType.Screen)
            return [];

        var telemetry = await CursorTelemetryCollector.LoadSidecarAsync(project.VideoPath);
        if (telemetry is null) return [];

        return DwellDetector.Detect(telemetry);
    }

    [AIFunction]
    [AIDescription(
        "Detect rapid cursor movement bursts from the project's cursor telemetry sidecar. " +
        "Returns highlight candidates: moments of high cursor activity that may be worth zooming into or marking. " +
        "Only available for screen recordings.")]
    public async Task<IReadOnlyList<SignalCandidate>> GetClickBurstCandidates(
        [AIDescription("Project id.")] string projectId,
        CancellationToken ct = default)
    {
        var project = app.GetProject(projectId);
        if (project.SourceType != HPDOS.Apps.AppRecorder.Project.SourceType.Screen)
            return [];

        var telemetry = await CursorTelemetryCollector.LoadSidecarAsync(project.VideoPath);
        if (telemetry is null) return [];

        return ClickBurstDetector.Detect(telemetry);
    }

    [AIFunction]
    [AIDescription(
        "Detect silent audio regions in the recording using ffmpeg. " +
        "Returns trim candidates: time ranges where the audio is silent (below -40dBFS for ≥1.5s). " +
        "Each candidate already has 250ms padding applied on each side so cuts sound natural. " +
        "Works on all source types (screen, camera, import) as long as the video has an audio track.")]
    public async Task<IReadOnlyList<SignalCandidate>> GetSilenceCandidates(
        [AIDescription("Project id.")] string projectId,
        CancellationToken ct = default)
    {
        var project = app.GetProject(projectId);
        var caps = FfmpegProber.Capabilities;
        return await SilenceDetector.DetectAsync(project.VideoPath, caps.FfmpegBinary, ct);
    }

    [AIFunction]
    [AIDescription(
        "Merge and score all provided signal candidates into a ranked list. " +
        "Overlapping candidates of the same action type are merged. " +
        "Multi-signal corroboration boosts the score. " +
        "Returns candidates ordered by score descending — apply the top ones.")]
    public IReadOnlyList<MergedCandidate> ScoreCandidates(
        [AIDescription("All raw candidates from dwell, burst, and silence detectors.")] IReadOnlyList<SignalCandidate> candidates,
        [AIDescription("Minimum confidence score (0.0–1.0) to include. Recommended: 0.35.")] double minScore = 0.35)
    {
        return ConfidenceScorer.Score(candidates)
            .Where(c => c.Score >= minScore)
            .ToList();
    }
}
