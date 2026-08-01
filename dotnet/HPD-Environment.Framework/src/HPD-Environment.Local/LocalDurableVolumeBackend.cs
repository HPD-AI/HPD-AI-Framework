namespace HPD.Environment.Local;

using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal interface ILocalDurableVolumeBackend : IDisposable
{
    bool Exists(string logicalId);

    string Create(
        string logicalId,
        long maximumBytes,
        string filesystemIdentity);

    string OpenExisting(
        string logicalId,
        long maximumBytes,
        string filesystemIdentity);

    void Erase(string logicalId, string filesystemIdentity);

    long MeasurePhysicalAllocatedBytes(string logicalId);

    void ReleaseAll();
}

internal static class LocalDurableVolumeBackend
{
    public static ILocalDurableVolumeBackend Create(
        LocalEnvironmentProviderOptions options,
        string storageRoot) =>
        options.DurableVolumeBackend switch
        {
            LocalDurableVolumeBackendKind.PlatformHardQuota
                when OperatingSystem.IsMacOS() =>
                new MacSparseBundleDurableVolumeBackend(storageRoot),
            LocalDurableVolumeBackendKind.PlatformHardQuota
                when OperatingSystem.IsLinux() =>
                new UnsupportedHardQuotaDurableVolumeBackend(
                    "the Linux Local provider requires a qualified project-quota backend"),
            LocalDurableVolumeBackendKind.PlatformHardQuota =>
                new UnsupportedHardQuotaDurableVolumeBackend(
                    "no production hard-quota durable-volume backend is available on this platform"),
            LocalDurableVolumeBackendKind.TestDirectory =>
                new TestDirectoryDurableVolumeBackend(storageRoot),
            _ => throw new InvalidOperationException(
                "LocalEnvironment.HardQuotaBackendInvalid: the configured durable-volume backend is not recognized."),
        };
}

internal sealed class UnsupportedHardQuotaDurableVolumeBackend(
    string reason) : ILocalDurableVolumeBackend
{
    public bool Exists(string logicalId)
    {
        _ = logicalId;
        throw Error();
    }

    public string Create(
        string logicalId,
        long maximumBytes,
        string filesystemIdentity)
    {
        _ = logicalId;
        _ = maximumBytes;
        _ = filesystemIdentity;
        throw Error();
    }

    public string OpenExisting(
        string logicalId,
        long maximumBytes,
        string filesystemIdentity)
    {
        _ = logicalId;
        _ = maximumBytes;
        _ = filesystemIdentity;
        throw Error();
    }

    public void Erase(string logicalId, string filesystemIdentity)
    {
        _ = logicalId;
        _ = filesystemIdentity;
        throw Error();
    }

    public long MeasurePhysicalAllocatedBytes(string logicalId)
    {
        _ = logicalId;
        throw Error();
    }

    public void ReleaseAll()
    {
    }

    public void Dispose()
    {
    }

    private InvalidOperationException Error() =>
        new(
            "LocalEnvironment.HardQuotaBackendUnavailable: " +
            reason +
            "; observation-only directories are not a production fallback.");
}

internal sealed class TestDirectoryDurableVolumeBackend :
    ILocalDurableVolumeBackend
{
    private readonly string _volumesRoot;

    public TestDirectoryDurableVolumeBackend(string storageRoot)
    {
        _volumesRoot = ProviderStateDirectory.EnsurePrivateRoot(
            Path.Combine(storageRoot, "volumes"),
            "LocalEnvironment.StorageRootInvalid");
    }

    public bool Exists(string logicalId) =>
        Directory.Exists(VolumePath(logicalId));

    public string Create(
        string logicalId,
        long maximumBytes,
        string filesystemIdentity)
    {
        _ = maximumBytes;
        _ = filesystemIdentity;
        string path = VolumePath(logicalId);
        Directory.CreateDirectory(path);
        return path;
    }

    public string OpenExisting(
        string logicalId,
        long maximumBytes,
        string filesystemIdentity)
    {
        _ = maximumBytes;
        _ = filesystemIdentity;
        string path = VolumePath(logicalId);
        if (!Directory.Exists(path))
            throw Missing();
        return path;
    }

    public void Erase(string logicalId, string filesystemIdentity)
    {
        _ = filesystemIdentity;
        string path = VolumePath(logicalId);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    public long MeasurePhysicalAllocatedBytes(string logicalId)
    {
        string path = VolumePath(logicalId);
        if (!Directory.Exists(path))
            return 0;
        long total = 0;
        foreach (string file in Directory.EnumerateFiles(
                     path,
                     "*",
                     SearchOption.AllDirectories))
            total = checked(total + new FileInfo(file).Length);
        return total;
    }

    public void ReleaseAll()
    {
    }

    public void Dispose()
    {
    }

    private string VolumePath(string logicalId) =>
        System.IO.Path.Combine(_volumesRoot, logicalId);

    private static InvalidOperationException Missing() =>
        new(
            "Environment.Storage.IntegrityCheckRequired: authoritative durable-volume content is missing.");
}

internal sealed class MacSparseBundleDurableVolumeBackend :
    ILocalDurableVolumeBackend
{
    private const int MaximumOutputBytes = 64 * 1024;
    private static readonly TimeSpan CommandTimeout =
        TimeSpan.FromSeconds(30);
    private readonly string _imagesRoot;
    private readonly string _mountsRoot;
    private readonly object _gate = new();
    private readonly Dictionary<string, FileStream> _leases =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _mountedLogicalIds =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte>
        ProcessLeases = new(StringComparer.Ordinal);

    public MacSparseBundleDurableVolumeBackend(string storageRoot)
    {
        _imagesRoot = ProviderStateDirectory.EnsurePrivateRoot(
            Path.Combine(storageRoot, "volume-images"),
            "LocalEnvironment.StorageRootInvalid");
        _mountsRoot = ProviderStateDirectory.EnsurePrivateRoot(
            Path.Combine(storageRoot, "volumes"),
            "LocalEnvironment.StorageRootInvalid");
    }

    public bool Exists(string logicalId) =>
        Directory.Exists(ImagePath(logicalId));

    public string Create(
        string logicalId,
        long maximumBytes,
        string filesystemIdentity)
    {
        if (maximumBytes < 16L * 1024 * 1024)
            throw new InvalidOperationException(
                "LocalEnvironment.VolumeCapacityInvalid: the macOS hard-quota backend requires at least 16 MiB.");
        string image = ImagePath(logicalId);
        AcquireLease(logicalId);
        if (Directory.Exists(image))
        {
            ReleaseLease(logicalId);
            throw new InvalidOperationException(
                "Environment.Storage.LegacyLayoutRejected: a Local durable-volume image already exists without accepted identity.");
        }
        try
        {
            Run(
                "/usr/bin/hdiutil",
                [
                    "create",
                    "-sectors",
                    checked((maximumBytes + 511) / 512)
                        .ToString(
                            System.Globalization
                                .CultureInfo.InvariantCulture),
                    "-type",
                    "SPARSEBUNDLE",
                    "-fs",
                    "Case-sensitive APFS",
                    "-volname",
                    VolumeName(logicalId),
                    "-nospotlight",
                    image,
                ]);
            string content = Attach(logicalId);
            WriteIdentityMarker(
                Path.GetDirectoryName(content)!,
                filesystemIdentity,
                maximumBytes);
            return content;
        }
        catch
        {
            bool detached = TryDetach(logicalId);
            if (detached && Directory.Exists(image))
                Directory.Delete(image, recursive: true);
            throw;
        }
    }

    public string OpenExisting(
        string logicalId,
        long maximumBytes,
        string filesystemIdentity)
    {
        AcquireLease(logicalId);
        if (!Directory.Exists(ImagePath(logicalId)))
        {
            ReleaseLease(logicalId);
            throw new InvalidOperationException(
                "Environment.Storage.IntegrityCheckRequired: authoritative Local sparse-volume image is missing.");
        }
        try
        {
            string content = Attach(logicalId);
            ValidateIdentityMarker(
                Path.GetDirectoryName(content)!,
                filesystemIdentity,
                maximumBytes);
            return content;
        }
        catch
        {
            _ = TryDetach(logicalId);
            throw;
        }
    }

    public void Erase(string logicalId, string filesystemIdentity)
    {
        AcquireLease(logicalId);
        string mount = MountPath(logicalId);
        if (IsMounted(mount))
        {
            ValidateIdentityMarker(
                mount,
                filesystemIdentity,
                expectedMaximumBytes: null);
            Run("/usr/bin/hdiutil", ["detach", mount]);
        }
        lock (_gate)
            _mountedLogicalIds.Remove(logicalId);
        string image = ImagePath(logicalId);
        if (Directory.Exists(image))
            Directory.Delete(image, recursive: true);
        if (Directory.Exists(mount))
            DeleteReleasedMountPoint(mount);
        ReleaseLease(logicalId);
        string leasePath = LeasePath(logicalId);
        if (File.Exists(leasePath))
            File.Delete(leasePath);
    }

    public long MeasurePhysicalAllocatedBytes(string logicalId)
    {
        string image = ImagePath(logicalId);
        if (!Directory.Exists(image))
            return 0;
        string first = Run(
            "/usr/bin/du",
            ["-sk", image]).StandardOutput.Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "";
        if (!long.TryParse(
                first,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long kibibytes) ||
            kibibytes < 0)
            throw new InvalidOperationException(
                "LocalEnvironment.StorageMeasurementFailed: sparse-volume allocated-byte evidence was invalid.");
        return checked(kibibytes * 1024);
    }

    public void ReleaseAll()
    {
        string[] mounted;
        lock (_gate)
            mounted = [.. _mountedLogicalIds];
        List<Exception>? failures = null;
        var failedLogicalIds = new HashSet<string>(
            StringComparer.Ordinal);
        foreach (string logicalId in mounted)
        {
            try
            {
                string mount = MountPath(logicalId);
                if (IsMounted(mount))
                    Run("/usr/bin/hdiutil", ["detach", mount]);
                if (Directory.Exists(mount))
                    DeleteReleasedMountPoint(mount);
                lock (_gate)
                    _mountedLogicalIds.Remove(logicalId);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
                failedLogicalIds.Add(logicalId);
            }
        }
        string[] leased;
        lock (_gate)
            leased = [.. _leases.Keys];
        foreach (string logicalId in leased)
        {
            if (!failedLogicalIds.Contains(logicalId))
                ReleaseLease(logicalId);
        }
        if (failures is not null)
            throw new AggregateException(
                "LocalEnvironment.StorageReleaseFailed: one or more mounted durable volumes could not be released.",
                failures);
    }

    public void Dispose()
    {
        ReleaseAll();
    }

    private string Attach(string logicalId)
    {
        string mount = MountPath(logicalId);
        Directory.CreateDirectory(mount);
        if (!IsMounted(mount))
        {
            if (Directory.EnumerateFileSystemEntries(mount).Any())
                throw new InvalidOperationException(
                    "Environment.Storage.IntegrityCheckRequired: the Local volume mountpoint contains unowned content.");
            Run(
                "/usr/bin/hdiutil",
                [
                    "attach",
                    ImagePath(logicalId),
                    "-mountpoint",
                    mount,
                    "-nobrowse",
                    "-noautoopen",
                    "-owners",
                    "on",
                ]);
        }
        lock (_gate)
            _mountedLogicalIds.Add(logicalId);
        string content = Path.Combine(mount, "data");
        Directory.CreateDirectory(content);
        return content;
    }

    private bool IsMounted(string mount)
    {
        if (!Directory.Exists(mount))
            return false;
        string canonical = Run(
            "/bin/realpath",
            [mount]).StandardOutput.Trim();
        if (canonical.Contains('\n') ||
            canonical.Contains('\r'))
            throw new InvalidOperationException(
                "LocalEnvironment.StorageRootInvalid: the canonical volume mountpoint is not one bounded path.");
        string marker = " on " + canonical + " (";
        string mounted = Run(
            "/sbin/mount",
            []).StandardOutput;
        return mounted
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Contains(
                marker,
                StringComparison.Ordinal));
    }

    private bool TryDetach(string logicalId)
    {
        try
        {
            string mount = MountPath(logicalId);
            if (IsMounted(mount))
                Run("/usr/bin/hdiutil", ["detach", mount]);
            lock (_gate)
                _mountedLogicalIds.Remove(logicalId);
            return true;
        }
        catch
        {
            // Creation is already failing. The caller retains the original
            // failure and reconstruction will reject an unproven image.
            return false;
        }
    }

    private static void DeleteReleasedMountPoint(string mount)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (true)
        {
            try
            {
                Directory.Delete(mount, recursive: true);
                return;
            }
            catch (IOException) when (
                DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static void WriteIdentityMarker(
        string mount,
        string filesystemIdentity,
        long maximumBytes)
    {
        string marker = Path.Combine(mount, ".hpd-volume-identity");
        if (File.Exists(marker))
            throw new InvalidOperationException(
                "Environment.Storage.IntegrityCheckRequired: a Local volume identity marker already exists.");
        string temporary = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = new UTF8Encoding(false, true)
                .GetBytes(
                filesystemIdentity + "\n" +
                maximumBytes.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) +
                "\n");
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, marker);
            FlushDirectory(mount);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void ValidateIdentityMarker(
        string mount,
        string filesystemIdentity,
        long? expectedMaximumBytes)
    {
        string marker = Path.Combine(mount, ".hpd-volume-identity");
        var info = new FileInfo(marker);
        if (!info.Exists ||
            info.LinkTarget is not null ||
            info.Length is <= 0 or > 8192)
            throw new InvalidOperationException(
                "Environment.Storage.IntegrityCheckRequired: the Local volume identity marker is missing or unsafe.");
        string[] lines = File.ReadAllLines(
            marker,
            new UTF8Encoding(false, true));
        if (lines.Length != 2 ||
            !string.Equals(
                lines[0],
                filesystemIdentity,
                StringComparison.Ordinal) ||
            !long.TryParse(
                lines[1],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long maximumBytes) ||
            maximumBytes <= 0 ||
            (expectedMaximumBytes is not null &&
             maximumBytes != expectedMaximumBytes.Value))
            throw new InvalidOperationException(
                "Environment.Storage.IntegrityCheckRequired: the mounted Local volume does not match accepted identity and capacity.");
    }

    private string ImagePath(string logicalId) =>
        Path.Combine(_imagesRoot, logicalId + ".sparsebundle");

    private string LeasePath(string logicalId) =>
        Path.Combine(_imagesRoot, logicalId + ".lease");

    private string MountPath(string logicalId) =>
        Path.Combine(_mountsRoot, logicalId);

    private static string VolumeName(string logicalId)
    {
        string bounded = logicalId.Length <= 48
            ? logicalId
            : logicalId[..48];
        return "HPD-" + bounded;
    }

    private void AcquireLease(string logicalId)
    {
        lock (_gate)
        {
            if (_leases.ContainsKey(logicalId))
                return;
        }

        string path = LeasePath(logicalId);
        if (!ProcessLeases.TryAdd(path, 0))
            throw OwnershipUnavailable();
        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                4096,
                FileOptions.WriteThrough);
            if (stream.Length == 0)
            {
                stream.SetLength(1);
                stream.Flush(flushToDisk: true);
            }
            FileInfo info = new(path);
            if (!info.Exists ||
                info.LinkTarget is not null ||
                info.Attributes.HasFlag(
                    FileAttributes.ReparsePoint))
                throw new InvalidOperationException(
                    "Environment.Storage.IntegrityCheckRequired: the Local volume ownership lease is linked or unsafe.");
            AcquireExclusiveLease(stream);
            VerifyLeasePathStillNamesLockedFile(
                path,
                stream);
            lock (_gate)
            {
                if (_leases.ContainsKey(logicalId))
                {
                    ReleaseExclusiveLease(stream);
                    stream.Dispose();
                }
                else
                {
                    _leases.Add(logicalId, stream);
                    stream = null;
                }
            }
        }
        catch
        {
            stream?.Dispose();
            ProcessLeases.TryRemove(path, out _);
            throw;
        }
    }

    private void ReleaseLease(string logicalId)
    {
        FileStream? stream;
        lock (_gate)
        {
            if (!_leases.Remove(logicalId, out stream))
                return;
        }
        try
        {
            ReleaseExclusiveLease(stream);
        }
        finally
        {
            stream.Dispose();
            ProcessLeases.TryRemove(
                LeasePath(logicalId),
                out _);
        }
    }

    private static void AcquireExclusiveLease(FileStream stream)
    {
        if (NativeFlock(
                Descriptor(stream),
                LockExclusive | LockNonBlocking) != 0)
            throw OwnershipUnavailable();
    }

    private static void ReleaseExclusiveLease(FileStream stream)
    {
        if (NativeFlock(Descriptor(stream), LockUnlock) != 0)
            throw new IOException(
                "Environment.Lifecycle.HelperOwnershipUnknown: the Local durable-volume ownership lease could not be released.",
                new Win32Exception(
                    Marshal.GetLastPInvokeError()));
    }

    private static void VerifyLeasePathStillNamesLockedFile(
        string path,
        FileStream stream)
    {
        if (NativeFileStatus(
                Descriptor(stream),
                out DarwinFileStatus opened) != 0 ||
            NativeLinkStatus(path, out DarwinFileStatus named) != 0)
            throw new IOException(
                "Environment.Storage.IntegrityCheckRequired: the Local volume ownership lease identity could not be observed.",
                new Win32Exception(
                    Marshal.GetLastPInvokeError()));
        if (opened.Device != named.Device ||
            opened.Inode != named.Inode ||
            opened.LinkCount != 1 ||
            named.LinkCount != 1)
            throw new InvalidOperationException(
                "Environment.Storage.IntegrityCheckRequired: the Local volume ownership lease path was replaced or linked during acquisition.");
    }

    private static int Descriptor(FileStream stream) =>
        checked((int)stream.SafeFileHandle
            .DangerousGetHandle());

    private static InvalidOperationException OwnershipUnavailable() =>
        new(
            "Environment.Lifecycle.HelperOwnershipUnknown: another Local provider incarnation owns the durable volume.");

    private static CommandResult Run(
        string executable,
        IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ??
            throw new InvalidOperationException(
                "LocalEnvironment.HardQuotaCommandFailed: the platform storage command did not start.");
        Task<string> stdout = ReadBoundedAsync(
            process.StandardOutput);
        Task<string> stderr = ReadBoundedAsync(
            process.StandardError);
        if (!process.WaitForExit(
                checked((int)CommandTimeout.TotalMilliseconds)))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            throw new InvalidOperationException(
                "LocalEnvironment.HardQuotaCommandTimedOut: the platform storage command exceeded its deadline.");
        }
        Task.WaitAll(stdout, stderr);
        var result = new CommandResult(
            stdout.Result,
            stderr.Result);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                "LocalEnvironment.HardQuotaCommandFailed: " +
                BoundedMessage(result.StandardError));
        return result;
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader)
    {
        char[] buffer = new char[4096];
        var value = new StringBuilder();
        while (true)
        {
            int read = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0)
                return value.ToString();
            if (value.Length + read > MaximumOutputBytes)
                throw new InvalidOperationException(
                    "LocalEnvironment.HardQuotaCommandOutputExceeded: platform storage output exceeded its bound.");
            value.Append(buffer, 0, read);
        }
    }

    private static string BoundedMessage(string value)
    {
        string normalized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length <= 1024
            ? normalized
            : normalized[..1024];
    }

    private static void FlushDirectory(string path)
    {
        int descriptor = OpenDirectoryForSync(path, 0);
        if (descriptor < 0)
            throw new IOException(
                "LocalEnvironment.StorageDurabilityFailed: the volume directory could not be opened for synchronization.",
                new Win32Exception(
                    Marshal.GetLastPInvokeError()));
        try
        {
            if (SyncFileDescriptor(descriptor) != 0)
                throw new IOException(
                    "LocalEnvironment.StorageDurabilityFailed: the volume directory synchronization failed.",
                    new Win32Exception(
                        Marshal.GetLastPInvokeError()));
        }
        finally
        {
            _ = CloseFileDescriptor(descriptor);
        }
    }

    [DllImport(
        "libc",
        EntryPoint = "open",
        SetLastError = true,
        CharSet = CharSet.Ansi)]
    private static extern int OpenDirectoryForSync(
        string path,
        int flags);

    [DllImport(
        "libc",
        EntryPoint = "fsync",
        SetLastError = true)]
    private static extern int SyncFileDescriptor(
        int descriptor);

    [DllImport(
        "libc",
        EntryPoint = "close",
        SetLastError = true)]
    private static extern int CloseFileDescriptor(
        int descriptor);

    private const int LockExclusive = 2;
    private const int LockNonBlocking = 4;
    private const int LockUnlock = 8;

    [DllImport(
        "libc",
        EntryPoint = "flock",
        SetLastError = true)]
    private static extern int NativeFlock(
        int descriptor,
        int operation);

    [DllImport(
        "libc",
        EntryPoint = "fstat",
        SetLastError = true)]
    private static extern int NativeFileStatus(
        int descriptor,
        out DarwinFileStatus status);

    [DllImport(
        "libc",
        EntryPoint = "lstat",
        SetLastError = true,
        CharSet = CharSet.Ansi)]
    private static extern int NativeLinkStatus(
        string path,
        out DarwinFileStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct DarwinFileStatus
    {
        public int Device;
        public ushort Mode;
        public ushort LinkCount;
        public ulong Inode;
        public uint UserId;
        public uint GroupId;
        public int SpecialDevice;
        public long AccessTimeSeconds;
        public long AccessTimeNanoseconds;
        public long ModificationTimeSeconds;
        public long ModificationTimeNanoseconds;
        public long ChangeTimeSeconds;
        public long ChangeTimeNanoseconds;
        public long BirthTimeSeconds;
        public long BirthTimeNanoseconds;
        public long Size;
        public long Blocks;
        public int BlockSize;
        public uint Flags;
        public uint Generation;
        public int Spare;
        public long Spare0;
        public long Spare1;
    }

    private sealed record CommandResult(
        string StandardOutput,
        string StandardError);
}
