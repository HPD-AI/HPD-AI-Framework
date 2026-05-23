namespace HPD.Execution.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Execution.AppleVirtualization.DevKit;
using HPD.Execution.Contracts;
using Xunit;

public sealed class AppleVirtualizationDevKitTests
{
    [Fact]
    public void Env_loader_parses_shell_export_file_and_unescapes_values()
    {
        using TemporaryPreparedImage image = TemporaryPreparedImage.Create("docker");
        string env = image.WriteEnv(
            engineKind: EngineControlPlaneKind.DockerCompatible,
            engineApi: EngineApiKind.DockerCompatible,
            authorityMode: EngineAuthorityMode.Rootful,
            socketPath: "/var/run/docker.sock",
            smokeImage: "docker.io/library/alpine:3.20",
            kernelCommandLine: "root=LABEL=cloudimg-rootfs\\ ro\\ rootwait\\ console=hvc0");

        AppleVirtualizationRealAcceptanceEnvironmentLoadResult result =
            AppleVirtualizationRealAcceptanceEnvironment.Load(env);

        result.Validation.IsValid.Should().BeTrue();
        result.Environment.Should().NotBeNull();
        result.Environment!.EngineKind.Should().Be(EngineControlPlaneKind.DockerCompatible);
        result.Environment.EngineApi.Should().Be(EngineApiKind.DockerCompatible);
        result.Environment.AuthorityMode.Should().Be(EngineAuthorityMode.Rootful);
        result.Environment.EngineSocketPath.Should().Be("/var/run/docker.sock");
        result.Environment.GuestKernelCommandLine.Should().Be("root=LABEL=cloudimg-rootfs ro rootwait console=hvc0");
    }

    [Theory]
    [InlineData("DockerCompatible", "DockerCompatible", "Rootless", "/run/user/1000/docker.sock", true)]
    [InlineData("DockerCompatible", "DockerCompatible", "Rootful", "/var/run/docker.sock", true)]
    [InlineData("Containerd", "ContainerdApi", "Rootful", "/run/containerd/containerd.sock", true)]
    [InlineData("Podman", "PodmanApi", "Rootless", "/run/user/1000/podman/podman.sock", true)]
    [InlineData("Podman", "PodmanApi", "Rootful", "/run/podman/podman.sock", true)]
    [InlineData("BuildKit", "BuildKitApi", "Rootless", "/run/user/1000/buildkit-default/buildkitd.sock", true)]
    [InlineData("BuildKit", "BuildKitApi", "Rootful", "/run/buildkit/buildkitd.sock", true)]
    [InlineData("Containerd", "DockerCompatible", "Rootful", "/run/containerd/containerd.sock", false)]
    [InlineData("BuildKit", "BuildKitApi", "Rootful", "/run/user/1000/buildkit-default/buildkitd.sock", false)]
    [InlineData("DockerCompatible", "DockerCompatible", "Rootful", "/run/user/1000/docker.sock", false)]
    public void Validator_enforces_engine_api_authority_and_socket_contract(
        string engineKind,
        string engineApi,
        string authorityMode,
        string socketPath,
        bool valid)
    {
        using TemporaryPreparedImage image = TemporaryPreparedImage.Create("engine");
        string env = image.WriteEnv(
            engineKind: Enum.Parse<EngineControlPlaneKind>(engineKind),
            engineApi: Enum.Parse<EngineApiKind>(engineApi),
            authorityMode: Enum.Parse<EngineAuthorityMode>(authorityMode),
            socketPath: socketPath);

        AppleVirtualizationRealAcceptanceEnvironment environment =
            AppleVirtualizationRealAcceptanceEnvironment.Load(env).Environment!;

        AppleVirtualizationDevKitValidationResult result =
            AppleVirtualizationRealAcceptanceValidator.Validate(environment);

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void Discovery_and_matrix_plan_find_prepared_envs_in_stable_order()
    {
        using TemporaryDirectory root = TemporaryDirectory.Create();
        using TemporaryPreparedImage buildKit = TemporaryPreparedImage.Create("buildkit", root.Path);
        using TemporaryPreparedImage containerd = TemporaryPreparedImage.Create("containerd", root.Path);
        buildKit.WriteEnv(
            engineKind: EngineControlPlaneKind.BuildKit,
            engineApi: EngineApiKind.BuildKitApi,
            authorityMode: EngineAuthorityMode.Rootful,
            socketPath: "/run/buildkit/buildkitd.sock",
            smokeImage: "hpd-buildkit-smoke:local");
        containerd.WriteEnv(
            engineKind: EngineControlPlaneKind.Containerd,
            engineApi: EngineApiKind.ContainerdApi,
            authorityMode: EngineAuthorityMode.Rootful,
            socketPath: "/run/containerd/containerd.sock",
            smokeImage: "docker.io/library/alpine:3.20");

        AppleVirtualizationRealAcceptanceMatrixPlan plan =
            AppleVirtualizationRealAcceptanceMatrix.CreatePlan(
                root.Path,
                new AppleVirtualizationPreparedImageDiscoveryOptions { ValidateFileSystem = true });

        plan.Diagnostics.Should().BeEmpty();
        plan.Entries.Should().HaveCount(2);
        plan.Entries.Select(static entry => entry.Name).Should().Equal("buildkit", "containerd");
        plan.Entries.Should().OnlyContain(static entry => entry.CanRun);
    }

    [Fact]
    public void Cleanup_plan_targets_serial_log_and_scratch_directory_without_deleting_prepared_image()
    {
        using TemporaryPreparedImage image = TemporaryPreparedImage.Create("cleanup");
        string env = image.WriteEnv();
        string scratch = Path.Combine(image.Root, ".hpd-real-acceptance-scratch");
        Directory.CreateDirectory(scratch);
        File.WriteAllText(image.SerialLogPath, "serial");

        AppleVirtualizationRealAcceptanceEnvironment environment =
            AppleVirtualizationRealAcceptanceEnvironment.Load(env).Environment!;

        AppleVirtualizationCleanupPlan plan = AppleVirtualizationCleanupPlanner.CreatePlan(environment);

        plan.Targets.Should().Contain(target =>
            target.Kind == AppleVirtualizationCleanupTargetKind.SerialLog &&
            target.Path == image.SerialLogPath &&
            target.Exists);
        plan.Targets.Should().Contain(target =>
            target.Kind == AppleVirtualizationCleanupTargetKind.ScratchDiskDirectory &&
            target.Path == scratch &&
            target.Exists);
        plan.Targets.Should().NotContain(target => target.Path == image.DiskPath);
    }

    [Fact]
    public void Host_prerequisites_report_current_platform_without_throwing()
    {
        AppleVirtualizationHostPrerequisiteReport report =
            AppleVirtualizationHostPrerequisites.InspectCurrentHost();

        if (OperatingSystem.IsMacOS())
        {
            report.CanRunAppleVirtualization.Should().BeTrue();
            report.Diagnostics.Should().BeEmpty();
        }
        else
        {
            report.CanRunAppleVirtualization.Should().BeFalse();
            report.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code == "AppleVirtualization.DevKit.HostPlatformUnsupported");
        }
    }

    [Fact]
    public void Image_preparation_builds_backend_command_for_selected_engine()
    {
        AppleVirtualizationDevKitPaths paths = AppleVirtualizationDevKitPaths.FromFrameworkRoot("/repo/framework", "/images");
        var preparation = new AppleVirtualizationImagePreparation(paths, new RecordingProcessRunner());

        AppleVirtualizationDevKitProcessCommand command = preparation.CreateCommand(new()
        {
            OutputRoot = "/images/docker",
            EngineKind = EngineControlPlaneKind.DockerCompatible,
            DiskSize = "8G",
            Force = true,
        });

        command.FileName.Should().Be("/repo/framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh");
        command.WorkingDirectory.Should().Be("/repo/framework");
        command.Arguments.Should().ContainInOrder("--output-root", "/images/docker");
        command.Arguments.Should().ContainInOrder("--disk-size", "8G");
        command.Arguments.Should().Contain("--install-docker");
        command.Arguments.Should().Contain("--force");
    }

    [Theory]
    [InlineData("Containerd", "--install-containerd")]
    [InlineData("Podman", "--install-podman")]
    [InlineData("BuildKit", "--install-buildkit")]
    public void Image_preparation_maps_engine_to_backend_install_flag(string engineKind, string installFlag)
    {
        AppleVirtualizationDevKitPaths paths = AppleVirtualizationDevKitPaths.FromFrameworkRoot("/repo/framework", "/images");
        var preparation = new AppleVirtualizationImagePreparation(paths, new RecordingProcessRunner());

        AppleVirtualizationDevKitProcessCommand command = preparation.CreateCommand(new()
        {
            OutputRoot = "/images/engine",
            EngineKind = Enum.Parse<EngineControlPlaneKind>(engineKind),
            NoRun = true,
        });

        command.Arguments.Should().Contain(installFlag);
        command.Arguments.Should().Contain("--no-run");
    }

    [Fact]
    public async Task Real_acceptance_executor_runs_prereqs_then_dotnet_with_env_variables()
    {
        using TemporaryPreparedImage image = TemporaryPreparedImage.Create("matrix");
        string env = image.WriteEnv(
            engineKind: EngineControlPlaneKind.DockerCompatible,
            engineApi: EngineApiKind.DockerCompatible,
            authorityMode: EngineAuthorityMode.Rootful,
            socketPath: "/var/run/docker.sock");
        File.WriteAllText(image.SerialLogPath, "old serial");
        AppleVirtualizationRealAcceptanceEnvironment environment =
            AppleVirtualizationRealAcceptanceEnvironment.Load(env).Environment!;
        var runner = new RecordingProcessRunner();
        runner.EnqueueResult(new AppleVirtualizationDevKitProcessResult { ExitCode = 0, StandardOutput = "prereq ok" });
        runner.EnqueueResult(new AppleVirtualizationDevKitProcessResult { ExitCode = 0, StandardOutput = "test ok" });
        var executor = new AppleVirtualizationRealAcceptanceExecutor(runner);

        AppleVirtualizationRealAcceptanceRunResult result = await executor.RunAsync(
            environment,
            new AppleVirtualizationRealAcceptanceRunOptions
            {
                TestProjectPath = "/repo/tests/applevz.csproj",
                PrerequisiteCheckScript = "/repo/check-real-acceptance-prereqs.sh",
            });

        result.Succeeded.Should().BeTrue();
        runner.Commands.Should().HaveCount(2);
        runner.Commands[0].FileName.Should().Be("/repo/check-real-acceptance-prereqs.sh");
        runner.Commands[0].Arguments.Should().Equal(env);
        runner.Commands[1].FileName.Should().Be("dotnet");
        runner.Commands[1].Arguments.Should().ContainInOrder("test", "/repo/tests/applevz.csproj", "-f", "net10.0");
        runner.Commands[1].Environment["HPD_APPLEVZ_CONTAINER_ENGINE_KIND"].Should().Be("DockerCompatible");
        File.Exists(image.SerialLogPath).Should().BeFalse();
    }

    [Fact]
    public async Task Real_acceptance_matrix_keep_going_runs_all_entries()
    {
        using TemporaryDirectory root = TemporaryDirectory.Create();
        using TemporaryPreparedImage docker = TemporaryPreparedImage.Create("docker", root.Path);
        using TemporaryPreparedImage buildkit = TemporaryPreparedImage.Create("buildkit", root.Path);
        docker.WriteEnv(
            engineKind: EngineControlPlaneKind.DockerCompatible,
            engineApi: EngineApiKind.DockerCompatible,
            authorityMode: EngineAuthorityMode.Rootful,
            socketPath: "/var/run/docker.sock");
        buildkit.WriteEnv(
            engineKind: EngineControlPlaneKind.BuildKit,
            engineApi: EngineApiKind.BuildKitApi,
            authorityMode: EngineAuthorityMode.Rootful,
            socketPath: "/run/buildkit/buildkitd.sock",
            smokeImage: "hpd-buildkit-smoke:local");
        AppleVirtualizationRealAcceptanceMatrixPlan plan =
            AppleVirtualizationRealAcceptanceMatrix.CreatePlan(
                root.Path,
                new AppleVirtualizationPreparedImageDiscoveryOptions { ValidateFileSystem = true });
        var runner = new RecordingProcessRunner();
        runner.EnqueueResult(new AppleVirtualizationDevKitProcessResult { ExitCode = 0 });
        runner.EnqueueResult(new AppleVirtualizationDevKitProcessResult { ExitCode = 1 });
        runner.EnqueueResult(new AppleVirtualizationDevKitProcessResult { ExitCode = 0 });
        runner.EnqueueResult(new AppleVirtualizationDevKitProcessResult { ExitCode = 0 });
        var executor = new AppleVirtualizationRealAcceptanceExecutor(runner);

        AppleVirtualizationRealAcceptanceMatrixRunResult result = await executor.RunMatrixAsync(
            plan,
            new AppleVirtualizationRealAcceptanceRunOptions
            {
                TestProjectPath = "/repo/tests/applevz.csproj",
                PrerequisiteCheckScript = "/repo/check-real-acceptance-prereqs.sh",
            },
            keepGoing: true);

        result.Runs.Should().HaveCount(2);
        result.Passed.Should().Be(1);
        result.Failed.Should().Be(1);
        runner.Commands.Should().HaveCount(4);
    }

    [Fact]
    public void Cleanup_executor_deletes_only_planned_transient_targets()
    {
        using TemporaryPreparedImage image = TemporaryPreparedImage.Create("cleanup-exec");
        string env = image.WriteEnv();
        string scratch = Path.Combine(image.Root, ".hpd-real-acceptance-scratch");
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(scratch, "scratch.raw"), "scratch");
        File.WriteAllText(image.SerialLogPath, "serial");
        AppleVirtualizationRealAcceptanceEnvironment environment =
            AppleVirtualizationRealAcceptanceEnvironment.Load(env).Environment!;

        AppleVirtualizationCleanupResult result =
            AppleVirtualizationCleanupExecutor.Execute(AppleVirtualizationCleanupPlanner.CreatePlan(environment));

        result.Succeeded.Should().BeTrue();
        File.Exists(image.SerialLogPath).Should().BeFalse();
        Directory.Exists(scratch).Should().BeFalse();
        File.Exists(image.DiskPath).Should().BeTrue();
    }

    private sealed class TemporaryPreparedImage : IDisposable
    {
        private readonly TemporaryDirectory? _ownedRoot;

        private TemporaryPreparedImage(string root, TemporaryDirectory? ownedRoot)
        {
            Root = root;
            _ownedRoot = ownedRoot;
            Directory.CreateDirectory(root);
            HelperPath = Path.Combine(root, OperatingSystem.IsWindows() ? "hpd-vz.exe" : "hpd-vz");
            KernelPath = Path.Combine(root, "vmlinux");
            InitrdPath = Path.Combine(root, "initrd.img");
            DiskPath = Path.Combine(root, "hpd-ubuntu.raw");
            SerialLogPath = Path.Combine(root, "apple-vz.serial.log");

            File.WriteAllText(HelperPath, string.Empty);
            File.WriteAllText(KernelPath, string.Empty);
            File.WriteAllText(InitrdPath, string.Empty);
            File.WriteAllText(DiskPath, string.Empty);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    HelperPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        public string Root { get; }
        public string HelperPath { get; }
        public string KernelPath { get; }
        public string InitrdPath { get; }
        public string DiskPath { get; }
        public string SerialLogPath { get; }

        public static TemporaryPreparedImage Create(string name, string? parent = null)
        {
            if (parent is null)
            {
                TemporaryDirectory ownedRoot = TemporaryDirectory.Create();
                return new TemporaryPreparedImage(Path.Combine(ownedRoot.Path, name), ownedRoot);
            }

            return new TemporaryPreparedImage(Path.Combine(parent, name), null);
        }

        public string WriteEnv(
            EngineControlPlaneKind engineKind = EngineControlPlaneKind.DockerCompatible,
            EngineApiKind engineApi = EngineApiKind.DockerCompatible,
            EngineAuthorityMode authorityMode = EngineAuthorityMode.Rootless,
            string socketPath = "/run/user/1000/docker.sock",
            string smokeImage = "alpine:3.20",
            string? kernelCommandLine = null)
        {
            string path = Path.Combine(Root, "hpd-applevz-real.env");
            using StreamWriter writer = new(path);
            writer.WriteLine("export HPD_APPLEVZ_REAL_CONTAINER_SMOKE=1");
            writer.WriteLine($"export HPD_APPLEVZ_REAL_HELPER_PATH={Escape(HelperPath)}");
            writer.WriteLine($"export HPD_APPLEVZ_GUEST_KERNEL={Escape(KernelPath)}");
            writer.WriteLine($"export HPD_APPLEVZ_GUEST_INITRD={Escape(InitrdPath)}");
            writer.WriteLine($"export HPD_APPLEVZ_GUEST_DISK={Escape(DiskPath)}");
            writer.WriteLine($"export HPD_APPLEVZ_GUEST_SERIAL_LOG={Escape(SerialLogPath)}");
            writer.WriteLine("export HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION=0.1.0");
            writer.WriteLine($"export HPD_APPLEVZ_CONTAINER_ENGINE_KIND={engineKind}");
            writer.WriteLine($"export HPD_APPLEVZ_CONTAINER_ENGINE_API={engineApi}");
            writer.WriteLine($"export HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE={authorityMode}");
            writer.WriteLine("export HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS=runtime-host");
            writer.WriteLine($"export HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH={socketPath}");
            writer.WriteLine($"export HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE={smokeImage}");
            writer.WriteLine("export HPD_APPLEVZ_ENGINE_PROVISIONING_ENABLED=false");
            writer.WriteLine("export HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_PACKAGE_INSTALL=false");
            writer.WriteLine("export HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_SERVICE_ENABLEMENT=false");
            if (kernelCommandLine is not null)
            {
                writer.WriteLine($"export HPD_APPLEVZ_GUEST_KERNEL_CMDLINE={kernelCommandLine}");
            }

            return path;
        }

        public void Dispose() => _ownedRoot?.Dispose();

        private static string Escape(string value) => value.Replace(" ", "\\ ", StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hpd-applevz-devkit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class RecordingProcessRunner : IAppleVirtualizationDevKitProcessRunner
    {
        private readonly Queue<AppleVirtualizationDevKitProcessResult> _results = [];

        public List<AppleVirtualizationDevKitProcessCommand> Commands { get; } = [];

        public void EnqueueResult(AppleVirtualizationDevKitProcessResult result) => _results.Enqueue(result);

        public ValueTask<AppleVirtualizationDevKitProcessResult> RunAsync(
            AppleVirtualizationDevKitProcessCommand command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return ValueTask.FromResult(_results.Count == 0
                ? new AppleVirtualizationDevKitProcessResult { ExitCode = 0 }
                : _results.Dequeue());
        }
    }
}
