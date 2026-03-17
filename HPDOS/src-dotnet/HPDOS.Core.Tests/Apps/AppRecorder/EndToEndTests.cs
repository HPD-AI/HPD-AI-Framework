using System.Text.Json;
using HPDOS.Apps.AppRecorder.Project;
using HPDOS.Apps.AppRecorder.Recording;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder;

/// <summary>
/// End-to-end scenario tests for Phase 0 (no real recording engine — uses FakeRecordingBackend).
/// </summary>
public class EndToEndTests : IAsyncLifetime
{
    private readonly List<string> _tempFiles = [];

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
        return Task.CompletedTask;
    }

    private string TempVideo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpdos_e2e_{Guid.NewGuid():N}.mp4");
        _tempFiles.Add(path);
        _tempFiles.Add(CursorTelemetryCollector.SidecarPathFor(path));
        return path;
    }

    private static ProjectModel EmptyProject(string videoPath = "/tmp/v.mp4") => new()
    {
        ProjectId = Guid.NewGuid().ToString("N"),
        SourceType = SourceType.Screen,
        ScreenMetadata = new ScreenSourceMetadata("display:0", 1920, 1080),
        VideoPath = videoPath,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // #80 Full recording → project → undo → redo
    [Fact]
    public void Scenario_AppendCommands_UndoAll_RedoAll()
    {
        var model = EmptyProject();
        

        for (int i = 0; i < 5; i++)
            model = model.Append(new AddZoomRegion(i * 1000, i * 1000 + 500, 1.5, 0.5, 0.5));

        Assert.Equal(4, model.UndoIndex);

        // Undo all 5 individually
        for (int i = 0; i < 5; i++)
            model = model.Undo();
        Assert.Equal(-1, model.UndoIndex);

        // Redo all 5
        for (int i = 0; i < 5; i++)
            model = model.Redo();
        Assert.Equal(4, model.UndoIndex);
    }

    // #81 SmartEdit simulation — 8 commands one txId, single undo removes all
    [Fact]
    public void Scenario_SmartEdit_SingleUndoRemovesAll()
    {
        var model = EmptyProject();
        var zoomCmds = Enumerable.Range(0, 5)
            .Select(i => (ProjectCommand)new AddZoomRegion(i * 1000, i * 1000 + 500, 1.5, 0.5, 0.5));
        var trimCmds = Enumerable.Range(0, 3)
            .Select(i => (ProjectCommand)new AddTrimRegion(i * 2000 + 5500, i * 2000 + 6500));

        model = model.AppendTransaction(zoomCmds.Concat(trimCmds), "smart1");
        Assert.Equal(7, model.UndoIndex);

        model = model.Undo();
        Assert.Equal(-1, model.UndoIndex);
        Assert.Empty(model.ActiveCommands);
    }

    // #82 Project serialize → deserialize → undo still works
    [Fact]
    public void Scenario_SerializeDeserialize_UndoCorrect()
    {
        var model = EmptyProject()
            .Append(new AddZoomRegion(0, 1000, 1.5, 0.5, 0.5))
            .Append(new AddTrimRegion(2000, 3000))
            .Append(new SetSpeed(0, 5000, 2.0));

        var json = JsonSerializer.Serialize(model, typeof(ProjectModel), ProjectJsonContext.Default.Options);
        var restored = (ProjectModel)JsonSerializer.Deserialize(json, typeof(ProjectModel), ProjectJsonContext.Default.Options)!;

        Assert.Equal(2, restored.UndoIndex);
        Assert.Equal(3, restored.Commands.Count);

        var undone = restored.Undo();
        Assert.Equal(1, undone.UndoIndex);
    }

    // #83 Telemetry sidecar survives project roundtrip — LoadSidecarAsync still readable
    [Fact]
    public async Task Scenario_TelemetrySidecar_SurvivesProjectRoundtrip()
    {
        var video = TempVideo();

        // Write a sidecar
        var collector = new CursorTelemetryCollector("sess-1", 1920, 1080, () => (0.5, 0.5));
        collector.Start();
        await Task.Delay(200);
        await collector.StopAsync(video);

        // Build a project pointing at that video
        var model = new ProjectModel
        {
            ProjectId = "e2e-1",
            SourceType = SourceType.Screen,
            ScreenMetadata = new ScreenSourceMetadata("display:0", 1920, 1080),
            VideoPath = video,
            TelemetryPath = CursorTelemetryCollector.SidecarPathFor(video),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Roundtrip the project
        var json = JsonSerializer.Serialize(model, typeof(ProjectModel), ProjectJsonContext.Default.Options);
        var restored = (ProjectModel)JsonSerializer.Deserialize(json, typeof(ProjectModel), ProjectJsonContext.Default.Options)!;

        // Sidecar should still be loadable
        var loaded = await CursorTelemetryCollector.LoadSidecarAsync(restored.VideoPath);
        Assert.NotNull(loaded);
        Assert.Equal("sess-1", loaded.SessionId);
    }

    // #84 Concurrent recordings (two sessions) — independent handles
    [Fact]
    public async Task Scenario_ConcurrentRecordings_IndependentHandles()
    {
        var backend = new FakeRecordingBackend();
        var s1 = new RecordingSource("display:0", "Screen 1", RecordingSourceKind.Screen, 1920, 1080);
        var s2 = new RecordingSource("display:1", "Screen 2", RecordingSourceKind.Screen, 2560, 1440);

        var h1Task = backend.StartAsync(s1, new RecordingOptions());
        var h2Task = backend.StartAsync(s2, new RecordingOptions());

        var handles = await Task.WhenAll(h1Task, h2Task);
        Assert.NotEqual(handles[0].SessionId, handles[1].SessionId);
        Assert.Equal("display:0", handles[0].Source.Id);
        Assert.Equal("display:1", handles[1].Source.Id);
    }

    // #85 Empty project export attempt — no crash on model
    [Fact]
    public void Scenario_EmptyProject_NoCommandsIsValid()
    {
        var model = EmptyProject();
        Assert.Equal(-1, model.UndoIndex);
        Assert.Empty(model.Commands);
        Assert.Empty(model.ActiveCommands);
        // Serialization of empty project should not throw
        var json = JsonSerializer.Serialize(model, typeof(ProjectModel), ProjectJsonContext.Default.Options);
        Assert.NotEmpty(json);
    }

    // #87 SetSpeed with multiplier = 1.0 — valid no-op
    [Fact]
    public void Scenario_SetSpeedMultiplierOne_IsValid()
    {
        var model = EmptyProject().Append(new SetSpeed(0, 5000, 1.0));
        Assert.Equal(0, model.UndoIndex);
    }

    // #88 AddTrimRegion where startMs == endMs — zero-length trim is structurally valid
    [Fact]
    public void Scenario_ZeroLengthTrimRegion_IsStructurallyValid()
    {
        // The model accepts it — validation of whether it should be rejected is Phase 1+
        var model = EmptyProject().Append(new AddTrimRegion(1000, 1000));
        Assert.Equal(0, model.UndoIndex);
        Assert.Single(model.Commands);
    }

    // #93 Cursor provider throws — loop should not crash collector
    [Fact]
    public async Task Scenario_CursorProviderThrows_LoopHandlesGracefully()
    {
        var video = TempVideo();
        var collector = new CursorTelemetryCollector(
            "sess-x", 1920, 1080,
            () => throw new InvalidOperationException("cursor unavailable"));

        collector.Start();
        await Task.Delay(300);

        // StopAsync should complete (samples may be empty, no crash)
        var tel = await collector.StopAsync(video);
        Assert.NotNull(tel);
    }
}
