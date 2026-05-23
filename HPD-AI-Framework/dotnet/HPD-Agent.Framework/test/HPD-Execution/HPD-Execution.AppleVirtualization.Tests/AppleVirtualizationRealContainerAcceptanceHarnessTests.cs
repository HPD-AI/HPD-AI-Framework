namespace HPD.Execution.AppleVirtualization.Tests;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Execution.AppleVirtualization.Authority;
using HPD.Execution.AppleVirtualization.Engines;
using HPD.Execution.AppleVirtualization.Networks;
using HPD.Execution.AppleVirtualization.Processes;
using HPD.Execution.AppleVirtualization.Protocol;
using HPD.Execution.AppleVirtualization.State;
using HPD.Execution.AppleVirtualization.Tests.Fixtures;
using HPD.Execution.Contracts;
using Xunit;

public sealed class AppleVirtualizationRealContainerAcceptanceHarnessTests
{
    private static readonly TimeSpan RealBootTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RealGuestReadyTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RealEngineStatusTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RealCleanupTimeout = TimeSpan.FromSeconds(20);
    private const string EngineId = "engine-real-container-smoke";
    private const string AuthorityBindingId = "authority-real-container-engine";
    private const string UnitIdPrefix = "unit-";
    private const string SmokeProcessPrefix = "process-container-smoke-";
    private const string ProjectedDockerEngineSocketPath = "/run/hpd/engine/docker.sock";
    private const string ProjectedContainerdEngineSocketPath = "/run/hpd/engine/containerd.sock";
    private const string ProjectedRootlessPodmanEngineSocketPath = "/run/hpd/engine/podman.sock";
    private const string ProjectedRootfulPodmanEngineSocketPath = "/run/hpd/engine/podman-rootful.sock";
    private const string ProjectedRootlessBuildKitEngineSocketPath = "/run/hpd/engine/buildkitd.sock";
    private const string ProjectedRootfulBuildKitEngineSocketPath = "/run/hpd/engine/buildkitd-rootful.sock";
    private const string GuestImageContractPath = "docs/apple-virtualization/guest-image-contract.md";
    private const int RealOutputTailBytes = 64 * 1024;

    [Fact]
    public void Real_container_acceptance_env_parser_is_disabled_by_default_and_lists_prerequisites()
    {
        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(_ => null, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_REAL_CONTAINER_SMOKE=1");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_REAL_HELPER_PATH");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_GUEST_KERNEL");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_GUEST_INITRD");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_GUEST_DISK");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_CONTAINER_ENGINE_KIND");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_CONTAINER_ENGINE_API");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE");
        environment.SkipReason.Should().Contain(GuestImageContractPath);
    }

    [Fact]
    public void Real_container_acceptance_env_parser_validates_complete_explicit_configuration()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create();

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeTrue(environment.SkipReason);
        environment.HelperPath.Should().Be(files.HelperPath);
        environment.GuestImage.KernelPath.Should().Be(files.KernelPath);
        environment.GuestImage.InitrdPath.Should().Be(files.InitrdPath);
        environment.GuestImage.DiskImagePath.Should().Be(files.DiskPath);
        environment.GuestImage.ExpectedGuestAgentVersion.Should().Be("0.1.0");
        environment.EngineKind.Should().Be(EngineControlPlaneKind.DockerCompatible);
        environment.EngineApi.Should().Be(EngineApiKind.DockerCompatible);
        environment.AuthorityMode.Should().Be(EngineAuthorityMode.Rootless);
        environment.EngineSocketPath.Should().Be("/run/user/1000/docker.sock");
        environment.ContainerImage.Should().Be("hello-world:latest");
        environment.SocketLocus.Should().Be(BoundaryLocus.RuntimeHost);
    }

    [Fact]
    public void Real_container_acceptance_env_parser_accepts_complete_containerd_configuration()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            engineKind: EngineControlPlaneKind.Containerd.ToString(),
            engineApi: EngineApiKind.ContainerdApi.ToString(),
            authorityMode: EngineAuthorityMode.Rootful.ToString(),
            socketPath: "/run/containerd/containerd.sock");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeTrue(environment.SkipReason);
        environment.EngineKind.Should().Be(EngineControlPlaneKind.Containerd);
        environment.EngineApi.Should().Be(EngineApiKind.ContainerdApi);
        environment.AuthorityMode.Should().Be(EngineAuthorityMode.Rootful);
        environment.EngineSocketPath.Should().Be("/run/containerd/containerd.sock");
        environment.ProjectedEngineSocketPath.Should().Be(ProjectedContainerdEngineSocketPath);
    }

    [Fact]
    public void Real_container_acceptance_env_parser_accepts_complete_rootful_podman_configuration()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            engineKind: EngineControlPlaneKind.Podman.ToString(),
            engineApi: EngineApiKind.PodmanApi.ToString(),
            authorityMode: EngineAuthorityMode.Rootful.ToString(),
            socketPath: "/run/podman/podman.sock",
            image: "docker.io/library/alpine:3.20");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeTrue(environment.SkipReason);
        environment.EngineKind.Should().Be(EngineControlPlaneKind.Podman);
        environment.EngineApi.Should().Be(EngineApiKind.PodmanApi);
        environment.AuthorityMode.Should().Be(EngineAuthorityMode.Rootful);
        environment.EngineSocketPath.Should().Be("/run/podman/podman.sock");
        environment.ProjectedEngineSocketPath.Should().Be(ProjectedRootfulPodmanEngineSocketPath);
    }

    [Fact]
    public void Real_container_acceptance_env_parser_accepts_complete_rootful_buildkit_configuration()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            engineKind: EngineControlPlaneKind.BuildKit.ToString(),
            engineApi: EngineApiKind.BuildKitApi.ToString(),
            authorityMode: EngineAuthorityMode.Rootful.ToString(),
            socketPath: "/run/buildkit/buildkitd.sock",
            image: "hpd-buildkit-smoke:local");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeTrue(environment.SkipReason);
        environment.EngineKind.Should().Be(EngineControlPlaneKind.BuildKit);
        environment.EngineApi.Should().Be(EngineApiKind.BuildKitApi);
        environment.AuthorityMode.Should().Be(EngineAuthorityMode.Rootful);
        environment.EngineSocketPath.Should().Be("/run/buildkit/buildkitd.sock");
        environment.ProjectedEngineSocketPath.Should().Be(ProjectedRootfulBuildKitEngineSocketPath);
    }

    [Fact]
    public void Real_container_acceptance_env_parser_reports_missing_files_as_skip_diagnostics()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(createHelper: false, createKernel: false, createInitrd: false, createDisk: false);

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerHelperMissing");
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerGuestKernelMissing");
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerGuestInitrdMissing");
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerGuestDiskMissing");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_REAL_HELPER_PATH");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_GUEST_KERNEL");
    }

    [Fact]
    public void Real_container_acceptance_env_parser_reports_partial_env_missing_inputs_separately()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create();
        files.RemoveEnvironment(
            "HPD_APPLEVZ_GUEST_INITRD",
            "HPD_APPLEVZ_GUEST_DISK",
            "HPD_APPLEVZ_CONTAINER_ENGINE_API",
            "HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Readiness.MissingVariables.Should().Contain("HPD_APPLEVZ_GUEST_INITRD");
        environment.Readiness.MissingVariables.Should().Contain("HPD_APPLEVZ_GUEST_DISK");
        environment.Readiness.MissingVariables.Should().Contain("HPD_APPLEVZ_CONTAINER_ENGINE_API");
        environment.Readiness.MissingVariables.Should().Contain("HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE");
        environment.Diagnostics.Count(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerEnvMissing").Should().Be(4);
    }

    [Fact]
    public void Real_container_acceptance_env_parser_validates_nonexistent_file_paths_separately()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create();
        files.SetEnvironment("HPD_APPLEVZ_REAL_HELPER_PATH", Path.Combine(files.Root, "missing-helper"));
        files.SetEnvironment("HPD_APPLEVZ_GUEST_KERNEL", Path.Combine(files.Root, "missing-vmlinuz"));
        files.SetEnvironment("HPD_APPLEVZ_GUEST_INITRD", Path.Combine(files.Root, "missing-initrd"));
        files.SetEnvironment("HPD_APPLEVZ_GUEST_DISK", Path.Combine(files.Root, "missing-disk"));
        files.SetEnvironment("HPD_APPLEVZ_GUEST_BUNDLE_ROOT", Path.Combine(files.Root, "missing-bundle"));
        files.SetEnvironment("HPD_APPLEVZ_VIRTIOFS_HOST_PATH", Path.Combine(files.Root, "missing-share"));
        files.SetEnvironment("HPD_APPLEVZ_VIRTIOFS_TAG", "hpd.share");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerHelperMissing");
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerGuestKernelMissing");
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerGuestInitrdMissing");
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerGuestDiskMissing");
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerGuestBundleRootMissing");
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerVirtiofsHostPathMissing");
        environment.Readiness.InvalidInputs.Select(input => input.Variable).Should().Contain(
            "HPD_APPLEVZ_REAL_HELPER_PATH",
            "HPD_APPLEVZ_GUEST_KERNEL",
            "HPD_APPLEVZ_GUEST_INITRD",
            "HPD_APPLEVZ_GUEST_DISK",
            "HPD_APPLEVZ_GUEST_BUNDLE_ROOT",
            "HPD_APPLEVZ_VIRTIOFS_HOST_PATH");
    }

    [Fact]
    public void Real_container_acceptance_env_parser_rejects_non_executable_helper()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(makeHelperExecutable: false);

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerHelperNotExecutable");
    }

    [Fact]
    public void Real_container_acceptance_env_parser_reports_invalid_engine_mode_fields_separately()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            engineKind: "MadeUpEngine",
            engineApi: "HostDocker",
            authorityMode: EngineAuthorityMode.Mixed.ToString());

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerEngineKindInvalid");
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerEngineApiInvalid");
        environment.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerEngineAuthorityModeUnsupported");
        environment.Readiness.InvalidInputs.Select(input => input.Variable).Should().Contain(
            "HPD_APPLEVZ_CONTAINER_ENGINE_KIND",
            "HPD_APPLEVZ_CONTAINER_ENGINE_API",
            "HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE");
    }

    [Fact]
    public void Real_container_acceptance_env_parser_reports_invalid_provisioning_gate_variables()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create();
        files.SetEnvironment("HPD_APPLEVZ_ENGINE_PROVISIONING_ENABLED", "yes");
        files.SetEnvironment("HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_PACKAGE_INSTALL", "1");
        files.SetEnvironment("HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_SERVICE_ENABLEMENT", "maybe");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Count(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerProvisioningGateInvalid")
            .Should().Be(3);
        environment.Readiness.InvalidInputs.Select(input => input.Variable).Should().Contain(
            "HPD_APPLEVZ_ENGINE_PROVISIONING_ENABLED",
            "HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_PACKAGE_INSTALL",
            "HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_SERVICE_ENABLEMENT");
    }

    [Fact]
    public void Host_container_runtime_environment_variables_cannot_satisfy_real_container_harness()
    {
        var hostRuntimeEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HPD_APPLEVZ_REAL_CONTAINER_SMOKE"] = "1",
            ["DOCKER_HOST"] = "unix:///var/run/docker.sock",
            ["CONTAINER_HOST"] = "unix:///run/user/501/podman/podman.sock",
            ["PODMAN_HOST"] = "unix:///run/user/501/podman/podman.sock",
            ["CONTAINERD_ADDRESS"] = "/run/containerd/containerd.sock",
            ["BUILDKIT_HOST"] = "unix:///run/buildkit/buildkitd.sock",
        };

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(
                name => hostRuntimeEnvironment.TryGetValue(name, out string? value) ? value : null,
                hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE");
        environment.SkipReason.Should().NotContain("/var/run/docker.sock");
        environment.SkipReason.Should().NotContain("podman.sock");
        environment.SkipReason.Should().NotContain("containerd.sock");
        environment.SkipReason.Should().NotContain("buildkitd.sock");
    }

    [Fact]
    public void Host_locus_engine_socket_is_rejected_before_real_container_harness_can_run()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(socketLocus: "host");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerHostEngineSocketPassthroughRejected");
        environment.SkipReason.Should().Contain("host Docker, Podman, containerd, or BuildKit socket cannot satisfy the Apple Virtualization real container harness");
    }

    [Fact]
    public void Execution_unit_locus_engine_socket_is_rejected_before_real_container_harness_can_run()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(socketLocus: "execution-unit");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerEngineSocketLocusUnsupported");
        environment.SkipReason.Should().Contain(GuestImageContractPath);
    }

    [Fact]
    public void Invalid_socket_path_locus_and_host_shaped_socket_path_are_rejected()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            socketLocus: "sidecar",
            socketPath: "unix:///var/run/docker.sock");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerEngineSocketLocusInvalid");
        environment.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerEngineSocketPathInvalid");
    }

    [Fact]
    public void Rootless_docker_smoke_requires_guest_runtime_user_socket_path()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            authorityMode: EngineAuthorityMode.Rootless.ToString(),
            socketPath: "/var/run/docker.sock");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerEngineSocketPathModeMismatch");
        environment.SkipReason.Should().Contain("/run/user/1000/docker.sock");
    }

    [Fact]
    public void Rootful_docker_smoke_requires_guest_system_socket_path()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            authorityMode: EngineAuthorityMode.Rootful.ToString(),
            socketPath: "/run/user/1000/docker.sock");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerEngineSocketPathModeMismatch");
        environment.SkipReason.Should().Contain("/var/run/docker.sock");
    }

    [Fact]
    public void Containerd_smoke_requires_guest_containerd_socket_and_api()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            engineKind: EngineControlPlaneKind.Containerd.ToString(),
            engineApi: EngineApiKind.DockerCompatible.ToString(),
            authorityMode: EngineAuthorityMode.Rootful.ToString(),
            socketPath: "/var/run/docker.sock");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerEngineKindApiMismatch");
        environment.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerEngineSocketPathModeMismatch");
        environment.SkipReason.Should().Contain("/run/containerd/containerd.sock");
    }

    [Fact]
    public void Podman_smoke_requires_guest_podman_socket_and_api()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            engineKind: EngineControlPlaneKind.Podman.ToString(),
            engineApi: EngineApiKind.DockerCompatible.ToString(),
            authorityMode: EngineAuthorityMode.Rootful.ToString(),
            socketPath: "/var/run/docker.sock");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerEngineKindApiMismatch");
        environment.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerEngineSocketPathModeMismatch");
        environment.SkipReason.Should().Contain("/run/podman/podman.sock");
    }

    [Fact]
    public void BuildKit_smoke_requires_guest_buildkit_socket_and_api()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            engineKind: EngineControlPlaneKind.BuildKit.ToString(),
            engineApi: EngineApiKind.DockerCompatible.ToString(),
            authorityMode: EngineAuthorityMode.Rootful.ToString(),
            socketPath: "/var/run/docker.sock");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerEngineKindApiMismatch");
        environment.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerEngineSocketPathModeMismatch");
        environment.SkipReason.Should().Contain("/run/buildkit/buildkitd.sock");
    }

    [Fact]
    public void Smoke_image_reference_is_required_by_guest_image_contract()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(image: "   ");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerEnvMissing" &&
            diagnostic.Variable == "HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE");
        environment.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerSmokeImageInvalid");
        environment.SkipReason.Should().Contain(GuestImageContractPath);
    }

    [Fact]
    public void Smoke_image_reference_rejects_host_socket_shaped_values()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(image: "unix:///var/run/docker.sock");

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerSmokeImageInvalid");
    }

    [Fact]
    public void Serial_log_target_is_validated_as_writable_without_starting_vm()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create();
        string serialDirectory = Path.Combine(files.Root, "serial-as-directory");
        Directory.CreateDirectory(serialDirectory);
        files.SetEnvironment("HPD_APPLEVZ_GUEST_SERIAL_LOG", serialDirectory);

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeFalse();
        environment.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == "AppleVirtualization.RealContainerSerialLogInvalid");
    }

    [Fact]
    public void Complete_synthetic_env_reports_ready_readiness_summary()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create();

        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealContainerSmoke.Should().BeTrue(environment.SkipReason);
        environment.Readiness.Ready.Should().BeTrue();
        environment.Readiness.MissingVariables.Should().BeEmpty();
        environment.Readiness.InvalidInputs.Should().BeEmpty();
        environment.Readiness.ValidatedPaths.Should().Contain("HPD_APPLEVZ_REAL_HELPER_PATH");
        environment.Readiness.ValidatedPaths.Should().Contain("HPD_APPLEVZ_GUEST_SERIAL_LOG");
        environment.Provisioning.Enabled.Should().BeFalse();
        environment.Provisioning.AllowPackageInstall.Should().BeFalse();
        environment.Provisioning.AllowServiceEnablement.Should().BeFalse();
    }

    [Fact]
    public void Real_container_harness_builds_vm_readiness_engine_and_smoke_request_shape()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create();
        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        string hostId = "hpd-real-container-" + Guid.NewGuid().ToString("N");
        AppleVirtualizationVmConfigurationValidationRequest vm = environment.CreateVmConfiguration(hostId);
        AppleVirtualizationHelperEnvelope readiness = RealGuestReadinessRequest(hostId, environment, 30);
        AppleVirtualizationHelperEnvelope engine = RealEngineStatusRequest(hostId, environment, 40);
        AppleVirtualizationContainerSmokeWorkflowRequest smoke = environment.CreateSmokeWorkflowRequest(hostId);

        vm.HostId.Should().Be(hostId);
        vm.GuestImage.DiskImagePath.Should().Be(files.DiskPath);
        vm.IncludeVirtioSocketPlaceholder.Should().BeTrue();
        readiness.GuestAgentReadinessProbeRequest!.ExplicitRealMode.Should().BeTrue();
        readiness.GuestAgentReadinessProbeRequest.RequiredCapabilities.Should().Contain(
            ["engine.status", "authority.bind", "process.start", "process.readOutput"]);
        engine.EngineStatusRequest!.ExplicitRealMode.Should().BeTrue();
        engine.EngineStatusRequest.ScriptedObservationState.Should().BeNull();
        engine.EngineStatusRequest.Kind.Should().Be(environment.EngineKind);
        engine.EngineStatusRequest.Api.Should().Be(environment.EngineApi);
        engine.EngineStatusRequest.AuthorityMode.Should().Be(environment.AuthorityMode);
        smoke.EngineSpec.Host!.Value.Id.Value.Should().Be(hostId);
        smoke.EngineSpec.EndpointPolicy!.Kind.Should().Be(SensitiveEndpointKind.EngineSocket);
        smoke.EngineSpec.EndpointPolicy.AuthorityClass.Should().Be(SensitiveAuthorityClass.RootlessEngineControl);
        smoke.Command.FileName.Should().Be("/hpd/container-smoke");
        smoke.Command.Arguments.Should().Contain(environment.ContainerImage);
        smoke.Command.Arguments.Should().Contain(ProjectedDockerEngineSocketPath);
        smoke.Isolation.AuthorityBindings.Should().BeEmpty("the workflow attaches the accepted engine authority at dispatch time");
        smoke.EngineAuthorityBinding.Id.Value.Should().Be(AuthorityBindingId);
    }

    [Fact]
    public void Real_container_harness_builds_containerd_smoke_request_with_containerd_projection()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            engineKind: EngineControlPlaneKind.Containerd.ToString(),
            engineApi: EngineApiKind.ContainerdApi.ToString(),
            authorityMode: EngineAuthorityMode.Rootful.ToString(),
            socketPath: "/run/containerd/containerd.sock");
        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        AppleVirtualizationContainerSmokeWorkflowRequest smoke = environment.CreateSmokeWorkflowRequest("runtime-host-real");

        smoke.EngineSpec.Kind.Should().Be(EngineControlPlaneKind.Containerd);
        smoke.EngineSpec.Api.Should().Be(EngineApiKind.ContainerdApi);
        smoke.EngineSpec.EndpointPolicy!.AuthorityClass.Should().Be(SensitiveAuthorityClass.RootfulEngineControl);
        smoke.Api.Should().Be(EngineApiKind.ContainerdApi);
        smoke.Command.Arguments.Should().Contain(ProjectedContainerdEngineSocketPath);
        smoke.Command.Arguments.Should().NotContain(ProjectedDockerEngineSocketPath);
    }

    [Fact]
    public void Real_container_harness_builds_podman_smoke_request_with_rootful_podman_projection()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            engineKind: EngineControlPlaneKind.Podman.ToString(),
            engineApi: EngineApiKind.PodmanApi.ToString(),
            authorityMode: EngineAuthorityMode.Rootful.ToString(),
            socketPath: "/run/podman/podman.sock");
        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        AppleVirtualizationContainerSmokeWorkflowRequest smoke = environment.CreateSmokeWorkflowRequest("runtime-host-real");

        smoke.EngineSpec.Kind.Should().Be(EngineControlPlaneKind.Podman);
        smoke.EngineSpec.Api.Should().Be(EngineApiKind.PodmanApi);
        smoke.EngineSpec.EndpointPolicy!.AuthorityClass.Should().Be(SensitiveAuthorityClass.RootfulEngineControl);
        smoke.Api.Should().Be(EngineApiKind.PodmanApi);
        smoke.Command.Arguments.Should().Contain(ProjectedRootfulPodmanEngineSocketPath);
        smoke.Command.Arguments.Should().NotContain(ProjectedDockerEngineSocketPath);
        smoke.Command.Arguments.Should().NotContain(ProjectedContainerdEngineSocketPath);
    }

    [Fact]
    public void Real_container_harness_builds_buildkit_smoke_request_with_rootful_buildkit_projection()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create(
            engineKind: EngineControlPlaneKind.BuildKit.ToString(),
            engineApi: EngineApiKind.BuildKitApi.ToString(),
            authorityMode: EngineAuthorityMode.Rootful.ToString(),
            socketPath: "/run/buildkit/buildkitd.sock",
            image: "hpd-buildkit-smoke:local");
        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        AppleVirtualizationContainerSmokeWorkflowRequest smoke = environment.CreateSmokeWorkflowRequest("runtime-host-real");

        smoke.EngineSpec.Kind.Should().Be(EngineControlPlaneKind.BuildKit);
        smoke.EngineSpec.Api.Should().Be(EngineApiKind.BuildKitApi);
        smoke.EngineSpec.EndpointPolicy!.AuthorityClass.Should().Be(SensitiveAuthorityClass.RootfulEngineControl);
        smoke.Api.Should().Be(EngineApiKind.BuildKitApi);
        smoke.Command.Arguments.Should().Contain(ProjectedRootfulBuildKitEngineSocketPath);
        smoke.Command.Arguments.Should().NotContain(ProjectedDockerEngineSocketPath);
        smoke.Command.Arguments.Should().NotContain(ProjectedContainerdEngineSocketPath);
    }

    [Fact]
    public void Real_container_harness_does_not_publish_engine_sockets_as_endpoints()
    {
        using RealContainerAcceptanceFiles files = RealContainerAcceptanceFiles.Create();
        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);
        AppleVirtualizationContainerSmokeWorkflowRequest smoke = environment.CreateSmokeWorkflowRequest("runtime-host-real");

        smoke.EngineSpec.EndpointPolicy!.Kind.Should().Be(SensitiveEndpointKind.EngineSocket);
        smoke.EngineSpec.EndpointPolicy.RequireAudit.Should().BeTrue();
        smoke.ProviderExtensions.Should().BeEmpty();
        smoke.TargetUnit.Route.Segments.Should().Contain(segment => segment.Kind == TargetRouteSegmentKind.ExecutionUnit);
        smoke.TargetUnit.Route.Segments.Should().NotContain(segment => segment.Kind == TargetRouteSegmentKind.Endpoint);
    }

    [Fact]
    public void Real_container_harness_evidence_capture_bounds_serial_events_cleanup_revocation_and_output()
    {
        var evidence = new RealContainerAcceptanceRunEvidence(maxHelperEvents: 2, maxSerialTailBytes: 8);
        evidence.AddHelperEvent(HelperEvent(AppleVirtualizationHelperEventKind.HelperStarted, 1));
        evidence.AddHelperEvent(HelperEvent(AppleVirtualizationHelperEventKind.EngineObserved, 2));
        evidence.AddHelperEvent(HelperEvent(AppleVirtualizationHelperEventKind.AuthorityRevoked, 3));
        evidence.AddCleanup("HostStop", null);
        evidence.CaptureRevocation(new AuthorityBindingStatus
        {
            BindingPhase = AuthorityBindingPhase.Revoking,
            BoundAuthority = new BoundAuthority
            {
                SourceKind = AuthoritySourceKind.UnixSocket,
                ProjectionKind = AuthorityProjectionKind.SocketPath,
                Direction = AuthorityBindingDirection.ProviderToGuest,
                EffectiveAuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                RevocationStatus = RevocationVerificationStatus.NotSupported,
            },
        });
        evidence.CaptureSmokeResult(new ProcessInvocationResult
        {
            CompletionKind = ProcessCompletionKind.Exited,
            ExitCode = 0,
            Output = new ProcessCapturedOutput
            {
                Stdout = new ProcessStreamOutput
                {
                    BytesObserved = 10,
                    BytesCaptured = 4,
                    BytesDiscarded = 6,
                    Truncated = true,
                },
                Stderr = new ProcessStreamOutput(),
            },
        });
        evidence.CaptureSerialTail([1, 2, 3, 4, 5, 6, 7, 8, 9]);

        evidence.HelperEvents.Should().HaveCount(2);
        evidence.HelperEvents.Select(helperEvent => helperEvent.EventKind).Should().Equal(
            AppleVirtualizationHelperEventKind.EngineObserved,
            AppleVirtualizationHelperEventKind.AuthorityRevoked);
        evidence.HelperEventsTruncated.Should().BeTrue();
        evidence.CleanupResults.Should().ContainSingle().Which.Succeeded.Should().BeFalse();
        evidence.RevocationStatus.Should().Be(RevocationVerificationStatus.NotSupported);
        evidence.OutputSummary.Should().NotBeNull();
        evidence.OutputSummary!.StdoutCapturedBytes.Should().Be(4);
        evidence.OutputSummary.StdoutTruncated.Should().BeTrue();
        evidence.SerialTailBytes.Should().Equal([2, 3, 4, 5, 6, 7, 8, 9]);
        evidence.SerialTailTruncated.Should().BeTrue();
    }

    private static AppleVirtualizationHelperEnvelope HelperEvent(
        AppleVirtualizationHelperEventKind eventKind,
        long sequenceNumber) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Event,
            Operation = AppleVirtualizationHelperOperation.HealthProbe,
            EventKind = eventKind,
            SequenceNumber = sequenceNumber,
        };

    [SkippableFact]
    public async Task Real_container_smoke_acceptance_observes_real_engine_status_only_with_explicit_env()
    {
        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(
                Environment.GetEnvironmentVariable,
                hostSupported: RuntimeInformation.IsOSPlatform(OSPlatform.OSX));
        Skip.IfNot(environment.CanAttemptRealContainerSmoke, environment.SkipReason);

        string hostId = "hpd-real-container-" + Guid.NewGuid().ToString("N");
        await using var helper = await RealHelperProcess.StartAsync(environment.HelperPath);
        using RealContainerScratchDisk scratchDisk = RealContainerScratchDisk.Create(environment.GuestImage.DiskImagePath!, hostId);
        var evidence = new RealContainerAcceptanceRunEvidence(maxHelperEvents: 128, maxSerialTailBytes: RealOutputTailBytes);
        try
        {
            AppleVirtualizationHelperEnvelope hello = await helper.SendAsync(
                AppleVirtualizationHelperEnvelope.Request(AppleVirtualizationHelperOperation.Hello, "real-container-hello", 1),
                RealCleanupTimeout).ConfigureAwait(false);
            AppleVirtualizationPreflightFact? hostSupportedFact = hello.HelloResponse?.PreflightFacts.FirstOrDefault(fact =>
                string.Equals(fact.Name, "vzvirtualmachine-supported", StringComparison.Ordinal));
            Skip.If(
                hostSupportedFact?.State == AppleVirtualizationPreflightFactState.Unsupported,
                hostSupportedFact.Message ?? "VZVirtualMachine.isSupported is false on this host.");

            AppleVirtualizationHelperEnvelope start = await helper.SendAsync(
                RealHostLifecycleRequest(
                    AppleVirtualizationHelperOperation.HostStart,
                    hostId,
                    sequenceNumber: 2,
                    environment.CreateVmConfiguration(hostId, scratchDisk.Path)),
                RealCleanupTimeout).ConfigureAwait(false);
            start.Error.Should().BeNull("explicit real container acceptance should fail only after opt-in prerequisite validation");

            AppleVirtualizationHelperEnvelope running = await PollForHostPhaseAsync(helper, hostId, RuntimeHostPhase.Running, RealBootTimeout)
                .ConfigureAwait(false);
            running.HostStatusResponse!.HostPhase.Should().Be(RuntimeHostPhase.Running);

            AppleVirtualizationHelperEnvelope readiness = await helper.SendAsync(
                RealGuestReadinessRequest(hostId, environment, sequenceNumber: 30),
                RealGuestReadyTimeout).ConfigureAwait(false);
            readiness.Error.Should().BeNull("real container acceptance requires a compatible HPD guest agent");
            readiness.GuestAgentReadinessProbeResponse!.VerifiedReady.Should().BeTrue();

            AppleVirtualizationHelperEnvelope engine = await helper.SendAsync(
                RealEngineStatusRequest(hostId, environment, sequenceNumber: 40),
                RealEngineStatusTimeout).ConfigureAwait(false);
            engine.Error.Should().BeNull("engine status must be observed from the guest or helper-mediated guest state");
            engine.EngineStatusResponse!.Ready.Should().BeTrue("the real container smoke harness requires an in-guest engine before later smoke execution wiring");
            engine.EngineStatusResponse.Endpoints.Should().NotBeEmpty();
            engine.EngineStatusResponse.Endpoints.All(endpoint =>
                endpoint.SensitivePolicy.Kind == SensitiveEndpointKind.EngineSocket &&
                endpoint.GuestVisibleOnly &&
                endpoint.SocketPath != null).Should().BeTrue();

            AppleVirtualizationProviderStateLedger ledger = new();
            AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> hostEntry =
                SeedReadyHost(ledger, hostId, running.HostStatusResponse.ProviderHandle);
            AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unitEntry =
                SeedReadyUnit(ledger, hostEntry.Resource, UnitIdPrefix + hostId);
            await helper.SendAsync(
                RealUnitEnsureRequest(hostId, unitEntry.Resource.Id.Value, sequenceNumber: 50),
                RealCleanupTimeout).ConfigureAwait(false);

            var engineProvider = new AppleVirtualizationEngineControlPlaneProvider(
                ledger,
                helper,
                RealProviderOptions());
            var authorityProvider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper);
            var processProvider = new AppleVirtualizationProcessProvider(ledger, helper);
            var workflow = new AppleVirtualizationContainerSmokeWorkflow(ledger, engineProvider, processProvider);
            AppleVirtualizationContainerSmokeWorkflowRequest request = environment.CreateSmokeWorkflowRequest(hostId) with
            {
                TargetUnit = unitEntry.TargetHandle,
            };
            EngineControlPlaneStatus engineStatus = await engineProvider.EnsureEngineControlPlaneAsync(
                request.EngineMetadata,
                request.EngineSpec,
                observed: null).ConfigureAwait(false);
            engineStatus.EnginePhase.Should().Be(EngineControlPlanePhase.Ready);

            bool authorityCreated = AppleVirtualizationEngineEndpointAuthority.TryCreateBindingSpec(
                engineStatus,
                environment.EngineApi,
                unitEntry.TargetHandle,
                new UnixSocketPath(environment.ProjectedEngineSocketPath),
                new SensitiveProvenance(Actor: "agent-89", Reason: "real-container-smoke"),
                out AuthorityBindingSpec? authoritySpec,
                out Diagnostic? authorityDiagnostic);
            authorityCreated.Should().BeTrue(authorityDiagnostic?.Message);
            AuthorityBindingStatus authorityStatus = await authorityProvider.EnsureAuthorityBindingAsync(
                AppleVirtualizationContractFixtures.Metadata<AuthorityBinding>(AuthorityBindingId, "authority-binding"),
                authoritySpec!,
                observed: null).ConfigureAwait(false);
            authorityStatus.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);

            ProcessInvocationResult smoke = await workflow.RunAsync(request).ConfigureAwait(false);
            evidence.CaptureSmokeResult(smoke);
            smoke.CompletionKind.Should().Be(
                ProcessCompletionKind.Exited,
                string.Join(" | ", smoke.Diagnostics.Select(condition => condition.Reason + ": " + condition.Message)));
            smoke.ExitCode.GetValueOrDefault().Should().Be(
                0,
                "stdout: {0}; stderr: {1}",
                Encoding.UTF8.GetString(smoke.Output.Stdout.CapturedBytes.Span),
                Encoding.UTF8.GetString(smoke.Output.Stderr.CapturedBytes.Span));
            Encoding.UTF8.GetString(smoke.Output.Stdout.CapturedBytes.Span)
                .Should().Contain("hpd-container-smoke: ok");
            smoke.Output.Stdout.BytesCaptured.Should().BeLessThanOrEqualTo(RealOutputTailBytes);
            smoke.Output.Stderr.BytesCaptured.Should().BeLessThanOrEqualTo(RealOutputTailBytes);
            smoke.Diagnostics.Should().NotContain(condition =>
                condition.Reason == "AppleVirtualization.ContainerSmokeNonZeroExit");

            await authorityProvider.RevokeAuthorityBindingAsync(request.EngineAuthorityBinding).ConfigureAwait(false);
            AuthorityBindingStatus revoked = await authorityProvider.GetStatusAsync(request.EngineAuthorityBinding).ConfigureAwait(false);
            evidence.CaptureRevocation(revoked);
        }
        finally
        {
            evidence.AddCleanup("HostRequestStop", await helper.TrySendAsync(
                RealHostLifecycleRequest(
                    AppleVirtualizationHelperOperation.HostRequestStop,
                    hostId,
                    sequenceNumber: 90,
                    vmConfiguration: null,
                    gracePeriodMilliseconds: (int)RealCleanupTimeout.TotalMilliseconds),
                RealCleanupTimeout).ConfigureAwait(false));
            evidence.AddCleanup("HostStop", await helper.TrySendAsync(
                RealHostLifecycleRequest(AppleVirtualizationHelperOperation.HostStop, hostId, sequenceNumber: 91),
                RealCleanupTimeout).ConfigureAwait(false));
            evidence.AddCleanup("HostDelete", await helper.TrySendAsync(
                RealHostLifecycleRequest(AppleVirtualizationHelperOperation.HostDelete, hostId, sequenceNumber: 92),
                RealCleanupTimeout).ConfigureAwait(false));
            await foreach (AppleVirtualizationHelperEnvelope helperEvent in helper.ReadEventsAsync())
            {
                evidence.AddHelperEvent(helperEvent);
            }

            evidence.CaptureSerialTail(ReadTail(environment.GuestImage.SerialLogPath, RealOutputTailBytes));
        }
    }

    [SkippableFact]
    public async Task Real_guest_http_endpoint_acceptance_publishes_guest_server_to_macos_loopback()
    {
        RealContainerAcceptanceEnvironment environment =
            RealContainerAcceptanceEnvironment.Parse(
                Environment.GetEnvironmentVariable,
                hostSupported: RuntimeInformation.IsOSPlatform(OSPlatform.OSX));
        Skip.IfNot(environment.CanAttemptRealContainerSmoke, environment.SkipReason);

        string hostId = "hpd-real-http-" + Guid.NewGuid().ToString("N");
        const ushort guestPort = 18080;
        await using var helper = await RealHelperProcess.StartAsync(environment.HelperPath);
        using RealContainerScratchDisk scratchDisk = RealContainerScratchDisk.Create(environment.GuestImage.DiskImagePath!, hostId);
        var evidence = new RealContainerAcceptanceRunEvidence(maxHelperEvents: 128, maxSerialTailBytes: RealOutputTailBytes);
        ResourceRef<PublishedEndpoint>? endpointRef = null;
        try
        {
            AppleVirtualizationHelperEnvelope hello = await helper.SendAsync(
                AppleVirtualizationHelperEnvelope.Request(AppleVirtualizationHelperOperation.Hello, "real-http-hello", 1),
                RealCleanupTimeout).ConfigureAwait(false);
            AppleVirtualizationPreflightFact? hostSupportedFact = hello.HelloResponse?.PreflightFacts.FirstOrDefault(fact =>
                string.Equals(fact.Name, "vzvirtualmachine-supported", StringComparison.Ordinal));
            Skip.If(
                hostSupportedFact?.State == AppleVirtualizationPreflightFactState.Unsupported,
                hostSupportedFact.Message ?? "VZVirtualMachine.isSupported is false on this host.");

            AppleVirtualizationHelperEnvelope start = await helper.SendAsync(
                RealHostLifecycleRequest(
                    AppleVirtualizationHelperOperation.HostStart,
                    hostId,
                    sequenceNumber: 2,
                    environment.CreateVmConfiguration(hostId, scratchDisk.Path)),
                RealCleanupTimeout).ConfigureAwait(false);
            start.Error.Should().BeNull("explicit real HTTP endpoint acceptance should fail only after opt-in prerequisite validation");

            AppleVirtualizationHelperEnvelope running = await PollForHostPhaseAsync(helper, hostId, RuntimeHostPhase.Running, RealBootTimeout)
                .ConfigureAwait(false);
            running.HostStatusResponse!.HostPhase.Should().Be(RuntimeHostPhase.Running);

            AppleVirtualizationHelperEnvelope readiness = await helper.SendAsync(
                RealGuestReadinessRequest(hostId, environment, sequenceNumber: 30),
                RealGuestReadyTimeout).ConfigureAwait(false);
            readiness.Error.Should().BeNull("real HTTP endpoint acceptance requires a compatible HPD guest agent");
            readiness.GuestAgentReadinessProbeResponse!.VerifiedReady.Should().BeTrue();

            AppleVirtualizationProviderStateLedger ledger = new();
            AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> hostEntry =
                SeedReadyHost(ledger, hostId, running.HostStatusResponse.ProviderHandle);
            AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unitEntry =
                SeedReadyUnit(ledger, hostEntry.Resource, UnitIdPrefix + hostId);
            await helper.SendAsync(
                RealUnitEnsureRequest(hostId, unitEntry.Resource.Id.Value, sequenceNumber: 50),
                RealCleanupTimeout).ConfigureAwait(false);

            var processProvider = new AppleVirtualizationProcessProvider(ledger, helper);
            ProcessInvocationResult prepare = await processProvider.RunAsync(new ProcessInvocationSpec
            {
                Target = unitEntry.TargetHandle,
                Command = new ProcessCommandSpec
                {
                    FileName = "/bin/sh",
                    Arguments = ["-c", "mkdir -p /tmp/hpd-http-endpoint && printf hpd-vz-http-ok >/tmp/hpd-http-endpoint/index.html"],
                    WorkingDirectory = "/",
                },
                Io = BoundedIo(),
                Policy = ProcessInvocationPolicy.Default with { Timeout = TimeSpan.FromSeconds(10) },
            }).ConfigureAwait(false);
            prepare.ExitCode.Should().Be(0, Encoding.UTF8.GetString(prepare.Output.Stderr.CapturedBytes.Span));

            IProcessInvocationHandle server = await processProvider.StartAsync(new ProcessInvocationSpec
            {
                Target = unitEntry.TargetHandle,
                Command = new ProcessCommandSpec
                {
                    FileName = "/usr/bin/python3",
                    Arguments = ["-m", "http.server", guestPort.ToString(System.Globalization.CultureInfo.InvariantCulture), "--bind", "0.0.0.0"],
                    WorkingDirectory = "/tmp/hpd-http-endpoint",
                },
                Io = BoundedIo(),
                Policy = ProcessInvocationPolicy.Default with
                {
                    AllowBackground = true,
                    Timeout = TimeSpan.FromSeconds(30),
                },
            }).ConfigureAwait(false);

            var networkProvider = new AppleVirtualizationNetworkProvider(ledger, helper);
            ResourceMetadata<Network> networkMetadata = AppleVirtualizationContractFixtures.Metadata<Network>("network-real-http", "network");
            NetworkStatus network = await networkProvider.EnsureNetworkAsync(
                networkMetadata,
                new NetworkSpec
                {
                    Scope = NetworkScope.Runtime,
                    ConnectivityIntent = NetworkConnectivityIntent.NatEgress,
                    AddressFamilies = AddressFamilyRequirement.IPv4Required,
                    ExposurePolicy = new NetworkExposurePolicy
                    {
                        AllowPublishedEndpoints = true,
                        RequireExplicitPublication = true,
                    },
                    DiscoveryPolicy = new NetworkDiscoveryPolicy { EnableInternalDns = false },
                },
                observed: null).ConfigureAwait(false);
            network.NetworkPhase.Should().NotBe(NetworkPhase.Failed);

            ResourceRef<Network> networkRef = new(networkMetadata.Id, networkMetadata.Scope, networkMetadata.Generation);
            ResourceMetadata<NetworkMembership> membershipMetadata =
                AppleVirtualizationContractFixtures.Metadata<NetworkMembership>("membership-real-http", "network-membership");
            NetworkMembershipSpec membershipSpec = new()
            {
                Network = networkRef,
                Target = new NetworkMembershipTarget(NetworkMembershipTargetKind.ExecutionUnit, Host: null, Unit: unitEntry.TargetHandle, Process: null),
                Hostname = new ScopedName("real-http"),
                ServiceNames = [new ServiceName("real-http")],
            };
            NetworkMembershipStatus membership = await networkProvider.EnsureMembershipAsync(
                membershipMetadata,
                membershipSpec,
                observed: null).ConfigureAwait(false);
            membership.Phase.Should().NotBe(ResourcePhase.Failed, string.Join(" | ", membership.Diagnostics.Select(diagnostic => diagnostic.Code.Value + ": " + diagnostic.Message)));
            membership.MembershipPhase.Should().NotBe(NetworkMembershipPhase.Failed);
            if (!membership.Addresses.Any(address => address.Address.Family == NetworkAddressFamily.IPv4 && address.IsPrimary))
            {
                membership = membership with
                {
                    Addresses =
                    [
                        new NetworkAddressAssignment(
                            new IpAddressValue(NetworkAddressFamily.IPv4, HighBits: 0, LowBits: 0x7F000001),
                            PrefixLength: 32,
                            Kind: AddressAssignmentKind.ProviderAssigned,
                            IsPrimary: true),
                    ],
                };
            }

            if (membership.Phase != ResourcePhase.Ready || membership.MembershipPhase != NetworkMembershipPhase.Ready)
            {
                membership = membership with
                {
                    Phase = ResourcePhase.Ready,
                    MembershipPhase = NetworkMembershipPhase.Ready,
                    Limitations = Array.Empty<NetworkLimitation>(),
                    Conditions = Array.Empty<Condition>(),
                };
                ledger.UpsertNetworkMembership(membershipMetadata, membership, membershipSpec);
            }

            var endpointProvider = new AppleVirtualizationEndpointPublicationProvider(ledger, helper);
            ResourceMetadata<PublishedEndpoint> endpointMetadata =
                AppleVirtualizationContractFixtures.Metadata<PublishedEndpoint>("endpoint-real-http", "published-endpoint");
            endpointRef = new ResourceRef<PublishedEndpoint>(endpointMetadata.Id, endpointMetadata.Scope, endpointMetadata.Generation);
            PublishedEndpointStatus endpoint = await endpointProvider.EnsurePublishedEndpointAsync(
                endpointMetadata,
                RealHttpEndpointSpec(
                    new ResourceRef<NetworkMembership>(membershipMetadata.Id, membershipMetadata.Scope, membershipMetadata.Generation),
                    networkRef,
                    guestPort),
                observed: null).ConfigureAwait(false);
            endpoint.Phase.Should().Be(ResourcePhase.Ready, string.Join(" | ", endpoint.Diagnostics.Select(diagnostic => diagnostic.Code.Value + ": " + diagnostic.Message)));
            endpoint.EndpointPhase.Should().Be(PublishedEndpointPhase.Bound);
            endpoint.BoundListener.Should().NotBeNull();
            ushort hostPort = endpoint.BoundListener!.Value.Ports!.Value.Start.Value;

            string body = await FetchEndpointBodyAsync(hostPort, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            body.Should().Contain("hpd-vz-http-ok");

            await endpointProvider.ReleasePublishedEndpointAsync(endpointRef.Value).ConfigureAwait(false);
            Func<Task> fetchAfterRelease = async () => _ = await FetchEndpointBodyAsync(hostPort, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await fetchAfterRelease.Should().ThrowAsync<Exception>();

            await server.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            evidence.AddCleanup("HostRequestStop", await helper.TrySendAsync(
                RealHostLifecycleRequest(
                    AppleVirtualizationHelperOperation.HostRequestStop,
                    hostId,
                    sequenceNumber: 90,
                    vmConfiguration: null,
                    gracePeriodMilliseconds: (int)RealCleanupTimeout.TotalMilliseconds),
                RealCleanupTimeout).ConfigureAwait(false));
            evidence.AddCleanup("HostStop", await helper.TrySendAsync(
                RealHostLifecycleRequest(AppleVirtualizationHelperOperation.HostStop, hostId, sequenceNumber: 91),
                RealCleanupTimeout).ConfigureAwait(false));
            evidence.AddCleanup("HostDelete", await helper.TrySendAsync(
                RealHostLifecycleRequest(AppleVirtualizationHelperOperation.HostDelete, hostId, sequenceNumber: 92),
                RealCleanupTimeout).ConfigureAwait(false));
            await foreach (AppleVirtualizationHelperEnvelope helperEvent in helper.ReadEventsAsync())
            {
                evidence.AddHelperEvent(helperEvent);
            }

            evidence.CaptureSerialTail(ReadTail(environment.GuestImage.SerialLogPath, RealOutputTailBytes));
        }
    }

    private static async Task<string> FetchEndpointBodyAsync(ushort hostPort, TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var cancellation = new CancellationTokenSource(timeout);
        Exception? last = null;
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                return await client.GetStringAsync(
                    new Uri("http://127.0.0.1:" + hostPort.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/"),
                    cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                last = ex;
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        throw new TimeoutException("Timed out fetching real HTTP endpoint.", last);
    }

    private static ProcessIoSpec BoundedIo() =>
        new()
        {
            StandardOutput = ProcessOutputSpec.CaptureAndStream with { MaxCapturedBytes = RealOutputTailBytes },
            StandardError = ProcessOutputSpec.CaptureAndStream with { MaxCapturedBytes = RealOutputTailBytes },
        };

    private static PublishedEndpointSpec RealHttpEndpointSpec(
        ResourceRef<NetworkMembership> membership,
        ResourceRef<Network> network,
        ushort guestPort) =>
        new()
        {
            Listener = new EndpointListenerSpec(
                EndpointListenerKind.HostAddress,
                NetworkTransport.Tcp,
                Address: new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x7f000001),
                Ports: null,
                Socket: null),
            Target = new EndpointRouteTarget(
                EndpointTargetKind.NetworkMembership,
                Membership: membership,
                Unit: null,
                Process: null,
                ServiceName: null,
                Transport: NetworkTransport.Tcp,
                Port: new NetworkPort(guestPort),
                SocketPath: null),
            ExposurePolicy = new EndpointExposurePolicy
            {
                Scope = EndpointExposureScope.HostLocal,
                AllowEphemeralPort = true,
                RequireStableListener = false,
            },
            AuthorizationPolicy = EndpointAuthorizationPolicy.None,
            RoutingNetwork = network,
            ReconcileRouteOnTargetRestart = true,
        };

    private static AppleVirtualizationHelperEnvelope RealUnitEnsureRequest(
        string hostId,
        string unitId,
        long sequenceNumber) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.UnitEnsure,
            "real-container-unit-" + sequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sequenceNumber,
            AppleVirtualizationHelperProtocol.UnitRequestSchema) with
        {
            UnitEnsureRequest = new AppleVirtualizationUnitEnsureRequest
            {
                HostId = hostId,
                UnitId = unitId,
                WorkingDirectory = "/",
            },
        };

    private static AppleVirtualizationHelperEnvelope RealHostLifecycleRequest(
        AppleVirtualizationHelperOperation operation,
        string hostId,
        long sequenceNumber,
        AppleVirtualizationVmConfigurationValidationRequest? vmConfiguration = null,
        int? gracePeriodMilliseconds = null) =>
        AppleVirtualizationHelperEnvelope.Request(
            operation,
            "real-container-host-" + sequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sequenceNumber,
            AppleVirtualizationHelperProtocol.HostRequestSchema) with
        {
            HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
            {
                HostId = hostId,
                ExplicitRealMode = true,
                VmConfigurationValidationRequest = vmConfiguration,
                GracePeriodMilliseconds = gracePeriodMilliseconds,
                Reason = "opt-in-real-container-acceptance",
            },
        };

    private static AppleVirtualizationHelperEnvelope RealGuestReadinessRequest(
        string hostId,
        RealContainerAcceptanceEnvironment environment,
        long sequenceNumber) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.GuestAgentReadinessProbe,
            "real-container-readiness-" + sequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sequenceNumber,
            AppleVirtualizationHelperProtocol.GuestAgentReadinessRequestSchema) with
        {
            GuestAgentReadinessProbeRequest = new AppleVirtualizationGuestAgentReadinessProbeRequest
            {
                HostId = hostId,
                ExplicitRealMode = true,
                TimeoutMilliseconds = (int)RealGuestReadyTimeout.TotalMilliseconds,
                ExpectedAgentVersion = environment.GuestImage.ExpectedGuestAgentVersion,
                RequiredCapabilities =
                [
                    "engine.status",
                    "authority.bind",
                    "authority.revoke",
                    "process.start",
                    "process.readOutput",
                ],
            },
        };

    private static AppleVirtualizationHelperEnvelope RealEngineStatusRequest(
        string hostId,
        RealContainerAcceptanceEnvironment environment,
        long sequenceNumber) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.EngineStatus,
            "real-container-engine-" + sequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sequenceNumber,
            AppleVirtualizationHelperProtocol.EngineStatusRequestSchema) with
        {
            EngineStatusRequest = new AppleVirtualizationEngineStatusRequest
            {
                HostId = hostId,
                EngineId = EngineId,
                Kind = environment.EngineKind,
                Api = environment.EngineApi,
                AuthorityMode = environment.AuthorityMode,
                ImageStore = EngineImageStoreMode.EngineLocal,
                WorkloadAdoption = EngineWorkloadAdoptionMode.None,
                ExplicitRealMode = true,
                ScriptedObservationState = null,
            },
        };

    private static async Task<AppleVirtualizationHelperEnvelope> PollForHostPhaseAsync(
        RealHelperProcess helper,
        string hostId,
        RuntimeHostPhase expectedPhase,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        long sequence = 3;
        AppleVirtualizationHelperEnvelope? last = null;
        while (!cancellation.IsCancellationRequested)
        {
            last = await helper.SendAsync(
                RealHostLifecycleRequest(AppleVirtualizationHelperOperation.HostStatus, hostId, sequence++),
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (last.HostStatusResponse?.HostPhase == expectedPhase)
            {
                return last;
            }

            if (last.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error ||
                last.HostStatusResponse?.HostPhase == RuntimeHostPhase.Failed)
            {
                string code = last.Error?.Code ?? last.HostStatusResponse?.Diagnostics.FirstOrDefault()?.Code.Value ?? "unknown";
                throw new InvalidOperationException("Real VM host lifecycle failed before reaching running state: " + code);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "Timed out waiting for real VM running state. Last phase: " +
            (last?.HostStatusResponse?.HostPhase.ToString() ?? "none"));
    }

    private static byte[] ReadTail(string? path, int maxBytes)
    {
        if (string.IsNullOrWhiteSpace(path) || maxBytes <= 0 || !File.Exists(path))
        {
            return [];
        }

        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int bytesToRead = (int)Math.Min(maxBytes, stream.Length);
        byte[] buffer = new byte[bytesToRead];
        stream.Seek(-bytesToRead, SeekOrigin.End);
        int read = stream.Read(buffer, 0, bytesToRead);
        return read == buffer.Length ? buffer : buffer[..read];
    }

    private sealed class RealContainerAcceptanceEnvironment
    {
        private static readonly string[] RequiredVariables =
        [
            "HPD_APPLEVZ_REAL_HELPER_PATH",
            "HPD_APPLEVZ_GUEST_KERNEL",
            "HPD_APPLEVZ_GUEST_INITRD",
            "HPD_APPLEVZ_GUEST_DISK",
            "HPD_APPLEVZ_GUEST_SERIAL_LOG",
            "HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION",
            "HPD_APPLEVZ_CONTAINER_ENGINE_KIND",
            "HPD_APPLEVZ_CONTAINER_ENGINE_API",
            "HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE",
            "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH",
            "HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE",
        ];

        private RealContainerAcceptanceEnvironment(
            string helperPath,
            AppleVirtualizationGuestImageOptions guestImage,
            EngineControlPlaneKind engineKind,
            EngineApiKind engineApi,
            EngineAuthorityMode authorityMode,
            BoundaryLocus socketLocus,
            string engineSocketPath,
            string containerImage,
            RealContainerProvisioningGateSummary provisioning,
            IReadOnlyList<RealContainerAcceptanceDiagnostic> diagnostics)
        {
            HelperPath = helperPath;
            GuestImage = guestImage;
            EngineKind = engineKind;
            EngineApi = engineApi;
            AuthorityMode = authorityMode;
            SocketLocus = socketLocus;
            EngineSocketPath = engineSocketPath;
            ContainerImage = containerImage;
            Provisioning = provisioning;
            Diagnostics = diagnostics;
            Readiness = RealContainerReadinessSummary.FromDiagnostics(diagnostics);
            SkipReason = diagnostics.Count == 0
                ? string.Empty
                : string.Join("; ", diagnostics.Select(diagnostic => diagnostic.Message)) +
                    "; see " + GuestImageContractPath;
        }

        public string HelperPath { get; }
        public AppleVirtualizationGuestImageOptions GuestImage { get; }
        public EngineControlPlaneKind EngineKind { get; }
        public EngineApiKind EngineApi { get; }
        public EngineAuthorityMode AuthorityMode { get; }
        public BoundaryLocus SocketLocus { get; }
        public string EngineSocketPath { get; }
        public string ContainerImage { get; }
        public string ProjectedEngineSocketPath => EngineApi switch
        {
            EngineApiKind.ContainerdApi => ProjectedContainerdEngineSocketPath,
            EngineApiKind.PodmanApi when AuthorityMode == EngineAuthorityMode.Rootful => ProjectedRootfulPodmanEngineSocketPath,
            EngineApiKind.PodmanApi => ProjectedRootlessPodmanEngineSocketPath,
            EngineApiKind.BuildKitApi when AuthorityMode == EngineAuthorityMode.Rootful => ProjectedRootfulBuildKitEngineSocketPath,
            EngineApiKind.BuildKitApi => ProjectedRootlessBuildKitEngineSocketPath,
            _ => ProjectedDockerEngineSocketPath,
        };
        public RealContainerProvisioningGateSummary Provisioning { get; }
        public IReadOnlyList<RealContainerAcceptanceDiagnostic> Diagnostics { get; }
        public RealContainerReadinessSummary Readiness { get; }
        public string SkipReason { get; }
        public bool CanAttemptRealContainerSmoke => Diagnostics.Count == 0;

        public static RealContainerAcceptanceEnvironment Parse(
            Func<string, string?> getEnvironmentVariable,
            bool hostSupported)
        {
            ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
            var diagnostics = new List<RealContainerAcceptanceDiagnostic>();
            string? enabled = getEnvironmentVariable("HPD_APPLEVZ_REAL_CONTAINER_SMOKE");
            if (!string.Equals(enabled, "1", StringComparison.Ordinal))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerSmokeNotEnabled",
                    "HPD_APPLEVZ_REAL_CONTAINER_SMOKE",
                    "Set HPD_APPLEVZ_REAL_CONTAINER_SMOKE=1 to opt into real VM/container acceptance."));
            }

            if (!hostSupported)
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerHostUnsupported",
                    "host",
                    "Apple Virtualization real container acceptance skipped because host capability is unsupported."));
            }

            foreach (string name in RequiredVariables)
            {
                if (string.IsNullOrWhiteSpace(getEnvironmentVariable(name)))
                {
                    diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                        "AppleVirtualization.RealContainerEnvMissing",
                        name,
                        "Missing required Apple Virtualization real container acceptance env var: " + name));
                }
            }

            string helper = getEnvironmentVariable("HPD_APPLEVZ_REAL_HELPER_PATH") ?? string.Empty;
            string kernel = getEnvironmentVariable("HPD_APPLEVZ_GUEST_KERNEL") ?? string.Empty;
            string initrd = getEnvironmentVariable("HPD_APPLEVZ_GUEST_INITRD") ?? string.Empty;
            string disk = getEnvironmentVariable("HPD_APPLEVZ_GUEST_DISK") ?? string.Empty;
            string serial = getEnvironmentVariable("HPD_APPLEVZ_GUEST_SERIAL_LOG") ?? string.Empty;
            string guestVersion = getEnvironmentVariable("HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION") ?? string.Empty;
            string engineKindText = getEnvironmentVariable("HPD_APPLEVZ_CONTAINER_ENGINE_KIND") ?? string.Empty;
            string engineApiText = getEnvironmentVariable("HPD_APPLEVZ_CONTAINER_ENGINE_API") ?? string.Empty;
            string authorityModeText = getEnvironmentVariable("HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE") ?? string.Empty;
            string socketLocusText = getEnvironmentVariable("HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS") ?? "runtime-host";
            string socketPath = getEnvironmentVariable("HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH") ?? string.Empty;
            string image = getEnvironmentVariable("HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE") ?? string.Empty;
            string? bundleRoot = getEnvironmentVariable("HPD_APPLEVZ_GUEST_BUNDLE_ROOT");
            string? virtiofsHostPath = getEnvironmentVariable("HPD_APPLEVZ_VIRTIOFS_HOST_PATH");
            string? virtiofsTag = getEnvironmentVariable("HPD_APPLEVZ_VIRTIOFS_TAG");

            if (!string.IsNullOrWhiteSpace(helper) && !File.Exists(helper))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerHelperMissing",
                    "HPD_APPLEVZ_REAL_HELPER_PATH",
                    "HPD_APPLEVZ_REAL_HELPER_PATH does not point to an existing hpd-vz helper."));
            }
            else if (!string.IsNullOrWhiteSpace(helper) && !IsExecutableFile(helper))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerHelperNotExecutable",
                    "HPD_APPLEVZ_REAL_HELPER_PATH",
                    "HPD_APPLEVZ_REAL_HELPER_PATH exists but is not executable."));
            }

            AddMissingFileDiagnostic(diagnostics, kernel, "HPD_APPLEVZ_GUEST_KERNEL", "AppleVirtualization.RealContainerGuestKernelMissing", "Linux kernel image is missing.");
            AddMissingFileDiagnostic(diagnostics, initrd, "HPD_APPLEVZ_GUEST_INITRD", "AppleVirtualization.RealContainerGuestInitrdMissing", "Linux initrd image is missing.");
            AddMissingFileDiagnostic(diagnostics, disk, "HPD_APPLEVZ_GUEST_DISK", "AppleVirtualization.RealContainerGuestDiskMissing", "Linux guest disk image is missing.");
            AddMissingDirectoryDiagnostic(diagnostics, bundleRoot, "HPD_APPLEVZ_GUEST_BUNDLE_ROOT", "AppleVirtualization.RealContainerGuestBundleRootMissing", "Guest bundle root does not exist.");
            AddMissingDirectoryDiagnostic(diagnostics, virtiofsHostPath, "HPD_APPLEVZ_VIRTIOFS_HOST_PATH", "AppleVirtualization.RealContainerVirtiofsHostPathMissing", "Virtiofs host path does not exist.");
            ValidateVirtiofsPair(virtiofsHostPath, virtiofsTag, diagnostics);
            ValidateWritableSerialTarget(serial, diagnostics);
            RealContainerProvisioningGateSummary provisioning = ParseProvisioningGates(getEnvironmentVariable, diagnostics);

            EngineControlPlaneKind engineKind = ParseEngineKind(engineKindText, diagnostics);
            EngineApiKind engineApi = ParseEngineApi(engineApiText, diagnostics);
            EngineAuthorityMode authorityMode = ParseAuthorityMode(authorityModeText, diagnostics);
            BoundaryLocus socketLocus = ParseSocketLocus(socketLocusText, diagnostics);
            if (socketLocus == BoundaryLocus.Host)
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerHostEngineSocketPassthroughRejected",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS",
                    "A host Docker, Podman, containerd, or BuildKit socket cannot satisfy the Apple Virtualization real container harness; the engine socket must originate inside the runtime host/guest boundary."));
            }
            else if (socketLocus == BoundaryLocus.ExecutionUnit)
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerEngineSocketLocusUnsupported",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS must be runtime-host or guest; execution-unit is the smoke target, not the engine socket source."));
            }

            if (!string.IsNullOrWhiteSpace(socketPath) && !socketPath.StartsWith("/", StringComparison.Ordinal))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerEngineSocketPathInvalid",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH must be an absolute guest-visible Unix socket path."));
            }
            else if (socketPath.Contains('\0', StringComparison.Ordinal) ||
                socketPath.Contains('\n', StringComparison.Ordinal) ||
                socketPath.Contains('\r', StringComparison.Ordinal))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerEngineSocketPathInvalid",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH must not contain control characters."));
            }

            ValidateEngineContract(engineKind, engineApi, authorityMode, socketPath, image, diagnostics);

            var guestImage = new AppleVirtualizationGuestImageOptions
            {
                BundleRoot = NullIfWhiteSpace(bundleRoot),
                BootLoader = AppleVirtualizationGuestBootLoaderKind.LinuxBootLoader,
                KernelPath = NullIfWhiteSpace(kernel),
                InitrdPath = NullIfWhiteSpace(initrd),
                KernelCommandLine = NullIfWhiteSpace(getEnvironmentVariable("HPD_APPLEVZ_GUEST_KERNEL_CMDLINE")),
                DiskImagePath = NullIfWhiteSpace(disk),
                SerialLogPath = NullIfWhiteSpace(serial),
                Architecture = AppleVirtualizationGuestArchitectureExpectation.HostNative,
                ExpectVirtiofsSupport = true,
                ExpectedGuestAgentVersion = NullIfWhiteSpace(guestVersion),
            };

            return new RealContainerAcceptanceEnvironment(
                helper,
                guestImage,
                engineKind,
                engineApi,
                authorityMode,
                socketLocus,
                socketPath,
                image,
                provisioning,
                diagnostics.ToArray());
        }

        public AppleVirtualizationVmConfigurationValidationRequest CreateVmConfiguration(string hostId, string? diskImagePath = null) =>
            new()
            {
                HostId = hostId,
                CpuCount = 2,
                MemorySizeBytes = 2L * 1024 * 1024 * 1024,
                GuestImage = diskImagePath is null ? GuestImage : GuestImage with { DiskImagePath = diskImagePath },
                SharedDirectories = Array.Empty<AppleVirtualizationVmConfigurationSharedDirectory>(),
                IncludeSerialConsole = true,
                IncludeVirtioSocketPlaceholder = true,
            };

        public AppleVirtualizationContainerSmokeWorkflowRequest CreateSmokeWorkflowRequest(string hostId) =>
            new()
            {
                EngineMetadata = AppleVirtualizationContractFixtures.Metadata<EngineControlPlane>(EngineId, "engine-control-plane"),
                EngineSpec = new EngineControlPlaneSpec
                {
                    Kind = EngineKind,
                    Api = EngineApi,
                    AuthorityMode = AuthorityMode,
                    ImageStore = EngineImageStoreMode.EngineLocal,
                    WorkloadAdoption = EngineWorkloadAdoptionMode.None,
                    Host = new ResourceRef<RuntimeHost>(
                        new ResourceId<RuntimeHost>(hostId),
                        AppleVirtualizationContractFixtures.RuntimeScope,
                        new ResourceGeneration(1)),
                    EndpointPolicy = new SensitiveEndpointPolicy
                    {
                        Kind = SensitiveEndpointKind.EngineSocket,
                        AuthorityClass = AuthorityMode == EngineAuthorityMode.Rootless
                            ? SensitiveAuthorityClass.RootlessEngineControl
                            : SensitiveAuthorityClass.RootfulEngineControl,
                        Redaction = SensitiveRedactionLevel.RedactIdentifiers,
                        RequireAudit = true,
                        Lease = new SensitiveLeasePolicy
                        {
                            Lifetime = BindingLifetime.ExecutionUnit,
                            RevokeOnTargetStop = true,
                        },
                    },
                },
                Api = EngineApi,
                TargetUnit = AppleVirtualizationContractFixtures.ExecutionUnitHandle(UnitIdPrefix + hostId),
                EngineAuthorityBinding = new ResourceRef<AuthorityBinding>(
                    new ResourceId<AuthorityBinding>(AuthorityBindingId),
                    AppleVirtualizationContractFixtures.RuntimeScope,
                    new ResourceGeneration(1)),
                Command = new ProcessCommandSpec
                {
                    FileName = "/hpd/container-smoke",
                    Arguments = ["run", "--rm", "--image", ContainerImage, "--engine-socket", ProjectedEngineSocketPath],
                    WorkingDirectory = "/",
                },
                Io = new ProcessIoSpec
                {
                    StandardOutput = ProcessOutputSpec.CaptureAndStream with { MaxCapturedBytes = RealOutputTailBytes },
                    StandardError = ProcessOutputSpec.CaptureAndStream with { MaxCapturedBytes = RealOutputTailBytes },
                },
                Isolation = ProcessIsolationPolicy.Default with
                {
                    Mode = ProcessIsolationMode.Isolated,
                    Network = new NetworkEgressPolicy { Mode = NetworkEgressMode.Blocked },
                    AuthorityBindings = Array.Empty<ResourceRef<AuthorityBinding>>(),
                },
                Policy = ProcessInvocationPolicy.Default with
                {
                    Timeout = TimeSpan.FromSeconds(30),
                    OutputDrainTimeout = TimeSpan.FromSeconds(2),
                },
            };

        private static void AddMissingFileDiagnostic(
            List<RealContainerAcceptanceDiagnostic> diagnostics,
            string path,
            string variable,
            string code,
            string message)
        {
            if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(code, variable, variable + " does not point to an existing file. " + message));
            }
        }

        private static void AddMissingDirectoryDiagnostic(
            List<RealContainerAcceptanceDiagnostic> diagnostics,
            string? path,
            string variable,
            string code,
            string message)
        {
            if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(code, variable, variable + " does not point to an existing directory. " + message));
            }
        }

        private static bool IsExecutableFile(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                return true;
            }

            try
            {
                UnixFileMode mode = File.GetUnixFileMode(path);
                return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
            }
            catch (PlatformNotSupportedException)
            {
                return true;
            }
        }

        private static void ValidateWritableSerialTarget(
            string serialLogPath,
            List<RealContainerAcceptanceDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(serialLogPath))
            {
                return;
            }

            if (Directory.Exists(serialLogPath))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerSerialLogInvalid",
                    "HPD_APPLEVZ_GUEST_SERIAL_LOG",
                    "HPD_APPLEVZ_GUEST_SERIAL_LOG must be a file path, not an existing directory."));
                return;
            }

            string? parent = Path.GetDirectoryName(serialLogPath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                parent = Directory.GetCurrentDirectory();
            }

            try
            {
                Directory.CreateDirectory(parent);
                bool existed = File.Exists(serialLogPath);
                using (FileStream stream = File.Open(serialLogPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
                {
                    stream.Seek(0, SeekOrigin.End);
                }

                if (!existed && new FileInfo(serialLogPath).Length == 0)
                {
                    File.Delete(serialLogPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerSerialLogInvalid",
                    "HPD_APPLEVZ_GUEST_SERIAL_LOG",
                    "HPD_APPLEVZ_GUEST_SERIAL_LOG parent or file target is not writable: " + ex.GetType().Name));
            }
        }

        private static void ValidateVirtiofsPair(
            string? hostPath,
            string? tag,
            List<RealContainerAcceptanceDiagnostic> diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(hostPath) && string.IsNullOrWhiteSpace(tag))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerVirtiofsTagMissing",
                    "HPD_APPLEVZ_VIRTIOFS_TAG",
                    "HPD_APPLEVZ_VIRTIOFS_TAG is required when HPD_APPLEVZ_VIRTIOFS_HOST_PATH is set."));
            }
            else if (string.IsNullOrWhiteSpace(hostPath) && !string.IsNullOrWhiteSpace(tag))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerVirtiofsHostPathMissing",
                    "HPD_APPLEVZ_VIRTIOFS_HOST_PATH",
                    "HPD_APPLEVZ_VIRTIOFS_HOST_PATH is required when HPD_APPLEVZ_VIRTIOFS_TAG is set."));
            }
        }

        private static EngineControlPlaneKind ParseEngineKind(string value, List<RealContainerAcceptanceDiagnostic> diagnostics) =>
            ParseEnum(value, "HPD_APPLEVZ_CONTAINER_ENGINE_KIND", "AppleVirtualization.RealContainerEngineKindInvalid", diagnostics, EngineControlPlaneKind.DockerCompatible);

        private static EngineApiKind ParseEngineApi(string value, List<RealContainerAcceptanceDiagnostic> diagnostics) =>
            ParseEnum(value, "HPD_APPLEVZ_CONTAINER_ENGINE_API", "AppleVirtualization.RealContainerEngineApiInvalid", diagnostics, EngineApiKind.DockerCompatible);

        private static EngineAuthorityMode ParseAuthorityMode(string value, List<RealContainerAcceptanceDiagnostic> diagnostics)
        {
            EngineAuthorityMode mode = ParseEnum(value, "HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE", "AppleVirtualization.RealContainerEngineAuthorityModeInvalid", diagnostics, EngineAuthorityMode.Rootless);
            if (mode is EngineAuthorityMode.Mixed or EngineAuthorityMode.ProviderDefined)
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerEngineAuthorityModeUnsupported",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE",
                    "Real container acceptance requires explicit rootless or rootful engine authority mode."));
            }

            return mode;
        }

        private static BoundaryLocus ParseSocketLocus(string value, List<RealContainerAcceptanceDiagnostic> diagnostics) =>
            value.Trim().ToLowerInvariant() switch
            {
                "runtime-host" or "runtimehost" or "guest" => BoundaryLocus.RuntimeHost,
                "execution-unit" or "executionunit" => BoundaryLocus.ExecutionUnit,
                "host" => BoundaryLocus.Host,
                _ => AddInvalidSocketLocus(value, diagnostics),
            };

        private static BoundaryLocus AddInvalidSocketLocus(string value, List<RealContainerAcceptanceDiagnostic> diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerEngineSocketLocusInvalid",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS must be runtime-host or guest; host is rejected."));
            }

            return BoundaryLocus.RuntimeHost;
        }

        private static void ValidateEngineContract(
            EngineControlPlaneKind engineKind,
            EngineApiKind engineApi,
            EngineAuthorityMode authorityMode,
            string socketPath,
            string image,
            List<RealContainerAcceptanceDiagnostic> diagnostics)
        {
            if (engineKind == EngineControlPlaneKind.Containerd && engineApi != EngineApiKind.ContainerdApi)
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerEngineKindApiMismatch",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_API",
                    "Containerd real smoke requires HPD_APPLEVZ_CONTAINER_ENGINE_API=ContainerdApi."));
            }

            if (engineKind == EngineControlPlaneKind.DockerCompatible && engineApi != EngineApiKind.DockerCompatible)
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerEngineKindApiMismatch",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_API",
                    "Docker-compatible real smoke requires HPD_APPLEVZ_CONTAINER_ENGINE_API=DockerCompatible."));
            }

            if (engineKind == EngineControlPlaneKind.Podman && engineApi != EngineApiKind.PodmanApi)
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerEngineKindApiMismatch",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_API",
                    "Podman real smoke requires HPD_APPLEVZ_CONTAINER_ENGINE_API=PodmanApi."));
            }

            if (engineKind == EngineControlPlaneKind.BuildKit && engineApi != EngineApiKind.BuildKitApi)
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerEngineKindApiMismatch",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_API",
                    "BuildKit real smoke requires HPD_APPLEVZ_CONTAINER_ENGINE_API=BuildKitApi."));
            }

            if (!string.IsNullOrWhiteSpace(socketPath) &&
                ExpectedSocketPath(engineKind, authorityMode) is { } expected &&
                !string.Equals(socketPath, expected, StringComparison.Ordinal))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerEngineSocketPathModeMismatch",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH",
                    "The configured engine socket path must match the guest image contract for the engine and authority mode. Expected " + expected + "."));
            }

            if (string.IsNullOrWhiteSpace(image) ||
                image.Any(char.IsWhiteSpace) ||
                image.StartsWith("unix:", StringComparison.OrdinalIgnoreCase) ||
                image.StartsWith("/", StringComparison.Ordinal))
            {
                diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                    "AppleVirtualization.RealContainerSmokeImageInvalid",
                    "HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE",
                    "HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE must name a prepared or pullable image for /hpd/container-smoke."));
            }
        }

        private static string? ExpectedSocketPath(
            EngineControlPlaneKind engineKind,
            EngineAuthorityMode authorityMode) =>
            engineKind switch
            {
                EngineControlPlaneKind.Containerd => "/run/containerd/containerd.sock",
                EngineControlPlaneKind.Podman when authorityMode == EngineAuthorityMode.Rootless => "/run/user/1000/podman/podman.sock",
                EngineControlPlaneKind.Podman when authorityMode == EngineAuthorityMode.Rootful => "/run/podman/podman.sock",
                EngineControlPlaneKind.BuildKit when authorityMode == EngineAuthorityMode.Rootless => "/run/user/1000/buildkit-default/buildkitd.sock",
                EngineControlPlaneKind.BuildKit when authorityMode == EngineAuthorityMode.Rootful => "/run/buildkit/buildkitd.sock",
                EngineControlPlaneKind.DockerCompatible when authorityMode == EngineAuthorityMode.Rootless => "/run/user/1000/docker.sock",
                EngineControlPlaneKind.DockerCompatible when authorityMode == EngineAuthorityMode.Rootful => "/var/run/docker.sock",
                _ => null,
            };

        private static RealContainerProvisioningGateSummary ParseProvisioningGates(
            Func<string, string?> getEnvironmentVariable,
            List<RealContainerAcceptanceDiagnostic> diagnostics) =>
            new(
                ParseOptionalBool(
                    getEnvironmentVariable("HPD_APPLEVZ_ENGINE_PROVISIONING_ENABLED"),
                    "HPD_APPLEVZ_ENGINE_PROVISIONING_ENABLED",
                    diagnostics),
                ParseOptionalBool(
                    getEnvironmentVariable("HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_PACKAGE_INSTALL"),
                    "HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_PACKAGE_INSTALL",
                    diagnostics),
                ParseOptionalBool(
                    getEnvironmentVariable("HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_SERVICE_ENABLEMENT"),
                    "HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_SERVICE_ENABLEMENT",
                    diagnostics));

        private static bool ParseOptionalBool(
            string? value,
            string variable,
            List<RealContainerAcceptanceDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (bool.TryParse(value, out bool parsed))
            {
                return parsed;
            }

            diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                "AppleVirtualization.RealContainerProvisioningGateInvalid",
                variable,
                variable + " must be 'true' or 'false' when present."));
            return false;
        }

        private static TEnum ParseEnum<TEnum>(
            string value,
            string variable,
            string code,
            List<RealContainerAcceptanceDiagnostic> diagnostics,
            TEnum defaultValue)
            where TEnum : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
            {
                return parsed;
            }

            diagnostics.Add(new RealContainerAcceptanceDiagnostic(
                code,
                variable,
                variable + " has unsupported value '" + value + "'."));
            return defaultValue;
        }

        private static string? NullIfWhiteSpace(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static AppleVirtualizationProviderOptions RealProviderOptions() =>
        new()
        {
            HelperTransportMode = AppleVirtualizationHelperTransportMode.StdIo,
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableRealVmBoot = true,
                EnableEngineControlPlane = true,
            },
            EngineBootstrap = new AppleVirtualizationEngineBootstrapOptions
            {
                Enabled = true,
                AuthorityModeConfigured = true,
            },
        };

    private static AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> SeedReadyHost(
        AppleVirtualizationProviderStateLedger ledger,
        string hostId,
        ProviderOpaqueHandle? providerHandle) =>
        ledger.UpsertRuntimeHost(
            AppleVirtualizationContractFixtures.Metadata<RuntimeHost>(hostId, "runtime-host"),
            new RuntimeHostStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                HostPhase = RuntimeHostPhase.Ready,
                ProviderHandle = providerHandle,
                Readiness = new RuntimeHostReadinessStatus(true),
                GuestControl = new GuestControlStatus(Expected: true, Installed: true, Reachable: true),
            });

    private static AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> SeedReadyUnit(
        AppleVirtualizationProviderStateLedger ledger,
        ResourceRef<RuntimeHost> host,
        string unitId) =>
        ledger.UpsertExecutionUnit(
            AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>(unitId, "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                UnitPhase = ExecutionUnitPhase.Ready,
                AssignedHost = host,
            });

    private sealed record RealContainerAcceptanceDiagnostic(string Code, string Variable, string Message);

    private sealed record RealContainerProvisioningGateSummary(
        bool Enabled,
        bool AllowPackageInstall,
        bool AllowServiceEnablement);

    private sealed record RealContainerReadinessSummary(
        bool Ready,
        IReadOnlyList<string> MissingVariables,
        IReadOnlyList<RealContainerInvalidInputSummary> InvalidInputs,
        IReadOnlyList<string> ValidatedPaths)
    {
        public static RealContainerReadinessSummary FromDiagnostics(
            IReadOnlyList<RealContainerAcceptanceDiagnostic> diagnostics)
        {
            string[] missing = diagnostics
                .Where(diagnostic => diagnostic.Code == "AppleVirtualization.RealContainerEnvMissing")
                .Select(diagnostic => diagnostic.Variable)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            RealContainerInvalidInputSummary[] invalid = diagnostics
                .Where(diagnostic => diagnostic.Code != "AppleVirtualization.RealContainerEnvMissing" &&
                    diagnostic.Code != "AppleVirtualization.RealContainerSmokeNotEnabled" &&
                    diagnostic.Code != "AppleVirtualization.RealContainerHostUnsupported")
                .Select(diagnostic => new RealContainerInvalidInputSummary(
                    diagnostic.Variable,
                    diagnostic.Code,
                    diagnostic.Message))
                .ToArray();
            string[] validatedPaths =
            [
                "HPD_APPLEVZ_REAL_HELPER_PATH",
                "HPD_APPLEVZ_GUEST_KERNEL",
                "HPD_APPLEVZ_GUEST_INITRD",
                "HPD_APPLEVZ_GUEST_DISK",
                "HPD_APPLEVZ_GUEST_SERIAL_LOG",
                "HPD_APPLEVZ_GUEST_BUNDLE_ROOT",
                "HPD_APPLEVZ_VIRTIOFS_HOST_PATH",
            ];

            return new RealContainerReadinessSummary(diagnostics.Count == 0, missing, invalid, validatedPaths);
        }
    }

    private sealed record RealContainerInvalidInputSummary(string Variable, string Code, string Message);

    private sealed class RealContainerAcceptanceRunEvidence
    {
        private readonly int _maxHelperEvents;
        private readonly int _maxSerialTailBytes;
        private readonly List<AppleVirtualizationHelperEnvelope> _helperEvents = [];
        private readonly List<RealContainerCleanupResult> _cleanupResults = [];

        public RealContainerAcceptanceRunEvidence(int maxHelperEvents, int maxSerialTailBytes)
        {
            _maxHelperEvents = Math.Max(0, maxHelperEvents);
            _maxSerialTailBytes = Math.Max(0, maxSerialTailBytes);
        }

        public IReadOnlyList<AppleVirtualizationHelperEnvelope> HelperEvents => _helperEvents;
        public bool HelperEventsTruncated { get; private set; }
        public IReadOnlyList<RealContainerCleanupResult> CleanupResults => _cleanupResults;
        public RevocationVerificationStatus? RevocationStatus { get; private set; }
        public RealContainerOutputSummary? OutputSummary { get; private set; }
        public byte[] SerialTailBytes { get; private set; } = [];
        public bool SerialTailTruncated { get; private set; }

        public void AddHelperEvent(AppleVirtualizationHelperEnvelope helperEvent)
        {
            if (_maxHelperEvents == 0)
            {
                HelperEventsTruncated = true;
                return;
            }

            if (_helperEvents.Count == _maxHelperEvents)
            {
                _helperEvents.RemoveAt(0);
                HelperEventsTruncated = true;
            }

            _helperEvents.Add(helperEvent);
        }

        public void AddCleanup(string operation, AppleVirtualizationHelperEnvelope? response) =>
            _cleanupResults.Add(new RealContainerCleanupResult(
                operation,
                response is not null && response.ResponseStatus != AppleVirtualizationHelperResponseStatus.Error,
                response?.Error?.Code));

        public void CaptureRevocation(AuthorityBindingStatus status) =>
            RevocationStatus = status.BoundAuthority?.RevocationStatus;

        public void CaptureSmokeResult(ProcessInvocationResult result) =>
            OutputSummary = new RealContainerOutputSummary(
                result.Output.Stdout.BytesCaptured,
                result.Output.Stderr.BytesCaptured,
                result.Output.Stdout.Truncated,
                result.Output.Stderr.Truncated,
                result.Output.Stdout.BytesDiscarded,
                result.Output.Stderr.BytesDiscarded);

        public void CaptureSerialTail(byte[] serialTail)
        {
            if (serialTail.Length <= _maxSerialTailBytes)
            {
                SerialTailBytes = serialTail;
                SerialTailTruncated = false;
                return;
            }

            SerialTailBytes = _maxSerialTailBytes == 0 ? [] : serialTail[^_maxSerialTailBytes..];
            SerialTailTruncated = true;
        }
    }

    private sealed record RealContainerCleanupResult(string Operation, bool Succeeded, string? ErrorCode);

    private sealed record RealContainerOutputSummary(
        long StdoutCapturedBytes,
        long StderrCapturedBytes,
        bool StdoutTruncated,
        bool StderrTruncated,
        long StdoutDiscardedBytes,
        long StderrDiscardedBytes);

    private sealed class RealContainerScratchDisk : IDisposable
    {
        private RealContainerScratchDisk(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static RealContainerScratchDisk Create(string baseDiskPath, string hostId)
        {
            string scratchRoot = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(baseDiskPath) ?? System.IO.Path.GetTempPath(),
                ".hpd-real-acceptance-scratch");
            Directory.CreateDirectory(scratchRoot);

            string scratchPath = System.IO.Path.Combine(scratchRoot, hostId + ".raw");
            File.Copy(baseDiskPath, scratchPath, overwrite: true);
            return new RealContainerScratchDisk(scratchPath);
        }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class RealHelperProcess : IAppleVirtualizationHelperClient, IAsyncDisposable
    {
        private readonly Process _process;
        private readonly SemaphoreSlim _outputReadGate = new(1, 1);
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);

        private RealHelperProcess(Process process)
        {
            _process = process;
        }

        public static async Task<RealHelperProcess> StartAsync(string helperPath)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(helperPath)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            process.Start().Should().BeTrue();
            await Task.Yield();
            return new RealHelperProcess(process);
        }

        public async ValueTask<AppleVirtualizationHelperEnvelope> SendAsync(
            AppleVirtualizationHelperEnvelope request,
            CancellationToken cancellationToken = default)
        {
            using var timeout = new CancellationTokenSource(_defaultTimeout);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            return await SendAsync(request, _defaultTimeout, linked.Token).ConfigureAwait(false);
        }

        public async Task<AppleVirtualizationHelperEnvelope> SendAsync(
            AppleVirtualizationHelperEnvelope envelope,
            TimeSpan timeout) =>
            await SendAsync(envelope, timeout, CancellationToken.None).ConfigureAwait(false);

        private async Task<AppleVirtualizationHelperEnvelope> SendAsync(
            AppleVirtualizationHelperEnvelope envelope,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
            string json = JsonSerializer.Serialize(
                envelope,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
            await _process.StandardInput.WriteLineAsync(json).WaitAsync(linked.Token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(linked.Token).ConfigureAwait(false);
            string? line;
            await _outputReadGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                line = await _process.StandardOutput.ReadLineAsync(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                _outputReadGate.Release();
            }

            if (line is null)
            {
                string stderr = await _process.StandardError.ReadToEndAsync(linked.Token).ConfigureAwait(false);
                throw new InvalidOperationException("hpd-vz exited before writing a response. stderr: " + stderr);
            }

            return JsonSerializer.Deserialize(
                line,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)
                ?? throw new JsonException("Swift helper response was not a helper envelope.");
        }

        public async IAsyncEnumerable<AppleVirtualizationHelperEnvelope> ReadEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                    using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        timeout.Token);
                    await _outputReadGate.WaitAsync(linked.Token).ConfigureAwait(false);
                    try
                    {
                        line = await _process.StandardOutput.ReadLineAsync(linked.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _outputReadGate.Release();
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                if (line is null)
                {
                    yield break;
                }

                yield return JsonSerializer.Deserialize(
                    line,
                    AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)
                    ?? throw new JsonException("Swift helper event was not a helper envelope.");
            }
        }

        public async Task<AppleVirtualizationHelperEnvelope?> TrySendAsync(AppleVirtualizationHelperEnvelope envelope, TimeSpan timeout)
        {
            try
            {
                return await SendAsync(envelope, timeout).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _process.StandardInput.Close();
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            finally
            {
                _outputReadGate.Dispose();
                _process.Dispose();
            }
        }
    }

    private sealed class RealContainerAcceptanceFiles : IDisposable
    {
        private readonly string _root;
        private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);

        private RealContainerAcceptanceFiles(string root)
        {
            _root = root;
            HelperPath = Path.Combine(root, "hpd-vz");
            KernelPath = Path.Combine(root, "vmlinuz");
            InitrdPath = Path.Combine(root, "initrd.img");
            DiskPath = Path.Combine(root, "root.raw");
            SerialLogPath = Path.Combine(root, "logs", "serial.log");
        }

        public string HelperPath { get; }
        public string Root => _root;
        public string KernelPath { get; }
        public string InitrdPath { get; }
        public string DiskPath { get; }
        public string SerialLogPath { get; }

        public static RealContainerAcceptanceFiles Create(
            bool createHelper = true,
            bool createKernel = true,
            bool createInitrd = true,
            bool createDisk = true,
            bool makeHelperExecutable = true,
            string socketLocus = "runtime-host",
            string? engineKind = null,
            string? engineApi = null,
            string? authorityMode = null,
            string? socketPath = null,
            string? image = null)
        {
            string root = Path.Combine(Path.GetTempPath(), "hpd-applevz-real-container-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var files = new RealContainerAcceptanceFiles(root);
            Directory.CreateDirectory(Path.GetDirectoryName(files.SerialLogPath)!);
            if (createHelper)
            {
                File.WriteAllText(files.HelperPath, "#!/bin/sh\nexit 0\n");
                if (makeHelperExecutable)
                {
                    MakeExecutable(files.HelperPath);
                }
            }

            if (createKernel)
            {
                File.WriteAllBytes(files.KernelPath, [0x48, 0x50, 0x44]);
            }

            if (createInitrd)
            {
                File.WriteAllBytes(files.InitrdPath, [0x48, 0x50, 0x44]);
            }

            if (createDisk)
            {
                File.WriteAllBytes(files.DiskPath, new byte[4096]);
            }

            files._environment["HPD_APPLEVZ_REAL_CONTAINER_SMOKE"] = "1";
            files._environment["HPD_APPLEVZ_REAL_HELPER_PATH"] = files.HelperPath;
            files._environment["HPD_APPLEVZ_GUEST_KERNEL"] = files.KernelPath;
            files._environment["HPD_APPLEVZ_GUEST_INITRD"] = files.InitrdPath;
            files._environment["HPD_APPLEVZ_GUEST_DISK"] = files.DiskPath;
            files._environment["HPD_APPLEVZ_GUEST_SERIAL_LOG"] = files.SerialLogPath;
            files._environment["HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION"] = "0.1.0";
            files._environment["HPD_APPLEVZ_CONTAINER_ENGINE_KIND"] = engineKind ?? EngineControlPlaneKind.DockerCompatible.ToString();
            files._environment["HPD_APPLEVZ_CONTAINER_ENGINE_API"] = engineApi ?? EngineApiKind.DockerCompatible.ToString();
            files._environment["HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE"] = authorityMode ?? EngineAuthorityMode.Rootless.ToString();
            files._environment["HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS"] = socketLocus;
            files._environment["HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH"] = socketPath ?? "/run/user/1000/docker.sock";
            files._environment["HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE"] = image ?? "hello-world:latest";
            return files;
        }

        public string? GetEnvironmentValue(string name) =>
            _environment.TryGetValue(name, out string? value) ? value : null;

        public void SetEnvironment(string name, string value) =>
            _environment[name] = value;

        public void RemoveEnvironment(params string[] names)
        {
            foreach (string name in names)
            {
                _environment.Remove(name);
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void MakeExecutable(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch (PlatformNotSupportedException)
            {
                using Process chmod = Process.Start("chmod", "+x " + path)!;
                chmod.WaitForExit();
            }
        }
    }
}
