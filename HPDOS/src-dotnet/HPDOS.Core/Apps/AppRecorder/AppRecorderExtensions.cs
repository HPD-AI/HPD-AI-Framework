using HPD.Agent;
using HPDOS.Apps.AppRecorder.Export;
using HPDOS.Apps.AppRecorder.Toolkits;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace HPDOS.Apps.AppRecorder;

public static class AppRecorderExtensions
{
    /// <summary>
    /// Register HPD Video services and toolkits.
    /// Call from within <c>options.ConfigureAgent</c> in KestrelHostBuilder (or any host).
    /// </summary>
    /// <example>
    /// builder.Services.AddAppRecorder();
    /// // then inside ConfigureAgent:
    /// agentBuilder.AddAppRecorderToolkits(sp);
    /// </example>
    public static IServiceCollection AddAppRecorder(this IServiceCollection services)
    {
        services.AddSingleton<AppRecorderApp>();
        return services;
    }

    /// <summary>
    /// Register both <see cref="AppRecorderToolkit"/> and <see cref="VideoEditorToolkit"/>
    /// with the agent builder. Call inside <c>options.ConfigureAgent</c>.
    /// </summary>
    public static AgentBuilder AddAppRecorderToolkits(this AgentBuilder agentBuilder, IServiceProvider sp)
    {
        var app = sp.GetRequiredService<AppRecorderApp>();
        agentBuilder.WithToolkit(new AppRecorderToolkit(app));
        agentBuilder.WithToolkit(new VideoEditorToolkit(app));
        return agentBuilder;
    }

    /// <summary>
    /// Initialize the <see cref="AppRecorderApp"/> after the host is built.
    /// Call after <c>builder.Build()</c>.
    /// </summary>
    public static WebApplication UseAppRecorder(this WebApplication app)
    {
        var recorder = app.Services.GetRequiredService<AppRecorderApp>();
        // Backend is injected by the platform host (CLI or MAUI) via SetBackend().
        // Probe ffmpeg encoders in the background so capabilities are ready before first export.
        _ = FfmpegProber.ProbeAsync();
        return app;
    }
}
