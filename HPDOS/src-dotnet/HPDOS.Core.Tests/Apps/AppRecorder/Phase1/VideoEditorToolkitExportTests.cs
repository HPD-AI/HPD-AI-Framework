using HPDOS.Apps.AppRecorder;
using HPDOS.Apps.AppRecorder.Export;
using HPDOS.Apps.AppRecorder.Project;
using HPDOS.Apps.AppRecorder.Toolkits;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase1;

/// <summary>
/// Integration tests for VideoEditorToolkit — export, save/load, import.
/// All tests are skipped when ffmpeg is not on PATH or HPDOS_TEST_VIDEO is not set.
/// </summary>
public class VideoEditorToolkitExportTests : IAsyncLifetime
{
    private readonly List<string> _tempFiles = [];

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        foreach (var f in _tempFiles) try { if (File.Exists(f)) File.Delete(f); } catch { }
        return Task.CompletedTask;
    }

    private string Temp(string ext)
    {
        var p = Path.Combine(Path.GetTempPath(), $"hpdos_vtk_{Guid.NewGuid():N}{ext}");
        _tempFiles.Add(p);
        return p;
    }

    private static bool FfmpegAvailable()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string? SampleVideo() => Environment.GetEnvironmentVariable("HPDOS_TEST_VIDEO");
    private static bool CanRun() => FfmpegAvailable() && SampleVideo() is { } v && File.Exists(v);

    private static async Task<(AppRecorderApp app, VideoEditorToolkit toolkit, string projectId)>
        MakeWithVideoAsync(string videoPath)
    {
        await FfmpegProber.ProbeAsync();
        var app = new AppRecorderApp();
        var tk = new VideoEditorToolkit(app);
        var projectId = await tk.ImportVideo(videoPath);
        return (app, tk, projectId);
    }

    // #1 ExportMp4 via toolkit returns path and file exists
    [SkippableFact]
    public async Task ExportMp4_ValidProject_ReturnsPath()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");

        var video = SampleVideo()!;
        var dest = Temp(".mp4");
        var (_, tk, projectId) = await MakeWithVideoAsync(video);

        var result = await tk.ExportMp4(projectId, "good", dest);

        Assert.Equal(dest, result.OutputPath);
        Assert.Equal("mp4", result.Format);
        Assert.True(File.Exists(dest));
        Assert.True(result.FileSizeBytes > 0);
    }

    // #2 ExportGif via toolkit returns path with .gif extension
    [SkippableFact]
    public async Task ExportGif_ValidProject_ReturnsPath()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");

        var video = SampleVideo()!;
        var dest = Temp(".gif");
        var (_, tk, projectId) = await MakeWithVideoAsync(video);

        var result = await tk.ExportGif(projectId, 15, "medium", dest);

        Assert.Equal(dest, result.OutputPath);
        Assert.Equal("gif", result.Format);
        Assert.True(File.Exists(dest));
        Assert.True(result.FileSizeBytes > 0);
    }

    // #3 SaveProject creates file on disk
    [SkippableFact]
    public async Task SaveProject_CreatesFile()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");

        var video = SampleVideo()!;
        var (_, tk, projectId) = await MakeWithVideoAsync(video);

        // SaveProject derives path from video path
        var stem = Path.GetFileNameWithoutExtension(video);
        var dir = Path.GetDirectoryName(video)!;
        var expectedPath = Path.Combine(dir, stem + ".hpdrecorder");
        _tempFiles.Add(expectedPath);

        var savedPath = await tk.SaveProject(projectId);

        Assert.Equal(expectedPath, savedPath);
        Assert.True(File.Exists(savedPath));
    }

    // #4 LoadProject roundtrips a saved project
    [SkippableFact]
    public async Task LoadProject_RoundTrips()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");

        var video = SampleVideo()!;
        var savePath = Temp(".hpdos");
        var (app, tk, projectId) = await MakeWithVideoAsync(video);

        // Add a command to give us something to verify
        var project = app.GetProject(projectId);
        project = project.Append(new AddTrimRegion(500, 1000));
        app.UpdateProject(project);

        // SaveAs then LoadProject
        await tk.SaveProjectAs(projectId, savePath);
        var loadedId = await tk.LoadProject(savePath);

        var loaded = app.GetProject(loadedId);
        Assert.Equal(video, loaded.VideoPath);
        Assert.Equal(1, loaded.Commands.Count);
    }

    // #5 ImportVideo creates a project with the correct video path
    [SkippableFact]
    public async Task ImportVideo_ReturnsProjectId()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");

        var video = SampleVideo()!;
        var app = new AppRecorderApp();
        var tk = new VideoEditorToolkit(app);

        var projectId = await tk.ImportVideo(video);

        Assert.NotEmpty(projectId);
        var project = app.GetProject(projectId);
        Assert.Equal(video, project.VideoPath);
    }

    // #6 SaveProjectAs returns the new path
    [SkippableFact]
    public async Task SaveProjectAs_ReturnsNewPath()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");

        var video = SampleVideo()!;
        var newPath = Temp(".hpdos");
        var (_, tk, projectId) = await MakeWithVideoAsync(video);

        var result = await tk.SaveProjectAs(projectId, newPath);

        Assert.Equal(newPath, result);
        Assert.True(File.Exists(newPath));
    }
}
