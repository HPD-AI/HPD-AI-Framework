namespace HPD.Environment.HostProcess.Platforms.Windows;

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

/// <summary>Owns native security objects used to prepare a confined Windows launch.</summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsLaunchSecurity
{
    private const uint TokenLaunchAccess = 0x008B; // AssignPrimary, Duplicate, Query, AdjustDefault.
    private const uint DisableMaxPrivilege = 0x0001;
    private const uint LuaToken = 0x0004;
    private const uint WriteRestricted = 0x0008;
    private const uint KillOnJobClose = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    public static SafeAccessTokenHandle CreateWriteRestrictedToken(SecurityIdentifierHandle workspaceSid)
    {
        ArgumentNullException.ThrowIfNull(workspaceSid);
        if (workspaceSid.IsInvalid || workspaceSid.IsClosed)
            throw new ArgumentException("An active workspace SID is required.", nameof(workspaceSid));

        if (!OpenProcessToken(new IntPtr(-1), TokenLaunchAccess, out var currentToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the current Windows token.");

        using (currentToken)
            return CreateWriteRestrictedToken(currentToken, workspaceSid);
    }

    internal static SafeAccessTokenHandle CreateWriteRestrictedToken(
        SafeAccessTokenHandle sourceToken, SecurityIdentifierHandle workspaceSid,
        SecurityIdentifierHandle? networkAccountSid = null)
    {
        ArgumentNullException.ThrowIfNull(workspaceSid);
        using var logon = new SecurityIdentifierHandle(GetLogonSid(sourceToken));
        bool workspaceRetained = false;
        bool accountRetained = false;
        try
        {
            workspaceSid.DangerousAddRef(ref workspaceRetained);
            networkAccountSid?.DangerousAddRef(ref accountRetained);
            SidAndAttributes[] restrictions = new SidAndAttributes[networkAccountSid is null ? 2 : 3];
            restrictions[0].Sid = workspaceSid.DangerousGetHandle();
            restrictions[1].Sid = logon.DangerousGetHandle();
            if (networkAccountSid is not null)
                restrictions[2].Sid = networkAccountSid.DangerousGetHandle();

            if (!CreateRestrictedToken(
                sourceToken, DisableMaxPrivilege | LuaToken | WriteRestricted,
                0, IntPtr.Zero, 0, IntPtr.Zero, (uint)restrictions.Length, restrictions, out var restrictedToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a write-restricted Windows token.");
            }

            try
            {
                SetLowIntegrity(restrictedToken);
                return restrictedToken;
            }
            catch
            {
                restrictedToken.Dispose();
                throw;
            }
        }
        finally
        {
            if (accountRetained)
                networkAccountSid!.DangerousRelease();
            if (workspaceRetained)
                workspaceSid.DangerousRelease();
        }
    }

    private static unsafe void SetLowIntegrity(SafeAccessTokenHandle token)
    {
        const int tokenIntegrityLevel = 25;
        const uint groupIntegrity = 0x20;
        using var lowIntegrity = new SecurityIdentifierHandle("S-1-16-4096");
        var label = new SidAndAttributes { Sid = lowIntegrity.DangerousGetHandle(), Attributes = groupIntegrity };
        const uint integritySidSize = 12; // One subauthority.
        if (!SetTokenInformation(token, tokenIntegrityLevel, (IntPtr)(&label), (uint)sizeof(SidAndAttributes) + integritySidSize))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not lower the Windows sandbox token integrity.");
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetTokenInformation(
        SafeAccessTokenHandle token, int informationClass, IntPtr information, uint size);

    private static string GetLogonSid(SafeAccessTokenHandle token)
    {
        const int tokenLogonSid = 28;
        GetTokenInformation(token, tokenLogonSid, IntPtr.Zero, 0, out int size);
        if (size <= 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not query the Windows logon SID.");

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(token, tokenLogonSid, buffer, size, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the Windows logon SID.");
            if (Marshal.ReadInt32(buffer) != 1)
                throw new InvalidOperationException("Expected one Windows logon SID.");
            return new SecurityIdentifier(Marshal.ReadIntPtr(buffer, IntPtr.Size)).Value;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(
        SafeAccessTokenHandle token, int informationClass, IntPtr buffer, int size, out int requiredSize);

    public static SafeFileHandle CreateProcessTreeJob()
    {
        var job = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
        if (job.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error, "Could not create the Windows process job.");
        }

        var limits = new ExtendedLimitInformation();
        limits.Basic.LimitFlags = KillOnJobClose;
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, in limits, Marshal.SizeOf<ExtendedLimitInformation>()))
        {
            int error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error, "Could not configure process-tree cleanup.");
        }

        return job;
    }

    // The launcher must assign its suspended child before allowing it to run.
    public static void AssignProcess(SafeFileHandle job, SafeProcessHandle process)
    {
        if (!AssignProcessToJobObject(job, process))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach the process to its Windows job.");
    }

    internal sealed class SecurityIdentifierHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SecurityIdentifierHandle(string value) : base(ownsHandle: true)
        {
            if (!ConvertStringSidToSidW(value, out var sid))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not parse the Windows workspace SID.");

            SetHandle(sid);
        }

        protected override bool ReleaseHandle() => LocalFree(handle) == IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation Basic;
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateRestrictedToken(
        SafeAccessTokenHandle token, uint flags, uint disableCount, IntPtr disableSids,
        uint deleteCount, IntPtr deletePrivileges, uint restrictCount, [In] SidAndAttributes[] restrictingSids,
        out SafeAccessTokenHandle restrictedToken);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSidToSidW(string value, out IntPtr sid);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeFileHandle CreateJobObjectW(IntPtr attributes, IntPtr name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(
        SafeFileHandle job, int informationClass, in ExtendedLimitInformation limits, int size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
}

