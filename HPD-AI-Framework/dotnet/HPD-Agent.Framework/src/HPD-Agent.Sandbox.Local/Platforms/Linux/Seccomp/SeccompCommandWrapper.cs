using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HPD.Sandbox.Local.Platforms.Linux.Seccomp;

/// <summary>
/// Wraps command execution with seccomp filter application.
/// </summary>
/// <remarks>
/// <para><b>Problem:</b></para>
/// <para>
/// We need network proxy bridges (socat) to create Unix sockets, but we also
/// want to block the user command from creating Unix sockets.
/// </para>
///
/// <para><b>Solution:</b></para>
/// <para>
/// Generate a wrapper script that:
/// </para>
/// <list type="number">
/// <item>Starts socat bridges (can create Unix sockets)</item>
/// <item>Forks a child process</item>
/// <item>In child: applies seccomp filter, then execs user command</item>
/// </list>
///
/// <para><b>Two Approaches:</b></para>
/// <list type="bullet">
/// <item><b>In-Process:</b> Apply filter in current .NET process (for testing)</item>
/// <item><b>Wrapper Script:</b> Generate shell script with embedded seccomp application</item>
/// </list>
/// </remarks>
public static class SeccompCommandWrapper
{
    /// <summary>
    /// Creates a shell script that applies seccomp before running the command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This approach uses a small C program compiled and executed inline to
    /// apply the seccomp filter. The C program is compiled on first use and cached.
    /// </para>
    /// </remarks>
    /// <param name="setupScript">Script to run BEFORE seccomp (e.g., start socat)</param>
    /// <param name="userCommand">User command to run AFTER seccomp</param>
    /// <param name="shell">Shell to use</param>
    /// <returns>Complete shell script</returns>
    public static string CreateWrapperScript(
        string setupScript,
        string userCommand,
        string shell = "/bin/sh")
    {
        // We use the hpd-apply-seccomp binary which is built once and reused
        var seccompHelper = GetSeccompHelperPath();

        var sb = new StringBuilder();

        // Setup phase (socat bridges, etc.) - runs WITHOUT seccomp
        if (!string.IsNullOrWhiteSpace(setupScript))
        {
            sb.AppendLine(setupScript);
        }

        // User command phase - runs WITH seccomp
        // The helper applies seccomp then execs the shell with the command
        sb.AppendLine($"exec {QuoteArg(seccompHelper)} {QuoteArg(shell)} -c {QuoteArg(userCommand)}");

        return sb.ToString();
    }

    /// <summary>
    /// Gets or creates the seccomp helper binary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The helper is a minimal C program that:
    /// </para>
    /// <list type="number">
    /// <item>Sets PR_SET_NO_NEW_PRIVS</item>
    /// <item>Applies the Unix socket blocking seccomp filter</item>
    /// <item>Execs the specified command</item>
    /// </list>
    /// </remarks>
    public static string GetSeccompHelperPath()
    {
        var helperDir = Path.Combine(Path.GetTempPath(), "hpd-sandbox");
        var helperPath = Path.Combine(helperDir, "apply-seccomp");

        // Check if helper already exists and is executable
        if (File.Exists(helperPath))
        {
            return helperPath;
        }

        // Create the helper
        Directory.CreateDirectory(helperDir);
        BuildSeccompHelper(helperPath);

        return helperPath;
    }

    /// <summary>
    /// Builds the seccomp helper binary.
    /// </summary>
    private static void BuildSeccompHelper(string outputPath)
    {
        var arch = RuntimeInformation.ProcessArchitecture;

        // Generate C source
        var source = GenerateSeccompHelperSource(arch);
        var sourcePath = outputPath + ".c";

        File.WriteAllText(sourcePath, source);

        // Compile with gcc
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gcc",
                Arguments = $"-O2 -static -o {QuoteArg(outputPath)} {QuoteArg(sourcePath)}",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            // Try without -static (some systems don't have static libc)
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "gcc",
                    Arguments = $"-O2 -o {QuoteArg(outputPath)} {QuoteArg(sourcePath)}",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to compile seccomp helper: {stderr}");
            }
        }

        // Make executable
        Process.Start("chmod", $"+x {QuoteArg(outputPath)}")?.WaitForExit();

        // Clean up source
        try { File.Delete(sourcePath); } catch { }
    }

    /// <summary>
    /// Generates the C source code for the seccomp helper.
    /// </summary>
    private static string GenerateSeccompHelperSource(Architecture arch)
    {
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
            #include <sys/syscall.h>

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
             */
            static struct sock_filter filter[] = {
                /* Load architecture */
                BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, arch)),
                /* Check architecture */
                BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, SECCOMP_AUDIT_ARCH, 0, 7),

                /* Load syscall number */
                BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, nr)),
                /* Check if socket() */
                BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, SYS_SOCKET, 2, 0),
                /* Check if socketpair() */
                BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, SYS_SOCKETPAIR, 1, 0),
                /* Not a socket syscall - allow */
                BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_ALLOW),

                /* Load arg0 (domain) */
                BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, args[0])),
                /* Check if AF_UNIX */
                BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, AF_UNIX, 0, 1),
                /* Block with EACCES */
                BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_ERRNO_EACCES),
                /* Allow */
                BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_ALLOW),
            };

            static struct sock_fprog prog = {
                .len = sizeof(filter) / sizeof(filter[0]),
                .filter = filter,
            };

            int main(int argc, char *argv[]) {
                if (argc < 2) {
                    fprintf(stderr, "Usage: %s <command> [args...]\n", argv[0]);
                    return 1;
                }

                /* Step 1: Set no-new-privs (required for seccomp without root) */
                if (prctl(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) != 0) {
                    perror("prctl(PR_SET_NO_NEW_PRIVS)");
                    return 1;
                }

                /* Step 2: Apply seccomp filter */
                if (prctl(PR_SET_SECCOMP, SECCOMP_MODE_FILTER, &prog, 0, 0) != 0) {
                    perror("prctl(PR_SET_SECCOMP)");
                    return 1;
                }

                /* Step 3: Execute the command */
                execvp(argv[1], &argv[1]);

                /* If we get here, exec failed */
                perror("execvp");
                return 1;
            }
            """;
    }

    /// <summary>
    /// Checks if the seccomp helper can be built (gcc available).
    /// </summary>
    public static bool CanBuildHelper()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "gcc",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a pre-built seccomp helper exists.
    /// </summary>
    public static bool HasHelper()
    {
        var helperPath = Path.Combine(Path.GetTempPath(), "hpd-sandbox", "apply-seccomp");
        return File.Exists(helperPath);
    }

    private static string QuoteArg(string arg) => $"'{arg.Replace("'", "'\\''")}'";
}
