namespace HPD.Execution.AppleVirtualization.DevKit;

using HPD.Execution.Contracts;

public sealed record AppleVirtualizationRealAcceptanceValidationOptions
{
    public bool CheckFileSystem { get; init; } = true;
}

public static class AppleVirtualizationRealAcceptanceValidator
{
    public static AppleVirtualizationDevKitValidationResult Validate(
        AppleVirtualizationRealAcceptanceEnvironment environment,
        AppleVirtualizationRealAcceptanceValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        options ??= new();

        List<AppleVirtualizationDevKitDiagnostic> diagnostics = [];

        RequireOptIn(environment, diagnostics);
        ValidateEngineContract(environment, diagnostics);
        ValidateSocketLocus(environment, diagnostics);
        ValidateAbsoluteSocket(environment, diagnostics);
        ValidateSmokeImage(environment, diagnostics);
        ValidateVirtiofsPair(environment, diagnostics);

        if (options.CheckFileSystem)
        {
            ValidateExistingFile(environment.HelperPath, "HPD_APPLEVZ_REAL_HELPER_PATH", diagnostics, executable: true);
            ValidateExistingFile(environment.GuestKernelPath, "HPD_APPLEVZ_GUEST_KERNEL", diagnostics);
            ValidateExistingFile(environment.GuestInitrdPath, "HPD_APPLEVZ_GUEST_INITRD", diagnostics);
            ValidateExistingFile(environment.GuestDiskPath, "HPD_APPLEVZ_GUEST_DISK", diagnostics);
            ValidateOptionalDirectory(environment.GuestBundleRoot, "HPD_APPLEVZ_GUEST_BUNDLE_ROOT", diagnostics);
            ValidateOptionalDirectory(environment.VirtiofsHostPath, "HPD_APPLEVZ_VIRTIOFS_HOST_PATH", diagnostics);
            ValidateSerialLogTarget(environment.GuestSerialLogPath, diagnostics);
        }

        return new AppleVirtualizationDevKitValidationResult { IsValid = diagnostics.Count == 0, Diagnostics = diagnostics };
    }

    public static string ExpectedSocketPath(EngineControlPlaneKind kind, EngineAuthorityMode authorityMode) =>
        kind switch
        {
            EngineControlPlaneKind.Containerd => "/run/containerd/containerd.sock",
            EngineControlPlaneKind.Podman when authorityMode == EngineAuthorityMode.Rootless => "/run/user/1000/podman/podman.sock",
            EngineControlPlaneKind.Podman when authorityMode == EngineAuthorityMode.Rootful => "/run/podman/podman.sock",
            EngineControlPlaneKind.BuildKit when authorityMode == EngineAuthorityMode.Rootless => "/run/user/1000/buildkit-default/buildkitd.sock",
            EngineControlPlaneKind.BuildKit when authorityMode == EngineAuthorityMode.Rootful => "/run/buildkit/buildkitd.sock",
            EngineControlPlaneKind.DockerCompatible when authorityMode == EngineAuthorityMode.Rootless => "/run/user/1000/docker.sock",
            EngineControlPlaneKind.DockerCompatible when authorityMode == EngineAuthorityMode.Rootful => "/var/run/docker.sock",
            _ => string.Empty
        };

    private static void RequireOptIn(
        AppleVirtualizationRealAcceptanceEnvironment environment,
        List<AppleVirtualizationDevKitDiagnostic> diagnostics)
    {
        if (!environment.Variables.TryGetValue("HPD_APPLEVZ_REAL_CONTAINER_SMOKE", out string? enabled) || enabled != "1")
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.RealContainerSmokeNotEnabled",
                "HPD_APPLEVZ_REAL_CONTAINER_SMOKE must be 1.",
                "HPD_APPLEVZ_REAL_CONTAINER_SMOKE"));
        }
    }

    private static void ValidateEngineContract(
        AppleVirtualizationRealAcceptanceEnvironment environment,
        List<AppleVirtualizationDevKitDiagnostic> diagnostics)
    {
        EngineApiKind expectedApi = environment.EngineKind switch
        {
            EngineControlPlaneKind.Containerd => EngineApiKind.ContainerdApi,
            EngineControlPlaneKind.Podman => EngineApiKind.PodmanApi,
            EngineControlPlaneKind.BuildKit => EngineApiKind.BuildKitApi,
            EngineControlPlaneKind.DockerCompatible => EngineApiKind.DockerCompatible,
            _ => EngineApiKind.ProviderDefined
        };

        if (expectedApi == EngineApiKind.ProviderDefined)
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.EngineKindUnsupported",
                $"Unsupported real acceptance engine kind: {environment.EngineKind}.",
                "HPD_APPLEVZ_CONTAINER_ENGINE_KIND"));
        }
        else if (environment.EngineApi != expectedApi)
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.EngineApiMismatch",
                $"{environment.EngineKind} requires {expectedApi}.",
                "HPD_APPLEVZ_CONTAINER_ENGINE_API"));
        }

        if (environment.EngineKind == EngineControlPlaneKind.Containerd &&
            environment.AuthorityMode != EngineAuthorityMode.Rootful)
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.ContainerdRequiresRootful",
                "Containerd real acceptance requires Rootful authority mode.",
                "HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE"));
        }

        string expectedSocket = ExpectedSocketPath(environment.EngineKind, environment.AuthorityMode);
        if (expectedSocket.Length == 0)
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.EngineAuthorityModeUnsupported",
                $"{environment.EngineKind} does not support {environment.AuthorityMode} real acceptance.",
                "HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE"));
        }
        else if (!string.Equals(environment.EngineSocketPath, expectedSocket, StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.EngineSocketMismatch",
                $"{environment.EngineKind} {environment.AuthorityMode} requires socket {expectedSocket}.",
                "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH",
                environment.EngineSocketPath));
        }
    }

    private static void ValidateSocketLocus(
        AppleVirtualizationRealAcceptanceEnvironment environment,
        List<AppleVirtualizationDevKitDiagnostic> diagnostics)
    {
        if (string.Equals(environment.EngineSocketLocus, "host", StringComparison.Ordinal) ||
            string.Equals(environment.EngineSocketLocus, "execution-unit", StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.EngineSocketLocusInvalid",
                "The engine socket must originate inside the runtime host/guest boundary.",
                "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS"));
        }
    }

    private static void ValidateAbsoluteSocket(
        AppleVirtualizationRealAcceptanceEnvironment environment,
        List<AppleVirtualizationDevKitDiagnostic> diagnostics)
    {
        if (environment.EngineSocketPath.Length == 0 || environment.EngineSocketPath[0] != '/')
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.EngineSocketPathNotAbsolute",
                "The engine socket must be an absolute guest-visible Unix socket path.",
                "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH",
                environment.EngineSocketPath));
        }

        foreach (char ch in environment.EngineSocketPath)
        {
            if (char.IsControl(ch))
            {
                diagnostics.Add(Error(
                    "AppleVirtualization.DevKit.EngineSocketPathControlCharacter",
                    "The engine socket path must not contain control characters.",
                    "HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH",
                    environment.EngineSocketPath));
                return;
            }
        }
    }

    private static void ValidateSmokeImage(
        AppleVirtualizationRealAcceptanceEnvironment environment,
        List<AppleVirtualizationDevKitDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(environment.SmokeImage))
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.SmokeImageMissing",
                "HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE must name a prepared or pullable image.",
                "HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE"));
        }
    }

    private static void ValidateVirtiofsPair(
        AppleVirtualizationRealAcceptanceEnvironment environment,
        List<AppleVirtualizationDevKitDiagnostic> diagnostics)
    {
        if (environment.VirtiofsHostPath is not null && environment.VirtiofsTag is null)
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.VirtiofsTagMissing",
                "HPD_APPLEVZ_VIRTIOFS_TAG is required when HPD_APPLEVZ_VIRTIOFS_HOST_PATH is set.",
                "HPD_APPLEVZ_VIRTIOFS_TAG"));
        }

        if (environment.VirtiofsTag is not null && environment.VirtiofsHostPath is null)
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.VirtiofsHostPathMissing",
                "HPD_APPLEVZ_VIRTIOFS_HOST_PATH is required when HPD_APPLEVZ_VIRTIOFS_TAG is set.",
                "HPD_APPLEVZ_VIRTIOFS_HOST_PATH"));
        }
    }

    private static void ValidateExistingFile(
        string path,
        string variable,
        List<AppleVirtualizationDevKitDiagnostic> diagnostics,
        bool executable = false)
    {
        if (!File.Exists(path))
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.FileMissing",
                $"{variable} does not point to an existing file.",
                variable,
                path));
            return;
        }

        if (executable && !IsExecutable(path))
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.FileNotExecutable",
                $"{variable} exists but is not executable.",
                variable,
                path));
        }
    }

    private static void ValidateOptionalDirectory(
        string? path,
        string variable,
        List<AppleVirtualizationDevKitDiagnostic> diagnostics)
    {
        if (path is not null && !Directory.Exists(path))
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.DirectoryMissing",
                $"{variable} does not point to an existing directory.",
                variable,
                path));
        }
    }

    private static void ValidateSerialLogTarget(
        string path,
        List<AppleVirtualizationDevKitDiagnostic> diagnostics)
    {
        if (Directory.Exists(path))
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.SerialLogIsDirectory",
                "HPD_APPLEVZ_GUEST_SERIAL_LOG must be a file path, not a directory.",
                "HPD_APPLEVZ_GUEST_SERIAL_LOG",
                path));
            return;
        }

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            diagnostics.Add(Error(
                "AppleVirtualization.DevKit.SerialLogParentMissing",
                "HPD_APPLEVZ_GUEST_SERIAL_LOG parent directory does not exist.",
                "HPD_APPLEVZ_GUEST_SERIAL_LOG",
                path));
        }
    }

    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase);
        }

        UnixFileMode mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }

    private static AppleVirtualizationDevKitDiagnostic Error(
        string code,
        string message,
        string? variable = null,
        string? path = null) =>
        AppleVirtualizationRealAcceptanceEnvironment.Error(code, message, variable, path);
}
