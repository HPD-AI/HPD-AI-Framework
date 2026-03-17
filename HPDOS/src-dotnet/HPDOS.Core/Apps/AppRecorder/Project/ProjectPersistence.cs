using System.Text.Json;

namespace HPDOS.Apps.AppRecorder.Project;

/// <summary>
/// Handles JSON serialisation and deserialisation of <see cref="ProjectModel"/>.
/// Uses the AOT-safe <see cref="ProjectJsonContext"/> source-generated serialiser.
/// </summary>
public static class ProjectPersistence
{
    public const string FileExtension = ".hpdrecorder";

    // ── Save ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialise <paramref name="project"/> to <paramref name="path"/>.
    /// The file is written atomically: a temp file is written first, then renamed.
    /// </summary>
    public static async Task SaveAsync(ProjectModel project, string path, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Write to a sibling temp file, then rename — prevents corrupt files on crash.
        var tmp = path + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            await JsonSerializer.SerializeAsync(fs, project, ProjectJsonContext.Default.ProjectModel, ct);

        File.Move(tmp, path, overwrite: true);
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deserialise a <see cref="ProjectModel"/> from <paramref name="path"/>.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="JsonException">The file content is not a valid project.</exception>
    public static async Task<ProjectModel> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        var project = await JsonSerializer.DeserializeAsync(fs, ProjectJsonContext.Default.ProjectModel, ct)
            ?? throw new JsonException($"Project file is empty or null: {path}");
        return project;
    }

    // ── Import ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a brand-new <see cref="ProjectModel"/> wrapping an existing video file.
    /// The project is NOT saved to disk — the caller decides the save path.
    /// </summary>
    public static ProjectModel CreateImportProject(string videoPath)
    {
        var projectId = Guid.NewGuid().ToString("N");
        return new ProjectModel
        {
            ProjectId = projectId,
            SourceType = SourceType.Import,
            ImportMetadata = new ImportSourceMetadata(OriginalPath: videoPath),
            VideoPath = videoPath,
            TelemetryPath = null,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        };
    }

    // ── Default path helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the default save path for a project alongside its video file.
    /// e.g. /path/to/recording.mp4 → /path/to/recording.hpdrecorder
    /// </summary>
    public static string DefaultPathFor(string videoPath)
    {
        var dir = Path.GetDirectoryName(videoPath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(videoPath);
        return Path.Combine(dir, stem + FileExtension);
    }
}
