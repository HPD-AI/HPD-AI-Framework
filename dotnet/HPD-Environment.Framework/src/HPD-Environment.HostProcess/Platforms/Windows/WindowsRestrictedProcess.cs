namespace HPD.Environment.HostProcess.Platforms.Windows;

using System.IO.Pipes;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

/// <summary>Owns the restricted child, its process tree, and the parent ends of its standard pipes.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsRestrictedProcess : IDisposable
{
    private readonly SafeFileHandle _job;
    private readonly IDisposable? _desktop;
    private readonly AnonymousPipeServerStream _stdin;
    private readonly AnonymousPipeServerStream _stdout;
    private readonly AnonymousPipeServerStream? _stderr;

    private WindowsRestrictedProcess(
        SafeFileHandle job, SafeProcessHandle process, int processId, IDisposable? desktop,
        AnonymousPipeServerStream stdin, AnonymousPipeServerStream stdout, AnonymousPipeServerStream? stderr)
    {
        _job = job;
        Process = process;
        ProcessId = processId;
        _desktop = desktop;
        _stdin = stdin;
        _stdout = stdout;
        _stderr = stderr;
    }

    public SafeProcessHandle Process { get; }
    public int ProcessId { get; }
    public void StopProcessTree() => _job.Dispose();
    public Stream StandardInput => _stdin;
    public Stream StandardOutput => _stdout;
    public Stream? StandardError => _stderr;

    public static WindowsRestrictedProcess Start(
        SafeAccessTokenHandle? token, string executable, string commandLine, string workingDirectory,
        IReadOnlyDictionary<string, string> environment, bool mergeStandardError = false,
        WindowsAppContainerProfile? appContainer = null)
    {
        var job = WindowsLaunchSecurity.CreateProcessTreeJob();
        AnonymousPipeServerStream? stdin = null, stdout = null, stderr = null;
        SafeProcessHandle? process = null;
        IDisposable? desktop = null;
        try
        {
            stdin = new(PipeDirection.Out, HandleInheritability.Inheritable);
            stdout = new(PipeDirection.In, HandleInheritability.Inheritable);
            if (!mergeStandardError)
                stderr = new(PipeDirection.In, HandleInheritability.Inheritable);

            (process, int processId, desktop) = WindowsLaunchSecurity.StartRestrictedProcess(
                token, job, executable, commandLine, workingDirectory, environment,
                new(stdin.ClientSafePipeHandle.DangerousGetHandle(),
                    stdout.ClientSafePipeHandle.DangerousGetHandle(),
                    (stderr ?? stdout).ClientSafePipeHandle.DangerousGetHandle()), appContainer);

            stdin.DisposeLocalCopyOfClientHandle();
            stdout.DisposeLocalCopyOfClientHandle();
            stderr?.DisposeLocalCopyOfClientHandle();
            return new(job, process, processId, desktop, stdin, stdout, stderr);
        }
        catch
        {
            job.Dispose();
            process?.Dispose();
            desktop?.Dispose();
            stdin?.Dispose();
            stdout?.Dispose();
            stderr?.Dispose();
            throw;
        }
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        using var waitHandle = new ProcessWaitHandle(Process);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ThreadPool.RegisterWaitForSingleObject(
            waitHandle, static (state, _) => ((TaskCompletionSource)state!).TrySetResult(),
            completion, Timeout.Infinite, executeOnlyOnce: true);
        try
        {
            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            registration.Unregister(null);
        }
    }

    public int? ReadExitCode()
    {
        if (WaitForSingleObject(Process, 0) != 0)
            return null;
        if (!GetExitCodeProcess(Process, out uint code))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return unchecked((int)code);
    }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeProcessHandle process)
            => SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false);
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeProcessHandle process, out uint code);

    public void Dispose()
    {
        _job.Dispose();
        _stdin.Dispose();
        _stdout.Dispose();
        _stderr?.Dispose();
        Process.Dispose();
        _desktop?.Dispose();
    }
}

