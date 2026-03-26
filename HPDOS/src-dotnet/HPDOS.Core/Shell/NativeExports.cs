using System.Runtime.InteropServices;
using HPDOS.Shell.Shell;

namespace HPDOS.Core.Shell;

/// <summary>
/// Native AOT exports for loading HPDOS.Core as a dylib from Tauri.
/// </summary>
public static class NativeExports
{
    /// <summary>
    /// Starts the Kestrel backend on a background thread. Non-blocking.
    /// Writes the bound port to the port file once ready.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "hpdos_start")]
    public static void Start()
    {
        // When loaded as a dylib, AppContext.BaseDirectory points at the host binary's
        // directory, not ours. Tauri sets HPDOS_BASE_DIR to the dylib's directory so
        // KestrelHostBuilder can find wwwroot and appsettings.json next to the dylib.
        var dylibDir = Environment.GetEnvironmentVariable("HPDOS_BASE_DIR");
        if (!string.IsNullOrEmpty(dylibDir))
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", dylibDir + Path.DirectorySeparatorChar);

        Thread thread = new(() =>
        {
            GUIMode.StartServerAsync().GetAwaiter().GetResult();
        });
        thread.IsBackground = true;
        thread.Start();
    }

    /// <summary>
    /// Stops the Kestrel backend gracefully and deletes the port file.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "hpdos_stop")]
    public static void Stop()
    {
        GUIMode.StopServerAsync().GetAwaiter().GetResult();
    }
}
