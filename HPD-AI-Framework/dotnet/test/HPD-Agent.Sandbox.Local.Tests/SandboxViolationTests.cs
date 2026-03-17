using FluentAssertions;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class SandboxViolationTests
{
    [Fact]
    public void SandboxViolation_CanBeCreated()
    {
        var violation = new SandboxViolation
        {
            Type = ViolationType.FilesystemWrite,
            Message = "Attempted write to /etc/passwd",
            Timestamp = DateTimeOffset.UtcNow,
            Path = "/etc/passwd"
        };

        violation.Type.Should().Be(ViolationType.FilesystemWrite);
        violation.Message.Should().Contain("/etc/passwd");
        violation.Path.Should().Be("/etc/passwd");
    }

    [Fact]
    public void SandboxViolation_PathIsOptional()
    {
        var violation = new SandboxViolation
        {
            Type = ViolationType.NetworkAccess,
            Message = "Network access denied",
            Timestamp = DateTimeOffset.UtcNow
        };

        violation.Path.Should().BeNull();
    }

    [Theory]
    [InlineData(ViolationType.FilesystemRead)]
    [InlineData(ViolationType.FilesystemWrite)]
    [InlineData(ViolationType.NetworkAccess)]
    public void ViolationType_AllTypesValid(ViolationType type)
    {
        var violation = new SandboxViolation
        {
            Type = type,
            Message = "Test",
            Timestamp = DateTimeOffset.UtcNow
        };

        violation.Type.Should().Be(type);
    }

    [Fact]
    public void ViolationType_HasExpectedValues()
    {
        Enum.GetValues<ViolationType>().Should().HaveCount(3);
    }
}
