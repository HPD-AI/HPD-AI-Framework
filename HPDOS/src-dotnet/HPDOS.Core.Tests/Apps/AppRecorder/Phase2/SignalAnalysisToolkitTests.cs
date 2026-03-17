using HPDOS.Apps.AppRecorder;
using HPDOS.Apps.AppRecorder.Intelligence;
using HPDOS.Apps.AppRecorder.Project;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase2;

/// <summary>
/// Integration tests for SignalAnalysisToolkit — wiring to AppRecorderApp + source type guards.
/// </summary>
public class SignalAnalysisToolkitTests
{
    private static (AppRecorderApp app, SignalAnalysisToolkit tk, string projectId) Make(SourceType sourceType)
    {
        var app = new AppRecorderApp();
        var tk = new SignalAnalysisToolkit(app);
        var project = new ProjectModel
        {
            ProjectId = Guid.NewGuid().ToString("N"),
            SourceType = sourceType,
            VideoPath = "/tmp/nonexistent.mp4",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        app.RegisterProject(project);
        return (app, tk, project.ProjectId);
    }

    // ── #49 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDwellCandidates_NonScreenProject_ReturnsEmpty()
    {
        var (_, tk, projectId) = Make(SourceType.Camera);
        var result = await tk.GetDwellCandidates(projectId);
        Assert.Empty(result);
    }

    // ── #50 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDwellCandidates_NoSidecarFile_ReturnsEmpty()
    {
        // SourceType.Screen but no .cursor.json sidecar beside the (nonexistent) video
        var (_, tk, projectId) = Make(SourceType.Screen);
        var result = await tk.GetDwellCandidates(projectId);
        Assert.Empty(result);
    }

    // ── #51 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetClickBurstCandidates_NonScreenProject_ReturnsEmpty()
    {
        var (_, tk, projectId) = Make(SourceType.Import);
        var result = await tk.GetClickBurstCandidates(projectId);
        Assert.Empty(result);
    }

    // ── #52 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ScoreCandidates_PassesThroughToConfidenceScorer()
    {
        var (_, tk, _) = Make(SourceType.Import);
        var candidates = new List<SignalCandidate>
        {
            new(0, 3000, SignalKind.AudioSilence, CandidateAction.Trim, 0.5),
            new(10000, 13000, SignalKind.AudioSilence, CandidateAction.Trim, 0.2),
        };
        // minScore=0.35 → only the 0.5 candidate passes
        var result = tk.ScoreCandidates(candidates, minScore: 0.35);
        Assert.Single(result);
        Assert.True(result[0].Score >= 0.35);
    }

    // ── #53 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ScoreCandidates_DefaultMinScore_FiltersBelow0Point35()
    {
        var (_, tk, _) = Make(SourceType.Import);
        var candidates = new List<SignalCandidate>
        {
            new(0, 3000, SignalKind.AudioSilence, CandidateAction.Trim, 0.2),
            new(10000, 13000, SignalKind.AudioSilence, CandidateAction.Trim, 0.4),
            new(20000, 23000, SignalKind.AudioSilence, CandidateAction.Trim, 0.6),
        };
        var result = tk.ScoreCandidates(candidates); // default minScore = 0.35
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(r.Score >= 0.35));
    }
}
