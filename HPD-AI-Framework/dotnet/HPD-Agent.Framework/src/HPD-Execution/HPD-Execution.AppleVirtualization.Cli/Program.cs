using HPD.Execution.AppleVirtualization.DevKit;
using HPD.Execution.Contracts;

return await AppleVirtualizationCli.RunAsync(args).ConfigureAwait(false);

internal static class AppleVirtualizationCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        CliArgs parsed = CliArgs.Parse(args.AsSpan(1));
        if (parsed.Help)
        {
            PrintUsage(command);
            return 0;
        }

        try
        {
            return command switch
            {
                "host" => Host(),
                "discover" => Discover(ResolvePaths(parsed), parsed),
                "validate" => Validate(parsed),
                "prepare" => await PrepareAsync(ResolvePaths(parsed), parsed).ConfigureAwait(false),
                "run" => await RunEnvAsync(ResolvePaths(parsed), parsed).ConfigureAwait(false),
                "matrix" => await RunMatrixAsync(ResolvePaths(parsed), parsed).ConfigureAwait(false),
                "cleanup" => Cleanup(parsed),
                _ => Unknown(command),
            };
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Host()
    {
        AppleVirtualizationHostPrerequisiteReport report = AppleVirtualizationHostPrerequisites.InspectCurrentHost();
        Console.WriteLine(report.CanRunAppleVirtualization
            ? "Apple Virtualization execution is available on this host."
            : "Apple Virtualization execution is not available on this host.");
        PrintDiagnostics(report.Diagnostics);
        return report.CanRunAppleVirtualization ? 0 : 1;
    }

    private static int Discover(AppleVirtualizationDevKitPaths paths, CliArgs args)
    {
        AppleVirtualizationRealAcceptanceMatrixPlan plan = AppleVirtualizationRealAcceptanceMatrix.CreatePlan(
            args.Get("--env-root") ?? paths.PreparedImageRoot,
            new AppleVirtualizationPreparedImageDiscoveryOptions { ValidateFileSystem = args.Has("--check-files") });

        PrintDiagnostics(plan.Diagnostics);
        foreach (AppleVirtualizationRealAcceptanceMatrixEntry entry in plan.Entries)
        {
            Console.WriteLine($"{(entry.CanRun ? "ok" : "invalid")} {entry.Name}");
            Console.WriteLine($"  env:    {entry.EnvFilePath}");
            Console.WriteLine($"  engine: {entry.Environment.EngineKind} / {entry.Environment.EngineApi} / {entry.Environment.AuthorityMode}");
            Console.WriteLine($"  socket: {entry.Environment.EngineSocketPath}");
            PrintDiagnostics(entry.Validation.Diagnostics, "  ");
        }

        return plan.Entries.Count == 0 || plan.Diagnostics.Any(IsError) || plan.Entries.Any(static entry => !entry.CanRun) ? 1 : 0;
    }

    private static int Validate(CliArgs args)
    {
        AppleVirtualizationRealAcceptanceEnvironment environment = LoadEnvironment(args.Required("--env-file"));
        AppleVirtualizationDevKitValidationResult validation = AppleVirtualizationRealAcceptanceValidator.Validate(
            environment,
            new AppleVirtualizationRealAcceptanceValidationOptions { CheckFileSystem = args.Has("--check-files") });

        PrintDiagnostics(validation.Diagnostics);
        Console.WriteLine(validation.IsValid ? "validation passed" : "validation failed");
        return validation.IsValid ? 0 : 1;
    }

    private static async Task<int> PrepareAsync(AppleVirtualizationDevKitPaths paths, CliArgs args)
    {
        string outputRoot = args.Required("--output-root");
        AppleVirtualizationImagePreparationRequest request = new()
        {
            OutputRoot = outputRoot,
            EngineKind = ParseEngine(args.Get("--engine") ?? "docker"),
            ImageUrl = args.Get("--image-url"),
            DiskSize = args.Get("--disk-size") ?? "16G",
            MemoryMegabytes = args.GetInt("--memory", 4096),
            CpuCount = args.GetInt("--cpus", 4),
            TimeoutSeconds = args.GetInt("--timeout", 1200),
            Force = args.Has("--force"),
            NoRun = args.Has("--no-run"),
        };

        var preparation = new AppleVirtualizationImagePreparation(paths, new AppleVirtualizationDevKitProcessRunner());
        AppleVirtualizationDevKitProcessCommand command = preparation.CreateCommand(request);
        if (args.Has("--dry-run"))
        {
            PrintCommand(command);
            return 0;
        }

        AppleVirtualizationImagePreparationResult result = await preparation.PrepareAsync(request).ConfigureAwait(false);
        PrintProcessResult(result.ProcessResult);
        if (result.Validation is not null)
        {
            PrintDiagnostics(result.Validation.Diagnostics);
        }

        return result.ProcessResult.Succeeded && (result.Validation is null || result.Validation.IsValid) ? 0 : 1;
    }

    private static async Task<int> RunEnvAsync(AppleVirtualizationDevKitPaths paths, CliArgs args)
    {
        AppleVirtualizationRealAcceptanceEnvironment environment = LoadEnvironment(args.Required("--env-file"));
        AppleVirtualizationRealAcceptanceRunOptions options = RunOptions(paths, args);
        var executor = new AppleVirtualizationRealAcceptanceExecutor(new AppleVirtualizationDevKitProcessRunner());
        if (args.Has("--dry-run"))
        {
            if (!options.SkipPrerequisites && !string.IsNullOrWhiteSpace(options.PrerequisiteCheckScript))
            {
                PrintCommand(executor.CreatePrerequisiteCommand(environment, options));
            }

            PrintCommand(executor.CreateTestCommand(environment, options));
            return 0;
        }

        AppleVirtualizationRealAcceptanceRunResult result = await executor.RunAsync(environment, options).ConfigureAwait(false);
        PrintDiagnostics(result.Validation.Diagnostics);
        if (result.PrerequisiteResult is not null)
        {
            PrintProcessResult(result.PrerequisiteResult);
        }

        if (result.TestResult is not null)
        {
            PrintProcessResult(result.TestResult);
        }

        Console.WriteLine(result.Succeeded ? "run passed" : "run failed");
        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> RunMatrixAsync(AppleVirtualizationDevKitPaths paths, CliArgs args)
    {
        AppleVirtualizationRealAcceptanceMatrixPlan plan = AppleVirtualizationRealAcceptanceMatrix.CreatePlan(
            args.Get("--env-root") ?? paths.PreparedImageRoot,
            new AppleVirtualizationPreparedImageDiscoveryOptions { ValidateFileSystem = true });
        PrintDiagnostics(plan.Diagnostics);

        if (args.Has("--dry-run"))
        {
            foreach (AppleVirtualizationRealAcceptanceMatrixEntry entry in plan.Entries)
            {
                Console.WriteLine($"{entry.Name}: {entry.Environment.EngineKind} / {entry.Environment.EngineApi} / {entry.Environment.AuthorityMode}");
                Console.WriteLine($"  {entry.EnvFilePath}");
            }

            return plan.HasRunnableEntries ? 0 : 1;
        }

        var executor = new AppleVirtualizationRealAcceptanceExecutor(new AppleVirtualizationDevKitProcessRunner());
        AppleVirtualizationRealAcceptanceMatrixRunResult result = await executor.RunMatrixAsync(
            plan,
            RunOptions(paths, args),
            keepGoing: args.Has("--keep-going")).ConfigureAwait(false);

        foreach (AppleVirtualizationRealAcceptanceRunResult run in result.Runs)
        {
            Console.WriteLine($"{(run.Succeeded ? "pass" : "fail")} {run.Environment.SourcePath}");
            if (run.PrerequisiteResult is not null && !run.PrerequisiteResult.Succeeded)
            {
                PrintProcessResult(run.PrerequisiteResult);
            }

            if (run.TestResult is not null && !run.TestResult.Succeeded)
            {
                PrintProcessResult(run.TestResult);
            }
        }

        Console.WriteLine($"matrix complete: {result.Passed} passed, {result.Failed} failed");
        return result.Succeeded ? 0 : 1;
    }

    private static int Cleanup(CliArgs args)
    {
        AppleVirtualizationRealAcceptanceEnvironment environment = LoadEnvironment(args.Required("--env-file"));
        AppleVirtualizationCleanupPlan plan = AppleVirtualizationCleanupPlanner.CreatePlan(environment);
        foreach (AppleVirtualizationCleanupTarget target in plan.Targets)
        {
            Console.WriteLine($"{(target.Exists ? "exists" : "missing")} {target.Kind}: {target.Path}");
        }

        if (args.Has("--dry-run"))
        {
            return 0;
        }

        AppleVirtualizationCleanupResult result = AppleVirtualizationCleanupExecutor.Execute(plan);
        PrintDiagnostics(result.Diagnostics);
        Console.WriteLine(result.Succeeded ? "cleanup complete" : "cleanup failed");
        return result.Succeeded ? 0 : 1;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine("unknown command: " + command);
        PrintUsage();
        return 2;
    }

    private static AppleVirtualizationRealAcceptanceRunOptions RunOptions(AppleVirtualizationDevKitPaths paths, CliArgs args) =>
        new()
        {
            TestProjectPath = args.Get("--test-project") ?? paths.RealAcceptanceTestProject,
            PrerequisiteCheckScript = args.Get("--prereq-script") ?? paths.PrerequisiteCheckScript,
            TargetFramework = args.Get("--framework") ?? "net10.0",
            Configuration = args.Get("--configuration"),
            SkipPrerequisites = args.Has("--skip-prereqs"),
            PreserveSerialLog = args.Has("--preserve-serial-log"),
        };

    private static AppleVirtualizationDevKitPaths ResolvePaths(CliArgs args)
    {
        string frameworkRoot = args.Get("--framework-root") ?? FindFrameworkRoot(Environment.CurrentDirectory);
        return AppleVirtualizationDevKitPaths.FromFrameworkRoot(frameworkRoot, args.Get("--env-root"));
    }

    private static string FindFrameworkRoot(string start)
    {
        DirectoryInfo? directory = new(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HPD-Agent.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new CliException("could not find HPD-Agent.slnx; pass --framework-root", 2);
    }

    private static AppleVirtualizationRealAcceptanceEnvironment LoadEnvironment(string envFile)
    {
        AppleVirtualizationRealAcceptanceEnvironmentLoadResult loaded =
            AppleVirtualizationRealAcceptanceEnvironment.Load(envFile);
        if (loaded.Environment is not null)
        {
            return loaded.Environment;
        }

        PrintDiagnostics(loaded.Validation.Diagnostics);
        throw new CliException("failed to load env file: " + envFile, 1);
    }

    private static EngineControlPlaneKind ParseEngine(string value) =>
        value.ToLowerInvariant() switch
        {
            "docker" or "dockercompatible" => EngineControlPlaneKind.DockerCompatible,
            "containerd" => EngineControlPlaneKind.Containerd,
            "podman" => EngineControlPlaneKind.Podman,
            "buildkit" => EngineControlPlaneKind.BuildKit,
            _ => throw new CliException("unsupported engine: " + value, 2),
        };

    private static void PrintDiagnostics(IReadOnlyList<AppleVirtualizationDevKitDiagnostic> diagnostics, string prefix = "")
    {
        foreach (AppleVirtualizationDevKitDiagnostic diagnostic in diagnostics)
        {
            Console.Error.WriteLine($"{prefix}{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}");
            if (!string.IsNullOrWhiteSpace(diagnostic.Path))
            {
                Console.Error.WriteLine($"{prefix}  path: {diagnostic.Path}");
            }
        }
    }

    private static void PrintCommand(AppleVirtualizationDevKitProcessCommand command)
    {
        Console.WriteLine(command.FileName);
        foreach (string argument in command.Arguments)
        {
            Console.WriteLine("  " + argument);
        }
    }

    private static void PrintProcessResult(AppleVirtualizationDevKitProcessResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            Console.Write(result.StandardOutput);
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            Console.Error.Write(result.StandardError);
        }
    }

    private static bool IsHelp(string arg) => arg is "-h" or "--help" or "help";

    private static bool IsError(AppleVirtualizationDevKitDiagnostic diagnostic) =>
        diagnostic.Severity == AppleVirtualizationDevKitDiagnosticSeverity.Error;

    private static void PrintUsage(string? command = null)
    {
        Console.WriteLine(command is null ? "hpd-applevz <command> [options]" : "hpd-applevz " + command + " [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  host");
        Console.WriteLine("  discover [--env-root PATH] [--check-files]");
        Console.WriteLine("  validate --env-file PATH [--check-files]");
        Console.WriteLine("  prepare --output-root PATH --engine docker|containerd|podman|buildkit [--force] [--dry-run]");
        Console.WriteLine("  run --env-file PATH [--dry-run] [--skip-prereqs]");
        Console.WriteLine("  matrix [--env-root PATH] [--keep-going] [--dry-run]");
        Console.WriteLine("  cleanup --env-file PATH [--dry-run]");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --framework-root PATH");
    }
}

internal sealed class CliArgs
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

    public bool Help { get; private set; }

    public static CliArgs Parse(ReadOnlySpan<string> args)
    {
        CliArgs parsed = new();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "-h" or "--help")
            {
                parsed.Help = true;
                continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new CliException("unexpected argument: " + arg, 2);
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parsed._values[arg] = args[++i];
            }
            else
            {
                parsed._values[arg] = null;
            }
        }

        return parsed;
    }

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Get(string name) => _values.TryGetValue(name, out string? value) ? value : null;

    public string Required(string name) =>
        Get(name) is { Length: > 0 } value
            ? value
            : throw new CliException("missing required option: " + name, 2);

    public int GetInt(string name, int defaultValue)
    {
        string? value = Get(name);
        if (value is null)
        {
            return defaultValue;
        }

        return int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new CliException(name + " must be an integer", 2);
    }
}

internal sealed class CliException : Exception
{
    public CliException(string message, int exitCode)
        : base(message) =>
        ExitCode = exitCode;

    public int ExitCode { get; }
}
