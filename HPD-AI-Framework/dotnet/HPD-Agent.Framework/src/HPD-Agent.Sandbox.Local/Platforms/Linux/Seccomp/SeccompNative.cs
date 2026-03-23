using System.Runtime.InteropServices;

namespace HPD.Sandbox.Local.Platforms.Linux.Seccomp;

/// <summary>
/// P/Invoke declarations for Linux seccomp system calls.
/// </summary>
/// <remarks>
/// <para><b>Seccomp (Secure Computing Mode):</b></para>
/// <para>
/// Seccomp is a Linux kernel feature that restricts the system calls
/// a process can make. We use SECCOMP_MODE_FILTER with BPF programs
/// to selectively block specific syscalls.
/// </para>
///
/// <para><b>Required Sequence:</b></para>
/// <list type="number">
/// <item>prctl(PR_SET_NO_NEW_PRIVS, 1) - Required before seccomp filter</item>
/// <item>prctl(PR_SET_SECCOMP, SECCOMP_MODE_FILTER, &amp;prog) - Apply BPF filter</item>
/// </list>
/// </remarks>
internal static partial class SeccompNative
{
    // prctl options
    public const int PR_SET_NO_NEW_PRIVS = 38;
    public const int PR_SET_SECCOMP = 22;
    public const int PR_GET_SECCOMP = 21;

    // Seccomp modes
    public const int SECCOMP_MODE_DISABLED = 0;
    public const int SECCOMP_MODE_STRICT = 1;
    public const int SECCOMP_MODE_FILTER = 2;

    // Seccomp return actions (for BPF program)
    public const uint SECCOMP_RET_KILL_PROCESS = 0x80000000;
    public const uint SECCOMP_RET_KILL_THREAD = 0x00000000;
    public const uint SECCOMP_RET_TRAP = 0x00030000;
    public const uint SECCOMP_RET_ERRNO = 0x00050000;
    public const uint SECCOMP_RET_USER_NOTIF = 0x7fc00000;
    public const uint SECCOMP_RET_LOG = 0x7ffc0000;
    public const uint SECCOMP_RET_ALLOW = 0x7fff0000;

    // Error numbers
    public const int EPERM = 1;
    public const int EACCES = 13;
    public const int ENOSYS = 38;

    // Socket address families
    public const uint AF_UNIX = 1;
    public const uint AF_LOCAL = 1; // Same as AF_UNIX
    public const uint AF_INET = 2;
    public const uint AF_INET6 = 10;

    // Syscall numbers - x86_64
    public const int SYS_socket_x64 = 41;
    public const int SYS_socketpair_x64 = 53;

    // Syscall numbers - aarch64 (ARM64)
    public const int SYS_socket_arm64 = 198;
    public const int SYS_socketpair_arm64 = 199;

    // BPF instruction classes
    public const ushort BPF_LD = 0x00;
    public const ushort BPF_LDX = 0x01;
    public const ushort BPF_ST = 0x02;
    public const ushort BPF_STX = 0x03;
    public const ushort BPF_ALU = 0x04;
    public const ushort BPF_JMP = 0x05;
    public const ushort BPF_RET = 0x06;
    public const ushort BPF_MISC = 0x07;

    // BPF ld/ldx fields
    public const ushort BPF_W = 0x00;    // 32-bit word
    public const ushort BPF_H = 0x08;    // 16-bit half word
    public const ushort BPF_B = 0x10;    // 8-bit byte
    public const ushort BPF_ABS = 0x20;  // Absolute offset
    public const ushort BPF_IND = 0x40;  // Indirect offset
    public const ushort BPF_MEM = 0x60;  // Memory
    public const ushort BPF_IMM = 0x00;  // Immediate

    // BPF jump fields
    public const ushort BPF_JA = 0x00;   // Jump always
    public const ushort BPF_JEQ = 0x10;  // Jump if equal
    public const ushort BPF_JGT = 0x20;  // Jump if greater than
    public const ushort BPF_JGE = 0x30;  // Jump if greater or equal
    public const ushort BPF_JSET = 0x40; // Jump if set

    // BPF source fields
    public const ushort BPF_K = 0x00;    // Constant
    public const ushort BPF_X = 0x08;    // Index register

    // seccomp_data offsets (for BPF_ABS loads)
    public const int SECCOMP_DATA_NR = 0;        // Syscall number (offset 0)
    public const int SECCOMP_DATA_ARCH = 4;      // Architecture (offset 4)
    public const int SECCOMP_DATA_IP_LO = 8;     // Instruction pointer low (offset 8)
    public const int SECCOMP_DATA_IP_HI = 12;    // Instruction pointer high (offset 12)
    public const int SECCOMP_DATA_ARGS = 16;     // Args start at offset 16, each 8 bytes

    // Architecture audit values
    public const uint AUDIT_ARCH_X86_64 = 0xc000003e;
    public const uint AUDIT_ARCH_AARCH64 = 0xc00000b7;

    /// <summary>
    /// Gets the audit architecture constant for the current platform.
    /// </summary>
    public static uint GetAuditArch() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => AUDIT_ARCH_X86_64,
        Architecture.Arm64 => AUDIT_ARCH_AARCH64,
        _ => throw new PlatformNotSupportedException($"Architecture not supported: {RuntimeInformation.ProcessArchitecture}")
    };

    /// <summary>
    /// Gets the socket syscall number for the current architecture.
    /// </summary>
    public static int GetSocketSyscallNumber() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => SYS_socket_x64,
        Architecture.Arm64 => SYS_socket_arm64,
        _ => throw new PlatformNotSupportedException($"Architecture not supported: {RuntimeInformation.ProcessArchitecture}")
    };

    /// <summary>
    /// Gets the socketpair syscall number for the current architecture.
    /// </summary>
    public static int GetSocketpairSyscallNumber() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => SYS_socketpair_x64,
        Architecture.Arm64 => SYS_socketpair_arm64,
        _ => throw new PlatformNotSupportedException($"Architecture not supported: {RuntimeInformation.ProcessArchitecture}")
    };

    /// <summary>
    /// prctl system call for process control operations.
    /// </summary>
    [LibraryImport("libc", SetLastError = true)]
    public static partial int prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

    /// <summary>
    /// Overload for passing a pointer (for seccomp filter).
    /// </summary>
    [LibraryImport("libc", SetLastError = true)]
    public static partial int prctl(int option, int arg2, IntPtr arg3, ulong arg4, ulong arg5);
}

/// <summary>
/// BPF instruction structure (8 bytes).
/// </summary>
/// <remarks>
/// <para>Each BPF instruction is 8 bytes:</para>
/// <list type="bullet">
/// <item>code (2 bytes): Operation code</item>
/// <item>jt (1 byte): Jump target if true</item>
/// <item>jf (1 byte): Jump target if false</item>
/// <item>k (4 bytes): Constant/offset value</item>
/// </list>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct BpfInstruction
{
    public ushort Code;
    public byte Jt;   // Jump true
    public byte Jf;   // Jump false
    public uint K;    // Constant

    public BpfInstruction(ushort code, byte jt, byte jf, uint k)
    {
        Code = code;
        Jt = jt;
        Jf = jf;
        K = k;
    }

    // Helper methods for creating common instructions

    /// <summary>Load word from seccomp_data at absolute offset.</summary>
    public static BpfInstruction LoadAbsolute(int offset) =>
        new((ushort)(SeccompNative.BPF_LD | SeccompNative.BPF_W | SeccompNative.BPF_ABS), 0, 0, (uint)offset);

    /// <summary>Jump if accumulator equals K.</summary>
    public static BpfInstruction JumpIfEqual(uint k, byte jt, byte jf) =>
        new((ushort)(SeccompNative.BPF_JMP | SeccompNative.BPF_JEQ | SeccompNative.BPF_K), jt, jf, k);

    /// <summary>Return with value K.</summary>
    public static BpfInstruction Return(uint k) =>
        new((ushort)(SeccompNative.BPF_RET | SeccompNative.BPF_K), 0, 0, k);
}

/// <summary>
/// BPF program structure for seccomp.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BpfProgram
{
    public ushort Length;      // Number of instructions
    public IntPtr Filter;      // Pointer to BpfInstruction array

    public BpfProgram(ushort length, IntPtr filter)
    {
        Length = length;
        Filter = filter;
    }
}
