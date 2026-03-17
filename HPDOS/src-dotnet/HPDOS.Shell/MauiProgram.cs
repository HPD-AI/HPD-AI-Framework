using HPDOS.Apps.AppRecorder;
using HPDOS.Shell.Bridge;
using HPDOS.Shell.Server;
using HPDOS.Shell.Shell;
using Microsoft.Maui.LifecycleEvents;

namespace HPDOS.Shell;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if DEBUG
        builder.Services.AddHybridWebViewDeveloperTools();
#endif

        builder.Services.AddSingleton<HPDOSBridge>();

        ShellConfig.SaveRemoteUrl = url =>
            Preferences.Default.Set("remote_server_url", url ?? "");

        var saved = Preferences.Default.Get<string>("remote_server_url", "");
        ShellConfig.RemoteServerUrl = string.IsNullOrWhiteSpace(saved) ? null : saved;

        builder.ConfigureLifecycleEvents(lifecycle =>
        {
#if MACCATALYST
            lifecycle.AddiOS(ios => ios.FinishedLaunching((app, options) =>
            {
                if (ShellConfig.RemoteServerUrl is null)
                {
                    _ = GUIMode.StartServerAsync().ContinueWith(t =>
                    {
                        // Wire the native recording backend once the DI container is ready.
                        if (t.IsCompletedSuccessfully && GUIMode.Services is { } sp
                            && OperatingSystem.IsMacCatalystVersionAtLeast(18, 2))
                        {
                            var recorderApp = sp.GetRequiredService<AppRecorderApp>();
                            recorderApp.SetBackend(new HPDOS.Shell.Recording.NativeRecordingBackend());
                        }
                    }, TaskScheduler.Default);
                }
                return true;
            }));
#elif WINDOWS
            lifecycle.AddWindows(windows => windows.OnLaunched((app, args) =>
            {
                if (ShellConfig.RemoteServerUrl is null)
                    _ = GUIMode.StartServerAsync();
            }));
#endif
        });

        return builder.Build();
    }
}
