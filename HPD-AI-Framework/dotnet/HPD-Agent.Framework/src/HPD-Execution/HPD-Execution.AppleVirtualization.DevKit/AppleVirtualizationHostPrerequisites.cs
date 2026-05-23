namespace HPD.Execution.AppleVirtualization.DevKit;

using System.Runtime.InteropServices;

public sealed record AppleVirtualizationHostPrerequisiteReport
{
    public bool IsMacOS { get; init; }
    public bool CanRunAppleVirtualization => IsMacOS;
    public IReadOnlyList<AppleVirtualizationDevKitDiagnostic> Diagnostics { get; init; } = Array.Empty<AppleVirtualizationDevKitDiagnostic>();
}

public static class AppleVirtualizationHostPrerequisites
{
    public static AppleVirtualizationHostPrerequisiteReport InspectCurrentHost()
    {
        bool isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        if (isMacOS)
        {
            return new() { IsMacOS = true };
        }

        return new()
        {
            IsMacOS = false,
            Diagnostics =
            [
                AppleVirtualizationRealAcceptanceEnvironment.Error(
                    "AppleVirtualization.DevKit.HostPlatformUnsupported",
                    "Apple Virtualization real execution requires macOS. This DevKit can still parse, validate, and plan prepared-image metadata on this host.")
            ]
        };
    }
}
