using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace HPD.Sandbox.Local.Platforms.Linux.Seccomp;

/// <summary>
/// Applies seccomp BPF filters to block specific syscalls.
/// </summary>
/// <remarks>
/// <para><b>Usage:</b></para>
/// <code>
/// // Apply filter to block Unix socket creation
/// SeccompFilter.ApplyUnixSocketBlockFilter();
///
/// // Now socket(AF_UNIX, ...) will return EACCES
/// </code>
///
/// <para><b>Important:</b></para>
/// <para>
/// Once a seccomp filter is applied, it cannot be removed. The filter
/// applies to the current process and all future child processes.
/// </para>
///
/// <para><b>Threading:</b></para>
/// <para>
/// Seccomp filters apply to all threads in the process. Apply the filter
/// before creating threads that should be restricted.
/// </para>
/// </remarks>
internal static class SeccompFilter
{
    private static bool _filterApplied;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets whether a seccomp filter has been applied.
    /// </summary>
    public static bool IsFilterApplied => _filterApplied;

    /// <summary>
    /// Gets whether seccomp is supported on this system and architecture.
    /// </summary>
    public static bool IsSupported =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
        IsArchitectureSupported() &&
        IsAvailable();

    /// <summary>
    /// Checks if seccomp is available on this system.
    /// </summary>
    public static bool IsAvailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        // Check if we can query seccomp status
        try
        {
            var result = SeccompNative.prctl(
                SeccompNative.PR_GET_SECCOMP,
                0, 0, 0, 0);

            // PR_GET_SECCOMP returns:
            // - 0: seccomp disabled
            // - 1: seccomp strict mode
            // - 2: seccomp filter mode
            // - -1 with EINVAL: seccomp not supported
            return result >= 0 || Marshal.GetLastWin32Error() != 22; // EINVAL = 22
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the current architecture is supported.
    /// </summary>
    public static bool IsArchitectureSupported()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        return arch is Architecture.X64 or Architecture.Arm64;
    }

    /// <summary>
    /// Applies a seccomp filter that blocks Unix domain socket creation.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <exception cref="PlatformNotSupportedException">If not on Linux or unsupported architecture.</exception>
    /// <exception cref="InvalidOperationException">If filter application fails.</exception>
    /// <remarks>
    /// <para><b>What gets blocked:</b></para>
    /// <list type="bullet">
    /// <item>socket(AF_UNIX, ...) - Returns EACCES</item>
    /// <item>socketpair(AF_UNIX, ...) - Returns EACCES</item>
    /// </list>
    ///
    /// <para><b>What is NOT blocked:</b></para>
    /// <list type="bullet">
    /// <item>Existing Unix socket file descriptors</item>
    /// <item>TCP/UDP sockets (AF_INET, AF_INET6)</item>
    /// <item>All other syscalls</item>
    /// </list>
    /// </remarks>
    public static void ApplyUnixSocketBlockFilter(ILogger? logger = null)
    {
        lock (_lock)
        {
            if (_filterApplied)
            {
                logger?.LogDebug("Seccomp filter already applied, skipping");
                return;
            }

            // Platform checks
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                throw new PlatformNotSupportedException(
                    "Seccomp is only available on Linux");
            }

            if (!IsArchitectureSupported())
            {
                throw new PlatformNotSupportedException(
                    $"Seccomp filter not supported on {RuntimeInformation.ProcessArchitecture}. " +
                    "Only x64 and ARM64 are supported.");
            }

            // Generate the BPF program
            var instructions = BpfProgramBuilder.CreateUnixSocketBlockFilter();
            BpfProgramBuilder.ValidateProgram(instructions);

            logger?.LogDebug(
                "Applying seccomp filter with {Count} instructions for {Arch}",
                instructions.Length,
                RuntimeInformation.ProcessArchitecture);

            // Apply the filter
            ApplyFilter(instructions);
            _filterApplied = true;

            logger?.LogInformation(
                "Seccomp filter applied: Unix socket creation blocked ({Arch})",
                RuntimeInformation.ProcessArchitecture);
        }
    }

    /// <summary>
    /// Applies a custom BPF program as a seccomp filter.
    /// </summary>
    /// <param name="instructions">The BPF instructions to apply.</param>
    /// <exception cref="InvalidOperationException">If filter application fails.</exception>
    public static void ApplyFilter(BpfInstruction[] instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        if (instructions.Length == 0)
            throw new ArgumentException("BPF program cannot be empty", nameof(instructions));

        // Step 1: Set PR_SET_NO_NEW_PRIVS
        // This is required before applying a seccomp filter without CAP_SYS_ADMIN
        var result = SeccompNative.prctl(
            SeccompNative.PR_SET_NO_NEW_PRIVS,
            1, 0, 0, 0);

        if (result != 0)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Failed to set PR_SET_NO_NEW_PRIVS: error {error} ({GetErrorName(error)})");
        }

        // Step 2: Apply the seccomp filter
        // We need to pin the instructions array and create a BpfProgram struct
        var handle = GCHandle.Alloc(instructions, GCHandleType.Pinned);
        try
        {
            var prog = new BpfProgram(
                (ushort)instructions.Length,
                handle.AddrOfPinnedObject());

            // Pin the program struct itself
            var progHandle = GCHandle.Alloc(prog, GCHandleType.Pinned);
            try
            {
                result = SeccompNative.prctl(
                    SeccompNative.PR_SET_SECCOMP,
                    SeccompNative.SECCOMP_MODE_FILTER,
                    progHandle.AddrOfPinnedObject(),
                    0, 0);

                if (result != 0)
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(
                        $"Failed to apply seccomp filter: error {error} ({GetErrorName(error)}). " +
                        GetSeccompErrorHelp(error));
                }
            }
            finally
            {
                progHandle.Free();
            }
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Tries to apply the Unix socket block filter without throwing.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    /// <returns>True if filter was applied, false otherwise.</returns>
    public static bool TryApplyUnixSocketBlockFilter(ILogger? logger = null)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                logger?.LogDebug("Seccomp not available: not Linux");
                return false;
            }

            if (!IsArchitectureSupported())
            {
                logger?.LogDebug("Seccomp not available: unsupported architecture {Arch}",
                    RuntimeInformation.ProcessArchitecture);
                return false;
            }

            if (!IsAvailable())
            {
                logger?.LogDebug("Seccomp not available on this kernel");
                return false;
            }

            ApplyUnixSocketBlockFilter(logger);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to apply seccomp filter");
            return false;
        }
    }

    private static string GetErrorName(int error) => error switch
    {
        1 => "EPERM",
        13 => "EACCES",
        14 => "EFAULT",
        22 => "EINVAL",
        38 => "ENOSYS",
        _ => $"errno={error}"
    };

    private static string GetSeccompErrorHelp(int error) => error switch
    {
        1 => "Permission denied. Ensure PR_SET_NO_NEW_PRIVS was set first.",
        22 => "Invalid argument. The BPF program may be malformed or contain invalid jumps.",
        38 => "Seccomp not supported by this kernel.",
        _ => "See 'man 2 seccomp' for more information."
    };
}
