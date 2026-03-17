using System.Runtime.InteropServices;

namespace HPD.Sandbox.Local.Platforms.Linux.Seccomp;

/// <summary>
/// Builds BPF programs for seccomp filtering.
/// </summary>
internal static class BpfProgramBuilder
{
    /// <summary>
    /// Creates a BPF program that blocks Unix domain socket creation.
    /// </summary>
    /// <returns>Array of BPF instructions for the filter.</returns>
    /// <remarks>
    /// <para><b>What gets blocked:</b></para>
    /// <list type="bullet">
    /// <item>socket(AF_UNIX, ...) - Returns EPERM</item>
    /// <item>socketpair(AF_UNIX, ...) - Returns EPERM</item>
    /// </list>
    ///
    /// <para><b>What is NOT blocked:</b></para>
    /// <list type="bullet">
    /// <item>Existing Unix socket file descriptors</item>
    /// <item>TCP/UDP sockets (AF_INET, AF_INET6)</item>
    /// <item>All other syscalls</item>
    /// </list>
    /// </remarks>
    public static BpfInstruction[] CreateUnixSocketBlockFilter()
    {
        var arch = SeccompNative.GetAuditArch();
        var socketNr = (uint)SeccompNative.GetSocketSyscallNumber();
        var socketpairNr = (uint)SeccompNative.GetSocketpairSyscallNumber();

        return
        [
            // 0: Load architecture
            BpfInstruction.LoadAbsolute(SeccompNative.SECCOMP_DATA_ARCH),

            // 1: Verify architecture (jump to KILL at instruction 10 if wrong)
            BpfInstruction.JumpIfEqual(arch, 0, 9),

            // 2: Load syscall number
            BpfInstruction.LoadAbsolute(SeccompNative.SECCOMP_DATA_NR),

            // 3: Check if socket() syscall
            BpfInstruction.JumpIfEqual(socketNr, 3, 0),

            // 4: Check if socketpair() syscall
            BpfInstruction.JumpIfEqual(socketpairNr, 2, 0),

            // 5: Not a socket syscall - ALLOW
            BpfInstruction.Return(SeccompNative.SECCOMP_RET_ALLOW),

            // 6: Load first argument (address family)
            BpfInstruction.LoadAbsolute(SeccompNative.SECCOMP_DATA_ARGS),

            // 7: Check if AF_UNIX
            BpfInstruction.JumpIfEqual(SeccompNative.AF_UNIX, 1, 0),

            // 8: Not AF_UNIX - ALLOW
            BpfInstruction.Return(SeccompNative.SECCOMP_RET_ALLOW),

            // 9: BLOCK - Return EPERM
            BpfInstruction.Return(SeccompNative.SECCOMP_RET_ERRNO | (uint)SeccompNative.EPERM),

            // 10: KILL - Wrong architecture
            BpfInstruction.Return(SeccompNative.SECCOMP_RET_KILL_PROCESS),
        ];
    }

    /// <summary>
    /// Validates a BPF program for basic correctness.
    /// </summary>
    /// <param name="instructions">The BPF instructions to validate.</param>
    /// <exception cref="ArgumentException">If the program is invalid.</exception>
    public static void ValidateProgram(BpfInstruction[] instructions)
    {
        if (instructions.Length == 0)
            throw new ArgumentException("BPF program cannot be empty", nameof(instructions));

        if (instructions.Length > 4096)
            throw new ArgumentException("BPF program exceeds maximum length of 4096", nameof(instructions));

        // Validate jump targets
        for (var i = 0; i < instructions.Length; i++)
        {
            var inst = instructions[i];

            // Check if this is a conditional jump instruction
            var opClass = inst.Code & 0x07;
            if (opClass == SeccompNative.BPF_JMP)
            {
                // For conditional jumps, verify targets are valid
                var opCode = inst.Code & 0xf0;
                if (opCode != SeccompNative.BPF_JA) // JA uses K, not Jt/Jf
                {
                    var trueTarget = i + 1 + inst.Jt;
                    var falseTarget = i + 1 + inst.Jf;

                    if (trueTarget >= instructions.Length)
                        throw new ArgumentException($"Invalid jump target at instruction {i}: Jt={inst.Jt} jumps past end", nameof(instructions));
                    if (falseTarget >= instructions.Length)
                        throw new ArgumentException($"Invalid jump target at instruction {i}: Jf={inst.Jf} jumps past end", nameof(instructions));
                }
            }
        }

        // Ensure last instruction is a return
        var last = instructions[^1];
        if ((last.Code & 0x07) != SeccompNative.BPF_RET)
            throw new ArgumentException("BPF program must end with a return instruction", nameof(instructions));
    }
}
