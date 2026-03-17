using HPD.Agent;
using HPD.MultiAgent;
using HPDOS.Apps.AppRecorder.Export;
using HPDOS.Apps.AppRecorder.Intelligence;
using HPDOS.Apps.AppRecorder.Project;
using HPDOS.Apps.AppRecorder.Recording;

namespace HPDOS.Apps.AppRecorder.Toolkits;

[Collapse("Video editing — timeline regions, annotations, export, project management")]
public sealed class VideoEditorToolkit(AppRecorderApp app)
{
    public VideoEditorToolkit() : this(null!) => throw new InvalidOperationException(
        "VideoEditorToolkit requires an AppRecorderApp instance. Use AddAppRecorderToolkits().");

    // ── Project inspection ────────────────────────────────────────────────────

    [AIFunction]
    [AIDescription(
        "Read the current project state before making any edits. " +
        "Returns all active regions, current duration, source type, and applied command count. " +
        "Always call this first — do not edit a project you have not read.")]
    public Task<ProjectStateResult> GetProjectState(
        [AIDescription("Project id returned by StopRecording or LoadProject.")] string projectId,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── Zoom ──────────────────────────────────────────────────────────────────

    [AIFunction]
    [AIDescription("Add a smooth camera zoom region to the timeline.")]
    public Task<string> AddZoomRegion(
        string projectId,
        [AIDescription("Region start in milliseconds.")] long startMs,
        [AIDescription("Region end in milliseconds.")] long endMs,
        [AIDescription("Zoom scale factor, e.g. 1.5 = 150%.")] double depth,
        [AIDescription("Normalised horizontal focus point (0.0–1.0).")] double cx,
        [AIDescription("Normalised vertical focus point (0.0–1.0).")] double cy,
        [AIDescription("Optional transaction id for atomic undo grouping.")] string? transactionId = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── Trim ──────────────────────────────────────────────────────────────────

    [AIFunction]
    [AIDescription("Mark a time range to be cut from the final export.")]
    public Task<string> AddTrimRegion(
        string projectId,
        long startMs, long endMs,
        string? transactionId = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── Speed ─────────────────────────────────────────────────────────────────

    [AIFunction]
    [AIDescription("Apply a speed multiplier to a time range. 0.5 = half speed, 2.0 = double speed.")]
    public Task<string> SetSpeed(
        string projectId,
        long startMs, long endMs,
        double multiplier,
        string? transactionId = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── Annotations ───────────────────────────────────────────────────────────

    [AIFunction]
    [AIDescription("Add a text, arrow, or image annotation visible during a time range.")]
    public Task<string> AddAnnotation(
        string projectId,
        [AIDescription("'text', 'arrow', or 'image'.")] string type,
        long startMs, long endMs,
        AnnotationPayload payload,
        string? transactionId = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── Keyframes ─────────────────────────────────────────────────────────────

    [AIFunction]
    [AIDescription("Place a named marker at a timestamp on the timeline.")]
    public Task<string> AddKeyframe(
        string projectId,
        long timeMs,
        [AIDescription("Optional label for this keyframe.")] string? label = null,
        string? transactionId = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── Split + transitions ───────────────────────────────────────────────────

    [AIFunction]
    [AIDescription("Insert a hard cut at the given timestamp.")]
    public Task<string> SplitAtPlayhead(
        string projectId,
        long timeMs,
        string? transactionId = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    [AIFunction]
    [AIDescription("Place a transition at a cut point. Type is any of the 400+ bundled transition names, e.g. 'fade', 'wipe-right', 'circle-in', 'blinds-horizontal'.")]
    public Task<string> AddTransition(
        string projectId,
        long timeMs,
        string type,
        int durationMs = 500,
        string? transactionId = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    [AIFunction]
    [AIDescription("List all available transitions with names and categories.")]
    public Task<IReadOnlyList<TransitionInfo>> GetTransitions(CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── Visual ────────────────────────────────────────────────────────────────

    [AIFunction]
    [AIDescription("Set the canvas background (solid colour, gradient, image, or preset).")]
    public Task<string> SetBackground(
        string projectId,
        BackgroundOptions options,
        string? transactionId = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    [AIFunction]
    [AIDescription("Set visual options: border radius, padding, drop shadow, background blur, motion blur.")]
    public Task<string> SetVisualOptions(
        string projectId,
        VisualOptions options,
        string? transactionId = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    [AIFunction]
    [AIDescription("Crop the visible canvas area.")]
    public Task<string> SetCrop(
        string projectId,
        CropOptions options,
        string? transactionId = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── Undo / redo ───────────────────────────────────────────────────────────

    [AIFunction]
    [AIDescription("Undo the last command (or last transaction if commands share a transactionId).")]
    public Task<string> Undo(string projectId, CancellationToken ct = default)
        => throw new NotImplementedException();

    [AIFunction]
    [AIDescription("Redo the next command or transaction.")]
    public Task<string> Redo(string projectId, CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── Export ────────────────────────────────────────────────────────────────

    [AIFunction]
    [AIDescription("Export the project as MP4. Quality: 'medium', 'good', or 'source'.")]
    public async Task<ExportResult> ExportMp4(
        string projectId,
        [AIDescription("'medium', 'good', or 'source'.")] string quality = "good",
        [AIDescription("Output file path. Defaults to project directory.")] string? outputPath = null,
        CancellationToken ct = default)
    {
        var project = app.GetProject(projectId);
        var dest = await ExportPipeline.ExportMp4Async(project, quality, outputPath, ct: ct);
        var info = new FileInfo(dest);
        return new ExportResult(dest, "mp4", info.Length, 0);
    }

    [AIFunction]
    [AIDescription("Export the project as an animated GIF.")]
    public async Task<ExportResult> ExportGif(
        string projectId,
        [AIDescription("Frames per second: 15, 20, 25, or 30.")] int fps = 20,
        [AIDescription("Size preset: 'medium', 'large', or 'original'.")] string size = "medium",
        [AIDescription("Output file path. Defaults to project directory.")] string? outputPath = null,
        CancellationToken ct = default)
    {
        var project = app.GetProject(projectId);
        var dest = await ExportPipeline.ExportGifAsync(project, fps, size, outputPath, ct: ct);
        var info = new FileInfo(dest);
        return new ExportResult(dest, "gif", info.Length, 0);
    }

    // ── Project persistence ───────────────────────────────────────────────────

    [AIFunction]
    [AIDescription("Save the project to its current path (fast save, equivalent to Cmd+S).")]
    public async Task<string> SaveProject(string projectId, CancellationToken ct = default)
    {
        var project = app.GetProject(projectId);
        var path = project.CurrentPath
            ?? ProjectPersistence.DefaultPathFor(project.VideoPath);
        await ProjectPersistence.SaveAsync(project, path, ct);
        app.UpdateProject(project.MarkSaved(path));
        return path;
    }

    [AIFunction]
    [AIDescription("Save the project to a new path.")]
    public async Task<string> SaveProjectAs(string projectId, string path, CancellationToken ct = default)
    {
        var project = app.GetProject(projectId);
        await ProjectPersistence.SaveAsync(project, path, ct);
        app.UpdateProject(project.MarkSaved(path));
        return path;
    }

    [AIFunction]
    [AIDescription("Load a .hpdrecorder project file. Returns the projectId.")]
    public async Task<string> LoadProject(string path, CancellationToken ct = default)
    {
        var project = await ProjectPersistence.LoadAsync(path, ct);
        // Re-stamp CurrentPath in case the file was moved since last save.
        project = project with { CurrentPath = path };
        return app.RegisterProject(project);
    }

    [AIFunction]
    [AIDescription("Import an existing video file for editing. Creates a new project with sourceType 'import'. Returns the projectId.")]
    public Task<string> ImportVideo(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Video file not found: {path}", path);
        var project = ProjectPersistence.CreateImportProject(path);
        return Task.FromResult(app.RegisterProject(project));
    }

    [AIFunction]
    [AIDescription("Reveal a file in Finder (macOS) or Explorer (Windows).")]
    public Task RevealInFinder(string path, CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── SmartEdit sub-agent ───────────────────────────────────────────────────

    [SubAgent]
    [AIDescription(
        "Invoke the SmartEdit agent on a project. " +
        "The agent will: " +
        "(1) read cursor dwell and click-burst signals (screen recordings only), " +
        "(2) detect silent audio regions, " +
        "(3) score and merge all candidates, " +
        "(4) apply the top edits — zoom regions, trim cuts, optional highlight markers — " +
        "all as one atomic transaction that can be undone with a single Undo. " +
        "Pass the projectId and describe what kinds of edits you want. " +
        "The agent will explain what it found and applied.")]
    public SubAgent SmartEdit() => SubAgentFactory.Create(
        name: "SmartEdit",
        description:
            "Analyses recording signals (cursor telemetry + audio) and applies intelligent edits " +
            "— zoom regions, trim cuts, highlight markers — as one undoable transaction.",
        agentConfig: new AgentConfig
        {
            Name = "SmartEdit",
            SystemInstructions =
                """
                You are SmartEdit, an AI video editor assistant. You have access to two toolkits:

                1. SignalAnalysis — reads raw signals from the recording:
                   - GetDwellCandidates: cursor dwell regions → zoom candidates
                   - GetClickBurstCandidates: rapid cursor bursts → highlight candidates
                   - GetSilenceCandidates: audio silence → trim candidates
                   - ScoreCandidates: merges and ranks candidates by confidence

                2. VideoEditor — applies edits to the project:
                   - AddZoomRegion, AddTrimRegion, AddKeyframe, etc.
                   - GetProjectState: read current project state

                Your workflow:
                1. Call GetProjectState to understand the project (source type, duration).
                2. Based on source type: call the appropriate signal detectors.
                   - Screen recordings: use all three detectors.
                   - Camera/import: only GetSilenceCandidates.
                3. Call ScoreCandidates with all collected candidates to get a ranked list.
                4. Apply the top candidates using VideoEditor tools, all with the SAME transactionId
                   so the user can undo everything at once. Generate one transactionId (a short UUID)
                   and reuse it for every edit in this session.
                5. Report clearly: what you found, what you applied, and the transactionId.

                Apply at most 5 zoom regions and 10 trim regions per call.
                Skip candidates with score < 0.35.
                Always prefer precision over quantity — fewer, high-confidence edits are better.
                """,
            MaxAgenticIterations = 15,
        },
        typeof(SignalAnalysisToolkit),
        typeof(VideoEditorToolkit)
    );

    // ── HighlightDetector sub-agent (Task 20) ─────────────────────────────────

    [SubAgent]
    [AIDescription(
        "Identify key moments in a recording using multi-signal analysis. " +
        "The agent analyses cursor bursts, dwell regions, and audio patterns to find the " +
        "most important moments — moments the viewer's attention should be drawn to. " +
        "Returns a list of highlight candidates with timestamps and confidence scores. " +
        "Does NOT apply edits — use SmartEdit or individual edit tools to apply them. " +
        "Pass the projectId and optionally describe what kind of highlights you are looking for.")]
    public SubAgent HighlightDetector() => SubAgentFactory.Create(
        name: "HighlightDetector",
        description:
            "Identifies the most important moments in a recording via multi-signal analysis " +
            "(cursor bursts, dwell, audio). Returns ranked highlight candidates — does not apply edits.",
        agentConfig: new AgentConfig
        {
            Name = "HighlightDetector",
            SystemInstructions =
                """
                You are HighlightDetector, an AI analyst for screen recordings. Your job is to
                identify the most important moments worth highlighting — NOT to apply edits.

                You have access to SignalAnalysis tools:
                   - GetDwellCandidates: cursor dwell → focus regions
                   - GetClickBurstCandidates: rapid cursor bursts → interaction hotspots
                   - GetSilenceCandidates: audio silence → natural break points
                   - ScoreCandidates: merges and ranks all signals

                Your workflow:
                1. Call GetProjectState to understand the project (source type, duration).
                2. Run all applicable signal detectors for the source type.
                   - Screen recordings: run all three detectors.
                   - Camera/import: only GetSilenceCandidates.
                3. Call ScoreCandidates with minScore=0.35 to get a ranked list.
                4. Report every candidate with:
                   - Timestamp range (startMs → endMs)
                   - Action type (zoom / highlight / trim)
                   - Confidence score
                   - Reason (what signal detected it)
                5. Summarise: total candidates found, top 3 by confidence, recommended next step.

                Do NOT apply any edits. Your output is analysis only.
                Be precise and explain your reasoning for each candidate.
                """,
            MaxAgenticIterations = 10,
        },
        typeof(SignalAnalysisToolkit),
        typeof(VideoEditorToolkit)
    );

    // ── Parallel export MultiAgent (Task 23) ──────────────────────────────────

    [MultiAgent("Export the current project as both MP4 (quality: good) and GIF (20fps, medium size) simultaneously. " +
                "Both exports run in parallel — faster than sequential. " +
                "The workflow input string must contain the projectId. Returns when both exports are complete.")]
    public Task<AgentWorkflowInstance> ExportAll()
        => AgentWorkflow.Create()
            .WithName("ParallelExport")
            .AddAgent("mp4-exporter", new AgentConfig
            {
                Name = "Mp4Exporter",
                SystemInstructions =
                    """
                    You are the MP4 export agent. Extract the projectId from the workflow input.
                    Call ExportMp4 with that projectId and quality 'good'.
                    Report the output path and file size when done.
                    """,
                MaxAgenticIterations = 5,
            })
            .AddAgent("gif-exporter", new AgentConfig
            {
                Name = "GifExporter",
                SystemInstructions =
                    """
                    You are the GIF export agent. Extract the projectId from the workflow input.
                    Call ExportGif with that projectId, fps=20, size='medium'.
                    Report the output path and file size when done.
                    """,
                MaxAgenticIterations = 5,
            })
            .From("START").To("mp4-exporter", "gif-exporter")
            .From("mp4-exporter", "gif-exporter").To("END")
            .BuildAsync();
}

// ── Result types ──────────────────────────────────────────────────────────────

public sealed record ProjectStateResult(
    string ProjectId,
    string SourceType,
    long DurationMs,
    int CommandCount,
    int ActiveCommandCount,
    IReadOnlyList<ZoomRegionSummary> ZoomRegions,
    IReadOnlyList<TrimRegionSummary> TrimRegions,
    IReadOnlyList<SpeedRegionSummary> SpeedRegions,
    IReadOnlyList<SplitPointSummary> SplitPoints,
    IReadOnlyList<TransitionSummary> Transitions,
    bool HasCrop,
    bool HasBackground,
    bool IsDirty
);

public sealed record ZoomRegionSummary(long StartMs, long EndMs, double Depth, double Cx, double Cy);
public sealed record TrimRegionSummary(long StartMs, long EndMs);
public sealed record SpeedRegionSummary(long StartMs, long EndMs, double Multiplier);
public sealed record SplitPointSummary(long TimeMs);
public sealed record TransitionSummary(string TransitionId, long TimeMs, string Type, int DurationMs);
public sealed record TransitionInfo(string Name, string Category);
public sealed record ExportResult(string OutputPath, string Format, long FileSizeBytes, long DurationMs);
