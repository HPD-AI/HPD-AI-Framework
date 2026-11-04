namespace HPD.Environment.HostProcess.Tests.Platforms;

using FluentAssertions;
using HPD.Environment.Contracts;
using HPD.Environment.HostProcess.Platforms;
using HPD.Environment.HostProcess.Platforms.Windows;
using HPD.Environment.HostProcess.ProcessIsolation;
using HPD.Environment.Runtime;

public sealed class WindowsProcessIsolationBackendTests
{
    [Fact]
    public async Task Fail_closed_policy_rejects_degraded_execution()
    {
        SandboxIsolationPlan plan = SandboxIsolationCompiler.Compile(
            ProcessIsolationPolicy.Default with
            {
                Degradation =
                    ProcessIsolationDegradationPolicy.FailClosed,
            });
        await using var backend =
            new WindowsProcessIsolationBackend(plan);

        ProcessIsolationDependencyCheck dependency =
            await backend.GetDependencyCheckAsync(default);
        dependency.IsAvailable.Should().BeFalse();
        dependency.Errors.Should().Contain(message =>
            message.Contains(
                "no accepted OS confinement",
                StringComparison.Ordinal));

        Func<Task> wrap = async () =>
            await backend.WrapCommandAsync(
                new CommandInvocation(
                    "cmd.exe",
                    ["/c", "echo test"]),
                default);
        await wrap.Should()
            .ThrowAsync<PlatformNotSupportedException>();
    }

    [Fact]
    public async Task Explicitly_degraded_policy_runs_without_false_isolation_claim()
    {
        SandboxIsolationPlan plan = SandboxIsolationCompiler.Compile(
            ProcessIsolationPolicy.Default with
            {
                Degradation =
                    ProcessIsolationDegradationPolicy.FailClosed with
                    {
                        Mode =
                            ProcessIsolationDegradationMode
                                .AllowProviderDegraded,
                    },
            });
        await using var backend =
            new WindowsProcessIsolationBackend(plan);

        ProcessIsolationDependencyCheck dependency =
            await backend.GetDependencyCheckAsync(default);
        dependency.IsAvailable.Should().BeTrue();
        dependency.Warnings.Should().Contain(message =>
            message.Contains(
                "without OS filesystem",
                StringComparison.Ordinal));

        PreparedSandboxCommand prepared =
            await backend.WrapCommandAsync(
                new CommandInvocation(
                    "cmd.exe",
                    ["/c", "echo test"]),
                default);
        prepared.FileName.Should().Be("cmd.exe");
        prepared.ArgumentList.Should().Equal("/c", "echo test");
    }
}
