namespace HPD.Environment.HostProcess.Tests.Platforms;

using FluentAssertions;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

using System.Security.Principal;
using HPD.Environment.Contracts;
using HPD.Environment.HostProcess.Platforms;
using HPD.Environment.HostProcess.Platforms.Windows;
using HPD.Environment.HostProcess.ProcessIsolation;
using HPD.Environment.Runtime;

public sealed class WindowsProcessIsolationBackendTests
{
    [Fact]
    public void Windows_executable_resolution_handles_absolute_relative_and_bare_paths()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var workspace = Directory.CreateTempSubdirectory("hpd-executable-resolution-");
        try
        {
            string systemCommand = Path.Combine(System.Environment.SystemDirectory, "cmd.exe");
            string localCommand = Path.Combine(workspace.FullName, "project-tool.exe");
            File.Copy(systemCommand, localCommand);
            var environment = new Dictionary<string, string>();
            var roots = new[] { workspace.FullName };

            HostProcessInvocationHandle.ResolveWindowsExecutable(systemCommand, workspace.FullName, roots, environment)
                .Should().Be(systemCommand);
            HostProcessInvocationHandle.ResolveWindowsExecutable(@".\project-tool", workspace.FullName, roots, environment)
                .Should().Be(localCommand);
            HostProcessInvocationHandle.ResolveWindowsExecutable("cmd", workspace.FullName, roots, environment)
                .Should().Be(systemCommand);
            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                if (OperatingSystem.IsWindows())
                    HostProcessInvocationHandle.ResolveWindowsExecutable(@"..\project-tool.exe", workspace.FullName, roots, environment);
            });
            Assert.Throws<FileNotFoundException>(() =>
            {
                if (OperatingSystem.IsWindows())
                    HostProcessInvocationHandle.ResolveWindowsExecutable("project-tool", workspace.FullName, roots, environment);
            });
            environment["PATH"] = workspace.FullName;
            HostProcessInvocationHandle.ResolveWindowsExecutable("project-tool", workspace.FullName, roots, environment)
                .Should().Be(localCommand);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AppContainer_sends_test_markers_to_a_configured_udp_receiver()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string? target = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_UDP_RECEIVER");
        if (string.IsNullOrWhiteSpace(target))
            return;

        var endpoint = System.Net.IPEndPoint.Parse(target);
        string runId = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_UDP_RUN_ID") ?? "";
        if (!Guid.TryParseExact(runId, "N", out var id))
            throw new InvalidOperationException("HPD_WINDOWS_UDP_RUN_ID must be a GUID in N format.");
        string executable = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_SOCKET_PROBE")
            ?? throw new InvalidOperationException("The native socket probe is required.");

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var control = new System.Net.Sockets.UdpClient(endpoint.AddressFamily);
        await control.SendAsync(System.Text.Encoding.ASCII.GetBytes($"{id:N}:before").AsMemory(), endpoint, deadline.Token);

        using var profile = new WindowsAppContainerProfile();
        using var workspace = WindowsSandboxWorkspace.Create(profile.Sid);
        string command = FormattableString.Invariant($"\"{executable}\" udp {endpoint.Address} {endpoint.Port} {id:N}:sandbox");
        using var child = WindowsRestrictedProcess.Start(null, executable, command,
            workspace.Path, AppContainerLaunchEnvironment(), appContainer: profile);
        await child.WaitForExitAsync(deadline.Token);
        child.ReadExitCode().Should().BeOneOf(0, 10);

        await control.SendAsync(System.Text.Encoding.ASCII.GetBytes($"{id:N}:after").AsMemory(), endpoint, deadline.Token);
    }

    [Fact]
    public async Task AppContainer_does_not_receive_a_reply_from_configured_remote_udp_resolver()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string? target = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_REMOTE_DNS");
        if (string.IsNullOrWhiteSpace(target))
            return;

        var endpoint = System.Net.IPEndPoint.Parse(target);
        string executable = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_SOCKET_PROBE")
            ?? throw new InvalidOperationException("The native socket probe is required.");
        string arguments = FormattableString.Invariant($"dns {endpoint.Address} {endpoint.Port}");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        async Task VerifyHostReplyAsync()
        {
            using var control = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            try
            {
                await control.WaitForExitAsync(deadline.Token);
                control.ExitCode.Should().Be(0, "the host must receive a DNS reply");
            }
            finally
            {
                if (!control.HasExited)
                    control.Kill();
            }
        }

        await VerifyHostReplyAsync();
        using var profile = new WindowsAppContainerProfile();
        using var workspace = WindowsSandboxWorkspace.Create(profile.Sid);
        using var child = WindowsRestrictedProcess.Start(null, executable, $"\"{executable}\" {arguments}",
            workspace.Path, AppContainerLaunchEnvironment(), appContainer: profile);
        await child.WaitForExitAsync(deadline.Token);
        child.ReadExitCode().Should().BeOneOf(10, 11);
        await VerifyHostReplyAsync();
    }

    [Fact]
    public async Task AppContainer_cannot_connect_to_configured_remote_tcp_endpoint()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string? target = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_REMOTE_TCP");
        if (string.IsNullOrWhiteSpace(target))
            return;
        var endpoint = System.Net.IPEndPoint.Parse(target);
        string executable = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_SOCKET_PROBE")
            ?? throw new InvalidOperationException("The native socket probe is required.");
        using var controlDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var control = new System.Net.Sockets.TcpClient(endpoint.AddressFamily);
        await control.ConnectAsync(endpoint.Address, endpoint.Port, controlDeadline.Token);

        using var profile = new WindowsAppContainerProfile();
        using var workspace = WindowsSandboxWorkspace.Create(profile.Sid);
        using var child = WindowsRestrictedProcess.Start(null, executable,
            FormattableString.Invariant($"\"{executable}\" tcp {endpoint.Address} {endpoint.Port}"),
            workspace.Path, AppContainerLaunchEnvironment(), appContainer: profile);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await child.WaitForExitAsync(deadline.Token);
            child.ReadExitCode().Should().Be(10);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // AppContainer can drop connection attempts instead of returning access denied.
            child.StopProcessTree();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await child.WaitForExitAsync(cleanup.Token);
        }
    }

    [Fact]
    public void Reopening_a_workspace_removes_abandoned_grants_and_preserves_other_acl_entries()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var abandoned = new WindowsAppContainerProfile();
        using var current = new WindowsAppContainerProfile();
        var root = Directory.CreateTempSubdirectory("hpd-grant-recovery-");
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(root.FullName.ToUpperInvariant())));
        string journalDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "HPD", "WindowsWorkspaceGrants", key);
        try
        {
            var unrelated = new SecurityIdentifier(WellKnownSidType.BuiltinGuestsSid, null);
            var acl = System.IO.FileSystemAclExtensions.GetAccessControl(root);
            foreach (var sid in new[] { abandoned.Sid, unrelated })
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    sid, System.Security.AccessControl.FileSystemRights.Read,
                    System.Security.AccessControl.AccessControlType.Allow));
            System.IO.FileSystemAclExtensions.SetAccessControl(root, acl);
            Directory.CreateDirectory(journalDir);
            string journal = Path.Combine(journalDir, abandoned.Sid.Value + ".grant");
            File.WriteAllBytes(journal, []);

            [System.Runtime.Versioning.SupportedOSPlatform("windows")]
            bool HasGrant(SecurityIdentifier sid) => System.IO.FileSystemAclExtensions.GetAccessControl(root)
                .GetAccessRules(true, false, typeof(SecurityIdentifier))
                .Cast<System.Security.AccessControl.FileSystemAccessRule>()
                .Any(rule => OperatingSystem.IsWindows() && rule.IdentityReference.Equals(sid));

            using (new WindowsWorkspaceGrant(root.FullName, current.Sid, writable: false))
            {
                File.Exists(journal).Should().BeFalse();
                HasGrant(abandoned.Sid).Should().BeFalse();
                HasGrant(current.Sid).Should().BeTrue();
                HasGrant(unrelated).Should().BeTrue();
            }
            HasGrant(current.Sid).Should().BeFalse();
            HasGrant(unrelated).Should().BeTrue();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AppContainer_acl_grant_allows_live_writes_to_an_ordinary_checkout()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var profile = new WindowsAppContainerProfile();
        var checkout = Directory.CreateTempSubdirectory("hpd-live-checkout-");
        var outside = Directory.CreateTempSubdirectory("hpd-live-outside-");
        try
        {
            string file = Path.Combine(checkout.FullName, "project.txt");
            File.WriteAllText(file, "original");
            var acl = System.IO.FileSystemAclExtensions.GetAccessControl(checkout);
            var grant = new System.Security.AccessControl.FileSystemAccessRule(
                profile.Sid, System.Security.AccessControl.FileSystemRights.Modify,
                System.Security.AccessControl.InheritanceFlags.ContainerInherit |
                System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                System.Security.AccessControl.PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow);
            acl.AddAccessRule(grant);
            System.IO.FileSystemAclExtensions.SetAccessControl(checkout, acl);

            string shell = Path.Combine(System.Environment.SystemDirectory, "cmd.exe");
            using var child = WindowsRestrictedProcess.Start(null, shell,
                $"\"{shell}\" /d /c \"type project.txt && echo updated>project.txt\"",
                checkout.FullName, AppContainerLaunchEnvironment(), appContainer: profile);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await child.WaitForExitAsync(deadline.Token);
            using var stdout = new StreamReader(child.StandardOutput);
            (await stdout.ReadToEndAsync(deadline.Token)).Should().Contain("original");
            child.ReadExitCode().Should().Be(0);
            File.ReadAllText(file).Trim().Should().Be("updated");

            string protectedFile = Path.Combine(outside.FullName, "protected.txt");
            File.WriteAllText(protectedFile, "protected");
            string junction = Path.Combine(checkout.FullName, "outside");
            using var link = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                shell, $"/d /c mklink /J \"{junction}\" \"{outside.FullName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            await link.WaitForExitAsync(deadline.Token);
            link.ExitCode.Should().Be(0);
            using var escape = WindowsRestrictedProcess.Start(null, shell,
                $"\"{shell}\" /d /c \"echo escaped>outside\\protected.txt\"",
                checkout.FullName, AppContainerLaunchEnvironment(), appContainer: profile);
            await escape.WaitForExitAsync(deadline.Token);
            File.ReadAllText(protectedFile).Should().Be("protected");
            Directory.Delete(junction);

            acl = System.IO.FileSystemAclExtensions.GetAccessControl(checkout);
            acl.RemoveAccessRuleSpecific(grant);
            var readOnlyGrant = new System.Security.AccessControl.FileSystemAccessRule(
                profile.Sid, System.Security.AccessControl.FileSystemRights.ReadAndExecute,
                grant.InheritanceFlags, grant.PropagationFlags, grant.AccessControlType);
            acl.AddAccessRule(readOnlyGrant);
            System.IO.FileSystemAclExtensions.SetAccessControl(checkout, acl);
            using var revoked = WindowsRestrictedProcess.Start(null, shell,
                $"\"{shell}\" /d /c \"type project.txt && echo revoked>project.txt\"",
                checkout.FullName, AppContainerLaunchEnvironment(), appContainer: profile);
            await revoked.WaitForExitAsync(deadline.Token);
            using var readOnlyOutput = new StreamReader(revoked.StandardOutput);
            (await readOnlyOutput.ReadToEndAsync(deadline.Token)).Should().Contain("updated");
            File.ReadAllText(file).Trim().Should().Be("updated");

            acl = System.IO.FileSystemAclExtensions.GetAccessControl(checkout);
            acl.RemoveAccessRuleSpecific(readOnlyGrant);
            System.IO.FileSystemAclExtensions.SetAccessControl(checkout, acl);
            using var removed = WindowsRestrictedProcess.Start(null, shell,
                $"\"{shell}\" /d /c type \"{file}\"",
                System.Environment.SystemDirectory, AppContainerLaunchEnvironment(), appContainer: profile);
            await removed.WaitForExitAsync(deadline.Token);
            removed.ReadExitCode().Should().NotBe(0);
        }
        finally
        {
            checkout.Delete(recursive: true);
            outside.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData(null)]
    public async Task AppContainer_child_cannot_deliver_tcp_or_udp_to_host_listeners(string? address)
    {
        if (!OperatingSystem.IsWindows())
            return;

        string? executable = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_SOCKET_PROBE");
        if (string.IsNullOrEmpty(executable))
            return;

        // Establish that both listeners and the native probe are usable on the host.
        var ip = address is null
            ? System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Select(entry => entry.Address)
                .FirstOrDefault(candidate => candidate.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !System.Net.IPAddress.IsLoopback(candidate))
            : System.Net.IPAddress.Parse(address);
        if (ip is null)
            return;
        using var tcp = new System.Net.Sockets.TcpListener(ip, 0);
        using var udp = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(ip, 0));
        tcp.Start();
        var tcpEndpoint = (System.Net.IPEndPoint)tcp.LocalEndpoint;
        var udpEndpoint = (System.Net.IPEndPoint)udp.Client.LocalEndPoint!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var host = new System.Net.Sockets.TcpClient(ip.AddressFamily);
        await host.ConnectAsync(ip, tcpEndpoint.Port, deadline.Token);
        using var connection = await tcp.AcceptTcpClientAsync(deadline.Token);
        using var sender = new System.Net.Sockets.UdpClient(ip.AddressFamily);
        await sender.SendAsync(new byte[] { 1 }.AsMemory(), udpEndpoint, deadline.Token);
        (await udp.ReceiveAsync(deadline.Token)).Buffer.Should().Equal(new byte[] { 1 });

        using var profile = new WindowsAppContainerProfile();
        using var workspace = WindowsSandboxWorkspace.Create(profile.Sid);
        var environment = AppContainerLaunchEnvironment();
        foreach (var (protocol, endpoint) in new[] { ("tcp", tcpEndpoint), ("udp", udpEndpoint) })
        {
            using var child = WindowsRestrictedProcess.Start(null, executable,
                FormattableString.Invariant($"\"{executable}\" {protocol} {ip} {endpoint.Port}"),
                workspace.Path, environment, appContainer: profile);
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            attempt.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await child.WaitForExitAsync(attempt.Token);
                if (protocol == "tcp")
                    child.ReadExitCode().Should().Be(10);
                else
                    child.ReadExitCode().Should().BeOneOf(0, 10);
            }
            catch (OperationCanceledException) when (protocol == "tcp" && !deadline.IsCancellationRequested)
            {
                child.StopProcessTree();
                await child.WaitForExitAsync(deadline.Token);
            }
        }

        tcp.Pending().Should().BeFalse();
        udp.Available.Should().Be(0);
    }

    [Fact]
    public async Task AppContainer_child_writes_to_its_workspace_and_redirects_output()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var profile = new WindowsAppContainerProfile();
        using var workspace = WindowsSandboxWorkspace.Create(profile.Sid);
        using var outside = new WindowsAppContainerProfile();
        using var otherWorkspace = WindowsSandboxWorkspace.Create(outside.Sid);
        string protectedFile = Path.Combine(otherWorkspace.Path, "protected.txt");
        File.WriteAllText(protectedFile, "original");
        string shell = Path.Combine(System.Environment.SystemDirectory, "cmd.exe");
        using var child = WindowsRestrictedProcess.Start(null, shell,
            $"\"{shell}\" /d /c \"del /q \"{protectedFile}\" >nul 2>&1 & echo inside>result.txt && type result.txt\"",
            workspace.Path, AppContainerLaunchEnvironment(), appContainer: profile);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await child.WaitForExitAsync(deadline.Token);
        child.ReadExitCode().Should().Be(0);
        using var output = new StreamReader(child.StandardOutput);
        (await output.ReadToEndAsync(deadline.Token)).Trim().Should().Be("inside");
        File.ReadAllText(Path.Combine(workspace.Path, "result.txt")).Trim().Should().Be("inside");
        File.ReadAllText(protectedFile).Should().Be("original");
    }

    private static Dictionary<string, string> AppContainerLaunchEnvironment()
    {
        var environment = LaunchEnvironment();
        environment["LOCALAPPDATA"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        return environment;
    }

    [Theory]
    [InlineData("HPD_APPCONTAINER_TEST_POWERSHELL", "-NoLogo -NoProfile -NonInteractive -Command \"Write-Output HPD_OK\"", "HPD_OK")]
    [InlineData("HPD_APPCONTAINER_TEST_GIT", "--version", "git version")]
    public async Task AppContainer_runs_configured_command_line_tools(
        string executableVariable, string arguments, string expectedOutput)
    {
        if (!OperatingSystem.IsWindows())
            return;

        string? executable = System.Environment.GetEnvironmentVariable(executableVariable);
        if (string.IsNullOrEmpty(executable))
            return;

        using var profile = new WindowsAppContainerProfile();
        using var workspace = WindowsSandboxWorkspace.Create(profile.Sid);
        using var child = WindowsRestrictedProcess.Start(null, executable, $"\"{executable}\" {arguments}",
            workspace.Path, AppContainerLaunchEnvironment(), appContainer: profile);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var stdout = new StreamReader(child.StandardOutput);
        using var stderr = new StreamReader(child.StandardError!);
        var output = stdout.ReadToEndAsync(deadline.Token);
        var errors = stderr.ReadToEndAsync(deadline.Token);
        await child.WaitForExitAsync(deadline.Token);
        child.ReadExitCode().Should().Be(0, "the tool reported {0}", await errors);
        (await output).Should().Contain(expectedOutput);
    }

    [Fact]
    public async Task Fail_closed_policy_rejects_degraded_execution()
    {
        SandboxIsolationPlan plan = SandboxIsolationCompiler.Compile(
            ProcessIsolationPolicy.Default with
            {
                Network = new NetworkEgressPolicy { Mode = NetworkEgressMode.Unrestricted },
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
                Network = new NetworkEgressPolicy { Mode = NetworkEgressMode.Unrestricted },
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

    [Theory]
    [InlineData(NetworkEgressMode.Blocked, ProcessIsolationDegradationMode.ObserveOnly)]
    [InlineData(NetworkEgressMode.Filtered, ProcessIsolationDegradationMode.AllowProviderDegraded)]
    public async Task Required_network_enforcement_rejects_degraded_execution(
        NetworkEgressMode networkMode, ProcessIsolationDegradationMode degradationMode)
    {
        var plan = SandboxIsolationCompiler.Compile(ProcessIsolationPolicy.Default with
        {
            Network = new NetworkEgressPolicy { Mode = networkMode },
            Interactive = ProcessIsolationPolicy.Default.Interactive with { AllowLocalBinding = true },
            Degradation = ProcessIsolationDegradationPolicy.FailClosed with { Mode = degradationMode },
        });
        await using var backend = new WindowsProcessIsolationBackend(plan);

        var dependencies = await backend.GetDependencyCheckAsync(default);
        dependencies.IsAvailable.Should().BeFalse();
        dependencies.Errors.Should().Contain(message => message.Contains("cannot enforce blocked or filtered network egress"));

        Func<Task> wrap = () => backend.WrapCommandAsync(new CommandInvocation("cmd.exe", []), default);
        await wrap.Should().ThrowAsync<PlatformNotSupportedException>();
    }

    [Fact]
    public async Task Blocked_policy_prepares_and_launches_an_AppContainer_command()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var plan = SandboxIsolationCompiler.Compile(ProcessIsolationPolicy.Default);
        await using var backend = new WindowsProcessIsolationBackend(plan);
        var prepared = await backend.WrapCommandAsync(
            new CommandInvocation("cmd", ["/d", "/c", "echo HPD_OK"]), default);
        prepared.UseWindowsAppContainer.Should().BeTrue();
        await using var invocation = new HostProcessInvocationHandle(new ProcessInvocationSpec
        {
            Target = new TargetHandle<ExecutionUnit>(
                new TargetRoute { Kind = new TargetKind("test"), Scope = default, ProviderId = HostProcessProviderDescriptor.ProviderId },
                TargetHandleLifetime.LiveCapability, TargetHandleAuthority.Control, ProviderGeneration: 0),
            Command = new ProcessCommandSpec
            {
                FileName = prepared.FileName,
                Arguments = prepared.ArgumentList,
            },
        }, sink: null);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await invocation.StartAppContainerAsync(plan, timeout.Token);
        var result = await invocation.WaitAsync(timeout.Token);
        result.ExitCode.Should().Be(0);
        System.Text.Encoding.UTF8.GetString(result.Output.Stdout.CapturedBytes.Span).Trim().Should().Be("HPD_OK");
    }

    [Fact]
    public void Windows_launch_security_creates_a_token_for_a_workspace_sid()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Use a distinct restricting SID. No workspace ACL changes are needed to create the token.
        using var sid = new WindowsLaunchSecurity.SecurityIdentifierHandle("S-1-5-21-1-2-3-1001");
        using var token = WindowsLaunchSecurity.CreateWriteRestrictedToken(sid);
        token.IsInvalid.Should().BeFalse();
    }

    [Fact]
    public async Task Closing_the_windows_job_terminates_its_process()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var job = WindowsLaunchSecurity.CreateProcessTreeJob();
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(System.Environment.SystemDirectory, "cmd.exe"),
            Arguments = "/d /c pause",
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        })!;
        try
        {
            WindowsLaunchSecurity.AssignProcess(job, process.SafeHandle);
            job.Dispose();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
            process.HasExited.Should().BeTrue();
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }

    [Fact]
    public async Task Restricted_child_cannot_delete_an_existing_file_outside_the_workspace()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Directory.CreateTempSubdirectory("hpd-delete-");
        try
        {
            string target = Path.Combine(root.FullName, "outside.txt");
            File.WriteAllText(target, "original");
            using var sid = new WindowsLaunchSecurity.SecurityIdentifierHandle("S-1-5-21-1-2-3-1001");
            using var token = WindowsLaunchSecurity.CreateWriteRestrictedToken(sid);
            string system = System.Environment.SystemDirectory;
            string shell = Path.Combine(system, "cmd.exe");
            using var child = WindowsRestrictedProcess.Start(token, shell,
                $"\"{shell}\" /d /c del /q \"{target}\" & echo finished", system, LaunchEnvironment());
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await child.WaitForExitAsync(deadline.Token);
            using var output = new StreamReader(child.StandardOutput);
            (await output.ReadToEndAsync()).Trim().Should().EndWith("finished");
            File.ReadAllText(target).Should().Be("original");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Restricted_child_writes_only_to_the_workspace()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string root = Directory.CreateTempSubdirectory("hpd-launch-").FullName;
        try
        {

            string sidValue = $"S-1-5-21-{Random.Shared.Next(1, int.MaxValue)}-1-2-1001";
            using var ownedWorkspace = WindowsSandboxWorkspace.Create(new SecurityIdentifier(sidValue));
            string workspace = ownedWorkspace.Path;

            using var sid = new WindowsLaunchSecurity.SecurityIdentifierHandle(sidValue);
            using var token = WindowsLaunchSecurity.CreateWriteRestrictedToken(sid);
            using var job = WindowsLaunchSecurity.CreateProcessTreeJob();
            string shell = Path.Combine(System.Environment.SystemDirectory, "cmd.exe");
            string allowed = Path.Combine(workspace, "allowed.txt");
            string denied = Path.Combine(root, "denied.txt");
            string existing = Path.Combine(root, "existing.txt");
            File.WriteAllText(existing, "original");
            string renamed = Path.Combine(workspace, "renamed.txt");
            string commandLine = $"\"{shell}\" /d /c \"echo %HPD_TEST_VALUE% > \"{allowed}\" & echo appended >> \"{allowed}\" & move /y \"{allowed}\" \"{renamed}\" & echo denied > \"{denied}\" & echo changed > \"{existing}\"\"";
            var environment = LaunchEnvironment();
            environment["HPD_TEST_VALUE"] = "ok";
            var child = WindowsLaunchSecurity.StartRestrictedProcess(token, job, shell, commandLine, workspace,
                environment);
            using var desktop = child.Desktop;
            using var process = child.Process;

            WaitForSingleObject(process, 10_000).Should().Be(0);
            GetExitCodeProcess(process, out uint exitCode).Should().BeTrue();
            exitCode.Should().BeLessThan(0x80000000);
            File.Exists(allowed).Should().BeFalse();
            File.ReadAllLines(renamed).Select(line => line.Trim()).Should().Equal("ok", "appended");
            File.Exists(denied).Should().BeFalse();
            File.ReadAllText(existing).Should().Be("original");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Network_fence_rejects_the_host_identity_before_changing_rules()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var identity = WindowsIdentity.GetCurrent();
        string machinePrefix = System.Environment.MachineName + "\\";
        if (!identity.Name.StartsWith(machinePrefix, StringComparison.OrdinalIgnoreCase))
            return;

        string account = identity.Name[machinePrefix.Length..];
        var error = Record.Exception(() =>
        {
            if (OperatingSystem.IsWindows())
                WindowsNetworkFence.Install(account, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        });
        error.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public async Task Closing_restricted_job_stops_descendants_after_the_parent_exits()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string sidValue = $"S-1-5-21-{Random.Shared.Next(1, int.MaxValue)}-1-2-1001";
        using var workspace = WindowsSandboxWorkspace.Create(new SecurityIdentifier(sidValue));
        using var sid = new WindowsLaunchSecurity.SecurityIdentifierHandle(sidValue);
        using var token = WindowsLaunchSecurity.CreateWriteRestrictedToken(sid);
        using var job = WindowsLaunchSecurity.CreateProcessTreeJob();
        string shell = Path.Combine(System.Environment.SystemDirectory, "cmd.exe");
        string ping = Path.Combine(System.Environment.SystemDirectory, "PING.EXE");
        string commandLine = $"\"{shell}\" /d /c start \"\" /b \"{ping}\" -t 127.0.0.1";
        var child = WindowsLaunchSecurity.StartRestrictedProcess(
            token, job, shell, commandLine, workspace.Path, LaunchEnvironment());
        using var desktop = child.Desktop;
        using var process = child.Process;

        WaitForSingleObject(process, 10_000).Should().Be(0);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        nuint[] ids = new nuint[8];
        while (true)
        {
            QueryInformationJobObject(job, 3, ids, ids.Length * IntPtr.Size, out _).Should().BeTrue();
            if (System.Runtime.InteropServices.MemoryMarshal.Cast<nuint, uint>(ids.AsSpan())[1] > 0)
                break;
            await Task.Delay(25, timeout.Token);
        }

        using var descendant = System.Diagnostics.Process.GetProcessById(checked((int)ids[8 / IntPtr.Size]));
        descendant.HasExited.Should().BeFalse();
        job.Dispose();
        await descendant.WaitForExitAsync(timeout.Token);
        descendant.HasExited.Should().BeTrue();
    }

    [Fact]
    public async Task Restricted_child_uses_redirected_standard_handles()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sid = new WindowsLaunchSecurity.SecurityIdentifierHandle("S-1-5-21-1-2-3-1001");
        using var token = WindowsLaunchSecurity.CreateWriteRestrictedToken(sid);
        string system = System.Environment.SystemDirectory;
        string shell = Path.Combine(system, "cmd.exe");
        using var child = WindowsRestrictedProcess.Start(
            token, shell, $"\"{shell}\" /d /v:on /c \"set /p line= & echo !line! & echo diagnostic 1>&2\"",
            system, LaunchEnvironment());

        await using var invocation = new HostProcessInvocationHandle(new ProcessInvocationSpec
        {
            Target = new TargetHandle<ExecutionUnit>(
                new TargetRoute { Kind = new TargetKind("test"), Scope = default, ProviderId = HostProcessProviderDescriptor.ProviderId },
                TargetHandleLifetime.LiveCapability, TargetHandleAuthority.Control, ProviderGeneration: 0),
            Command = new ProcessCommandSpec { FileName = shell },
            Io = new ProcessIoSpec
            {
                StandardInput = new ProcessInputSpec { Kind = ProcessInputKind.Stream },
            },
        }, sink: null);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await invocation.AttachRestrictedProcessAsync(child, timeout.Token);
        await invocation.WriteStdinAsync("hello from HPD\r\n"u8.ToArray(), timeout.Token);
        await invocation.CloseStdinAsync(timeout.Token);
        var result = await invocation.WaitAsync(timeout.Token);
        result.ExitCode.Should().Be(0);
        System.Text.Encoding.UTF8.GetString(result.Output.Stdout.CapturedBytes.Span).Trim().Should().Be("hello from HPD");
        System.Text.Encoding.UTF8.GetString(result.Output.Stderr.CapturedBytes.Span).Trim().Should().Be("diagnostic");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Native_probe_reaches_tcp_and_udp_listeners_under_the_restricted_launcher(string address)
    {
        if (!OperatingSystem.IsWindows())
            return;

        string? executable = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_SOCKET_PROBE");
        if (string.IsNullOrEmpty(executable))
            return;

        using var sid = new WindowsLaunchSecurity.SecurityIdentifierHandle("S-1-5-21-1-2-3-1001");
        using var token = WindowsLaunchSecurity.CreateWriteRestrictedToken(sid);
        await CheckNativeProbeAsync(executable, token, address, blocked: false);
    }

    [Fact]
    public async Task Native_probe_is_denied_after_installing_the_account_fence()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Opt in from an elevated test session with a disposable non-admin local account.
        string? account = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_FENCE_TEST_ACCOUNT");
        if (string.IsNullOrEmpty(account))
            return;

        string executable = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_SOCKET_PROBE")
            ?? throw new InvalidOperationException("Specify the compiled socket probe.");
        string password = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_FENCE_TEST_PASSWORD")
            ?? throw new InvalidOperationException("Specify the disposable account password.");
        if (!LogonUser(account, ".", password, 2, 0, out var source))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        using var sourceToken = source;
        using var identity = new WindowsIdentity(sourceToken.DangerousGetHandle());
        using var accountSid = new WindowsLaunchSecurity.SecurityIdentifierHandle(identity.User!.Value);
        using var workspaceSid = new WindowsLaunchSecurity.SecurityIdentifierHandle("S-1-5-21-1-2-3-1001");
        using var token = WindowsLaunchSecurity.CreateWriteRestrictedToken(sourceToken, workspaceSid, accountSid);
        await CheckNativeProbeAsync(executable, token, "127.0.0.1", blocked: false);
        await CheckNativeProbeAsync(executable, token, "::1", blocked: false);
        int? originalNullAccess = await CheckNullDeviceAsync(executable, token);
        originalNullAccess.Should().BeOneOf(0, 10);

        Guid sublayer = Guid.NewGuid(), ipv4 = Guid.NewGuid(), ipv6 = Guid.NewGuid();
        WindowsNetworkFence.Install(account, sublayer, ipv4, ipv6);
        try
        {
            using (new WindowsNullDeviceGrant(identity.User!))
            {
                (await CheckNullDeviceAsync(executable, token)).Should().Be(0);
                string? git = System.Environment.GetEnvironmentVariable("HPD_WINDOWS_FENCE_TEST_GIT");
                if (!string.IsNullOrWhiteSpace(git))
                    await CheckProvisionedGitAsync(git, token, identity.User!);
            }
            (await CheckNullDeviceAsync(executable, token)).Should().Be(originalNullAccess);
            await CheckNativeProbeAsync(executable, token, "127.0.0.1", blocked: true);
            await CheckNativeProbeAsync(executable, token, "::1", blocked: true);
        }
        finally
        {
            WindowsNetworkFence.Remove(sublayer, ipv4, ipv6);
        }

        await CheckNativeProbeAsync(executable, token, "127.0.0.1", blocked: false);
        await CheckNativeProbeAsync(executable, token, "::1", blocked: false);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task<int?> CheckNullDeviceAsync(string executable, SafeAccessTokenHandle token)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var child = WindowsRestrictedProcess.Start(token, executable,
            $"\"{executable}\" nul", System.Environment.SystemDirectory, LaunchEnvironment());
        await child.WaitForExitAsync(deadline.Token);
        return child.ReadExitCode();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task CheckProvisionedGitAsync(
        string git, SafeAccessTokenHandle token, SecurityIdentifier accountSid)
    {
        using var workspace = WindowsSandboxWorkspace.Create(accountSid);
        var environment = LaunchEnvironment();
        environment["HOME"] = workspace.Path;
        environment["USERPROFILE"] = workspace.Path;
        environment["TEMP"] = workspace.Path;
        environment["TMP"] = workspace.Path;
        environment["GIT_CONFIG_NOSYSTEM"] = "1";

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        (await RunGitAsync("--version")).Should().StartWith("git version");
        await RunGitAsync("init --quiet --template=");
        File.WriteAllText(Path.Combine(workspace.Path, "probe.txt"), "HPD sandbox test");
        (await RunGitAsync("status --porcelain")).Should().Contain("?? probe.txt");
        await RunGitAsync("add -- probe.txt");
        (await RunGitAsync("status --porcelain")).Should().Contain("A  probe.txt");

        async Task<string> RunGitAsync(string arguments)
        {
            using var child = WindowsRestrictedProcess.Start(token, git,
                $"\"{git}\" -c \"safe.directory={workspace.Path}\" {arguments}", workspace.Path, environment);
            using var stdout = new StreamReader(child.StandardOutput);
            using var stderr = new StreamReader(child.StandardError!);
            var output = stdout.ReadToEndAsync(deadline.Token);
            var errors = stderr.ReadToEndAsync(deadline.Token);
            await child.WaitForExitAsync(deadline.Token);
            child.ReadExitCode().Should().Be(0, "Git {0} reported {1}", arguments, await errors);
            return await output;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task CheckNativeProbeAsync(
        string executable, Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token, string address, bool blocked)
    {
        var ip = System.Net.IPAddress.Parse(address);
        using var tcp = new System.Net.Sockets.TcpListener(ip, 0);
        using var udp = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(ip, 0));
        tcp.Start();

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var (protocol, endpoint) in new[]
        {
            ("tcp", (System.Net.IPEndPoint)tcp.LocalEndpoint),
            ("udp", (System.Net.IPEndPoint)udp.Client.LocalEndPoint!),
        })
        {
            using var child = WindowsRestrictedProcess.Start(token, executable,
                FormattableString.Invariant($"\"{executable}\" {protocol} {address} {endpoint.Port}"),
                System.Environment.SystemDirectory, LaunchEnvironment());
            await child.WaitForExitAsync(deadline.Token);
            child.ReadExitCode().Should().Be(blocked ? 10 : 0, "{0} over {1} should match the fence state", protocol, address);
        }

        if (blocked)
        {
            tcp.Pending().Should().BeFalse();
            udp.Available.Should().Be(0);

            var tcpEndpoint = (System.Net.IPEndPoint)tcp.LocalEndpoint;
            using var hostTcp = new System.Net.Sockets.TcpClient(ip.AddressFamily);
            await hostTcp.ConnectAsync(ip, tcpEndpoint.Port, deadline.Token);
            using var hostConnection = await tcp.AcceptTcpClientAsync(deadline.Token);

            using var hostUdp = new System.Net.Sockets.UdpClient(ip.AddressFamily);
            var udpEndpoint = (System.Net.IPEndPoint)udp.Client.LocalEndPoint!;
            await hostUdp.SendAsync(new byte[] { 42 }.AsMemory(), udpEndpoint, deadline.Token);
            var hostPacket = await udp.ReceiveAsync(deadline.Token);
            hostPacket.Buffer.Should().Equal(new byte[] { 42 });
            return;
        }

        using var connection = await tcp.AcceptTcpClientAsync(deadline.Token);
        var packet = await udp.ReceiveAsync(deadline.Token);
        packet.Buffer.Should().Equal(new byte[] { (byte)'P' });
    }

    [DllImport("advapi32.dll", EntryPoint = "LogonUserW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(
        string user, string domain, string password, int logonType, int provider,
        out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token);

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Udp_probe_rejects_a_restricted_token_without_an_egress_fence(string address)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sid = new WindowsLaunchSecurity.SecurityIdentifierHandle("S-1-5-21-1-2-3-1001");
        using var token = WindowsLaunchSecurity.CreateWriteRestrictedToken(sid);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task verification = WindowsNetworkProbe.VerifyDatagramAsync(token, System.Net.IPAddress.Parse(address), deadline.Token);
        Func<Task> verify = () => verification;
        await verify.Should().ThrowAsync<InvalidOperationException>().WithMessage("*UDP send was not explicitly denied*");
    }

    [Fact]
    public async Task Network_probe_rejects_a_restricted_token_without_an_egress_fence()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var sid = new WindowsLaunchSecurity.SecurityIdentifierHandle("S-1-5-21-1-2-3-1001");
        using var token = WindowsLaunchSecurity.CreateWriteRestrictedToken(sid);

        Task verification = WindowsNetworkProbe.VerifyAsync(token, CancellationToken.None);
        Func<Task> verify = () => verification;
        await verify.Should().ThrowAsync<InvalidOperationException>().WithMessage("*connected to the*probe listener*");
    }


    private static Dictionary<string, string> LaunchEnvironment() => new()
    {
        ["SystemRoot"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows),
    };


    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool QueryInformationJobObject(
        SafeFileHandle job, int informationClass, [Out] nuint[] buffer, int length, out int returnedLength);
}
