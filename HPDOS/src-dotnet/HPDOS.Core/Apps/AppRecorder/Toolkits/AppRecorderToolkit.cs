using HPD.Agent;
using HPDOS.Apps.AppRecorder.Project;
using HPDOS.Apps.AppRecorder.Recording;

namespace HPDOS.Apps.AppRecorder.Toolkits;

[Collapse("Screen recording — list sources, start and stop capture")]
public sealed class AppRecorderToolkit(AppRecorderApp app)
{
    // Satisfy the source generator's ToolkitRegistry — this toolkit is always passed
    // as an instance via AddAppRecorderToolkits(), never auto-instantiated.
    public AppRecorderToolkit() : this(null!) => throw new InvalidOperationException(
        "AppRecorderToolkit requires an AppRecorderApp instance. Use AddAppRecorderToolkits().");

    [AIFunction]
    [AIDescription("List all available recording sources: full screens and capturable windows.")]
    public Task<IReadOnlyList<RecordingSource>> ListSources(CancellationToken ct = default)
        => app.ListSourcesAsync(ct);

    // Tracks the most recently started session so StopRecording needs no arg.
    private string? _activeSessionId;

    [AIFunction]
    [AIDescription("Begin screen or window capture. Returns the session id used to stop recording.")]
    public async Task<string> StartRecording(
        [AIDescription("Source id from ListSources.")] string sourceId,
        [AIDescription("Optional recording options (frame rate, audio).")] RecordingOptions? options = null,
        CancellationToken ct = default)
    {
        var sources = await app.ListSourcesAsync(ct);
        var source = sources.FirstOrDefault(s => s.Id == sourceId)
            ?? throw new ArgumentException($"Source '{sourceId}' not found. Call ListSources first.");

        var handle = await app.StartRecordingAsync(source, options ?? new RecordingOptions(), ct);
        _activeSessionId = handle.SessionId;
        return handle.SessionId;
    }

    [AIFunction]
    [AIDescription("Stop the active recording. Returns the projectId and confirms whether telemetry was written.")]
    public async Task<StopRecordingResult> StopRecording(CancellationToken ct = default)
    {
        var sessionId = _activeSessionId
            ?? throw new InvalidOperationException("No active recording. Call StartRecording first.");
        _activeSessionId = null;

        var (projectId, result) = await app.StopRecordingAsync(sessionId, ct);
        return new StopRecordingResult(
            ProjectId: projectId,
            TelemetryWritten: result.TelemetryPath is not null,
            DurationMs: (long)result.Duration.TotalMilliseconds
        );
    }

    // ── RecordAndExport skill (Task 21) ───────────────────────────────────────

    [Skill]
    [AIDescription("Guided workflow: record the screen, run SmartEdit to apply AI edits, then export as MP4. " +
                   "All steps happen automatically. You only need to pick the source and stop recording when done.")]
    public Skill RecordAndExport() => SkillFactory.Create(
        name: "RecordAndExport",
        description:
            "Record screen → AI smart edit (zoom/trim) → export MP4. " +
            "All steps guided. Pick a source, record, stop — the agent handles the rest.",
        functionResult:
            """
            RecordAndExport skill activated. Follow these steps in order:

            1. Call ListSources to enumerate available screens and windows.
            2. Present the sources and ask the user to pick one (or use show_source_picker if available).
            3. Call StartRecording with the chosen sourceId.
            4. Call show_recording_hud if available, then tell the user to record and stop when done.
            5. When the user stops (or calls StopRecording), call StopRecording to get the projectId.
            6. Call the SmartEdit sub-agent with the projectId to apply intelligent edits.
            7. Call ExportMp4 with quality 'good'.
            8. Call show_export_complete if available, then report the output path to the user.
            """
    );

    // ── QuickShare skill (Task 22) ────────────────────────────────────────────

    [Skill]
    [AIDescription("Guided workflow: record the screen, trim silent regions, export as GIF, and copy to clipboard. " +
                   "Optimised for quick sharing — minimal editing, fast output.")]
    public Skill QuickShare() => SkillFactory.Create(
        name: "QuickShare",
        description:
            "Record screen → trim silence → export GIF → copy to clipboard. " +
            "Fastest path from recording to a shareable clip.",
        functionResult:
            """
            QuickShare skill activated. Follow these steps in order:

            1. Call ListSources to enumerate available screens and windows.
            2. Present the sources and ask the user to pick one (or use show_source_picker if available).
            3. Call StartRecording with the chosen sourceId.
            4. Call show_recording_hud if available, then tell the user to record and stop when done.
            5. When the user stops, call StopRecording to get the projectId.
            6. Call GetSilenceCandidates for the projectId.
            7. For each silence candidate with Confidence >= 0.4, call AddTrimRegion to cut it out.
               Use a single transactionId (e.g. "quickshare-trim") for all trim commands.
            8. Call ExportGif with fps=20 and size='medium'.
            9. Call show_export_complete if available.
            10. Tell the user the GIF is ready at the output path and ready to share.
            """
    );
}

public sealed record StopRecordingResult(string ProjectId, bool TelemetryWritten, long DurationMs);
