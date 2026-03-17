using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace HPD.Sandbox.Local.Platforms.Linux.Seccomp;

/// <summary>
/// Manages the seccomp helper binary for applying seccomp filters to child processes.
/// </summary>
/// <remarks>
/// <para><b>Binary Resolution Order:</b></para>
/// <list type="number">
/// <item>Pre-built binary in runtimes/{rid}/native/ (NuGet package)</item>
/// <item>Pre-built binary next to assembly</item>
/// <item>Cached binary in /tmp/hpd-sandbox/</item>
/// <item>Runtime compilation via gcc (fallback)</item>
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
    private string? _helperPath;
    private bool _disposed;

    public SeccompChildProcess(ILogger? logger = null)
    {
        _logger = logger;
        _cacheDir = Path.Combine(Path.GetTempPath(), "hpd-sandbox");
    }

    /// <summary>
    /// Gets the path to the seccomp helper binary.
    /// Prefers embedded/packaged binaries, falls back to runtime compilation.
    /// </summary>
    public async Task<string> EnsureHelperAsync(CancellationToken cancellationToken = default)
    {
        if (_helperPath != null && File.Exists(_helperPath))
            return _helperPath;

        var archSuffix = GetArchSuffix();
        var helperName = $"apply-seccomp-{archSuffix}";

        // 1. Check for pre-built binary in NuGet runtimes folder
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        var runtimesPath = Path.Combine(assemblyDir, "runtimes", $"linux-{archSuffix}", "native", helperName);
        if (File.Exists(runtimesPath) && IsExecutable(runtimesPath))
        {
            _helperPath = runtimesPath;
            _logger?.LogDebug("Using packaged seccomp helper: {Path}", _helperPath);
            return _helperPath;
        }

        // 2. Check for pre-built binary next to assembly
        var localPath = Path.Combine(assemblyDir, helperName);
        if (File.Exists(localPath) && IsExecutable(localPath))
        {
            _helperPath = localPath;
            _logger?.LogDebug("Using local seccomp helper: {Path}", _helperPath);
            return _helperPath;
        }

        // 3. Check for cached binary
        Directory.CreateDirectory(_cacheDir);
        var cachedPath = Path.Combine(_cacheDir, helperName);
        if (File.Exists(cachedPath) && IsExecutable(cachedPath))
        {
            _helperPath = cachedPath;
            _logger?.LogDebug("Using cached seccomp helper: {Path}", _helperPath);
            return _helperPath;
        }

        // 4. Fall back to runtime compilation
        _logger?.LogInformation("No pre-built seccomp helper found, compiling at runtime...");
        _helperPath = cachedPath;
        await BuildHelperAsync(cancellationToken);
        _logger?.LogInformation("Seccomp helper compiled: {Path}", _helperPath);

        return _helperPath;
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

    private string GenerateHelperSource()
    {
        var arch = RuntimeInformation.ProcessArchitecture;

        var (socketSyscall, socketpairSyscall, auditArch) = arch switch
        {
            Architecture.X64 => (41, 53, "0xc000003e"),
            Architecture.Arm64 => (198, 199, "0xc00000b7"),
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {arch}")
        };

        return $$"""
            /*
             * HPD Sandbox - Seccomp Helper
             *
             * Applies a seccomp filter that blocks Unix socket creation,
             * then execs the specified command.
             *
             * Usage: apply-seccomp <shell> -c <command>
             *
             * Generated for: {{arch}}
             */

            #include <stdio.h>
            #include <stdlib.h>
            #include <stddef.h>
            #include <unistd.h>
            #include <errno.h>
            #include <string.h>
            #include <sys/prctl.h>
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

            int main(int argc, char *argv[]) {
                if (argc < 2) {
                    fprintf(stderr, "HPD Sandbox Seccomp Helper\n");
                    fprintf(stderr, "Usage: %s <command> [args...]\n", argv[0]);
                    fprintf(stderr, "\nBlocks Unix socket creation via seccomp, then execs command.\n");
                    return 1;
                }

                /* Step 1: Set no-new-privs
                 * Required to apply seccomp filter without CAP_SYS_ADMIN.
                 * This also prevents the process from gaining privileges via
                 * setuid/setgid binaries or file capabilities.
                 */
                if (prctl(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) != 0) {
                    perror("prctl(PR_SET_NO_NEW_PRIVS)");
                    return 1;
                }

                /* Step 2: Apply seccomp filter
                 * Once applied, this filter cannot be removed.
                 * It will be inherited by all child processes.
                 */
                if (prctl(PR_SET_SECCOMP, SECCOMP_MODE_FILTER, &prog, 0, 0) != 0) {
                    perror("prctl(PR_SET_SECCOMP)");
                    return 1;
                }

                /* Step 3: Execute the command
                 * The command will run with the seccomp filter active.
                 * Any attempt to call socket(AF_UNIX, ...) will fail with EACCES.
                 */
                execvp(argv[1], &argv[1]);

                /* If we get here, exec failed */
                fprintf(stderr, "execvp(%s): %s\n", argv[1], strerror(errno));
                return 127;
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
