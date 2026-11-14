namespace HPD.Environment.HostProcess.Platforms.Windows;

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

[SupportedOSPlatform("windows")]
internal static partial class WindowsLaunchSecurity
{
    private const uint CreateSuspended = 0x0004;
    private const uint CreateUnicodeEnvironment = 0x0400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint JobListAttribute = 0x0002000D;
    private const uint HandleListAttribute = 0x00020002;
    private const uint SecurityCapabilitiesAttribute = 0x00020009;
    private const uint UseStandardHandles = 0x100;
    private const uint DesktopAllAccess = 0x01FF;

    // The caller owns the job for the entire invocation, including its descendants.
    // Only explicitly listed standard handles are inherited.
    public static unsafe (SafeProcessHandle Process, int ProcessId, IDisposable? Desktop) StartRestrictedProcess(
        SafeAccessTokenHandle? token,
        SafeFileHandle job,
        string executable,
        string commandLine,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        WindowsStandardHandles? standardHandles = null,
        WindowsAppContainerProfile? appContainer = null)
    {
        if ((token is null) == (appContainer is null))
            throw new ArgumentException("Supply either a restricted token or an AppContainer profile.");
        if (!Path.IsPathFullyQualified(executable) || !Path.IsPathFullyQualified(workingDirectory))
            throw new ArgumentException("The executable and working directory must be absolute paths.");
        if (commandLine.Contains('\0'))
            throw new ArgumentException("The command line contains a null character.", nameof(commandLine));

        char[] environmentBlock = CreateEnvironmentBlock(environment);
        string name = $"HPD-Sandbox-{Guid.NewGuid():N}";
        DesktopHandle? desktop = null;
        IntPtr desktopName = IntPtr.Zero;
        try
        {
            if (token is not null)
            {
                string descriptor = $"D:(A;;GA;;;{GetLogonSid(token)})";
                if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(descriptor, 1, out IntPtr security, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the private desktop access policy.");
                desktop = CreatePrivateDesktop(name, security);
                if (desktop.IsInvalid)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a private Windows desktop.");
                desktopName = Marshal.StringToHGlobalUni(name);
            }
            // Attach the job at creation so a host crash cannot strand a suspended child.
            nuint bytes = 0;
            int attributeCount = standardHandles is null ? 1 : 2;
            if (appContainer is not null)
                attributeCount++;
            InitializeProcThreadAttributeList(IntPtr.Zero, attributeCount, 0, ref bytes);
            if (bytes == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not size the Windows launch attributes.");

            IntPtr attributes = Marshal.AllocHGlobal(checked((nint)bytes));
            ProcessInformation child;
            bool initialized = false;
            bool jobRetained = false;
            try
            {
                initialized = InitializeProcThreadAttributeList(attributes, attributeCount, 0, ref bytes);
                if (!initialized)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not initialize Windows launch attributes.");

                job.DangerousAddRef(ref jobRetained);
                IntPtr jobHandle = job.DangerousGetHandle();
                if (!UpdateProcThreadAttribute(attributes, 0, JobListAttribute,
                    (IntPtr)(&jobHandle), (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not set the Windows launch job.");
                }

                // The streams and their client handles stay alive until process creation completes.
                Span<IntPtr> handles = stackalloc IntPtr[3];
                handles.Clear();
                if (standardHandles is { } io)
                {
                    handles[0] = io.Input;
                    handles[1] = io.Output;
                    handles[2] = io.Error;
                }

                var startup = new StartupInfoEx
                {
                    Startup = new StartupInfo
                    {
                        Size = Marshal.SizeOf<StartupInfoEx>(),
                        Desktop = desktopName,
                        Flags = standardHandles is null ? 0u : UseStandardHandles,
                        StandardInput = handles[0],
                        StandardOutput = handles[1],
                        StandardError = handles[2],
                    },
                    Attributes = attributes,
                };
                fixed (IntPtr* inherited = handles)
                fixed (char* environmentPointer = environmentBlock)
                {
                    if (standardHandles is not null &&
                        !UpdateProcThreadAttribute(attributes, 0, HandleListAttribute, (IntPtr)inherited,
                            (nuint)(handles.Length * IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not set the Windows standard handle list.");
                    }

                    var capabilities = new SecurityCapabilities { Sid = appContainer?.SidPointer ?? IntPtr.Zero };
                    if (appContainer is not null &&
                        !UpdateProcThreadAttribute(attributes, 0, SecurityCapabilitiesAttribute,
                            (IntPtr)(&capabilities), (nuint)sizeof(SecurityCapabilities), IntPtr.Zero, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not set AppContainer security capabilities.");

                    char[] arguments = (commandLine + '\0').ToCharArray();
                    uint flags = CreateSuspended | CreateUnicodeEnvironment | ExtendedStartupInfoPresent;
                    bool started = token is null
                        ? CreateProcessW(executable, arguments, IntPtr.Zero, IntPtr.Zero, standardHandles is not null,
                            flags | 0x08000000, (IntPtr)environmentPointer, workingDirectory, in startup, out child)
                        : CreateProcessAsUserW(token, executable, arguments, IntPtr.Zero, IntPtr.Zero, standardHandles is not null,
                            flags, (IntPtr)environmentPointer, workingDirectory, in startup, out child);
                    if (!started)
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new Win32Exception(error, $"Could not launch the restricted Windows process (Win32 error {error}).");
                    }
                }
            }
            finally
            {
                if (initialized)
                    DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
                if (jobRetained)
                    job.DangerousRelease();
            }

            using var thread = new SafeFileHandle(child.Thread, ownsHandle: true);
            var process = new SafeProcessHandle(child.Process, ownsHandle: true);
            try
            {
                if (ResumeThread(thread) == uint.MaxValue)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resume the restricted Windows process.");
                return (process, checked((int)child.ProcessId), desktop);
            }
            catch
            {
                TerminateProcess(process, 1);
                process.Dispose();
                throw;
            }
        }
        catch
        {
            desktop?.Dispose();
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(desktopName);
        }
    }

    internal readonly record struct WindowsStandardHandles(IntPtr Input, IntPtr Output, IntPtr Error);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities
    {
        public IntPtr Sid, Capabilities;
        public uint Count, Reserved;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(
        string application, [In, Out] char[] commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, IntPtr environment, string workingDirectory,
        in StartupInfoEx startup, out ProcessInformation information);

    private static char[] CreateEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var entries = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in environment)
        {
            if (string.IsNullOrEmpty(name) || name.Contains('=') || name.Contains('\0') ||
                value is null || value.Contains('\0'))
            {
                throw new ArgumentException("Invalid Windows environment variable.", nameof(environment));
            }

            if (!entries.TryAdd(name, value))
                throw new ArgumentException("Duplicate Windows environment variable.", nameof(environment));
        }

        return entries.Count == 0
            ? ['\0', '\0']
            : (string.Join('\0', entries.Select(entry => $"{entry.Key}={entry.Value}")) + "\0\0").ToCharArray();
    }

    private static DesktopHandle CreatePrivateDesktop(string name, IntPtr security)
    {
        try
        {
            var attributes = new SecurityAttributes
            {
                Size = Marshal.SizeOf<SecurityAttributes>(),
                Descriptor = security,
            };
            return CreateDesktopW(name, IntPtr.Zero, IntPtr.Zero, 0, DesktopAllAccess, in attributes);
        }
        finally
        {
            LocalFree(security);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Size;
        public IntPtr Descriptor;
        public int Inherit;
    }

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string text, uint revision, out IntPtr descriptor, IntPtr size);

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved, Desktop, Title;
        public uint X, Y, Width, Height, Columns, Rows, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedBytes, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo Startup;
        public IntPtr Attributes;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(
        IntPtr attributes, int count, uint flags, ref nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(
        IntPtr attributes, uint flags, nuint attribute, IntPtr value, nuint size,
        IntPtr previousValue, IntPtr returnedSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(IntPtr attributes);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }

    private sealed class DesktopHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public DesktopHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => CloseDesktop(handle);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial DesktopHandle CreateDesktopW(
        string name, IntPtr device, IntPtr mode, uint flags, uint access, in SecurityAttributes attributes);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(IntPtr desktop);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessAsUserW(
        SafeAccessTokenHandle token, string application, [In, Out] char[] commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint flags, IntPtr environment, string workingDirectory, in StartupInfoEx startup,
        out ProcessInformation information);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint ResumeThread(SafeFileHandle thread);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeProcessHandle process, uint exitCode);
}
