using System.Reflection;
using HPD.Agent;
using HPDOS.Apps.AppRecorder;
using HPDOS.Apps.AppRecorder.Toolkits;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder;

public class AppRecorderExtensionsTests
{
    // #75 AddAppRecorder() registers AppRecorderApp as singleton
    [Fact]
    public void AddAppRecorder_RegistersAppRecorderAppAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddAppRecorder();

        var provider = services.BuildServiceProvider();
        var app = provider.GetRequiredService<AppRecorderApp>();
        Assert.NotNull(app);
    }

    // #76 Two calls to AddAppRecorder() → single singleton (same instance)
    [Fact]
    public void AddAppRecorder_TwoCalls_ReturnsSameSingleton()
    {
        var services = new ServiceCollection();
        services.AddAppRecorder();
        services.AddAppRecorder();

        var provider = services.BuildServiceProvider();
        var a = provider.GetRequiredService<AppRecorderApp>();
        var b = provider.GetRequiredService<AppRecorderApp>();
        Assert.Same(a, b);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Invoke the source-generated CreateToolkit for a toolkit instance and return the AIFunction names.
    /// This mirrors exactly what AgentBuilder.CreateFunctionsFromInstance does at runtime.
    /// </summary>
    private static List<string> GetFunctionNames<T>(T instance) where T : class
    {
        var asm = typeof(AppRecorderApp).Assembly;
        var regTypeName = $"{typeof(T).Name}Registration";
        var reg = asm.GetTypes().First(t => t.Name == regTypeName);
        var create = reg.GetMethod("CreateToolkit", BindingFlags.Public | BindingFlags.Static)!;
        var fns = (IEnumerable<AIFunction>)create.Invoke(null, new object?[] { instance, null })!;
        return fns.Select(f => f.Name).ToList();
    }

    // #77 AddAppRecorderToolkits() adds both toolkits — agent sees the expected tools
    [Fact]
    public void AddAppRecorderToolkits_RegistersBothToolkitsWithAgent()
    {
        var services = new ServiceCollection();
        services.AddAppRecorder();
        var sp = services.BuildServiceProvider();

        var agentBuilder = new AgentBuilder();
        agentBuilder.AddAppRecorderToolkits(sp);

        // Both toolkits should appear as instance registrations
        var names = agentBuilder._instanceRegistrations.Select(r => r.ToolTypeName).ToList();
        Assert.Contains("AppRecorderToolkit", names);
        Assert.Contains("VideoEditorToolkit", names);
    }

    // #78 UseAppRecorder() is chainable — returns the same WebApplication
    // WebApplication requires a full host — test the extension method directly via
    // the IServiceCollection path instead (UseAppRecorder has no side effects yet).
    [Fact]
    public void AddAppRecorder_ReturnsServiceCollection_Chainable()
    {
        var services = new ServiceCollection();
        var returned = services.AddAppRecorder();
        Assert.Same(services, returned);
    }

    // #79 Agent tool names — AppRecorderToolkit exposes ListSources, StartRecording, StopRecording
    [Fact]
    public void AppRecorderToolkit_ExposesExpectedToolNames()
    {
        var app = new AppRecorderApp();
        var toolkit = new AppRecorderToolkit(app);
        var names = GetFunctionNames(toolkit);

        Assert.Contains("ListSources", names);
        Assert.Contains("StartRecording", names);
        Assert.Contains("StopRecording", names);
    }

    // #79 Agent tool names — VideoEditorToolkit exposes all editing + export + project tools
    [Fact]
    public void VideoEditorToolkit_ExposesExpectedToolNames()
    {
        var app = new AppRecorderApp();
        var toolkit = new VideoEditorToolkit(app);
        var names = GetFunctionNames(toolkit);

        Assert.Contains("GetProjectState", names);
        Assert.Contains("AddZoomRegion", names);
        Assert.Contains("AddTrimRegion", names);
        Assert.Contains("SetSpeed", names);
        Assert.Contains("AddAnnotation", names);
        Assert.Contains("AddKeyframe", names);
        Assert.Contains("SplitAtPlayhead", names);
        Assert.Contains("AddTransition", names);
        Assert.Contains("GetTransitions", names);
        Assert.Contains("SetBackground", names);
        Assert.Contains("SetVisualOptions", names);
        Assert.Contains("SetCrop", names);
        Assert.Contains("Undo", names);
        Assert.Contains("Redo", names);
        Assert.Contains("ExportMp4", names);
        Assert.Contains("ExportGif", names);
        Assert.Contains("SaveProject", names);
        Assert.Contains("SaveProjectAs", names);
        Assert.Contains("LoadProject", names);
        Assert.Contains("ImportVideo", names);
        Assert.Contains("RevealInFinder", names);
    }

    // #79 Total tool count — no accidental additions or removals
    [Fact]
    public void AppRecorderToolkit_ExactToolCount()
    {
        var app = new AppRecorderApp();
        // 5 AIFunctions + 1 container tool = 6
        var names = GetFunctionNames(new AppRecorderToolkit(app));
        Assert.Equal(6, names.Count);
    }

    [Fact]
    public void VideoEditorToolkit_ExactToolCount()
    {
        var app = new AppRecorderApp();
        // 24 AIFunctions + 1 container tool = 25
        var names = GetFunctionNames(new VideoEditorToolkit(app));
        Assert.Equal(25, names.Count);
    }
}
