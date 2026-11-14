namespace HPD.Environment.HostProcess.Platforms.Windows;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsAppContainerProfile : IDisposable
{
    private readonly string _name = $"HPD.Sandbox.{Guid.NewGuid():N}";
    private IntPtr _sid;

    public WindowsAppContainerProfile()
    {
        Marshal.ThrowExceptionForHR(CreateAppContainerProfile(_name, _name, _name, IntPtr.Zero, 0, out _sid));
    }

    internal IntPtr SidPointer => _sid != IntPtr.Zero ? _sid : throw new ObjectDisposedException(nameof(WindowsAppContainerProfile));
    public SecurityIdentifier Sid => new(SidPointer);

    public void Dispose()
    {
        if (_sid == IntPtr.Zero)
            return;

        Marshal.ThrowExceptionForHR(DeleteAppContainerProfile(_name));
        FreeSid(_sid);
        _sid = IntPtr.Zero;
    }

    [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CreateAppContainerProfile(string name, string display, string description,
        IntPtr capabilities, uint count, out IntPtr sid);

    [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DeleteAppContainerProfile(string name);

    [LibraryImport("advapi32.dll")]
    private static partial IntPtr FreeSid(IntPtr sid);
}
