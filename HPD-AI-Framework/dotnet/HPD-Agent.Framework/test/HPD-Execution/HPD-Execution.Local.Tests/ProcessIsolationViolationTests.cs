using FluentAssertions;
using Xunit;

namespace HPD.Execution.Local.Tests;

public class ProcessIsolationViolationTests
{
    [Fact]
    public void ProcessIsolationViolation_CanBeCreated()
    {
        var violation = new ProcessIsolationViolation
        {
            Type = ProcessIsolationViolationType.FilesystemWrite,
            Message = "Attempted write to /etc/passwd",
            Timestamp = DateTimeOffset.UtcNow,
            Path = "/etc/passwd"
        };

        violation.Type.Should().Be(ProcessIsolationViolationType.FilesystemWrite);
        violation.Message.Should().Contain("/etc/passwd");
        violation.Path.Should().Be("/etc/passwd");
    }

    [Fact]
    public void ProcessIsolationViolation_PathIsOptional()
    {
        var violation = new ProcessIsolationViolation
        {
            Type = ProcessIsolationViolationType.NetworkAccess,
            Message = "Network access denied",
            Timestamp = DateTimeOffset.UtcNow
        };

        violation.Path.Should().BeNull();
    }

    [Theory]
    [InlineData(ProcessIsolationViolationType.FilesystemRead)]
    [InlineData(ProcessIsolationViolationType.FilesystemWrite)]
    [InlineData(ProcessIsolationViolationType.NetworkAccess)]
    public void ProcessIsolationViolationType_AllTypesValid(ProcessIsolationViolationType type)
    {
        var violation = new ProcessIsolationViolation
        {
            Type = type,
            Message = "Test",
            Timestamp = DateTimeOffset.UtcNow
        };

        violation.Type.Should().Be(type);
    }

    [Fact]
    public void ProcessIsolationViolationType_HasExpectedValues()
    {
        Enum.GetValues<ProcessIsolationViolationType>().Should().HaveCount(3);
    }
}
