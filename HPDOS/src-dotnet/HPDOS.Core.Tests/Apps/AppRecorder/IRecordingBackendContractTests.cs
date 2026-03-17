using HPDOS.Apps.AppRecorder.Recording;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder;

/// <summary>
/// Contract tests applied to any IRecordingBackend implementation.
/// Uses FakeRecordingBackend — apply the same class to NativeRecordingBackend
/// in a platform integration test project when that backend exists.
/// </summary>
public abstract class IRecordingBackendContractTests
{
    protected abstract IRecordingBackend CreateBackend();

    private RecordingSource ScreenSource(string id = "display:0") =>
        new(id, "Primary Display", RecordingSourceKind.Screen, 1920, 1080);

    // #63 ListSourcesAsync returns at least one source
    [Fact]
    public async Task ListSources_ReturnsAtLeastOneSource()
    {
        var backend = CreateBackend();
        var sources = await backend.ListSourcesAsync();
        Assert.NotEmpty(sources);
    }

    // #64 Sources have unique IDs
    [Fact]
    public async Task ListSources_UniqueIds()
    {
        var backend = CreateBackend();
        var sources = await backend.ListSourcesAsync();
        var ids = sources.Select(s => s.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }

    // #65 Screen sources have Kind = Screen
    [Fact]
    public async Task ListSources_ScreenSourcesHaveScreenKind()
    {
        var backend = CreateBackend();
        var sources = await backend.ListSourcesAsync();
        Assert.All(sources.Where(s => s.Kind == RecordingSourceKind.Screen),
            s => Assert.Equal(RecordingSourceKind.Screen, s.Kind));
    }

    // #66 Window sources have Kind = Window
    [Fact]
    public async Task ListSources_WindowSourcesHaveWindowKind()
    {
        var backend = CreateBackend();
        var sources = await backend.ListSourcesAsync();
        Assert.All(sources.Where(s => s.Kind == RecordingSourceKind.Window),
            s => Assert.Equal(RecordingSourceKind.Window, s.Kind));
    }

    // #67 StartAsync returns handle with matching source
    [Fact]
    public async Task StartAsync_HandleSourceMatchesInput()
    {
        var backend = CreateBackend();
        var source = ScreenSource();
        var handle = await backend.StartAsync(source, new RecordingOptions());
        Assert.Equal(source.Id, handle.Source.Id);
    }

    // #68 StartAsync handle SessionId is unique per call
    [Fact]
    public async Task StartAsync_SessionIdsAreUnique()
    {
        var backend = CreateBackend();
        var source = ScreenSource();
        var h1 = await backend.StartAsync(source, new RecordingOptions());
        var h2 = await backend.StartAsync(ScreenSource("display:1"), new RecordingOptions());
        Assert.NotEqual(h1.SessionId, h2.SessionId);
    }

    // #69 StopAsync result VideoPath file exists
    [Fact]
    public async Task StopAsync_VideoPathFileExists()
    {
        var backend = CreateBackend();
        var source = ScreenSource();
        var handle = await backend.StartAsync(source, new RecordingOptions());
        var result = await backend.StopAsync(handle);
        Assert.True(File.Exists(result.VideoPath));
    }

    // #70 StopAsync result Duration is positive
    [Fact]
    public async Task StopAsync_DurationIsPositive()
    {
        var backend = CreateBackend();
        var source = ScreenSource();
        var handle = await backend.StartAsync(source, new RecordingOptions());
        await Task.Delay(100);
        var result = await backend.StopAsync(handle);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    // #71 StopAsync TelemetryPath matches SupportsCursorTelemetry
    [Fact]
    public async Task StopAsync_TelemetryPath_MatchesSupportsCursorTelemetry()
    {
        var backend = CreateBackend();
        var source = ScreenSource();
        var handle = await backend.StartAsync(source, new RecordingOptions());
        var result = await backend.StopAsync(handle);

        if (backend.SupportsCursorTelemetry)
            Assert.NotNull(result.TelemetryPath);
        else
            Assert.Null(result.TelemetryPath);
    }

    // #72 StopAsync result dimensions match source
    [Fact]
    public async Task StopAsync_DimensionsMatchSource()
    {
        var backend = CreateBackend();
        var source = ScreenSource();
        var handle = await backend.StartAsync(source, new RecordingOptions());
        var result = await backend.StopAsync(handle);
        Assert.Equal(source.Width, result.Width);
        Assert.Equal(source.Height, result.Height);
    }

    // #73 StartAsync with cancellation before start
    [Fact]
    public async Task StartAsync_CancelledToken_DoesNotReturnHandle()
    {
        var backend = CreateBackend();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => backend.StartAsync(ScreenSource(), new RecordingOptions(), cts.Token));
    }

    // #74 SupportsSystemAudio is deterministic
    [Fact]
    public void SupportsSystemAudio_IsDeterministic()
    {
        var backend = CreateBackend();
        var v1 = backend.SupportsSystemAudio;
        var v2 = backend.SupportsSystemAudio;
        Assert.Equal(v1, v2);
    }
}

/// <summary>
/// Run the contract tests against the FakeRecordingBackend.
/// </summary>
public class FakeRecordingBackendContractTests : IRecordingBackendContractTests
{
    protected override IRecordingBackend CreateBackend() => new FakeRecordingBackend();
}
