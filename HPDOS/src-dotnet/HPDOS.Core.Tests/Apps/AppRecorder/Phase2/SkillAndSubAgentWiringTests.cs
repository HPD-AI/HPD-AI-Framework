using HPD.Agent;
using HPD.MultiAgent;
using HPDOS.Apps.AppRecorder;
using HPDOS.Apps.AppRecorder.Intelligence;
using HPDOS.Apps.AppRecorder.Toolkits;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase2;

/// <summary>
/// Tests for Skill wiring (#54–59), SubAgent wiring (#60–67), and MultiAgent wiring (#68–71).
/// These are pure factory/metadata tests — no AI inference required.
/// </summary>
public class SkillAndSubAgentWiringTests
{
    private static AppRecorderToolkit MakeRecorderToolkit()
    {
        var app = new AppRecorderApp();
        return new AppRecorderToolkit(app);
    }

    private static VideoEditorToolkit MakeEditorToolkit()
    {
        var app = new AppRecorderApp();
        return new VideoEditorToolkit(app);
    }

    // ── Skill wiring (#54–59) ──────────────────────────────────────────────────

    // #54
    [Fact]
    public void RecordAndExport_SkillFactory_HasNonEmptyFunctionResult()
    {
        var skill = MakeRecorderToolkit().RecordAndExport();
        Assert.NotNull(skill.FunctionResult);
        Assert.NotEmpty(skill.FunctionResult!);
    }

    // #55
    [Fact]
    public void RecordAndExport_SkillFactory_NameAndDescriptionSet()
    {
        var skill = MakeRecorderToolkit().RecordAndExport();
        Assert.Equal("RecordAndExport", skill.Name);
        Assert.NotEmpty(skill.Description);
    }

    // #56
    [Fact]
    public void QuickShare_SkillFactory_HasNonEmptyFunctionResult()
    {
        var skill = MakeRecorderToolkit().QuickShare();
        Assert.NotNull(skill.FunctionResult);
        Assert.NotEmpty(skill.FunctionResult!);
    }

    // #57
    [Fact]
    public void QuickShare_SkillFactory_NameAndDescriptionSet()
    {
        var skill = MakeRecorderToolkit().QuickShare();
        Assert.Equal("QuickShare", skill.Name);
        Assert.NotEmpty(skill.Description);
    }

    // #58
    [Fact]
    public void RecordAndExport_FunctionResult_MentionsSmartEdit()
    {
        var skill = MakeRecorderToolkit().RecordAndExport();
        Assert.Contains("SmartEdit", skill.FunctionResult, StringComparison.OrdinalIgnoreCase);
    }

    // #59
    [Fact]
    public void QuickShare_FunctionResult_MentionsGetSilenceCandidates()
    {
        var skill = MakeRecorderToolkit().QuickShare();
        Assert.Contains("GetSilenceCandidates", skill.FunctionResult, StringComparison.OrdinalIgnoreCase);
    }

    // ── SubAgent wiring (#60–67) ───────────────────────────────────────────────

    // #60
    [Fact]
    public void SmartEdit_SubAgent_NameIsSmartEdit()
    {
        var sub = MakeEditorToolkit().SmartEdit();
        Assert.Equal("SmartEdit", sub.Name);
    }

    // #61
    [Fact]
    public void SmartEdit_SubAgent_HasSystemInstructions()
    {
        var sub = MakeEditorToolkit().SmartEdit();
        Assert.NotEmpty(sub.AgentConfig.SystemInstructions ?? "");
    }

    // #62
    [Fact]
    public void SmartEdit_SubAgent_MaxAgenticIterationsIs15()
    {
        var sub = MakeEditorToolkit().SmartEdit();
        Assert.Equal(15, sub.AgentConfig.MaxAgenticIterations);
    }

    // #63
    [Fact]
    public void SmartEdit_SubAgent_ToolkitTypesIncludeSignalAnalysis()
    {
        var sub = MakeEditorToolkit().SmartEdit();
        Assert.Contains(typeof(SignalAnalysisToolkit), sub.ToolkitTypes);
    }

    // #64
    [Fact]
    public void SmartEdit_SubAgent_ToolkitTypesIncludeVideoEditor()
    {
        var sub = MakeEditorToolkit().SmartEdit();
        Assert.Contains(typeof(VideoEditorToolkit), sub.ToolkitTypes);
    }

    // #65
    [Fact]
    public void HighlightDetector_SubAgent_NameIsHighlightDetector()
    {
        var sub = MakeEditorToolkit().HighlightDetector();
        Assert.Equal("HighlightDetector", sub.Name);
    }

    // #66
    [Fact]
    public void HighlightDetector_SubAgent_MaxAgenticIterationsIs10()
    {
        var sub = MakeEditorToolkit().HighlightDetector();
        Assert.Equal(10, sub.AgentConfig.MaxAgenticIterations);
    }

    // #67
    [Fact]
    public void HighlightDetector_SubAgent_SystemInstructions_MentionsDoNotApplyEdits()
    {
        var sub = MakeEditorToolkit().HighlightDetector();
        var instructions = sub.AgentConfig.SystemInstructions ?? "";
        Assert.Contains("NOT", instructions, StringComparison.Ordinal);
        Assert.Contains("edits", instructions, StringComparison.OrdinalIgnoreCase);
    }

    // ── MultiAgent wiring (#68–71) ─────────────────────────────────────────────

    // #68
    [Fact]
    public async Task ExportAll_ReturnsWorkflowInstance()
    {
        var instance = await MakeEditorToolkit().ExportAll();
        Assert.NotNull(instance);
    }

    // #69
    [Fact]
    public async Task ExportAll_WorkflowName_IsParallelExport()
    {
        var instance = await MakeEditorToolkit().ExportAll();
        Assert.Equal("ParallelExport", instance.WorkflowName);
    }

    // #70
    [Fact]
    public async Task ExportAll_ContainsMp4ExporterAgent()
    {
        var instance = await MakeEditorToolkit().ExportAll();
        var mermaid = instance.ExportConfigJson();
        Assert.Contains("mp4-exporter", mermaid, StringComparison.OrdinalIgnoreCase);
    }

    // #71
    [Fact]
    public async Task ExportAll_ContainsGifExporterAgent()
    {
        var instance = await MakeEditorToolkit().ExportAll();
        var mermaid = instance.ExportConfigJson();
        Assert.Contains("gif-exporter", mermaid, StringComparison.OrdinalIgnoreCase);
    }
}
