using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HPD.Base.Sqlite;

internal static partial class SqliteAdministrationDurability
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareAll = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;

    public static void FlushDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            using SafeFileHandle handle = CreateFileW(
                directory,
                GenericRead,
                ShareAll,
                0,
                OpenExisting,
                BackupSemantics,
                0);
            if (handle.IsInvalid || !FlushFileBuffers(handle))
                throw new IOException("SQLite restore directory metadata could not be durably flushed.", new Win32Exception(Marshal.GetLastPInvokeError()));
            return;
        }

        int descriptor = Open(directory, 0);
        if (descriptor < 0)
            throw new IOException("SQLite restore directory metadata could not be opened.", new Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            if (Fsync(descriptor) != 0)
                throw new IOException("SQLite restore directory metadata could not be durably flushed.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        finally
        {
            _ = Close(descriptor);
        }
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
    private static partial bool FlushFileBuffers(SafeFileHandle handle);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static partial int Fsync(int descriptor);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int descriptor);
}
