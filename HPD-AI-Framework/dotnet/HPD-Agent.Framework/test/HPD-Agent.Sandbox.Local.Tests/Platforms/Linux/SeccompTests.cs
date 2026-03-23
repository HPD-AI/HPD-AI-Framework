using System.Runtime.InteropServices;
using HPD.Sandbox.Local.Platforms.Linux.Seccomp;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Platforms.Linux;

public class SeccompNativeTests
{
    [Fact]
    public void Constants_HaveCorrectValues()
    {
        // Verify key constants match Linux kernel definitions
        Assert.Equal(38, SeccompNative.PR_SET_NO_NEW_PRIVS);
        Assert.Equal(22, SeccompNative.PR_SET_SECCOMP);
        Assert.Equal(2, SeccompNative.SECCOMP_MODE_FILTER);
        Assert.Equal(1u, SeccompNative.AF_UNIX);
    }

    [Fact]
    public void SyscallNumbers_CorrectForX64()
    {
        Assert.Equal(41, SeccompNative.SYS_socket_x64);
        Assert.Equal(53, SeccompNative.SYS_socketpair_x64);
    }

    [Fact]
    public void SyscallNumbers_CorrectForArm64()
    {
        Assert.Equal(198, SeccompNative.SYS_socket_arm64);
        Assert.Equal(199, SeccompNative.SYS_socketpair_arm64);
    }

    [Fact]
    public void AuditArch_CorrectValues()
    {
        Assert.Equal(0xc000003eu, SeccompNative.AUDIT_ARCH_X86_64);
        Assert.Equal(0xc00000b7u, SeccompNative.AUDIT_ARCH_AARCH64);
    }

    [Fact]
    public void SeccompReturnValues_HaveCorrectBits()
    {
        Assert.Equal(0x7fff0000u, SeccompNative.SECCOMP_RET_ALLOW);
        Assert.Equal(0x00050000u, SeccompNative.SECCOMP_RET_ERRNO);
        Assert.Equal(0x80000000u, SeccompNative.SECCOMP_RET_KILL_PROCESS);
    }

    [Fact]
    public void GetAuditArch_ReturnsCorrectValueForArchitecture()
    {
        var arch = RuntimeInformation.ProcessArchitecture;

        if (arch == Architecture.X64)
        {
            Assert.Equal(SeccompNative.AUDIT_ARCH_X86_64, SeccompNative.GetAuditArch());
        }
        else if (arch == Architecture.Arm64)
        {
            Assert.Equal(SeccompNative.AUDIT_ARCH_AARCH64, SeccompNative.GetAuditArch());
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => SeccompNative.GetAuditArch());
        }
    }

    [Fact]
    public void GetSocketSyscallNumber_ReturnsCorrectValue()
    {
        var arch = RuntimeInformation.ProcessArchitecture;

        if (arch == Architecture.X64)
        {
            Assert.Equal(41, SeccompNative.GetSocketSyscallNumber());
        }
        else if (arch == Architecture.Arm64)
        {
            Assert.Equal(198, SeccompNative.GetSocketSyscallNumber());
        }
    }

    [Fact]
    public void GetSocketpairSyscallNumber_ReturnsCorrectValue()
    {
        var arch = RuntimeInformation.ProcessArchitecture;

        if (arch == Architecture.X64)
        {
            Assert.Equal(53, SeccompNative.GetSocketpairSyscallNumber());
        }
        else if (arch == Architecture.Arm64)
        {
            Assert.Equal(199, SeccompNative.GetSocketpairSyscallNumber());
        }
    }
}

public class BpfInstructionTests
{
    [Fact]
    public void LoadAbsolute_CreatesCorrectInstruction()
    {
        var instr = BpfInstruction.LoadAbsolute(0);

        Assert.Equal((ushort)(SeccompNative.BPF_LD | SeccompNative.BPF_W | SeccompNative.BPF_ABS), instr.Code);
        Assert.Equal((byte)0, instr.Jt);
        Assert.Equal((byte)0, instr.Jf);
        Assert.Equal(0u, instr.K);
    }

    [Fact]
    public void JumpIfEqual_CreatesCorrectInstruction()
    {
        var instr = BpfInstruction.JumpIfEqual(42, 2, 1);

        Assert.Equal((ushort)(SeccompNative.BPF_JMP | SeccompNative.BPF_JEQ | SeccompNative.BPF_K), instr.Code);
        Assert.Equal((byte)2, instr.Jt);
        Assert.Equal((byte)1, instr.Jf);
        Assert.Equal(42u, instr.K);
    }

    [Fact]
    public void Return_CreatesCorrectInstruction()
    {
        var instr = BpfInstruction.Return(SeccompNative.SECCOMP_RET_ALLOW);

        Assert.Equal((ushort)(SeccompNative.BPF_RET | SeccompNative.BPF_K), instr.Code);
        Assert.Equal(SeccompNative.SECCOMP_RET_ALLOW, instr.K);
    }

    [Fact]
    public void BpfInstruction_HasCorrectSize()
    {
        // BPF instruction should be 8 bytes
        Assert.Equal(8, Marshal.SizeOf<BpfInstruction>());
    }
}

public class SeccompChildProcessTests
{
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var helper = new SeccompChildProcess();
        Assert.NotNull(helper);
        helper.Dispose();
    }
}
