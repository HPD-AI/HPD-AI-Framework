namespace HPD.Execution.AppleVirtualization.DevKit;

using HPD.Execution.Contracts;

public sealed record AppleVirtualizationImagePreparationRequest
{
    public required string OutputRoot { get; init; }
    public EngineControlPlaneKind EngineKind { get; init; } = EngineControlPlaneKind.DockerCompatible;
    public string? ImageUrl { get; init; }
    public string DiskSize { get; init; } = "16G";
    public int MemoryMegabytes { get; init; } = 4096;
    public int CpuCount { get; init; } = 4;
    public int TimeoutSeconds { get; init; } = 1200;
    public bool Force { get; init; }
    public bool NoRun { get; init; }
}

public sealed record AppleVirtualizationImagePreparationResult
{
    public required AppleVirtualizationDevKitProcessCommand Command { get; init; }
    public required AppleVirtualizationDevKitProcessResult ProcessResult { get; init; }
    public AppleVirtualizationRealAcceptanceEnvironment? Environment { get; init; }
    public AppleVirtualizationDevKitValidationResult? Validation { get; init; }
}

public sealed class AppleVirtualizationImagePreparation
{
    private readonly AppleVirtualizationDevKitPaths _paths;
    private readonly IAppleVirtualizationDevKitProcessRunner _runner;

    public AppleVirtualizationImagePreparation(
        AppleVirtualizationDevKitPaths paths,
        IAppleVirtualizationDevKitProcessRunner runner)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public AppleVirtualizationDevKitProcessCommand CreateCommand(AppleVirtualizationImagePreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputRoot);

        List<string> arguments =
        [
            "--output-root",
            request.OutputRoot,
            "--disk-size",
            request.DiskSize,
            "--memory",
            request.MemoryMegabytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--cpus",
            request.CpuCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--timeout",
            request.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ];

        if (!string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            arguments.Add("--image-url");
            arguments.Add(request.ImageUrl);
        }

        string engineArgument = request.EngineKind switch
        {
            EngineControlPlaneKind.DockerCompatible => "--install-docker",
            EngineControlPlaneKind.Containerd => "--install-containerd",
            EngineControlPlaneKind.Podman => "--install-podman",
            EngineControlPlaneKind.BuildKit => "--install-buildkit",
            _ => throw new NotSupportedException("Unsupported Apple VZ prepared-image engine: " + request.EngineKind),
        };
        arguments.Add(engineArgument);

        if (request.NoRun)
        {
            arguments.Add("--no-run");
        }

        if (request.Force)
        {
            arguments.Add("--force");
        }

        return new()
        {
            FileName = _paths.GuestImagePreparationScript,
            Arguments = arguments,
            WorkingDirectory = _paths.FrameworkRoot,
        };
    }

    public async ValueTask<AppleVirtualizationImagePreparationResult> PrepareAsync(
        AppleVirtualizationImagePreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        AppleVirtualizationDevKitProcessCommand command = CreateCommand(request);
        AppleVirtualizationDevKitProcessResult processResult =
            await _runner.RunAsync(command, cancellationToken).ConfigureAwait(false);

        AppleVirtualizationRealAcceptanceEnvironment? environment = null;
        AppleVirtualizationDevKitValidationResult? validation = null;
        string envPath = Path.Combine(request.OutputRoot, "hpd-applevz-real.env");
        if (processResult.Succeeded && File.Exists(envPath))
        {
            AppleVirtualizationRealAcceptanceEnvironmentLoadResult loaded =
                AppleVirtualizationRealAcceptanceEnvironment.Load(envPath);
            environment = loaded.Environment;
            validation = loaded.Environment is null
                ? loaded.Validation
                : AppleVirtualizationRealAcceptanceValidator.Validate(
                    loaded.Environment,
                    new AppleVirtualizationRealAcceptanceValidationOptions { CheckFileSystem = true });
        }

        return new()
        {
            Command = command,
            ProcessResult = processResult,
            Environment = environment,
            Validation = validation,
        };
    }
}
