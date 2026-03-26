namespace HPDOS.Core.Shell;

public static class HpdosDataPaths
{
    public static string Root =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "hpdos");

    public static string Sessions => Path.Combine(Root, "sessions");

    public static string Preferences => Path.Combine(Root, "preferences.json");

    /// <summary>
    /// Written by GUIMode when Kestrel starts; deleted on stop.
    /// Contains just the port number as a plain integer string.
    /// </summary>
    public static string ActivePortFile => Path.Combine(Root, "port");

    /// <summary>
    /// Root directory for HPDOS-managed app binaries.
    /// Each app lives in a subdirectory: Apps/{appId}/{executable}
    /// </summary>
    public static string Apps => Path.Combine(Root, "apps");
}
