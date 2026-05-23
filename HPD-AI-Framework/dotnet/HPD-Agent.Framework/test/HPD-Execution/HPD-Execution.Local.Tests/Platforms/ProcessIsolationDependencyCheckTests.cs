using FluentAssertions;
using HPD.Execution.Local.Platforms;
using Xunit;

namespace HPD.Execution.Local.Tests.Platforms;

public sealed class ProcessIsolationDependencyCheckTests
{
    [Fact]
    public void IsAvailable_WhenNoErrors_ReturnsTrue()
    {
        var check = new ProcessIsolationDependencyCheck
        {
            Warnings = ["optional layer unavailable"],
        };

        check.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_WhenErrors_ReturnsFalse()
    {
        var check = new ProcessIsolationDependencyCheck
        {
            Errors = ["required dependency missing"],
        };

        check.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void FromIssues_SeparatesErrorsAndWarnings()
    {
        var check = ProcessIsolationDependencyCheck.FromIssues(
        [
            ProcessIsolationDependencyIssue.Error(
                "linux.bwrap.missing",
                "bubblewrap",
                "bubblewrap (bwrap) is not installed"),
            ProcessIsolationDependencyIssue.Warning(
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
            issue.Severity == ProcessIsolationDependencySeverity.Warning);
    }

    [Fact]
    public void IsAvailable_WhenOnlyWarningIssues_ReturnsTrue()
    {
        var check = ProcessIsolationDependencyCheck.FromIssues(
        [
            ProcessIsolationDependencyIssue.Warning(
                "linux.seccomp.unsupported",
                "seccomp",
                "Seccomp is not supported on this system")
        ]);

        check.IsAvailable.Should().BeTrue();
    }
}
