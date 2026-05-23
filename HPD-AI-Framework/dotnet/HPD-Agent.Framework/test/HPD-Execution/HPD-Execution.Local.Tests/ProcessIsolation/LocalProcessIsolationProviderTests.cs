namespace HPD.Execution.Local.Tests.ProcessIsolation;

using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using HPD.Execution.Local;
using HPD.Execution.Contracts;
using HPD.Execution.Runtime;
using HPD.Execution.Local.ProcessIsolation;
using Xunit;

public sealed class LocalProcessIsolationProviderTests
{
    [Fact]
    public async Task Module_registers_process_isolation_provider_and_reports_capability()
    {
        var registry = new ExecutionProviderRegistry();

        registry.RegisterLocalProcessIsolation();

        IReadOnlyList<ProviderDescriptor> providers = await registry.ListAsync();
        ProviderCapabilityReport report = await registry.GetCapabilitiesAsync(LocalProcessIsolationProvider.LocalProviderId);

        registry.ProcessIsolationProviders.Should().ContainSingle();
        providers.Should().ContainSingle(provider => provider.ContractKinds == ProviderContractKind.ProcessIsolation);
        report.Capabilities.Should().Contain(fact =>
            fact.AppliesTo == ProviderContractKind.ProcessIsolation &&
            fact.State == CapabilityState.Supported);
    }

    [Fact]
    public async Task Prepare_compiles_policy_and_marks_invocation()
    {
        var provider = new LocalProcessIsolationProvider();
        ProcessInvocationSpec invocation = new()
        {
            Target = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "unit-1"),
            Command = new ProcessCommandSpec { FileName = "/usr/bin/npm", Arguments = ["install"] },
            Isolation = new ProcessIsolationPolicy
            {
                Mode = ProcessIsolationMode.Isolated,
                Filesystem = new FilesystemAccessPolicy
                {
                    Rules =
                    [
                        new PathAccessRule { Kind = PathAccessRuleKind.AllowWrite, Path = new HostPath("/workspace") },
                        new PathAccessRule { Kind = PathAccessRuleKind.DenyRead, Path = new HostPath("/home/agent/.ssh") },
                    ],
                },
                Network = new NetworkEgressPolicy
                {
                    Mode = NetworkEgressMode.Filtered,
                    AllowedDomains =
                    [
                        new DomainRule { Pattern = "registry.npmjs.org", Kind = DomainRuleKind.ExactHost },
                    ],
                },
            },
        };

        ProcessIsolationPlan plan = await provider.PlanIsolationAsync(invocation, invocation.Isolation);
        IsolatedProcessCommand prepared = await provider.PrepareAsync(invocation, invocation.Isolation, plan);

        plan.Diagnostics.Should().ContainSingle(message => message.Contains("filesystem-rules=2", StringComparison.Ordinal));
        prepared.Invocation.ProviderExtensions.Should().Contain(extension =>
            extension.ProviderId == LocalProcessIsolationProvider.LocalProviderId &&
            extension.SchemaId.Value == "hpd.execution.local.process-isolation.plan");
        provider.LastPreparedPlan.Should().NotBeNull();
        provider.LastPreparedPlan!.Filesystem.Rules.Should().HaveCount(2);
        provider.LastPreparedPlan.Network.AllowedDomains.Single().Pattern.Canonical.Should().Be("registry.npmjs.org");
    }

    [Fact]
    public async Task Local_process_provider_registers_process_invocation_capability()
    {
        var registry = new ExecutionProviderRegistry();

        registry.RegisterLocalProcessProvider();

        IReadOnlyList<ProviderDescriptor> providers = await registry.ListAsync();
        ProviderCapabilityReport report = await registry.GetCapabilitiesAsync(LocalProcessProvider.LocalProviderId);

        registry.ProcessProviders.Should().ContainSingle();
        providers.Should().ContainSingle(provider => provider.ContractKinds == ProviderContractKind.ProcessInvocation);
        report.Capabilities.Should().Contain(fact =>
            fact.AppliesTo == ProviderContractKind.ProcessInvocation &&
            fact.State == CapabilityState.Supported);
    }

    [Fact]
    public async Task Local_process_provider_runs_prepared_process_invocation()
    {
        var registry = new ExecutionProviderRegistry();
        registry.RegisterLocalProcessProvider();
        IProcessProvider provider = registry.ProcessProviders.Single();

        ProcessInvocationSpec invocation = new()
        {
            Target = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "unit-1"),
            Command = ShellInvocation("local-provider-ok"),
            Isolation = ProcessIsolationPolicy.Default with { Mode = ProcessIsolationMode.Disabled },
        };

        ProcessInvocationResult result = await provider.RunAsync(invocation);

        result.CompletionKind.Should().Be(ProcessCompletionKind.Completed);
        result.ExitCode.Should().Be(0);
        System.Text.Encoding.UTF8.GetString(result.Output.Stdout.CapturedBytes.ToArray())
            .Trim()
            .Should()
            .Be("local-provider-ok");
    }

    [SkippableFact]
    public async Task Execution_runtime_runs_through_local_isolation_and_process_providers()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows local OS sandboxing is unsupported.");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Skip.IfNot(await CommandExistsAsync("bwrap"), "bubblewrap is required for local Linux process isolation.");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Skip.IfNot(await CommandExistsAsync("sandbox-exec"), "sandbox-exec is required for local macOS process isolation.");

        await using var manager = new LocalProcessIsolationManager();
        var registry = new ExecutionProviderRegistry();
        registry.RegisterLocalProcessIsolation(manager);
        registry.RegisterLocalProcessProvider();
        var runtime = new InMemoryExecutionRuntime(registry);

        ProcessInvocationSpec invocation = new()
        {
            Target = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "unit-1"),
            Command = ShellInvocation("runtime-local-isolated-ok"),
            Isolation = MinimalExecutableIsolation(),
        };

        ProcessInvocationResult result = await runtime.RunProcessAsync(invocation);

        result.CompletionKind.Should().Be(ProcessCompletionKind.Completed);
        result.ExitCode.Should().Be(0);
        System.Text.Encoding.UTF8.GetString(result.Output.Stdout.CapturedBytes.ToArray())
            .Trim()
            .Should()
            .Be("runtime-local-isolated-ok");
    }

    private static TargetHandle<T> Handle<T>(TargetRouteSegmentKind kind, string id)
        where T : IOperationTargetMarker =>
        new(
            new TargetRoute
            {
                Kind = new TargetKind(typeof(T).Name),
                Scope = new ResourceScope("test-runtime"),
                Segments = [new TargetRouteSegment(kind, id)],
            },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Invoke);

    private static ProcessCommandSpec ShellInvocation(string message)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessCommandSpec
            {
                FileName = "cmd.exe",
                Arguments = ["/c", $"echo {message}"],
            };
        }

        return new ProcessCommandSpec
        {
            FileName = "/bin/sh",
            Arguments = ["-c", $"printf '%s\\n' {Quote(message)}"],
        };
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static ProcessIsolationPolicy MinimalExecutableIsolation() =>
        new()
        {
            Mode = ProcessIsolationMode.Isolated,
            Filesystem = new FilesystemAccessPolicy
            {
                DangerousPaths = DangerousPathPolicy.Default with
                {
                    ProtectSensitiveDefaults = false,
                },
            },
            Network = new NetworkEgressPolicy
            {
                Mode = NetworkEgressMode.Unrestricted,
            },
            Violations = ProcessViolationPolicy.Default with
            {
                Action = ProcessViolationAction.ObserveOnly,
            },
        };

    private static async Task<bool> CommandExistsAsync(string command)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = command,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };

            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
