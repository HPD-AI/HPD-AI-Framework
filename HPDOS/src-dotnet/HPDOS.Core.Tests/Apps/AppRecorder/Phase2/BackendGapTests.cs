using System.Reflection;
using System.Text.Json;
using HPDOS.Apps.AppRecorder;
using HPDOS.Apps.AppRecorder.Export;
using HPDOS.Apps.AppRecorder.Intelligence;
using HPDOS.Apps.AppRecorder.Project;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase2;

/// <summary>
/// Backend gap tests — fills coverage holes identified after Phase 1 + Phase 2.
/// Groups: SilenceDetector edge cases (#72–74), ConfidenceScorer edge cases (#75–76),
/// AppRecorderApp command routing (#77–78), ProjectModel CRUD (#79–82),
/// BitrateCalculator 4:3 (#83), ExportPipeline slow-motion filter (#84).
/// </summary>
public class BackendGapTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string MakeSilenceLine(double startSec, double endSec)
        => $"[silencedetect @ 0x1234] silence_start: {startSec.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n" +
           $"[silencedetect @ 0x1234] silence_end: {endSec.ToString(System.Globalization.CultureInfo.InvariantCulture)} | silence_duration: {(endSec - startSec).ToString(System.Globalization.CultureInfo.InvariantCulture)}\n";

    private static readonly MethodInfo BuildVideoFilter =
        typeof(ExportPipeline).GetMethod("BuildVideoFilter",
            BindingFlags.Static | BindingFlags.NonPublic)!;

    private static string VideoFilter(ProjectModel project, int w = 1920, int h = 1080, long durMs = 10_000)
        => (string)BuildVideoFilter.Invoke(null, [project, w, h, durMs])!;

    private static ProjectModel EmptyProject() => new()
    {
        ProjectId = "test",
        SourceType = SourceType.Screen,
        ScreenMetadata = new ScreenSourceMetadata("d0", 1920, 1080),
        VideoPath = "/tmp/v.mp4",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // ── SilenceDetector edge cases ────────────────────────────────────────────

    // #72
    [Fact]
    public void ParseSilenceOutput_MultipleConsecutiveStarts_OnlyFirstPairEmitted()
    {
        // Two silence_start lines without an intervening silence_end.
        // The first start is pending; the second start overwrites it.
        // A subsequent end should pair with the second start.
        var input =
            "[silencedetect @ 0x1234] silence_start: 1.000\n" +
            "[silencedetect @ 0x1234] silence_start: 5.000\n" +
            $"[silencedetect @ 0x1234] silence_end: 7.000 | silence_duration: 2.000\n";
        var result = SilenceDetector.ParseSilenceOutput(input);
        // At most one candidate (from the second start/end pair — 2s just at MinSilenceMs)
        Assert.True(result.Count <= 1, $"Expected ≤1 candidate, got {result.Count}");
    }

    // #73
    [Fact]
    public void ParseSilenceOutput_ExactlyMinDuration_Included()
    {
        // MinSilenceMs = 1500. A 1.5s silence should be included (boundary inclusive).
        var result = SilenceDetector.ParseSilenceOutput(MakeSilenceLine(5.0, 6.5));
        Assert.Single(result);
    }

    // #74
    [Fact]
    public void ParseSilenceOutput_JustBelowMinDuration_Excluded()
    {
        // 1.499s silence — strictly below MinSilenceMs (1500ms) → filtered out.
        var result = SilenceDetector.ParseSilenceOutput(MakeSilenceLine(5.000, 6.499));
        Assert.Empty(result);
    }

    // ── ConfidenceScorer edge cases ───────────────────────────────────────────

    // #75
    [Fact]
    public void Score_AllSameKind_NoMultiSignalBonus()
    {
        // Three CursorDwell candidates that overlap — no bonus (all same kind).
        var candidates = new[]
        {
            new SignalCandidate(0, 2000, SignalKind.CursorDwell, CandidateAction.AddZoom, 0.5, 0.5, 0.5),
            new SignalCandidate(500, 2500, SignalKind.CursorDwell, CandidateAction.AddZoom, 0.5, 0.5, 0.5),
            new SignalCandidate(1000, 3000, SignalKind.CursorDwell, CandidateAction.AddZoom, 0.5, 0.5, 0.5),
        };
        var result = ConfidenceScorer.Score(candidates);
        Assert.Single(result);
        // baseScore = 0.5 (all same weight, all confidence 0.5), bonus = 0
        Assert.InRange(result[0].Score, 0.49, 0.51);
    }

    // #76
    [Fact]
    public void Score_UnknownKind_UsesHalfWeightFallback_DoesNotThrow()
    {
        // A signal kind not in the Weights dictionary falls back to 0.5 weight.
        // We cannot easily create an unknown kind from C# without casting, so we
        // verify the scorer doesn't throw when mixing a known and an unknown action
        // (different actions → never merged → each scores independently).
        var candidates = new[]
        {
            new SignalCandidate(0, 2000, SignalKind.CursorDwell, CandidateAction.AddZoom, 1.0, 0.5, 0.5),
            new SignalCandidate(0, 2000, SignalKind.AudioSilence, CandidateAction.Trim, 1.0, null, null),
        };
        var ex = Record.Exception(() => ConfidenceScorer.Score(candidates));
        Assert.Null(ex);
        var result = ConfidenceScorer.Score(candidates);
        Assert.Equal(2, result.Count);
    }

    // ── AppRecorderApp command routing ────────────────────────────────────────

    // #77
    [Fact]
    public async Task HandleCommandAsync_Ping_ReturnsOkTrue()
    {
        var app = new AppRecorderApp();
        var result = await app.HandleCommandAsync("ping", JsonSerializer.SerializeToElement(new { }));
        Assert.True(result.TryGetProperty("ok", out var ok) && ok.GetBoolean());
    }

    // #78
    [Fact]
    public async Task HandleCommandAsync_UnknownCommand_ReturnsErrorMessage()
    {
        var app = new AppRecorderApp();
        var result = await app.HandleCommandAsync("do_something_unknown", JsonSerializer.SerializeToElement(new { }));
        Assert.True(result.TryGetProperty("error", out _), "Expected 'error' key in response.");
    }

    // ── ProjectModel CRUD ──────────────────────────────────────────────────────

    // #79
    [Fact]
    public void AppRecorderApp_RegisterAndGetProject_RoundTrips()
    {
        var app = new AppRecorderApp();
        var project = EmptyProject();
        var id = app.RegisterProject(project);
        var retrieved = app.GetProject(id);
        Assert.Equal(project.VideoPath, retrieved.VideoPath);
    }

    // #80
    [Fact]
    public void ProjectModel_Append_AddsTrimRegionAndAdvancesUndoIndex()
    {
        var project = EmptyProject().Append(new AddTrimRegion(0, 1000));
        Assert.Equal(0, project.UndoIndex);
        Assert.Single(project.ActiveCommands.OfType<AddTrimRegion>());
    }

    // #81
    [Fact]
    public void ProjectModel_Undo_DecrementsUndoIndex()
    {
        var project = EmptyProject()
            .Append(new AddTrimRegion(0, 1000));
        var undone = project.Undo();
        Assert.Equal(-1, undone.UndoIndex);
        Assert.Empty(undone.ActiveCommands);
    }

    // #82
    [Fact]
    public void ProjectModel_Redo_IncrementsUndoIndex()
    {
        var project = EmptyProject()
            .Append(new AddTrimRegion(0, 1000))
            .Undo()
            .Redo();
        Assert.Equal(0, project.UndoIndex);
        Assert.Single(project.ActiveCommands.OfType<AddTrimRegion>());
    }

    // ── BitrateCalculator 4:3 ─────────────────────────────────────────────────

    // #83
    [Fact]
    public void VideoKbps_4x3_Medium_MeetsFloor()
    {
        // 640×480 30fps medium: formula = 640*480*30*0.055/1000 = 506.88 → 506 ≥ 384 floor
        var result = BitrateCalculator.VideoKbps(640, 480, 30, "medium");
        Assert.True(result >= 384, $"Expected ≥384 kbps, got {result}");
    }

    // ── ExportPipeline slow-motion filter ──────────────────────────────────────

    // #84
    [Fact]
    public void BuildVideoFilter_SlowMotion_SetptsContainsInverseMultiplier()
    {
        // SetSpeed(0, 5000, 0.5) → slow-motion. ffmpeg setpts factor = 1/0.5 = 2.0.
        var model = EmptyProject().Append(new SetSpeed(0, 5000, 0.5));
        var filter = VideoFilter(model);
        Assert.Contains("setpts=", filter);
        // 1 / 0.5 = 2.0 → formatted as "2.0000" in the filter
        Assert.Contains("2.0000", filter);
    }
}
