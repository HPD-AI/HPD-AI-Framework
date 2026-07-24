using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding;
using HPD.Environment.Contracts;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>Shared stateless services used by trusted execution planners.</summary>
internal sealed class DebugExecutionPlannerServices(
    DebugAdapterSelector selector,
    DebugAdapterCatalog catalog,
    IDebugAdapterConfigurationComposer configurationComposer,
    IDebugAdapterTrustPolicy trustPolicy)
{
    public async ValueTask<(
        DebugAdapterDescriptor Descriptor,
        IDebugAdapterFactory Factory,
        DebugAdapterResolutionContext Resolution)> SelectAsync(
        DebugExecutionPlanningContext context,
        DebugAdapterSelectionOperation operation,
        DebugTargetKind targetKind,
        string? runtimeLanguage,
        string? fileExtension,
        CancellationToken cancellationToken)
    {
        ValidateExplicit(context.ExplicitAdapterId, targetKind);
        var resolution = Resolution(context, operation == DebugAdapterSelectionOperation.Attach
            ? "debug.adapter.attach"
            : "debug.adapter.launch");
        var selection = await selector.SelectAsync(new DebugAdapterSelectionContext
        {
            Operation = operation,
            ExplicitAdapterId = context.ExplicitAdapterId,
            Language = context.LanguageHint,
            RuntimeLanguageHint = runtimeLanguage,
            FileExtension = fileExtension,
            TargetKind = targetKind,
            MatchedRootMarkers = context.Evidence.MatchedMarkers,
            ProjectMarkerFingerprint = context.Evidence.Fingerprint,
            Resolution = resolution
        }, cancellationToken).ConfigureAwait(false);
        var (descriptor, factory) = selection.Kind switch
        {
            DebugAdapterSelectionKind.Available
                when selection.Entry is not null && selection.Factory is not null =>
                (selection.Entry.Descriptor, selection.Factory),
            DebugAdapterSelectionKind.NoMatch =>
                throw new DebugStartPlanningException(
                    "adapter_not_found",
                    "No trusted debug adapter matches the target."),
            DebugAdapterSelectionKind.Unavailable =>
                throw new DebugStartPlanningException(
                    "adapter_unavailable",
                    "Matching debug adapters are unavailable."),
            DebugAdapterSelectionKind.Ambiguous =>
                throw new DebugStartPlanningException(
                    "adapter_ambiguous",
                    "Multiple debug adapters match the target."),
            _ => throw new DebugStartPlanningException(
                "adapter_unavailable",
                "Debug adapter selection failed.")
        };
        return (descriptor, factory, resolution with
        {
            TrustDecision = trustPolicy.Evaluate(descriptor)
        });
    }

    public IDebugAdapterConfigurationComposer ConfigurationComposer => configurationComposer;

    private DebugAdapterResolutionContext Resolution(
        DebugExecutionPlanningContext context,
        string authorizationScope)
    {
        var process = context.Runtime.ProcessExecution ??
            throw new InvalidOperationException(
                "Debug execution planning requires a process execution binding.");
        return new DebugAdapterResolutionContext
        {
            WorkspaceRoot = context.CanonicalWorkspacePath,
            EnvironmentId = process.EnvironmentId,
            EnvironmentRevision = process.EnvironmentRevision,
            TargetPlatform = RuntimeInformation.RuntimeIdentifier,
            PolicyRevision = 0,
            EndpointCatalogRevision = 0,
            AuthorizationScope = authorizationScope,
            FilteredEnvironment = ImmutableDictionary<string, string?>.Empty,
            ProcessExecution = process,
            ProcessSandbox = context.Runtime.ProcessSandbox,
            TrustDecision = new DebugAdapterTrustDecision
            {
                TrustLevel = DebugAdapterTrustLevel.Denied,
                PolicyRevision = "unresolved",
                ReasonCode = "SELECTION_PENDING"
            }
        };
    }

    private void ValidateExplicit(string? adapterId, DebugTargetKind targetKind)
    {
        if (string.IsNullOrWhiteSpace(adapterId))
            return;
        if (!catalog.TryGet(adapterId, out var entry))
            throw new DebugStartPlanningException(
                "adapter_not_registered",
                "The requested debug adapter is not registered.");
        if ((entry.Descriptor.TargetKinds & targetKind) == 0)
            throw new DebugStartPlanningException(
                "adapter_incompatible_with_target",
                "The requested debug adapter is incompatible with the target.");
        if (trustPolicy.Evaluate(entry.Descriptor).TrustLevel != DebugAdapterTrustLevel.Trusted)
            throw new DebugStartPlanningException(
                "permission_denied",
                "The requested debug adapter is not trusted by current policy.");
    }
}

/// <summary>Plans direct source-file execution for adapters that support it.</summary>
internal sealed class DirectSourceDebugExecutionTargetPlanner(
    DebugExecutionPlannerServices services) : IDebugExecutionTargetPlanner
{
    public string Id => "direct-source";
    public int Priority => 100;

    public async ValueTask<DebugExecutionPlanningResult> EvaluateAsync(
        DebugExecutionPlanningContext context,
        CancellationToken cancellationToken)
    {
        if (context.Target is not SourceFileDebugTarget target)
            return DebugExecutionPlanningResult.NotApplicable;
        if (new[] { ".cs", ".csx", ".fs", ".fsx", ".vb" }.Contains(
                Path.GetExtension(context.CanonicalTargetPath),
                StringComparer.OrdinalIgnoreCase))
            throw new DebugStartPlanningException(
                "source_target_requires_project",
                "A .NET source file requires an application project or executable target.");
        DebugInputBounds.ValidateArguments(target.Arguments);
        var selected = await services.SelectAsync(
            context,
            DebugAdapterSelectionOperation.Launch,
            DebugTargetKind.SourceFile,
            runtimeLanguage: null,
            Path.GetExtension(context.CanonicalTargetPath),
            cancellationToken).ConfigureAwait(false);
        var adapter = await selected.Factory.CreateSemanticLaunchPlanAsync(
            services.ConfigurationComposer,
            selected.Descriptor,
            selected.Resolution,
            new DebugSemanticLaunchConfiguration(
                context.CanonicalTargetPath,
                context.CanonicalWorkspacePath,
                DebugTargetKind.SourceFile,
                DebugAdapterProgramKind.SourceFile,
                target.Arguments,
                context.Operation.StopOnEntry),
            cancellationToken).ConfigureAwait(false);
        return DebugExecutionPlanningResult.Applicable(new DirectAdapterDebugExecutionPlan
        {
            PlannerId = Id,
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            EnvironmentId = adapter.EnvironmentId,
            EnvironmentRevision = adapter.EnvironmentRevision,
            CanonicalWorkingDirectory = context.CanonicalWorkspacePath,
            InitialConfiguration = DebugInitialConfigurationMapper.Map(
                context.Operation.InitialConfiguration,
                context.Operation.StopOnEntry,
                context.Workspace),
            Adapter = adapter
        });
    }
}

/// <summary>Plans direct execution of an already-built artifact.</summary>
internal sealed class DirectExecutableDebugExecutionTargetPlanner(
    DebugExecutionPlannerServices services,
    IDebugExecutableArtifactClassifier artifactClassifier) : IDebugExecutionTargetPlanner
{
    public string Id => "direct-executable";
    public int Priority => 100;

    public async ValueTask<DebugExecutionPlanningResult> EvaluateAsync(
        DebugExecutionPlanningContext context,
        CancellationToken cancellationToken)
    {
        if (context.Target is not ExecutableDebugTarget target)
            return DebugExecutionPlanningResult.NotApplicable;
        DebugInputBounds.ValidateArguments(target.Arguments);
        if (!File.Exists(context.CanonicalTargetPath))
            throw new DebugStartPlanningException(
                "debug_artifact_not_found",
                "The requested executable artifact does not exist.");
        var artifact = await artifactClassifier.ClassifyAsync(
            context, cancellationToken).ConfigureAwait(false);
        if (artifact.Kind == DebugExecutableArtifactKind.Test)
            throw new DebugStartPlanningException(
                "debug_test_artifact_requires_test_target",
                "The selected artifact is produced by a test project. Use targetKind=\"test\".");
        var language = InferRuntime(context);
        var selected = await services.SelectAsync(
            context,
            DebugAdapterSelectionOperation.Launch,
            DebugTargetKind.Executable,
            language,
            fileExtension: null,
            cancellationToken).ConfigureAwait(false);
        var adapter = await selected.Factory.CreateSemanticLaunchPlanAsync(
            services.ConfigurationComposer,
            selected.Descriptor,
            selected.Resolution,
            new DebugSemanticLaunchConfiguration(
                context.CanonicalTargetPath,
                context.CanonicalWorkspacePath,
                DebugTargetKind.Executable,
                DebugAdapterProgramKind.ExecutableFile,
                target.Arguments,
                context.Operation.StopOnEntry),
            cancellationToken).ConfigureAwait(false);
        return DebugExecutionPlanningResult.Applicable(new DirectAdapterDebugExecutionPlan
        {
            PlannerId = Id,
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            EnvironmentId = adapter.EnvironmentId,
            EnvironmentRevision = adapter.EnvironmentRevision,
            CanonicalWorkingDirectory = context.CanonicalWorkspacePath,
            InitialConfiguration = DebugInitialConfigurationMapper.Map(
                context.Operation.InitialConfiguration,
                context.Operation.StopOnEntry,
                context.Workspace),
            Adapter = adapter
        });
    }

    private static string? InferRuntime(DebugExecutionPlanningContext context)
        => context.Evidence.MatchedPaths.Any(path =>
               path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
            ? "fsharp"
            : context.Evidence.MatchedPaths.Any(path =>
                  path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) ||
              context.CanonicalTargetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? "csharp"
                : null;
}

/// <summary>Plans direct .NET application-project execution from evaluated output metadata.</summary>
internal sealed class DotNetApplicationDebugExecutionTargetPlanner(
    DebugExecutionPlannerServices services,
    IDotNetDebugProjectEvaluator evaluator) : IDebugExecutionTargetPlanner
{
    public string Id => "dotnet-application";
    public int Priority => 200;

    public async ValueTask<DebugExecutionPlanningResult> EvaluateAsync(
        DebugExecutionPlanningContext context,
        CancellationToken cancellationToken)
    {
        if (context.Target is not ApplicationProjectDebugTarget target)
            return DebugExecutionPlanningResult.NotApplicable;
        DebugInputBounds.ValidateArguments(target.Arguments);
        var project = DotNetProjectSelection.Select(context, target.ProjectPath);
        var evaluation = await evaluator.EvaluateAsync(new()
        {
            CanonicalProjectPath = project,
            Configuration = target.Configuration,
            TargetFramework = target.TargetFramework,
            ProcessExecution = context.Runtime.ProcessExecution!,
            ProcessSandbox = context.Runtime.ProcessSandbox,
            Workspace = context.Workspace
        }, cancellationToken).ConfigureAwait(false);
        switch (evaluation.ProjectKind)
        {
            case DotNetDebugProjectKind.Test:
                throw new DebugStartPlanningException(
                    "debug_test_target_requires_test_target",
                    "The selected project is a test project. Use targetKind=\"test\".");
            case DotNetDebugProjectKind.Library:
                throw new DebugStartPlanningException(
                    "debug_library_requires_host",
                    "The selected library requires a semantic host.");
            case not DotNetDebugProjectKind.Application:
                throw new DebugStartPlanningException(
                    "debug_project_execution_shape_unknown",
                    "The project execution shape could not be proven.");
        }
        if (!evaluation.ArtifactIsCurrent)
            throw new DebugStartPlanningException(
                "debug_build_required",
                "The exact evaluated application artifact is missing or stale.");
        var selected = await services.SelectAsync(
            context,
            DebugAdapterSelectionOperation.Launch,
            DebugTargetKind.Executable,
            evaluation.ProjectPath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                ? "fsharp"
                : "csharp",
            fileExtension: null,
            cancellationToken).ConfigureAwait(false);
        var adapter = await selected.Factory.CreateSemanticLaunchPlanAsync(
            services.ConfigurationComposer,
            selected.Descriptor,
            selected.Resolution,
            new DebugSemanticLaunchConfiguration(
                evaluation.TargetPath,
                Path.GetDirectoryName(project)!,
                DebugTargetKind.Executable,
                DebugAdapterProgramKind.ExecutableFile,
                target.Arguments,
                context.Operation.StopOnEntry),
            cancellationToken).ConfigureAwait(false);
        return DebugExecutionPlanningResult.Applicable(new DirectAdapterDebugExecutionPlan
        {
            PlannerId = Id,
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            EnvironmentId = adapter.EnvironmentId,
            EnvironmentRevision = adapter.EnvironmentRevision,
            CanonicalWorkingDirectory = Path.GetDirectoryName(project)!,
            InitialConfiguration = DebugInitialConfigurationMapper.Map(
                context.Operation.InitialConfiguration,
                context.Operation.StopOnEntry,
                context.Workspace),
            ProjectEvaluation = DotNetEvaluationMetadata.Project(evaluation),
            Adapter = adapter
        });
    }
}

/// <summary>Plans .NET test execution through the evaluated test platform.</summary>
internal sealed class DotNetTestDebugExecutionTargetPlanner(
    DebugExecutionPlannerServices services,
    IDotNetDebugProjectEvaluator evaluator) : IDebugExecutionTargetPlanner
{
    public string Id => "dotnet-test";
    public int Priority => 200;

    public async ValueTask<DebugExecutionPlanningResult> EvaluateAsync(
        DebugExecutionPlanningContext context,
        CancellationToken cancellationToken)
    {
        if (context.Target is not TestDebugTarget target ||
            target.Framework is not (DebugTestFramework.Auto or DebugTestFramework.DotNet))
            return DebugExecutionPlanningResult.NotApplicable;
        DebugInputBounds.ValidateFilter(target.Filter);
        var project = DotNetProjectSelection.Select(context, target.ProjectPath);
        var evaluation = await evaluator.EvaluateAsync(new()
        {
            CanonicalProjectPath = project,
            Configuration = target.Configuration,
            TargetFramework = target.TargetFramework,
            ProcessExecution = context.Runtime.ProcessExecution!,
            ProcessSandbox = context.Runtime.ProcessSandbox,
            Workspace = context.Workspace
        }, cancellationToken).ConfigureAwait(false);
        if (evaluation.ProjectKind != DotNetDebugProjectKind.Test)
            throw new DebugStartPlanningException(
                evaluation.ProjectKind == DotNetDebugProjectKind.Library
                    ? "debug_library_requires_host"
                    : "debug_test_project_required",
                "The selected target is not a supported .NET test project.");
        if (!evaluation.ArtifactIsCurrent)
            throw new DebugStartPlanningException(
                "debug_build_required",
                "The exact evaluated test output is missing or stale.");

        return evaluation.TestPlatform switch
        {
            DotNetTestPlatformKind.VSTest =>
                await HostedVSTestAsync(context, target, project, evaluation, cancellationToken)
                    .ConfigureAwait(false),
            DotNetTestPlatformKind.MicrosoftTestingPlatform when evaluation.IsDirectlyExecutable =>
                await DirectMtpAsync(context, target, project, evaluation, cancellationToken)
                    .ConfigureAwait(false),
            _ => throw new DebugStartPlanningException(
                "debug_test_platform_unsupported",
                "The evaluated .NET test platform has no qualified execution strategy.")
        };
    }

    private async ValueTask<DebugExecutionPlanningResult> HostedVSTestAsync(
        DebugExecutionPlanningContext context,
        TestDebugTarget target,
        string project,
        DotNetDebugProjectEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        var selected = await services.SelectAsync(
            context,
            DebugAdapterSelectionOperation.Attach,
            DebugTargetKind.Process,
            "csharp",
            fileExtension: null,
            cancellationToken).ConfigureAwait(false);
        var arguments = new List<string>
        {
            "test",
            project,
            "--no-build",
            "--no-restore",
            "--framework",
            evaluation.SelectedTargetFramework
        };
        if (!string.IsNullOrWhiteSpace(target.Filter))
        {
            arguments.Add("--filter");
            arguments.Add(target.Filter);
        }
        var process = context.Runtime.ProcessExecution!;
        return DebugExecutionPlanningResult.Applicable(new HostedAttachDebugExecutionPlan
        {
            PlannerId = "dotnet-vstest",
            SemanticStartKind = DebugSemanticStartKind.HostedLaunchAttach,
            EnvironmentId = process.EnvironmentId,
            EnvironmentRevision = process.EnvironmentRevision,
            CanonicalWorkingDirectory = Path.GetDirectoryName(project)!,
            InitialConfiguration = DebugInitialConfigurationMapper.Map(
                context.Operation.InitialConfiguration,
                context.Operation.StopOnEntry,
                context.Workspace),
            ProjectEvaluation = DotNetEvaluationMetadata.Project(evaluation),
            Host = new DebugHostProcessPlan
            {
                Role = "dotnet-test-runner",
                Invocation = new ProcessInvocationSpec
                {
                    Target = process.ExecutionTarget,
                    Role = ProcessRole.Task,
                    Command = new ProcessCommandSpec
                    {
                        FileName = "dotnet",
                        Arguments = arguments,
                        WorkingDirectory = Path.GetDirectoryName(project),
                        Environment = new Dictionary<string, string?>
                        {
                            ["VSTEST_HOST_DEBUG"] = "1",
                            ["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
                        }
                    },
                    Policy = ProcessInvocationPolicy.Default with
                    {
                        AllowBackground = true,
                        StopProcessTree = true,
                        StopOnRunCancellation = true
                    },
                    Isolation = ProcessIsolationPolicy.Default with
                    {
                        // The debugger must attach across the host boundary and
                        // VSTest must retain its dynamically allocated local IPC
                        // channel. Current process-isolation contracts cannot
                        // express a per-PID debugger grant plus an exact dynamic
                        // loopback endpoint. This trusted, planner-built command
                        // therefore follows the same attach exception as the
                        // adapter transport. Permission, workspace containment,
                        // fixed executable/arguments, ownership, and cleanup are
                        // all revalidated before it starts.
                        Mode = ProcessIsolationMode.Disabled
                    },
                    Io = ProcessIoSpec.Default with
                    {
                        StandardOutput = ProcessOutputSpec.CaptureAndStream with
                        {
                            MaxCapturedBytes = 16 * 1024
                        },
                        StandardError = ProcessOutputSpec.CaptureAndStream with
                        {
                            MaxCapturedBytes = 16 * 1024
                        }
                    }
                }
            },
            Readiness = new DebugHostReadinessPlan
            {
                ProtocolId = VSTestHostDebugReadinessParser.Protocol
            },
            Attach = new DebugDeferredAttachPlan
            {
                AdapterId = selected.Descriptor.Id,
                Resolution = selected.Resolution,
                WorkingDirectory = Path.GetDirectoryName(project)!
            }
        });
    }

    private async ValueTask<DebugExecutionPlanningResult> DirectMtpAsync(
        DebugExecutionPlanningContext context,
        TestDebugTarget target,
        string project,
        DotNetDebugProjectEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        var selected = await services.SelectAsync(
            context,
            DebugAdapterSelectionOperation.Launch,
            DebugTargetKind.Executable,
            "csharp",
            fileExtension: null,
            cancellationToken).ConfigureAwait(false);
        var arguments = string.IsNullOrWhiteSpace(target.Filter)
            ? Array.Empty<string>()
            : new[] { "--filter", target.Filter };
        var adapter = await selected.Factory.CreateSemanticLaunchPlanAsync(
            services.ConfigurationComposer,
            selected.Descriptor,
            selected.Resolution,
            new DebugSemanticLaunchConfiguration(
                evaluation.AppHostPath ?? evaluation.TargetPath,
                Path.GetDirectoryName(project)!,
                DebugTargetKind.Executable,
                DebugAdapterProgramKind.ExecutableFile,
                arguments,
                context.Operation.StopOnEntry),
            cancellationToken).ConfigureAwait(false);
        return DebugExecutionPlanningResult.Applicable(new DirectAdapterDebugExecutionPlan
        {
            PlannerId = "dotnet-mtp",
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            EnvironmentId = adapter.EnvironmentId,
            EnvironmentRevision = adapter.EnvironmentRevision,
            CanonicalWorkingDirectory = Path.GetDirectoryName(project)!,
            InitialConfiguration = DebugInitialConfigurationMapper.Map(
                context.Operation.InitialConfiguration,
                context.Operation.StopOnEntry,
                context.Workspace),
            ProjectEvaluation = DotNetEvaluationMetadata.Project(evaluation),
            Adapter = adapter
        });
    }

}

/// <summary>Projects trusted .NET evaluation evidence into bounded public metadata.</summary>
internal static class DotNetEvaluationMetadata
{
    public static DebugProjectEvaluationMetadata Project(
        DotNetDebugProjectEvaluation evaluation)
        => new(
            evaluation.ProjectKind.ToString(),
            evaluation.TestPlatform.ToString(),
            evaluation.SelectedTargetFramework,
            Path.GetFileName(evaluation.TargetPath),
            evaluation.EvaluationFingerprint,
            evaluation.ArtifactIsCurrent);
}

/// <summary>Validates bounded model-facing arguments consumed by execution planners.</summary>
internal static class DebugInputBounds
{
    public static void ValidateArguments(IReadOnlyList<string>? arguments)
    {
        if (arguments is null)
            return;
        if (arguments.Count > 128 ||
            arguments.Any(argument => argument is null || argument.Length > 4096 ||
                argument.Contains('\0')) ||
            arguments.Sum(argument => argument.Length) > 32 * 1024)
            throw new DebugStartPlanningException(
                "invalid_request",
                "Target arguments exceed debugger input bounds.");
    }

    public static void ValidateFilter(string? filter)
    {
        if (filter is null)
            return;
        if (filter.Length is < 1 or > 4096 || filter.Contains('\0') ||
            filter.Contains('\r') || filter.Contains('\n'))
            throw new DebugStartPlanningException(
                "debug_test_filter_invalid",
                "The test filter is empty or exceeds safe bounds.");
    }
}

/// <summary>Selects one canonical .NET project from explicit or discovered evidence.</summary>
internal static class DotNetProjectSelection
{
    public static string Select(
        DebugExecutionPlanningContext context,
        string? explicitProjectPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitProjectPath))
        {
            var selectionRoot = Directory.Exists(context.CanonicalTargetPath)
                ? context.CanonicalTargetPath
                : Path.GetDirectoryName(context.CanonicalTargetPath)!;
            var candidate = context.Workspace.ResolvePath(
                Path.IsPathRooted(explicitProjectPath)
                    ? explicitProjectPath
                    : Path.Combine(selectionRoot, explicitProjectPath));
            DotNetDebugProjectEvaluator.CanonicalContained(
                candidate,
                context.Workspace,
                "debug_project_outside_workspace");
            if (!DotNetDebugProjectEvaluator.IsProject(candidate) || !File.Exists(candidate))
                throw new DebugStartPlanningException(
                    "debug_project_not_found",
                    "The selected .NET project does not exist.");
            return candidate;
        }

        if (File.Exists(context.CanonicalTargetPath) &&
            DotNetDebugProjectEvaluator.IsProject(context.CanonicalTargetPath))
            return context.CanonicalTargetPath;

        var candidates = Candidates(context);
        if (candidates.Count == 0)
            throw new DebugStartPlanningException(
                "debug_project_not_found",
                "No supported .NET project was found.");
        if (candidates.Count > 1)
            throw new DebugStartPlanningException(
                "debug_project_ambiguous",
                "Multiple .NET projects match the target.");
        return candidates[0];
    }

    internal static IReadOnlyList<string> Candidates(
        DebugExecutionPlanningContext context)
    {
        var candidates = context.Evidence.MatchedPaths
            .Where(DotNetDebugProjectEvaluator.IsProject)
            .Concat(DiscoverDirect(context.CanonicalTargetPath))
            .Concat(ProjectsFromSolutions(
                context.Evidence.MatchedPaths,
                context.Workspace))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(257)
            .ToArray();
        if (candidates.Length > 256)
            throw new DebugStartPlanningException(
                "debug_project_ambiguous",
                "The project candidate set exceeds the trusted bound.");
        return candidates;
    }

    private static IEnumerable<string> DiscoverDirect(string target)
    {
        if (File.Exists(target) && DotNetDebugProjectEvaluator.IsProject(target))
            return [target];
        var directory = Directory.Exists(target)
            ? target
            : Path.GetDirectoryName(target);
        if (directory is null || !Directory.Exists(directory))
            return [];
        return Directory.EnumerateFiles(directory, "*.*proj", SearchOption.TopDirectoryOnly)
            .Where(DotNetDebugProjectEvaluator.IsProject)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(257);
    }

    private static IEnumerable<string> ProjectsFromSolutions(
        IReadOnlyList<string> markerPaths,
        AgentWorkspace workspace)
    {
        foreach (var solution in markerPaths.Where(path =>
                     path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)))
        {
            IReadOnlyList<string> relativeProjects;
            try
            {
                if (new FileInfo(solution).Length > 1024 * 1024)
                    throw new DebugStartPlanningException(
                        "debug_project_evaluation_failed",
                        "The selected solution exceeds the trusted evidence bound.");
                if (solution.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                {
                    var settings = new System.Xml.XmlReaderSettings
                    {
                        DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                        MaxCharactersInDocument = 1024 * 1024
                    };
                    using var reader = System.Xml.XmlReader.Create(solution, settings);
                    var values = XDocument.Load(reader, LoadOptions.None).Descendants()
                        .Where(element => element.Name.LocalName == "Project")
                        .Select(element => element.Attribute("Path")?.Value)
                        .OfType<string>()
                        .Take(258)
                        .ToArray();
                    if (values.Length > 257)
                        throw new DebugStartPlanningException(
                            "debug_project_evaluation_failed",
                            "The selected solution exceeds the trusted project-count bound.");
                    relativeProjects = values;
                }
                else
                {
                    var lines = File.ReadLines(solution).Take(10_002).ToArray();
                    if (lines.Length > 10_001)
                        throw new DebugStartPlanningException(
                            "debug_project_evaluation_failed",
                            "The selected solution exceeds the trusted line bound.");
                    relativeProjects = lines
                        .Select(line => line.Split(',').ElementAtOrDefault(1)?.Trim().Trim('"'))
                        .OfType<string>()
                        .Where(DotNetDebugProjectEvaluator.IsProject)
                        .Take(258)
                        .ToArray();
                    if (relativeProjects.Count > 257)
                        throw new DebugStartPlanningException(
                            "debug_project_evaluation_failed",
                            "The selected solution exceeds the trusted project-count bound.");
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                throw new DebugStartPlanningException(
                    "debug_project_evaluation_failed",
                    "The selected solution could not be read.");
            }
            foreach (var relative in relativeProjects)
            {
                var candidate = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(solution)!, relative));
                DotNetDebugProjectEvaluator.CanonicalContained(
                    candidate, workspace, "debug_project_outside_workspace");
                if (File.Exists(candidate) && DotNetDebugProjectEvaluator.IsProject(candidate))
                    yield return candidate;
            }
        }
    }
}
