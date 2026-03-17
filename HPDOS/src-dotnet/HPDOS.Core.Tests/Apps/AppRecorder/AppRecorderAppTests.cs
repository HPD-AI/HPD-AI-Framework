using System.Text.Json;
using HPDOS.Apps.AppRecorder;
using HPDOS.Apps.AppRecorder.Project;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder;

public class AppRecorderAppTests
{
    private static ProjectModel MakeProject(string id = "p1") => new()
    {
        ProjectId = id,
        SourceType = SourceType.Screen,
        ScreenMetadata = new ScreenSourceMetadata("display:0", 1920, 1080),
        VideoPath = "/tmp/test.mp4",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // #54 InitializeAsync completes
    [Fact]
    public async Task InitializeAsync_Completes()
    {
        var app = new AppRecorderApp();
        await app.InitializeAsync(null!); // PlatformContext is null-safe at this stage
    }

    // #56 GetProject returns stored project
    [Fact]
    public void GetProject_ReturnsStoredProject()
    {
        var app = new AppRecorderApp();
        var project = MakeProject("p1");
        app.UpdateProject(project);
        var retrieved = app.GetProject("p1");
        Assert.Same(project, retrieved);
    }

    // #57 GetProject throws for unknown id
    [Fact]
    public void GetProject_ThrowsForUnknownId()
    {
        var app = new AppRecorderApp();
        Assert.Throws<KeyNotFoundException>(() => app.GetProject("nope"));
    }

    // #58 UpdateProject overwrites existing
    [Fact]
    public void UpdateProject_OverwritesExisting()
    {
        var app = new AppRecorderApp();
        var v1 = MakeProject("p1");
        var v2 = v1.Append(new AddZoomRegion(0, 1000, 1.5, 0.5, 0.5));

        app.UpdateProject(v1);
        app.UpdateProject(v2);

        var result = app.GetProject("p1");
        Assert.Equal(1, result.Commands.Count);
    }

    // OpenShot gap #2 — Update then re-read consistency
    [Fact]
    public void UpdateProject_SubsequentGetReturnsUpdatedState()
    {
        var app = new AppRecorderApp();
        var original = MakeProject("p1");
        app.UpdateProject(original);

        var updated = original.Append(new AddTrimRegion(500, 1000));
        app.UpdateProject(updated);

        var read = app.GetProject("p1");
        Assert.Equal(1, read.Commands.Count);
        Assert.IsType<AddTrimRegion>(read.Commands[0]);
    }

    // OpenShot gap #1 — TryGetProject returns null on miss (if implemented)
    // AppRecorderApp currently uses GetProject (throws). This test documents the gap
    // and verifies the throwing behavior; add TryGetProject when implemented.
    [Fact]
    public void GetProject_MissingId_ThrowsKeyNotFoundException()
    {
        var app = new AppRecorderApp();
        var ex = Assert.Throws<KeyNotFoundException>(() => app.GetProject("missing"));
        Assert.Contains("missing", ex.Message);
    }

    // #59 HandleCommandAsync("ping")
    [Fact]
    public async Task HandleCommandAsync_Ping_ReturnsOk()
    {
        var app = new AppRecorderApp();
        var result = await app.HandleCommandAsync("ping", default);
        Assert.True(result.GetProperty("ok").GetBoolean());
    }

    // #60 HandleCommandAsync unknown command
    [Fact]
    public async Task HandleCommandAsync_UnknownCommand_ReturnsError()
    {
        var app = new AppRecorderApp();
        var result = await app.HandleCommandAsync("xyz", default);
        var error = result.GetProperty("error").GetString();
        Assert.Contains("xyz", error);
    }

    // #61 Concurrent UpdateProject calls — last write wins, no crash
    [Fact]
    public async Task ConcurrentUpdateProject_NoCrash()
    {
        var app = new AppRecorderApp();
        var base_ = MakeProject("p1");
        app.UpdateProject(base_);

        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            var model = base_.Append(new AddZoomRegion(i * 100, i * 100 + 500, 1.5, 0.5, 0.5));
            app.UpdateProject(model);
        }));

        await Task.WhenAll(tasks);

        // Should not throw and project should be retrievable
        var p = app.GetProject("p1");
        Assert.NotNull(p);
    }

    // #62 Concurrent GetProject calls — all return valid model
    [Fact]
    public async Task ConcurrentGetProject_AllReturnValidModel()
    {
        var app = new AppRecorderApp();
        app.UpdateProject(MakeProject("p1"));

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() => app.GetProject("p1")));
        var results = await Task.WhenAll(tasks);

        Assert.All(results, p => Assert.NotNull(p));
    }

    // #55 SetBackend stores backend (verified indirectly — no crash, no exception)
    [Fact]
    public void SetBackend_DoesNotThrow()
    {
        var app = new AppRecorderApp();
        app.SetBackend(new FakeRecordingBackend());
    }
}
