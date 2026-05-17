using FluentAssertions;
using HPD.Sandbox.Local.Platforms;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Platforms;

public sealed class SandboxDependencyCheckTests
{
    [Fact]
    public void IsAvailable_WhenNoErrors_ReturnsTrue()
    {
        var check = new SandboxDependencyCheck
        {
            Warnings = ["optional layer unavailable"],
        };

        check.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_WhenErrors_ReturnsFalse()
    {
        var check = new SandboxDependencyCheck
        {
            Errors = ["required dependency missing"],
        };

        check.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void FromIssues_SeparatesErrorsAndWarnings()
    {
        var check = SandboxDependencyCheck.FromIssues(
        [
            SandboxDependencyIssue.Error(
                "linux.bwrap.missing",
                "bubblewrap",
                "bubblewrap (bwrap) is not installed"),
            SandboxDependencyIssue.Warning(
                "linux.seccomp.helper.missing",
                "seccomp",
                "No pre-built seccomp helper was found")
        ]);

        check.IsAvailable.Should().BeFalse();
        check.Errors.Should().ContainSingle("bubblewrap (bwrap) is not installed");
        check.Warnings.Should().ContainSingle("No pre-built seccomp helper was found");
        check.Issues.Should().HaveCount(2);
        check.Issues.Should().Contain(issue =>
            issue.Code == "linux.seccomp.helper.missing" &&
            issue.Component == "seccomp" &&
            issue.Severity == SandboxDependencySeverity.Warning);
    }

    [Fact]
    public void IsAvailable_WhenOnlyWarningIssues_ReturnsTrue()
    {
        var check = SandboxDependencyCheck.FromIssues(
        [
            SandboxDependencyIssue.Warning(
                "linux.seccomp.unsupported",
                "seccomp",
                "Seccomp is not supported on this system")
        ]);

        check.IsAvailable.Should().BeTrue();
    }
}
