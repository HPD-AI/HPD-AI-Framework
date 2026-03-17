using System.Collections.Concurrent;
using System.Text.Json;
using HPDOS.Apps.AppRecorder.Project;
using HPDOS.Apps.AppRecorder.Recording;
using HPDOS.Core.Platform;

namespace HPDOS.Apps.AppRecorder;

/// <summary>
/// HPD Video application module. Registered into HPDOSPlatform as "hpd-video".
/// Owns the recording backend, project store, and command dispatch.
/// </summary>
public sealed class AppRecorderApp : IApplication
{
    public string Id => "hpd-video";
    public string Name => "HPD Video";
    public string Version => "1.0.0";

    private IRecordingBackend? _backend;
    private PlatformContext? _context;

    // Live projects keyed by projectId.
    private readonly ConcurrentDictionary<string, ProjectModel> _projects = new();

    // Active recordings keyed by sessionId → (handle, telemetry collector).
    private readonly ConcurrentDictionary<string, ActiveRecording> _activeRecordings = new();

    private sealed record ActiveRecording(
        RecordingHandle Handle,
        CursorTelemetryCollector? Telemetry  // null when backend doesn't support telemetry
    );

    public ValueTask InitializeAsync(PlatformContext context, CancellationToken ct = default)
    {
        _context = context;
        // Backend is injected via SetBackend before or after initialization.
        // The platform calls InitializeAsync; the host sets the backend.
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Set the platform-specific recording backend.
    /// Called by the host (CLI or MAUI) after construction.
    /// </summary>
    public void SetBackend(IRecordingBackend backend) => _backend = backend;

    // ── Project store ─────────────────────────────────────────────────────────

    public ProjectModel GetProject(string projectId) =>
        _projects.TryGetValue(projectId, out var project)
            ? project
            : throw new KeyNotFoundException($"Project '{projectId}' not found.");

    public ProjectModel UpdateProject(ProjectModel project)
    {
        _projects[project.ProjectId] = project;
        return project;
    }

    /// <summary>
    /// Register a project that was loaded from disk (or created from an import).
    /// Returns the projectId.
    /// </summary>
    public string RegisterProject(ProjectModel project)
    {
        _projects[project.ProjectId] = project;
        return project.ProjectId;
    }

    // ── Recording lifecycle ────────────────────────────────────────────────────

    public IRecordingBackend Backend => _backend
        ?? throw new InvalidOperationException("No recording backend set. Call SetBackend() first.");

    /// <summary>
    /// List available capture sources via the backend.
    /// </summary>
    public Task<IReadOnlyList<RecordingSource>> ListSourcesAsync(CancellationToken ct = default)
        => Backend.ListSourcesAsync(ct);

    /// <summary>
    /// Start a recording session. Starts cursor telemetry collection if the backend supports it.
    /// Returns the session handle.
    /// </summary>
    public async Task<RecordingHandle> StartRecordingAsync(RecordingSource source, RecordingOptions options, CancellationToken ct = default)
    {
        var handle = await Backend.StartAsync(source, options, ct);

        CursorTelemetryCollector? telemetry = null;
        if (Backend.SupportsCursorTelemetry)
        {
            telemetry = new CursorTelemetryCollector(
                sessionId: handle.SessionId,
                displayWidth: source.Width,
                displayHeight: source.Height,
                cursorProvider: () => Backend.GetCursorPosition(source.Width, source.Height)
            );
            telemetry.Start();
        }

        _activeRecordings[handle.SessionId] = new ActiveRecording(handle, telemetry);
        return handle;
    }

    /// <summary>
    /// Stop the active recording. Finalises cursor telemetry sidecar, creates and registers
    /// a new ProjectModel. Returns the projectId.
    /// </summary>
    public async Task<(string ProjectId, RecordingResult Result)> StopRecordingAsync(
        string sessionId, CancellationToken ct = default)
    {
        if (!_activeRecordings.TryRemove(sessionId, out var active))
            throw new KeyNotFoundException($"No active recording for session '{sessionId}'.");

        var result = await Backend.StopAsync(active.Handle, ct);

        string? telemetryPath = null;
        if (active.Telemetry is not null)
        {
            var telemetry = await active.Telemetry.StopAsync(result.VideoPath);
            telemetryPath = CursorTelemetryCollector.SidecarPathFor(result.VideoPath);
            await active.Telemetry.DisposeAsync();
        }

        // Create and register the project.
        var projectId = Guid.NewGuid().ToString("N");
        var project = new ProjectModel
        {
            ProjectId = projectId,
            SourceType = active.Handle.Source.Kind == RecordingSourceKind.Screen
                ? SourceType.Screen : SourceType.Camera,
            ScreenMetadata = active.Handle.Source.Kind == RecordingSourceKind.Screen
                ? new ScreenSourceMetadata(active.Handle.Source.Id, active.Handle.Source.Width, active.Handle.Source.Height)
                : null,
            VideoPath = result.VideoPath,
            TelemetryPath = telemetryPath,
            CreatedAt = active.Handle.StartedAt,
            ModifiedAt = DateTimeOffset.UtcNow,
        };
        RegisterProject(project);

        return (projectId, result with { TelemetryPath = telemetryPath });
    }

    // ── Command dispatch ──────────────────────────────────────────────────────

    public ValueTask<JsonElement> HandleCommandAsync(string command, JsonElement payload, CancellationToken ct = default)
    {
        // Command routing for frontend → C# bridge messages.
        // Toolkit AIFunctions are the primary agent interface; this handles
        // direct frontend bridge calls (e.g. HUD stop button).
        var result = command switch
        {
            "ping" => JsonSerializer.SerializeToElement(new { ok = true }),
            _ => JsonSerializer.SerializeToElement(new { error = $"Unknown command: {command}" })
        };

        return new ValueTask<JsonElement>(result);
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}
