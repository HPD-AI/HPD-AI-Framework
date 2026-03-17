using HPDOS.Apps.AppRecorder.Project;
using System.Text.Json;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase1;

public class ProjectPersistenceTests : IAsyncLifetime
{
    private readonly List<string> _tempPaths = [];

    private string TempPath(string ext = ".hpdrecorder")
    {
        var p = Path.Combine(Path.GetTempPath(), $"hpdos_persist_{Guid.NewGuid():N}{ext}");
        _tempPaths.Add(p);
        _tempPaths.Add(p + ".tmp");
        return p;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        foreach (var f in _tempPaths)
            if (File.Exists(f)) File.Delete(f);
        return Task.CompletedTask;
    }

    private static ProjectModel Sample() => new()
    {
        ProjectId = "proj-1",
        SourceType = SourceType.Screen,
        ScreenMetadata = new ScreenSourceMetadata("display:0", 1920, 1080),
        VideoPath = "/tmp/rec.mp4",
        TelemetryPath = "/tmp/rec.cursor.json",
        CreatedAt = DateTimeOffset.UtcNow,
        ModifiedAt = DateTimeOffset.UtcNow,
    };

    // #1 SaveAsync creates file
    [Fact]
    public async Task SaveAsync_CreatesFile()
    {
        var path = TempPath();
        await ProjectPersistence.SaveAsync(Sample(), path);
        Assert.True(File.Exists(path));
    }

    // #2 Atomic write — no .tmp file left after save
    [Fact]
    public async Task SaveAsync_IsAtomic_TempFileReplaced()
    {
        var path = TempPath();
        await ProjectPersistence.SaveAsync(Sample(), path);
        Assert.False(File.Exists(path + ".tmp"));
    }

    // #3 Roundtrip preserves all fields
    [Fact]
    public async Task SaveAsync_Roundtrip_PreservesAllFields()
    {
        var path = TempPath();
        var original = Sample();
        await ProjectPersistence.SaveAsync(original, path);
        var loaded = await ProjectPersistence.LoadAsync(path);

        Assert.Equal(original.ProjectId, loaded.ProjectId);
        Assert.Equal(original.SourceType, loaded.SourceType);
        Assert.Equal(original.VideoPath, loaded.VideoPath);
        Assert.Equal(original.TelemetryPath, loaded.TelemetryPath);
        Assert.Equal(original.ScreenMetadata, loaded.ScreenMetadata);
    }

    // #4 Command stack round-trips
    [Fact]
    public async Task SaveAsync_PreservesCommandStack()
    {
        var path = TempPath();
        var model = Sample()
            .Append(new AddZoomRegion(0, 1000, 1.5, 0.5, 0.5))
            .Append(new AddTrimRegion(2000, 3000))
            .Append(new SetSpeed(0, 5000, 2.0))
            .Append(new AddSplitPoint(4000))
            .Append(new AddTransition("t1", 4000, "fade", 500));

        await ProjectPersistence.SaveAsync(model, path);
        var loaded = await ProjectPersistence.LoadAsync(path);

        Assert.Equal(model.Commands.Count, loaded.Commands.Count);
        Assert.Equal(model.UndoIndex, loaded.UndoIndex);
        for (int i = 0; i < model.Commands.Count; i++)
            Assert.Equal(model.Commands[i].GetType(), loaded.Commands[i].GetType());
    }

    // #5 Second save overwrites first
    [Fact]
    public async Task SaveAsync_OverwritesExisting()
    {
        var path = TempPath();
        await ProjectPersistence.SaveAsync(Sample(), path);

        var updated = Sample() with { VideoPath = "/tmp/updated.mp4" };
        await ProjectPersistence.SaveAsync(updated, path);

        var loaded = await ProjectPersistence.LoadAsync(path);
        Assert.Equal("/tmp/updated.mp4", loaded.VideoPath);
    }

    // #6 LoadAsync throws FileNotFoundException for missing file
    [Fact]
    public async Task LoadAsync_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => ProjectPersistence.LoadAsync("/tmp/does_not_exist_hpdos.hpdrecorder"));
    }

    // #7 LoadAsync throws JsonException for corrupt file
    [Fact]
    public async Task LoadAsync_ThrowsOnCorruptJson()
    {
        var path = TempPath();
        await File.WriteAllTextAsync(path, "{bad json");
        await Assert.ThrowsAsync<JsonException>(() => ProjectPersistence.LoadAsync(path));
    }

    // #8 UndoIndex preserved
    [Fact]
    public async Task LoadAsync_PreservesUndoIndex()
    {
        var path = TempPath();
        var model = Sample()
            .Append(new AddZoomRegion(0, 1000, 1.5, 0.5, 0.5))
            .Append(new AddZoomRegion(0, 1000, 1.5, 0.5, 0.5))
            .Append(new AddZoomRegion(0, 1000, 1.5, 0.5, 0.5))
            .Undo(); // 3 appends → index 2, undo → index 1

        await ProjectPersistence.SaveAsync(model, path);
        var loaded = await ProjectPersistence.LoadAsync(path);
        Assert.Equal(1, loaded.UndoIndex);
    }

    // #9 null TelemetryPath preserved
    [Fact]
    public async Task LoadAsync_NullTelemetryPath_Preserved()
    {
        var path = TempPath();
        var model = Sample() with { TelemetryPath = null };
        await ProjectPersistence.SaveAsync(model, path);
        var loaded = await ProjectPersistence.LoadAsync(path);
        Assert.Null(loaded.TelemetryPath);
    }

    // #10 DefaultPathFor appends .hpdrecorder
    [Fact]
    public void DefaultPathFor_AppendsSuffix()
    {
        var result = ProjectPersistence.DefaultPathFor("/tmp/rec.mp4");
        Assert.Equal("/tmp/rec.hpdrecorder", result);
    }

    // #11 CreateImportProject sets VideoPath
    [Fact]
    public void CreateImportProject_SetsVideoPath()
    {
        var project = ProjectPersistence.CreateImportProject("/tmp/video.mp4");
        Assert.Equal("/tmp/video.mp4", project.VideoPath);
    }

    // #12 CreateImportProject has SourceType.Import
    [Fact]
    public void CreateImportProject_SourceTypeIsImport()
    {
        var project = ProjectPersistence.CreateImportProject("/tmp/video.mp4");
        Assert.Equal(SourceType.Import, project.SourceType);
    }

    // #13 CreateImportProject has empty command stack
    [Fact]
    public void CreateImportProject_EmptyCommandStack()
    {
        var project = ProjectPersistence.CreateImportProject("/tmp/video.mp4");
        Assert.Empty(project.Commands);
    }

    // #14 CreateImportProject has null TelemetryPath
    [Fact]
    public void CreateImportProject_NullTelemetryPath()
    {
        var project = ProjectPersistence.CreateImportProject("/tmp/video.mp4");
        Assert.Null(project.TelemetryPath);
    }

    // #15 SaveAsync respects CancellationToken
    [Fact]
    public async Task SaveAsync_CancellationToken_Cancels()
    {
        var path = TempPath();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ProjectPersistence.SaveAsync(Sample(), path, cts.Token));
    }
}
