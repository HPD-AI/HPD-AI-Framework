namespace HPD.Environment.HostProcess.Platforms.Windows;

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

/// <summary>Grants a provisioned sandbox identity access to the Windows null device.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsNullDeviceGrant : IDisposable
{
    private const int KernelObject = 6;
    private const uint DaclSecurityInformation = 4;
    private const int FileGenericReadWrite = 0x0012019f;
    private readonly SafeFileHandle _device;
    private readonly SecurityIdentifier _sid;

    public WindowsNullDeviceGrant(SecurityIdentifier sid)
    {
        ArgumentNullException.ThrowIfNull(sid);
        if (!sid.IsAccountSid())
            throw new ArgumentException("A dedicated sandbox account SID is required.", nameof(sid));

        _sid = sid;
        _device = CreateFileW(@"\\.\NUL", 0x00060000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (_device.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            _device.Dispose();
            throw new Win32Exception(error, "Cannot open NUL for sandbox ACL provisioning.");
        }

        try { Update(add: true); }
        catch
        {
            _device.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_device.IsClosed)
            return;
        try { Update(add: false); }
        finally { _device.Dispose(); }
    }

    private void Update(bool add)
    {
        Check(GetSecurityInfo(_device, KernelObject, DaclSecurityInformation,
            IntPtr.Zero, IntPtr.Zero, out var dacl, IntPtr.Zero, out var descriptor));
        try
        {
            if (dacl == IntPtr.Zero)
                throw new InvalidOperationException("Expected a discretionary ACL on the null device.");

            var bytes = new byte[unchecked((ushort)Marshal.ReadInt16(dacl, 2))];
            Marshal.Copy(dacl, bytes, 0, bytes.Length);
            var acl = new RawAcl(bytes, 0);
            int index = acl.Count;
            bool removed = false;
            for (int i = 0; i < acl.Count; i++)
            {
                if (acl[i] is CommonAce ace && ace.SecurityIdentifier.Equals(_sid))
                {
                    if (add)
                        throw new InvalidOperationException("The sandbox account already has a null-device ACL entry.");
                    if (ace.AceQualifier == AceQualifier.AccessAllowed && ace.AceFlags == AceFlags.None &&
                        ace.AccessMask == FileGenericReadWrite)
                    {
                        acl.RemoveAce(i);
                        removed = true;
                        break;
                    }
                }
                if (add && (acl[i].AceFlags & AceFlags.Inherited) != 0)
                    index = Math.Min(index, i);
            }

            if (add)
                acl.InsertAce(index, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed,
                    FileGenericReadWrite, _sid, isCallback: false, opaque: null));
            else if (!removed)
                return;

            var updated = new byte[acl.BinaryLength];
            acl.GetBinaryForm(updated, 0);
            Check(SetSecurityInfo(_device, KernelObject, DaclSecurityInformation,
                IntPtr.Zero, IntPtr.Zero, updated, IntPtr.Zero));
        }
        finally { LocalFree(descriptor); }
    }

    private static void Check(uint error)
    {
        if (error != 0)
            throw new Win32Exception(unchecked((int)error), "Could not update the sandbox null-device grant.");
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityInfo(SafeFileHandle handle, int objectType, uint info,
        IntPtr owner, IntPtr group, out IntPtr dacl, IntPtr sacl, out IntPtr descriptor);

    [LibraryImport("advapi32.dll")]
    private static partial uint SetSecurityInfo(SafeFileHandle handle, int objectType, uint info,
        IntPtr owner, IntPtr group, byte[] dacl, IntPtr sacl);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
