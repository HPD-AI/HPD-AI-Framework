using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
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
                context.RuntimeWorkspace),
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
        if (await context.RuntimeWorkspace.Files.GetInfoAsync(
                DebugWorkspaceAccess.FromEffective(
                    context.RuntimeWorkspace,
                    context.CanonicalTargetPath),
                cancellationToken).ConfigureAwait(false) is not
            { Kind: WorkspaceEntryKind.File })
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
                context.RuntimeWorkspace),
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
        var project = await DotNetProjectSelection.SelectAsync(
            context,
            target.ProjectPath,
            cancellationToken).ConfigureAwait(false);
        var evaluation = await evaluator.EvaluateAsync(new()
        {
            CanonicalProjectPath = project,
            Configuration = target.Configuration,
            TargetFramework = target.TargetFramework,
            ProcessExecution = context.Runtime.ProcessExecution!,
            ProcessSandbox = context.Runtime.ProcessSandbox,
            RuntimeWorkspace = context.RuntimeWorkspace,
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
        var effectiveContext = context with
        {
            Runtime = context.Runtime with
            {
                ProcessSandbox = evaluation.EffectiveSandbox
            }
        };
        var selected = await services.SelectAsync(
            effectiveContext,
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
                effectiveContext.Operation.StopOnEntry),
            cancellationToken).ConfigureAwait(false);
        return DebugExecutionPlanningResult.Applicable(new DirectAdapterDebugExecutionPlan
        {
            PlannerId = Id,
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            EnvironmentId = adapter.EnvironmentId,
            EnvironmentRevision = adapter.EnvironmentRevision,
            CanonicalWorkingDirectory = Path.GetDirectoryName(project)!,
            InitialConfiguration = DebugInitialConfigurationMapper.Map(
                effectiveContext.Operation.InitialConfiguration,
                effectiveContext.Operation.StopOnEntry,
                effectiveContext.RuntimeWorkspace),
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
        var project = await DotNetProjectSelection.SelectAsync(
            context,
            target.ProjectPath,
            cancellationToken).ConfigureAwait(false);
        var evaluation = await evaluator.EvaluateAsync(new()
        {
            CanonicalProjectPath = project,
            Configuration = target.Configuration,
            TargetFramework = target.TargetFramework,
            ProcessExecution = context.Runtime.ProcessExecution!,
            ProcessSandbox = context.Runtime.ProcessSandbox,
            RuntimeWorkspace = context.RuntimeWorkspace,
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

        var effectiveContext = context with
        {
            Runtime = context.Runtime with
            {
                ProcessSandbox = evaluation.EffectiveSandbox
            }
        };
        return evaluation.TestPlatform switch
        {
            DotNetTestPlatformKind.VSTest =>
                await HostedVSTestAsync(effectiveContext, target, project, evaluation, cancellationToken)
                    .ConfigureAwait(false),
            DotNetTestPlatformKind.MicrosoftTestingPlatform when evaluation.IsDirectlyExecutable =>
                await DirectMtpAsync(effectiveContext, target, project, evaluation, cancellationToken)
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
                context.RuntimeWorkspace),
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
                    Isolation = new ProcessIsolationPolicy
                    {
                        Mode = ProcessIsolationMode.Isolated,
                        Network = new NetworkEgressPolicy
                        {
                            Mode = NetworkEgressMode.Filtered,
                            AllowedDomains =
                            [
                                new DomainRule
                                {
                                    Pattern = "localhost",
                                    Kind = DomainRuleKind.Localhost,
                                    Reason = "debug-ready-loopback",
                                },
                            ],
                            RequireProxyMediation = false,
                        },
                        Interactive = ProcessInteractivePolicy.Default with
                        {
                            AllowStdin = true,
                            AllowLocalBinding = true,
                        },
                        Degradation =
                            ProcessIsolationDegradationPolicy.FailClosed,
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
                context.RuntimeWorkspace),
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
    public static async ValueTask<string> SelectAsync(
        DebugExecutionPlanningContext context,
        string? explicitProjectPath,
        CancellationToken cancellationToken)
    {
        RuntimeWorkspaceBinding workspace = context.RuntimeWorkspace;
        WorkspacePath target = DebugWorkspaceAccess.FromEffective(
            workspace,
            context.CanonicalTargetPath);
        WorkspaceFileInfo targetInfo =
            await workspace.Files.GetInfoAsync(
                target,
                cancellationToken).ConfigureAwait(false) ??
            throw new DebugStartPlanningException(
                "debug_project_not_found",
                "No supported .NET project was selected.");
        if (!string.IsNullOrWhiteSpace(explicitProjectPath))
        {
            WorkspacePath selectionRoot =
                targetInfo.Kind == WorkspaceEntryKind.Directory
                    ? target
                    : DebugWorkspaceAccess.Parent(target) ??
                        DebugWorkspaceAccess.Root(target.RootId);
            WorkspacePath candidatePath =
                DebugWorkspaceAccess.ResolveRelative(
                    workspace,
                    selectionRoot,
                    explicitProjectPath.Replace('\\', '/'));
            string candidate = DotNetDebugProjectEvaluator.CanonicalPath(
                DebugWorkspaceAccess.ToEffective(
                    workspace,
                    candidatePath));
            WorkspaceFileInfo? candidateInfo =
                await workspace.Files.GetInfoAsync(
                    candidatePath,
                    cancellationToken).ConfigureAwait(false);
            if (!DotNetDebugProjectEvaluator.IsProject(candidate) ||
                candidateInfo is not
                    { Kind: WorkspaceEntryKind.File })
            {
                throw new DebugStartPlanningException(
                    "debug_project_not_found",
                    "The selected .NET project does not exist.");
            }
            return candidate;
        }

        if (targetInfo.Kind == WorkspaceEntryKind.File &&
            DotNetDebugProjectEvaluator.IsProject(context.CanonicalTargetPath))
        {
            return context.CanonicalTargetPath;
        }

        IReadOnlyList<string> candidates = await CandidatesAsync(
            context,
            cancellationToken).ConfigureAwait(false);
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

    internal static async ValueTask<IReadOnlyList<string>> CandidatesAsync(
        DebugExecutionPlanningContext context,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> evidenceProjects = context.Evidence.MatchedPaths
            .Where(DotNetDebugProjectEvaluator.IsProject)
            .Select(path => CanonicalWorkspacePath(
                context.RuntimeWorkspace,
                path))
            .ToArray();
        IEnumerable<string> directProjects = await DiscoverDirectAsync(
            context,
            cancellationToken).ConfigureAwait(false);
        IEnumerable<string> solutionProjects = await ProjectsFromSolutionsAsync(
            context,
            cancellationToken).ConfigureAwait(false);
        string[] candidates = evidenceProjects
            .Concat(directProjects)
            .Concat(solutionProjects)
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

    private static string CanonicalWorkspacePath(
        RuntimeWorkspaceBinding workspace,
        string effectivePath)
    {
        WorkspacePath? logical =
            DebugWorkspaceAccess.TryFromEffective(
                workspace,
                effectivePath);
        return logical is { } path
            ? DebugWorkspaceAccess.ToEffective(workspace, path)
            : DotNetDebugProjectEvaluator.CanonicalPath(
                effectivePath);
    }

    private static async ValueTask<IReadOnlyList<string>> DiscoverDirectAsync(
        DebugExecutionPlanningContext context,
        CancellationToken cancellationToken)
    {
        RuntimeWorkspaceBinding workspace = context.RuntimeWorkspace;
        WorkspacePath target = DebugWorkspaceAccess.FromEffective(
            workspace,
            context.CanonicalTargetPath);
        WorkspaceFileInfo? targetInfo =
            await workspace.Files.GetInfoAsync(
                target,
                cancellationToken).ConfigureAwait(false);
        if (targetInfo is null)
        {
            return [];
        }
        if (targetInfo.Kind == WorkspaceEntryKind.File &&
            DotNetDebugProjectEvaluator.IsProject(context.CanonicalTargetPath))
        {
            return [context.CanonicalTargetPath];
        }
        WorkspacePath directory = targetInfo.Kind ==
            WorkspaceEntryKind.Directory
            ? target
            : DebugWorkspaceAccess.Parent(target) ??
                DebugWorkspaceAccess.Root(target.RootId);
        WorkspaceListResultSet listing =
            await workspace.Files.ListAsync(
                directory,
                new WorkspaceListOptions
                {
                    Recursive = false,
                    MaxEntries = 258,
                },
                cancellationToken).ConfigureAwait(false);
        return listing.Entries
            .Where(entry =>
                entry.Kind == WorkspaceEntryKind.File &&
                IsProjectLeaf(entry.Path.RelativePath))
            .Select(entry => DebugWorkspaceAccess.ToEffective(
                workspace,
                entry.Path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(257)
            .ToArray();
    }

    private static async ValueTask<IReadOnlyList<string>>
        ProjectsFromSolutionsAsync(
        DebugExecutionPlanningContext context,
        CancellationToken cancellationToken)
    {
        RuntimeWorkspaceBinding workspace = context.RuntimeWorkspace;
        var projects = new List<string>();
        foreach (string solution in context.Evidence.MatchedPaths.Where(path =>
                     path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspacePath solutionPath;
            try
            {
                solutionPath = DebugWorkspaceAccess.FromEffective(
                    workspace,
                    solution);
                WorkspaceTextSnapshot solutionText =
                    await DebugWorkspaceAccess.ReadTextAsync(
                        workspace,
                        solutionPath,
                        1024 * 1024,
                        cancellationToken).ConfigureAwait(false);
                IReadOnlyList<string> relativeProjects;
                if (solution.EndsWith(
                        ".slnx",
                        StringComparison.OrdinalIgnoreCase))
                {
                    relativeProjects = ReadSolutionXml(
                        solutionText.Content).ToArray();
                }
                else
                {
                    relativeProjects = ReadSolutionText(
                        solutionText.Content).ToArray();
                }

                WorkspacePath solutionDirectory =
                    DebugWorkspaceAccess.Parent(solutionPath) ??
                    DebugWorkspaceAccess.Root(solutionPath.RootId);
                foreach (string relative in relativeProjects)
                {
                    string normalized = relative.Replace('\\', '/');
                    WorkspacePath candidatePath =
                        DebugWorkspaceAccess.ResolveEffectiveRelative(
                            workspace,
                            solutionDirectory,
                            normalized);
                    WorkspaceFileInfo? candidateInfo =
                        await workspace.Files.GetInfoAsync(
                            candidatePath,
                            cancellationToken).ConfigureAwait(false);
                    string candidate =
                        DotNetDebugProjectEvaluator.CanonicalPath(
                            DebugWorkspaceAccess.ToEffective(
                                workspace,
                                candidatePath));
                    if (candidateInfo is
                            { Kind: WorkspaceEntryKind.File } &&
                        DotNetDebugProjectEvaluator.IsProject(candidate))
                    {
                        projects.Add(candidate);
                    }
                }
            }
            catch (DebugStartPlanningException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or
                    UnauthorizedAccessException or
                    System.Xml.XmlException or
                    ArgumentException)
            {
                throw new DebugStartPlanningException(
                    "debug_project_evaluation_failed",
                    "The selected solution could not be read.");
            }
        }
        return projects.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<string> ReadSolutionXml(string content)
    {
        var settings = new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            MaxCharactersInDocument = 1024 * 1024,
        };
        using var text = new StringReader(content);
        using var reader = System.Xml.XmlReader.Create(text, settings);
        string[] values = XDocument.Load(reader, LoadOptions.None)
            .Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .OfType<string>()
            .Take(258)
            .ToArray();
        if (values.Length > 257)
        {
            throw new DebugStartPlanningException(
                "debug_project_evaluation_failed",
                "The selected solution exceeds the trusted project-count bound.");
        }
        return values;
    }

    private static IEnumerable<string> ReadSolutionText(string content)
    {
        string[] lines = content
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .Take(10_002)
            .ToArray();
        if (lines.Length > 10_001)
        {
            throw new DebugStartPlanningException(
                "debug_project_evaluation_failed",
                "The selected solution exceeds the trusted line bound.");
        }
        string[] values = lines
            .Select(line => line.Split(',').ElementAtOrDefault(1)
                ?.Trim()
                .Trim('"'))
            .OfType<string>()
            .Where(DotNetDebugProjectEvaluator.IsProject)
            .Take(258)
            .ToArray();
        if (values.Length > 257)
        {
            throw new DebugStartPlanningException(
                "debug_project_evaluation_failed",
                "The selected solution exceeds the trusted project-count bound.");
        }
        return values;
    }

    private static bool IsProjectLeaf(string path)
    {
        int separator = path.LastIndexOf('/');
        string leaf = separator < 0
            ? path
            : path[(separator + 1)..];
        return DotNetDebugProjectEvaluator.IsProject(leaf);
    }
}
