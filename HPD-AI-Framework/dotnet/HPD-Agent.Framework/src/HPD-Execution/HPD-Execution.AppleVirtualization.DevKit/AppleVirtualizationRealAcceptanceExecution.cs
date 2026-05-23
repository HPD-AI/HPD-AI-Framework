namespace HPD.Execution.AppleVirtualization.DevKit;

public sealed record AppleVirtualizationRealAcceptanceRunOptions
{
    public required string TestProjectPath { get; init; }
    public string TestFilter { get; init; } =
        "FullyQualifiedName~Real_container_smoke_acceptance_observes_real_engine_status_only_with_explicit_env";
    public string TargetFramework { get; init; } = "net10.0";
    public string? Configuration { get; init; }
    public string? PrerequisiteCheckScript { get; init; }
    public bool SkipPrerequisites { get; init; }
    public bool PreserveSerialLog { get; init; }
}

public sealed record AppleVirtualizationRealAcceptanceRunResult
{
    public required AppleVirtualizationRealAcceptanceEnvironment Environment { get; init; }
    public required AppleVirtualizationDevKitValidationResult Validation { get; init; }
    public AppleVirtualizationDevKitProcessCommand? PrerequisiteCommand { get; init; }
    public AppleVirtualizationDevKitProcessResult? PrerequisiteResult { get; init; }
    public AppleVirtualizationDevKitProcessCommand? TestCommand { get; init; }
    public AppleVirtualizationDevKitProcessResult? TestResult { get; init; }
    public bool Succeeded => Validation.IsValid &&
        (PrerequisiteResult is null || PrerequisiteResult.Succeeded) &&
        TestResult?.Succeeded == true;
}

public sealed record AppleVirtualizationRealAcceptanceMatrixRunResult
{
    public IReadOnlyList<AppleVirtualizationRealAcceptanceRunResult> Runs { get; init; } =
        Array.Empty<AppleVirtualizationRealAcceptanceRunResult>();
    public IReadOnlyList<AppleVirtualizationDevKitDiagnostic> Diagnostics { get; init; } =
        Array.Empty<AppleVirtualizationDevKitDiagnostic>();
    public int Passed => Runs.Count(static run => run.Succeeded);
    public int Failed => Runs.Count(static run => !run.Succeeded) + Diagnostics.Count(static diagnostic => diagnostic.Severity == AppleVirtualizationDevKitDiagnosticSeverity.Error);
    public bool Succeeded => Failed == 0;
}

public sealed class AppleVirtualizationRealAcceptanceExecutor
{
    private readonly IAppleVirtualizationDevKitProcessRunner _runner;

    public AppleVirtualizationRealAcceptanceExecutor(IAppleVirtualizationDevKitProcessRunner runner) =>
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    public static AppleVirtualizationRealAcceptanceRunOptions CreateDefaultOptions(AppleVirtualizationDevKitPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new()
        {
            TestProjectPath = paths.RealAcceptanceTestProject,
            PrerequisiteCheckScript = paths.PrerequisiteCheckScript,
        };
    }

    public AppleVirtualizationDevKitProcessCommand CreatePrerequisiteCommand(
        AppleVirtualizationRealAcceptanceEnvironment environment,
        AppleVirtualizationRealAcceptanceRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PrerequisiteCheckScript);
        return new()
        {
            FileName = options.PrerequisiteCheckScript,
            Arguments = [environment.SourcePath],
        };
    }

    public AppleVirtualizationDevKitProcessCommand CreateTestCommand(
        AppleVirtualizationRealAcceptanceEnvironment environment,
        AppleVirtualizationRealAcceptanceRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TestProjectPath);

        List<string> arguments =
        [
            "test",
            options.TestProjectPath,
            "-f",
            options.TargetFramework,
            "--filter",
            options.TestFilter,
            "-v",
            "minimal",
        ];

        if (!string.IsNullOrWhiteSpace(options.Configuration))
        {
            arguments.Add("-c");
            arguments.Add(options.Configuration);
        }

        return new()
        {
            FileName = "dotnet",
            Arguments = arguments,
            Environment = ToProcessEnvironment(environment),
        };
    }

    public async ValueTask<AppleVirtualizationRealAcceptanceRunResult> RunAsync(
        AppleVirtualizationRealAcceptanceEnvironment environment,
        AppleVirtualizationRealAcceptanceRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        AppleVirtualizationDevKitValidationResult validation =
            AppleVirtualizationRealAcceptanceValidator.Validate(
                environment,
                new AppleVirtualizationRealAcceptanceValidationOptions { CheckFileSystem = true });
        if (!validation.IsValid)
        {
            return new() { Environment = environment, Validation = validation };
        }

        AppleVirtualizationDevKitProcessCommand? prerequisiteCommand = null;
        AppleVirtualizationDevKitProcessResult? prerequisiteResult = null;
        if (!options.SkipPrerequisites && !string.IsNullOrWhiteSpace(options.PrerequisiteCheckScript))
        {
            prerequisiteCommand = CreatePrerequisiteCommand(environment, options);
            prerequisiteResult = await _runner.RunAsync(prerequisiteCommand, cancellationToken).ConfigureAwait(false);
            if (!prerequisiteResult.Succeeded)
            {
                return new()
                {
                    Environment = environment,
                    Validation = validation,
                    PrerequisiteCommand = prerequisiteCommand,
                    PrerequisiteResult = prerequisiteResult,
                };
            }
        }

        if (!options.PreserveSerialLog && !string.IsNullOrWhiteSpace(environment.GuestSerialLogPath))
        {
            File.Delete(environment.GuestSerialLogPath);
        }

        AppleVirtualizationDevKitProcessCommand testCommand = CreateTestCommand(environment, options);
        AppleVirtualizationDevKitProcessResult testResult =
            await _runner.RunAsync(testCommand, cancellationToken).ConfigureAwait(false);

        return new()
        {
            Environment = environment,
            Validation = validation,
            PrerequisiteCommand = prerequisiteCommand,
            PrerequisiteResult = prerequisiteResult,
            TestCommand = testCommand,
            TestResult = testResult,
        };
    }

    public async ValueTask<AppleVirtualizationRealAcceptanceMatrixRunResult> RunMatrixAsync(
        AppleVirtualizationRealAcceptanceMatrixPlan plan,
        AppleVirtualizationRealAcceptanceRunOptions options,
        bool keepGoing = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        List<AppleVirtualizationRealAcceptanceRunResult> runs = [];
        foreach (AppleVirtualizationRealAcceptanceMatrixEntry entry in plan.Entries)
        {
            AppleVirtualizationRealAcceptanceRunResult run =
                await RunAsync(entry.Environment, options, cancellationToken).ConfigureAwait(false);
            runs.Add(run);
            if (!run.Succeeded && !keepGoing)
            {
                break;
            }
        }

        return new()
        {
            Runs = runs,
            Diagnostics = plan.Diagnostics,
        };
    }

    private static IReadOnlyDictionary<string, string?> ToProcessEnvironment(
        AppleVirtualizationRealAcceptanceEnvironment environment)
    {
        Dictionary<string, string?> variables = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> item in environment.Variables)
        {
            variables[item.Key] = item.Value;
        }

        return variables;
    }
}
