using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.Sandbox.Platforms.Linux.Seccomp;

/// <summary>
/// Manages the seccomp helper binary for applying seccomp filters to child processes.
/// </summary>
/// <remarks>
/// <para><b>Binary Resolution Order:</b></para>
/// <list type="number">
/// <item>Pre-built binary in runtimes/{rid}/native/ (NuGet package)</item>
/// <item>Pre-built binary next to assembly</item>
/// <item>Cached binary in /tmp/hpd-sandbox/</item>
/// <item>Runtime compilation via gcc, only when explicitly enabled</item>
/// </list>
///
/// <para><b>Why a separate binary?</b></para>
/// <para>
/// We need to apply seccomp AFTER the socat processes start but BEFORE the user
/// command runs. Since seccomp affects all threads in a process, we need a child
/// process that applies seccomp and then execs the user command.
/// </para>
/// </remarks>
public sealed class SeccompChildProcess : IDisposable
{
    private readonly ILogger? _logger;
    private readonly string _cacheDir;
    private readonly string? _explicitHelperPath;
    private readonly bool _allowRuntimeCompilation;
    private string? _helperPath;
    private bool _disposed;

    public SeccompChildProcess(
        ILogger? logger = null,
        string? explicitHelperPath = null,
        bool allowRuntimeCompilation = false,
        string? cacheDir = null)
    {
        _logger = logger;
        if (explicitHelperPath is not null && !Path.IsPathRooted(explicitHelperPath))
            throw new ArgumentException("Seccomp helper path must be absolute.", nameof(explicitHelperPath));

        _explicitHelperPath = explicitHelperPath;
        _allowRuntimeCompilation = allowRuntimeCompilation;
        _cacheDir = cacheDir ?? Path.Combine(Path.GetTempPath(), "hpd-sandbox");
    }

    /// <summary>
    /// Gets the path to the seccomp helper binary.
    /// Prefers packaged binaries and falls back to runtime compilation only when explicitly enabled.
    /// </summary>
    public async Task<string> EnsureHelperAsync(CancellationToken cancellationToken = default)
    {
        if (_helperPath != null && File.Exists(_helperPath))
            return _helperPath;

        if (TryResolvePrebuiltHelper(out var resolvedHelperPath))
        {
            _helperPath = resolvedHelperPath;
            return _helperPath;
        }

        if (!_allowRuntimeCompilation)
        {
            throw new InvalidOperationException(
                "No pre-built seccomp helper was found and runtime compilation is disabled.");
        }

        var archSuffix = GetArchSuffix();
        var helperName = $"apply-seccomp-{archSuffix}";
        Directory.CreateDirectory(_cacheDir);
        var cachedPath = Path.Combine(_cacheDir, helperName);

        // Fall back to runtime compilation
        _logger?.LogInformation("No pre-built seccomp helper found, compiling at runtime...");
        _helperPath = cachedPath;
        await BuildHelperAsync(cancellationToken);
        _logger?.LogInformation("Seccomp helper compiled: {Path}", _helperPath);

        return _helperPath;
    }

    internal bool TryResolvePrebuiltHelper(out string helperPath)
    {
        var archSuffix = GetArchSuffix();
        var helperName = $"apply-seccomp-{archSuffix}";

        if (_explicitHelperPath is not null)
        {
            if (File.Exists(_explicitHelperPath) && IsExecutable(_explicitHelperPath))
            {
                helperPath = _explicitHelperPath;
                _logger?.LogDebug("Using explicit seccomp helper: {Path}", helperPath);
                return true;
            }

            throw new FileNotFoundException(
                "Explicit seccomp helper path does not exist or is empty.",
                _explicitHelperPath);
        }

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        var runtimesPath = Path.Combine(assemblyDir, "runtimes", $"linux-{archSuffix}", "native", helperName);
        if (File.Exists(runtimesPath) && IsExecutable(runtimesPath))
        {
            helperPath = runtimesPath;
            _logger?.LogDebug("Using packaged seccomp helper: {Path}", helperPath);
            return true;
        }

        var localPath = Path.Combine(assemblyDir, helperName);
        if (File.Exists(localPath) && IsExecutable(localPath))
        {
            helperPath = localPath;
            _logger?.LogDebug("Using local seccomp helper: {Path}", helperPath);
            return true;
        }

        var cachedPath = Path.Combine(_cacheDir, helperName);
        if (File.Exists(cachedPath) && IsExecutable(cachedPath))
        {
            helperPath = cachedPath;
            _logger?.LogDebug("Using cached seccomp helper: {Path}", helperPath);
            return true;
        }

        helperPath = string.Empty;
        return false;
    }

    private static bool IsExecutable(string path)
    {
        try
        {
            var info = new FileInfo(path);
            // Check file exists and has some content
            return info.Exists && info.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task BuildHelperAsync(CancellationToken cancellationToken)
    {
        var sourcePath = _helperPath + ".c";
        var source = GenerateHelperSource();

        await File.WriteAllTextAsync(sourcePath, source, cancellationToken);

        try
        {
            // Try static linking first (more portable)
            var result = await TryCompileAsync(sourcePath, _helperPath!, "-O2 -static", cancellationToken);

            if (!result.Success)
            {
                // Fall back to dynamic linking
                result = await TryCompileAsync(sourcePath, _helperPath!, "-O2", cancellationToken);
            }

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to compile seccomp helper: {result.Error}\n" +
                    "Ensure gcc is installed: sudo apt install gcc");
            }

            // Make executable
            await RunCommandAsync("chmod", $"+x {_helperPath}", cancellationToken);
        }
        finally
        {
            // Clean up source file
            try { File.Delete(sourcePath); } catch { }
        }
    }

    private async Task<(bool Success, string Error)> TryCompileAsync(
        string sourcePath, string outputPath, string flags, CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gcc",
                Arguments = $"{flags} -o {QuoteArg(outputPath)} {QuoteArg(sourcePath)}",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode == 0, stderr);
    }

    private static async Task RunCommandAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        await process.WaitForExitAsync(cancellationToken);
    }

    internal string GenerateHelperSource() =>
        GenerateHelperSource(RuntimeInformation.ProcessArchitecture);

    internal static string GenerateHelperSource(Architecture arch)
    {
        var (socketSyscall, socketpairSyscall, auditArch) = arch switch
        {
            Architecture.X64 => (41, 53, "0xc000003e"),
            Architecture.Arm64 => (198, 199, "0xc00000b7"),
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {arch}")
        };

        return $$"""
            /*
             * HPD Process Isolation - Seccomp Helper
             *
             * Applies a seccomp filter that blocks Unix socket creation,
             * then execs the specified command.
             *
             * Usage: apply-seccomp <shell> -c <command>
             *
             * Generated for: {{arch}}
             */

            #define _GNU_SOURCE

            #include <stdio.h>
            #include <stdlib.h>
            #include <stddef.h>
            #include <unistd.h>
            #include <errno.h>
            #include <string.h>
            #include <signal.h>
            #include <sched.h>
            #include <sys/prctl.h>
            #include <sys/mount.h>
            #include <sys/stat.h>
            #include <sys/wait.h>
            #include <linux/seccomp.h>
            #include <linux/filter.h>
            #include <linux/audit.h>

            /* Architecture-specific constants */
            #define SECCOMP_AUDIT_ARCH {{auditArch}}
            #define SYS_SOCKET {{socketSyscall}}
            #define SYS_SOCKETPAIR {{socketpairSyscall}}

            /* Address family */
            #define AF_UNIX 1

            /* Seccomp return value with errno */
            #define SECCOMP_RET_ERRNO_EACCES (SECCOMP_RET_ERRNO | 13)

            /*
             * BPF filter that blocks socket(AF_UNIX, ...) and socketpair(AF_UNIX, ...)
             *
             * This allows:
             * - TCP sockets (AF_INET, AF_INET6)
             * - All other syscalls
             * - Operations on existing Unix socket FDs
             *
             * This blocks:
             * - Creating NEW Unix domain socket FDs
             */
            static struct sock_filter filter[] = {
                /* Load architecture */
                BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, arch)),
                /* Verify architecture - if wrong, allow (kernel will handle) */
                BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, SECCOMP_AUDIT_ARCH, 0, 7),

                /* Load syscall number */
                BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, nr)),
                /* Check if socket() syscall */
                BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, SYS_SOCKET, 2, 0),
                /* Check if socketpair() syscall */
                BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, SYS_SOCKETPAIR, 1, 0),
                /* Not a socket syscall - allow */
                BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_ALLOW),

                /* Load arg0 (domain/address family) */
                BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, args[0])),
                /* Check if AF_UNIX (1) */
                BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, AF_UNIX, 0, 1),
                /* It's AF_UNIX - block with EACCES */
                BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_ERRNO_EACCES),
                /* Not AF_UNIX - allow */
                BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_ALLOW),
            };

            static struct sock_fprog prog = {
                .len = sizeof(filter) / sizeof(filter[0]),
                .filter = filter,
            };

            static int apply_seccomp(void) {
                if (prctl(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) != 0) {
                    perror("prctl(PR_SET_NO_NEW_PRIVS)");
                    return -1;
                }

                if (prctl(PR_SET_SECCOMP, SECCOMP_MODE_FILTER, &prog, 0, 0) != 0) {
                    perror("prctl(PR_SET_SECCOMP)");
                    return -1;
                }

                return 0;
            }

            static void remount_proc_if_possible(void) {
                umount2("/proc", MNT_DETACH);
                mkdir("/proc", 0555);
                if (mount("proc", "/proc", "proc", MS_NOSUID | MS_NOEXEC | MS_NODEV, NULL) != 0) {
                    /* Some nested environments do not allow this. Keep seccomp active. */
                    fprintf(stderr, "warning: mount(/proc): %s\n", strerror(errno));
                }
            }

            static int exec_with_seccomp(char *argv[]) {
                if (apply_seccomp() != 0) {
                    return 1;
                }

                execvp(argv[0], argv);
                fprintf(stderr, "execvp(%s): %s\n", argv[0], strerror(errno));
                return 127;
            }

            static int run_with_best_effort_reaper(char *argv[]) {
                if (unshare(CLONE_NEWNS | CLONE_NEWPID) != 0) {
                    fprintf(stderr, "warning: unshare(CLONE_NEWNS|CLONE_NEWPID): %s\n", strerror(errno));
                    return exec_with_seccomp(argv);
                }

                pid_t child = fork();
                if (child < 0) {
                    perror("fork");
                    return 1;
                }

                if (child == 0) {
                    remount_proc_if_possible();
                    return exec_with_seccomp(argv);
                }

                int status = 0;
                int child_status = 1;
                for (;;) {
                    pid_t reaped = wait(&status);
                    if (reaped < 0) {
                        if (errno == EINTR) {
                            continue;
                        }
                        break;
                    }

                    if (reaped == child) {
                        child_status = status;
                    }
                }

                if (WIFEXITED(child_status)) {
                    return WEXITSTATUS(child_status);
                }
                if (WIFSIGNALED(child_status)) {
                    return 128 + WTERMSIG(child_status);
                }
                return 1;
            }

            int main(int argc, char *argv[]) {
                if (argc < 2) {
                    fprintf(stderr, "HPD Process Isolation Seccomp Helper\n");
                    fprintf(stderr, "Usage: %s <command> [args...]\n", argv[0]);
                    fprintf(stderr, "\nBlocks Unix socket creation via seccomp, then execs command.\n");
                    return 1;
                }

                return run_with_best_effort_reaper(&argv[1]);
            }
            """;
    }

    private static string GetArchSuffix()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => "unknown"
        };
    }

    private static string QuoteArg(string arg) => $"'{arg.Replace("'", "'\\''")}'";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Optionally clean up helper binary
        // We leave it for reuse by default
    }
}
