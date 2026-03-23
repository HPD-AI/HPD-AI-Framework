using System.Runtime.InteropServices;

namespace HPD.Sandbox.Local.Platforms;

/// <summary>
/// Detects the current operating system platform.
/// </summary>
internal static class PlatformDetector
{
    private static PlatformType? _cached;

    /// <summary>
    /// Gets the current platform type.
    /// </summary>
    public static PlatformType Current => _cached ??= Detect();

    private static PlatformType Detect()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return PlatformType.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return PlatformType.MacOS;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return PlatformType.Windows;

        throw new PlatformNotSupportedException("Unknown operating system");
    }
}

/// <summary>
/// Platform types supported by the sandbox.
/// </summary>
public enum PlatformType
{
    Linux,
    MacOS,
    Windows  // Not currently supported
}
