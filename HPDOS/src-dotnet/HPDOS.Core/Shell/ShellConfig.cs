namespace HPDOS.Shell.Shell;

// Simple static config shared between GUIMode, MauiShellApp, and the WebView page.
// AOT-safe: no reflection, no DI at this layer.
public static class ShellConfig
{
    public static int Port { get; set; }
    public static CancellationToken ShutdownToken { get; set; }
    public static Action? RequestShutdown { get; set; }

    // Remote server support.
    // When set, the MAUI shell skips starting a local Kestrel and points the
    // WebView at this URL instead. Null means local mode (default).
    public static string? RemoteServerUrl { get; set; }

    // The URL the WebView (and browser mode) should load.
    // Uses the remote URL if configured, otherwise the local Kestrel.
    public static string ActiveUrl =>
        RemoteServerUrl ?? $"http://localhost:{Port}";

    // Wired by HPDOS.Shell to Preferences.Default.Set(...) so Core has no
    // MAUI dependency. Called whenever RemoteServerUrl changes.
    public static Action<string?>? SaveRemoteUrl { get; set; }

    // Auth mode.
    // True when HPD.Auth is registered and all agent API endpoints require a valid token.
    // Set by KestrelHostBuilder from appsettings "Auth:Enabled" or when running in
    // remote/serve mode. False by default — local single-user installs need no login.
    public static bool AuthEnabled { get; set; }

    // Serve mode — set by `hpdos serve` before calling KestrelHostBuilder.Build().
    // When true: Kestrel binds to 0.0.0.0 (public), host filtering opens up, auth is forced on.
    public static bool IsServeMode { get; set; }
}
