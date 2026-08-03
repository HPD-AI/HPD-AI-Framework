using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HPD.Base.Sqlite;

internal sealed class SqliteAdministrationPathGuard
{
    private readonly SqliteFileIdentity _directoryIdentity;
    private readonly SqliteFileIdentity? _databaseIdentity;

    private SqliteAdministrationPathGuard(
        string databasePath,
        string directoryPath,
        SqliteFileIdentity directoryIdentity,
        SqliteFileIdentity? databaseIdentity)
    {
        DatabasePath = databasePath;
        DirectoryPath = directoryPath;
        _directoryIdentity = directoryIdentity;
        _databaseIdentity = databaseIdentity;
    }

    public string DatabasePath { get; }
    public string DirectoryPath { get; }

    public static SqliteAdministrationPathGuard Capture(string databasePath, bool activeRequired = true)
    {
        string full = Path.GetFullPath(databasePath);
        string directory = Path.GetDirectoryName(full)
            ?? throw new IOException("SQLite administration destination directory is unavailable.");
        ValidatePathChain(directory);
        SqliteFileIdentity directoryIdentity = SqliteNativeFileIdentity.Read(directory, directory: true);
        SqliteFileIdentity? databaseIdentity = null;
        if (File.Exists(full))
        {
            ValidateRegularFile(full);
            databaseIdentity = SqliteNativeFileIdentity.Read(full, directory: false);
        }
        else if (activeRequired)
        {
            throw new FileNotFoundException("SQLite administration database file is unavailable.", full);
        }
        if (databaseIdentity is { } activeIdentity && directoryIdentity.Device != activeIdentity.Device)
            throw new IOException("SQLite administration destination is not on the provider-owned filesystem.");
        return new SqliteAdministrationPathGuard(full, directory, directoryIdentity, databaseIdentity);
    }

    public void RevalidateActive()
    {
        if (_databaseIdentity is not { } expected)
            throw new IOException("SQLite administration did not capture an active database identity.");
        RevalidateDirectory();
        ValidateRegularFile(DatabasePath);
        RequireIdentity(DatabasePath, directory: false, expected);
    }

    public void RevalidateDirectory()
    {
        ValidatePathChain(DirectoryPath);
        RequireIdentity(DirectoryPath, directory: true, _directoryIdentity);
    }

    public void ValidateReplacementActive()
    {
        RevalidateDirectory();
        ValidateRegularFile(DatabasePath);
        if (SqliteNativeFileIdentity.Read(DatabasePath, directory: false).Device != _directoryIdentity.Device)
            throw new IOException("SQLite replacement is not on the provider-owned filesystem.");
    }

    public void ValidateSibling(string path, bool mustExist, bool expectedDatabaseIdentity = false)
    {
        string full = Path.GetFullPath(path);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(full), DirectoryPath, comparison)
            || string.Equals(full, DatabasePath, comparison))
            throw new IOException("SQLite administration path escaped the provider-owned destination directory.");
        RevalidateDirectory();
        if (!mustExist)
        {
            if (File.Exists(full) || Directory.Exists(full))
                throw new IOException("SQLite administration sibling path is already occupied.");
            return;
        }

        ValidateRegularFile(full);
        SqliteFileIdentity identity = SqliteNativeFileIdentity.Read(full, directory: false);
        if (identity.Device != _directoryIdentity.Device
            || expectedDatabaseIdentity && identity != _databaseIdentity)
            throw new IOException("SQLite administration sibling identity changed.");
    }

    private static void RequireIdentity(string path, bool directory, SqliteFileIdentity expected)
    {
        if (SqliteNativeFileIdentity.Read(path, directory) != expected)
            throw new IOException("SQLite administration filesystem identity changed.");
    }

    private static void ValidateRegularFile(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        var file = new FileInfo(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            || file.LinkTarget is not null)
            throw new IOException("SQLite administration requires a provider-owned regular database file.");
    }

    private static void ValidatePathChain(string directory)
    {
        string full = Path.GetFullPath(directory);
        string root = Path.GetPathRoot(full)
            ?? throw new IOException("SQLite administration destination root is unavailable.");
        string current = root;
        string relative = Path.GetRelativePath(root, full);
        if (!string.Equals(relative, ".", StringComparison.Ordinal))
        {
            foreach (string component in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                var info = new DirectoryInfo(current);
                FileAttributes attributes = info.Attributes;
                if ((attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
                    throw new IOException("SQLite administration rejects symlink or reparse-point path components.");
            }
        }
    }
}

internal readonly record struct SqliteFileIdentity(ulong Device, ulong FileId);

internal static unsafe partial class SqliteNativeFileIdentity
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareAll = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;

    public static SqliteFileIdentity Read(string path, bool directory)
    {
        if (OperatingSystem.IsWindows())
            return ReadWindows(path, directory);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            return ReadUnix(path);
        throw new PlatformNotSupportedException("SQLite administration filesystem identity is unsupported on this platform.");
    }

    private static SqliteFileIdentity ReadWindows(string path, bool directory)
    {
        uint flags = OpenReparsePoint | (directory ? BackupSemantics : 0u);
        using SafeFileHandle handle = CreateFileW(path, GenericRead, ShareAll, 0, OpenExisting, flags, 0);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out ByHandleFileInformation information))
            throw new IOException("SQLite administration filesystem identity could not be read.", new Win32Exception(Marshal.GetLastPInvokeError()));
        ulong fileId = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
        return new SqliteFileIdentity(information.VolumeSerialNumber, fileId);
    }

    private static SqliteFileIdentity ReadUnix(string path)
    {
        byte* buffer = stackalloc byte[512];
        if (LStat(path, (nint)buffer) != 0)
            throw new IOException("SQLite administration filesystem identity could not be read.", new Win32Exception(Marshal.GetLastPInvokeError()));
        ulong device = OperatingSystem.IsMacOS() ? *(uint*)buffer : *(ulong*)buffer;
        ulong fileId = *(ulong*)(buffer + 8);
        return new SqliteFileIdentity(device, fileId);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [LibraryImport("libc", EntryPoint = "lstat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LStat(string path, nint buffer);
}
