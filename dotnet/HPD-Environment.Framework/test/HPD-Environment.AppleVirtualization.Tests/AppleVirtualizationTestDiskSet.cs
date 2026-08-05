using HPD.Environment.AppleVirtualization;

namespace HPD.Environment.AppleVirtualization.Tests;

internal static class AppleVirtualizationTestDiskSet
{
    public static IReadOnlyList<AppleVirtualizationDiskAttachmentOptions> Create(
        string? systemPath,
        string? runtimePath = null,
        string? appDataPath = null)
    {
        string system = systemPath ?? string.Empty;
        return
        [
            Attachment(AppleVirtualizationDiskRole.System, system),
            Attachment(AppleVirtualizationDiskRole.Runtime, runtimePath ?? system + ".runtime"),
            Attachment(AppleVirtualizationDiskRole.AppData, appDataPath ?? system + ".apps"),
        ];
    }

    public static string? Path(
        AppleVirtualizationGuestImageOptions options,
        AppleVirtualizationDiskRole role) =>
        options.DiskAttachments.SingleOrDefault(attachment => attachment.Role == role)
            ?.DiskImagePath;

    public static AppleVirtualizationGuestImageOptions ReplaceSystem(
        AppleVirtualizationGuestImageOptions options,
        string systemPath) =>
        options with { DiskAttachments = Create(systemPath) };

    private static AppleVirtualizationDiskAttachmentOptions Attachment(
        AppleVirtualizationDiskRole role,
        string path) =>
        new()
        {
            Role = role,
            DiskImagePath = path,
            CachingMode = AppleVirtualizationDiskCachingMode.Cached,
            SynchronizationMode = AppleVirtualizationDiskSynchronizationMode.Full,
        };
}
