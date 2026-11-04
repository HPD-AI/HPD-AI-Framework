using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using HPD.Agent.Security;
using HPD.Environment.Contracts;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>Semantic execution shape obtained from evaluated .NET project metadata.</summary>
internal enum DotNetDebugProjectKind
{
    Application,
    Test,
    Library,
    Unknown
}

/// <summary>Supported .NET test-process hosting strategy.</summary>
internal enum DotNetTestPlatformKind
{
    None,
    VSTest,
    MicrosoftTestingPlatform,
    Unknown
}

/// <summary>Bounded package evidence used for project classification.</summary>
internal sealed record DotNetPackageEvidence(string Id, string? Version);

/// <summary>Exact evaluated output for one referenced project.</summary>
internal sealed record DotNetProjectReferenceOutput(
    string ProjectPath,
    string TargetPath,
    bool RequiresRuntimeCopy);

/// <summary>Evaluated semantics for one MSBuild <c>ProjectReference</c> item.</summary>
internal sealed record DotNetEvaluatedProjectReference(
    string ProjectPath,
    bool ReferenceOutputAssembly,
    string? OutputItemType);

/// <summary>Trusted request for sandboxed MSBuild project evaluation.</summary>
internal sealed record DotNetDebugProjectEvaluationRequest
{
    public required string CanonicalProjectPath { get; init; }
    public required string Configuration { get; init; }
    public string? TargetFramework { get; init; }
    public required RuntimeProcessExecutionBinding ProcessExecution { get; init; }
    public required RuntimeWorkspaceBinding RuntimeWorkspace { get; init; }
    public AgentSandboxRuntime ProcessSandbox { get; init; } = AgentSandboxRuntime.Default;
}

/// <summary>Evaluated project shape and exact output identity.</summary>
internal sealed record DotNetDebugProjectEvaluation
{
    public required string ProjectPath { get; init; }
    public required DotNetDebugProjectKind ProjectKind { get; init; }
    public required DotNetTestPlatformKind TestPlatform { get; init; }
    public required string AssemblyName { get; init; }
    public string? OutputType { get; init; }
    public required bool IsTestProject { get; init; }
    public required bool IsDirectlyExecutable { get; init; }
    public required IReadOnlyList<string> TargetFrameworks { get; init; }
    public required string SelectedTargetFramework { get; init; }
    public required string TargetPath { get; init; }
    public string? AppHostPath { get; init; }
    public required IReadOnlyList<string> ProjectReferences { get; init; }
    public required IReadOnlyList<DotNetProjectReferenceOutput> ProjectReferenceOutputs { get; init; }
    public required IReadOnlyList<DotNetPackageEvidence> Packages { get; init; }
    public required string EvaluationFingerprint { get; init; }
    public required bool ArtifactIsCurrent { get; init; }
    public AgentSandboxRuntime EffectiveSandbox { get; init; } = AgentSandboxRuntime.Default;
}

/// <summary>Evaluates .NET projects through a fixed, bounded MSBuild query.</summary>
internal interface IDotNetDebugProjectEvaluator
{
    ValueTask<DotNetDebugProjectEvaluation> EvaluateAsync(
        DotNetDebugProjectEvaluationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Process-provider-backed implementation of trusted .NET project evaluation.</summary>
internal sealed class DotNetDebugProjectEvaluator : IDotNetDebugProjectEvaluator
{
    private const string PropertyNames =
        "MSBuildProjectFullPath,TargetFramework,TargetFrameworks,TargetPath,AssemblyName," +
        "OutputType,IsTestProject,IsTestApplication,IsTestingPlatformApplication," +
        "UseMicrosoftTestingPlatformRunner,GenerateProgramFile,UseAppHost,RuntimeIdentifier,ProjectDir";

    public async ValueTask<DotNetDebugProjectEvaluation> EvaluateAsync(
        DotNetDebugProjectEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await ValidateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var first = await QueryAsync(request, targetFramework: null, cancellationToken)
            .ConfigureAwait(false);
        var frameworks = Frameworks(first.Properties);
        var selectedFramework = SelectFramework(request.TargetFramework, frameworks);
        var evaluated = string.IsNullOrWhiteSpace(selectedFramework)
            ? first
            : await QueryAsync(request, selectedFramework, cancellationToken).ConfigureAwait(false);

        var properties = evaluated.Properties;
        var targetPath = CanonicalOutput(properties.GetValueOrDefault("TargetPath"), request);
        var directProjectReferences = evaluated.ProjectReferences
            .Select(reference => reference with
            {
                ProjectPath = CanonicalPath(reference.ProjectPath)
            })
            .GroupBy(reference => reference.ProjectPath, StringComparer.Ordinal)
            .Select(group => new DotNetEvaluatedProjectReference(
                group.Key,
                group.Any(reference => reference.ReferenceOutputAssembly),
                group.Select(reference => reference.OutputItemType)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))))
            .OrderBy(reference => reference.ProjectPath, StringComparer.Ordinal)
            .ToArray();
        var packages = evaluated.Packages
            .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .ToArray();
        var referenceOutputs = new Dictionary<string, DotNetProjectReferenceOutput>(
            StringComparer.Ordinal);
        var activeSandbox = request.ProcessSandbox;
        var pendingReferences = new Queue<(string ProjectPath, bool RequiresRuntimeCopy)>(
            directProjectReferences.Select(reference => (
                reference.ProjectPath,
                reference.ReferenceOutputAssembly)));
        var visitedReferences = new Dictionary<string, bool>(StringComparer.Ordinal);
        while (pendingReferences.TryDequeue(out var pending))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visitedReferences.TryGetValue(pending.ProjectPath, out var previouslyRuntime) &&
                (previouslyRuntime || !pending.RequiresRuntimeCopy))
                continue;
            visitedReferences[pending.ProjectPath] = pending.RequiresRuntimeCopy;
            if (visitedReferences.Count > 64)
                throw new DebugStartPlanningException(
                    "debug_build_required",
                    "The transitive project-reference graph exceeds the trusted bound.");
            var referenceRequest = request with
            {
                CanonicalProjectPath = pending.ProjectPath,
                ProcessSandbox = activeSandbox
            };
            var referenceBaseline = await QueryAsync(
                referenceRequest,
                targetFramework: null,
                cancellationToken).ConfigureAwait(false);
            var referenceFrameworks = Frameworks(referenceBaseline.Properties);
            var referenceFramework = referenceFrameworks.Contains(
                selectedFramework,
                StringComparer.Ordinal)
                ? selectedFramework
                : referenceFrameworks.Count == 1
                    ? referenceFrameworks[0]
                    : throw new DebugStartPlanningException(
                        "debug_target_framework_ambiguous",
                        "A referenced project has no unambiguous compatible target framework.");
            var referenceEvaluation = string.Equals(
                referenceFramework,
                referenceBaseline.Properties.GetValueOrDefault("TargetFramework"),
                StringComparison.Ordinal)
                ? referenceBaseline
                : await QueryAsync(
                    referenceRequest,
                    referenceFramework,
                    cancellationToken).ConfigureAwait(false);
            referenceOutputs[pending.ProjectPath] = new(
                pending.ProjectPath,
                CanonicalOutput(
                    referenceEvaluation.Properties.GetValueOrDefault("TargetPath"),
                    referenceRequest),
                pending.RequiresRuntimeCopy);
            foreach (var transitive in referenceEvaluation.ProjectReferences
                         .Select(item => item with
                         {
                             ProjectPath = CanonicalPath(item.ProjectPath)
                         })
                         .OrderBy(item => item.ProjectPath, StringComparer.Ordinal))
                pendingReferences.Enqueue((
                    transitive.ProjectPath,
                    pending.RequiresRuntimeCopy &&
                    transitive.ReferenceOutputAssembly));
        }

        var hasKnownTestFramework = packages.Any(package =>
            IsKnownVSTestFramework(package.Id));
        var hasVSTest = packages.Any(package => package.Id.Equals(
            "Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase));
        var hasMtp = Boolean(properties, "IsTestingPlatformApplication") ||
            Boolean(properties, "UseMicrosoftTestingPlatformRunner") ||
            packages.Any(package => package.Id.Contains(
                "Microsoft.Testing.Platform", StringComparison.OrdinalIgnoreCase));
        var isTest = Boolean(properties, "IsTestProject") ||
            Boolean(properties, "IsTestApplication") ||
            hasVSTest ||
            hasMtp ||
            hasKnownTestFramework;
        var conflictingTestHostEvidence = hasMtp &&
            hasVSTest &&
            !Boolean(properties, "IsTestingPlatformApplication") &&
            !Boolean(properties, "UseMicrosoftTestingPlatformRunner");
        var outputType = properties.GetValueOrDefault("OutputType");
        var executable = outputType is not null &&
            (outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
             outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase));
        var kind = conflictingTestHostEvidence
            ? DotNetDebugProjectKind.Unknown
            : isTest
            ? DotNetDebugProjectKind.Test
            : executable
                ? DotNetDebugProjectKind.Application
                : outputType?.Equals("Library", StringComparison.OrdinalIgnoreCase) == true
                    ? DotNetDebugProjectKind.Library
                    : DotNetDebugProjectKind.Unknown;
        var platform = conflictingTestHostEvidence
            ? DotNetTestPlatformKind.Unknown
            : !isTest
            ? DotNetTestPlatformKind.None
            : hasMtp
                ? DotNetTestPlatformKind.MicrosoftTestingPlatform
                : hasVSTest
                    ? DotNetTestPlatformKind.VSTest
                    : DotNetTestPlatformKind.Unknown;

        var fingerprintText = string.Join('\n',
            properties.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}")
                .Concat(visitedReferences.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}:runtime={pair.Value}"))
                .Concat(referenceOutputs.Values
                    .OrderBy(reference => reference.ProjectPath, StringComparer.Ordinal)
                    .Select(reference =>
                        $"{reference.ProjectPath}=>{reference.TargetPath}:runtime={reference.RequiresRuntimeCopy}"))
                .Concat(packages.Select(package => $"{package.Id}@{package.Version}")));
        return new DotNetDebugProjectEvaluation
        {
            ProjectPath = request.CanonicalProjectPath,
            ProjectKind = kind,
            TestPlatform = platform,
            AssemblyName = properties.GetValueOrDefault("AssemblyName") ??
                Path.GetFileNameWithoutExtension(request.CanonicalProjectPath),
            OutputType = outputType,
            IsTestProject = isTest,
            IsDirectlyExecutable = executable && hasMtp,
            TargetFrameworks = frameworks,
            SelectedTargetFramework = selectedFramework,
            TargetPath = targetPath,
            AppHostPath = await FindAppHostAsync(
                request.RuntimeWorkspace,
                targetPath,
                Boolean(properties, "UseAppHost"),
                cancellationToken).ConfigureAwait(false),
            ProjectReferences = visitedReferences.Keys
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray(),
            ProjectReferenceOutputs = referenceOutputs.Values
                .OrderBy(reference => reference.ProjectPath, StringComparer.Ordinal)
                .ToArray(),
            Packages = packages,
            EvaluationFingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintText))).ToLowerInvariant(),
            ArtifactIsCurrent = await IsCurrentAsync(
                targetPath,
                request.CanonicalProjectPath,
                referenceOutputs.Values.ToArray(),
                kind,
                await FindAppHostAsync(
                    request.RuntimeWorkspace,
                    targetPath,
                    Boolean(properties, "UseAppHost"),
                    cancellationToken).ConfigureAwait(false),
                request.RuntimeWorkspace,
                cancellationToken).ConfigureAwait(false),
            EffectiveSandbox = activeSandbox
        };
    }

    private static async ValueTask<QueryResult> QueryAsync(
        DotNetDebugProjectEvaluationRequest request,
        string? targetFramework,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "msbuild",
            request.CanonicalProjectPath,
            "-nologo",
            "-verbosity:quiet",
            $"-getProperty:{PropertyNames}",
            "-getItem:ProjectReference,PackageReference",
            $"-p:Configuration={request.Configuration}"
        };
        if (!string.IsNullOrWhiteSpace(targetFramework))
            arguments.Add($"-p:TargetFramework={targetFramework}");
        ProcessInvocationResult result;
        try
        {
            result = await request.ProcessExecution.ProcessProvider.RunAsync(new ProcessInvocationSpec
            {
            Target = request.ProcessExecution.ExecutionTarget,
            Role = ProcessRole.Task,
            Command = new ProcessCommandSpec
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(request.CanonicalProjectPath),
                Environment = new Dictionary<string, string?>
                {
                    ["DOTNET_CLI_UI_LANGUAGE"] = "en-US",
                    ["MSBUILDTERMINALLOGGER"] = "off"
                }
            },
            Limits = new ProcessLimitSpec(ProcessCount: 16, MemoryBytes: 1024L * 1024 * 1024),
            Policy = ProcessInvocationPolicy.Default with
            {
                Timeout = TimeSpan.FromSeconds(20),
                StopProcessTree = true
            },
            Isolation = request.ProcessSandbox.ToProcessIsolationPolicy(
                Path.GetDirectoryName(request.CanonicalProjectPath)!),
            Io = ProcessIoSpec.Default with
            {
                StandardOutput = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = false,
                    MaxCapturedBytes = 1024 * 1024
                },
                StandardError = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = false,
                    MaxCapturedBytes = 64 * 1024
                }
            }
            }, output: null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new DebugStartPlanningException(
                "debug_project_evaluation_failed",
                "The selected environment could not evaluate the .NET project.");
        }
        if (result.CompletionKind is not (ProcessCompletionKind.Completed or ProcessCompletionKind.Exited) ||
            result.ExitCode is not 0)
            throw new DebugStartPlanningException(
                "debug_project_evaluation_failed",
                "The selected environment could not evaluate the .NET project.");

        var text = Encoding.UTF8.GetString(result.Output.Stdout.CapturedBytes.Span);
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new DebugStartPlanningException(
                "debug_project_evaluation_failed",
                "MSBuild returned no structured project evaluation.");
        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;
            var properties = root.GetProperty("Properties").EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.GetRawText(),
                    StringComparer.Ordinal);
            var projectReferences = ReadProjectReferences(root);
            var packages = ReadPackages(root);
            return new QueryResult(properties, projectReferences, packages);
        }
        catch (Exception exception) when (
            exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new DebugStartPlanningException(
                "debug_project_evaluation_failed",
                "MSBuild returned malformed structured project evaluation.");
        }
    }

    private static IReadOnlyList<DotNetEvaluatedProjectReference> ReadProjectReferences(
        JsonElement root)
    {
        if (!root.TryGetProperty("Items", out var items) ||
            !items.TryGetProperty("ProjectReference", out var values) ||
            values.ValueKind != JsonValueKind.Array)
            return [];
        return values.EnumerateArray().Take(256).Select(item =>
        {
            var path = item.TryGetProperty("FullPath", out var fullPath)
                ? fullPath.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(path))
                throw new JsonException(
                    "An evaluated ProjectReference has no FullPath.");
            var referenceOutputAssembly = ReadOptionalBoolean(
                item,
                "ReferenceOutputAssembly",
                defaultValue: true);
            var outputItemType = item.TryGetProperty(
                    "OutputItemType",
                    out var outputItemTypeElement) &&
                outputItemTypeElement.ValueKind == JsonValueKind.String
                    ? outputItemTypeElement.GetString()
                    : null;
            return new DotNetEvaluatedProjectReference(
                path,
                referenceOutputAssembly,
                string.IsNullOrWhiteSpace(outputItemType)
                    ? null
                    : outputItemType);
        }).ToArray();
    }

    private static bool ReadOptionalBoolean(
        JsonElement item,
        string propertyName,
        bool defaultValue)
    {
        if (!item.TryGetProperty(propertyName, out var element) ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return defaultValue;
        if (element.ValueKind == JsonValueKind.True)
            return true;
        if (element.ValueKind == JsonValueKind.False)
            return false;
        if (element.ValueKind != JsonValueKind.String)
            throw new JsonException(
                $"Evaluated {propertyName} metadata is not Boolean.");
        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;
        if (bool.TryParse(text, out var value))
            return value;
        throw new JsonException(
            $"Evaluated {propertyName} metadata is not Boolean.");
    }

    private static IReadOnlyList<DotNetPackageEvidence> ReadPackages(JsonElement root)
    {
        if (!root.TryGetProperty("Items", out var items) ||
            !items.TryGetProperty("PackageReference", out var values) ||
            values.ValueKind != JsonValueKind.Array)
            return [];
        return values.EnumerateArray().Take(256).Select(item =>
        {
            var id = item.TryGetProperty("Identity", out var identity)
                ? identity.GetString()
                : null;
            var version = item.TryGetProperty("Version", out var versionProperty)
                ? versionProperty.GetString()
                : null;
            return string.IsNullOrWhiteSpace(id)
                ? null
                : new DotNetPackageEvidence(id, version);
        }).OfType<DotNetPackageEvidence>().ToArray();
    }

    private static IReadOnlyList<string> Frameworks(
        IReadOnlyDictionary<string, string?> properties)
    {
        var values = properties.GetValueOrDefault("TargetFrameworks");
        if (string.IsNullOrWhiteSpace(values))
            values = properties.GetValueOrDefault("TargetFramework");
        return (values ?? string.Empty).Split(
            ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string SelectFramework(string? requested, IReadOnlyList<string> frameworks)
    {
        if (requested is not null && !frameworks.Contains(requested, StringComparer.Ordinal))
            throw new DebugStartPlanningException(
                "debug_target_framework_invalid",
                "The requested target framework is not declared by the project.");
        if (requested is not null)
            return requested;
        return frameworks.Count switch
        {
            1 => frameworks[0],
            > 1 => throw new DebugStartPlanningException(
                "debug_target_framework_ambiguous",
                "The project declares multiple target frameworks; select one explicitly."),
            _ => throw new DebugStartPlanningException(
                "debug_project_execution_shape_unknown",
                "The project has no evaluated target framework.")
        };
    }

    private static async ValueTask ValidateAsync(
        DotNetDebugProjectEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        WorkspacePath? projectPath =
            DebugWorkspaceAccess.TryFromEffective(
                request.RuntimeWorkspace,
                request.CanonicalProjectPath);
        if (projectPath is null)
        {
            throw new DebugStartPlanningException(
                "debug_project_not_found",
                "No supported .NET project was selected.");
        }
        WorkspaceFileInfo? projectInfo =
            await request.RuntimeWorkspace.Files.GetInfoAsync(
                projectPath.Value,
                cancellationToken).ConfigureAwait(false);
        if (projectInfo is not { Kind: WorkspaceEntryKind.File } ||
            !IsProject(request.CanonicalProjectPath))
            throw new DebugStartPlanningException(
                "debug_project_not_found",
                "No supported .NET project was selected.");
        _ = CanonicalPath(request.CanonicalProjectPath);
        if (request.Configuration.Length is < 1 or > 64 ||
            request.Configuration.Any(character =>
                !char.IsLetterOrDigit(character) && character is not ('-' or '_' or '.')))
            throw new DebugStartPlanningException(
                "invalid_request",
                "The build configuration is invalid.");
    }

    private static string CanonicalOutput(
        string? path,
        DotNetDebugProjectEvaluationRequest request)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new DebugStartPlanningException(
                "debug_artifact_not_found",
                "MSBuild did not evaluate an exact target output.");
        var canonical = Path.GetFullPath(
            path,
            Path.GetDirectoryName(request.CanonicalProjectPath)!);
        return CanonicalPath(canonical);
    }

    internal static string CanonicalPath(string path) => Path.GetFullPath(path);

    private static bool Boolean(IReadOnlyDictionary<string, string?> properties, string name)
        => bool.TryParse(properties.GetValueOrDefault(name), out var value) && value;

    private static bool IsKnownVSTestFramework(string packageId)
        => packageId.Equals("xunit", StringComparison.OrdinalIgnoreCase) ||
           packageId.StartsWith("xunit.runner.", StringComparison.OrdinalIgnoreCase) ||
           packageId.Equals("NUnit", StringComparison.OrdinalIgnoreCase) ||
           packageId.StartsWith("NUnit3TestAdapter", StringComparison.OrdinalIgnoreCase) ||
           packageId.Equals("MSTest.TestFramework", StringComparison.OrdinalIgnoreCase) ||
           packageId.Equals("MSTest.TestAdapter", StringComparison.OrdinalIgnoreCase);

    private static async ValueTask<string?> FindAppHostAsync(
        RuntimeWorkspaceBinding workspace,
        string targetPath,
        bool useAppHost,
        CancellationToken cancellationToken)
    {
        if (!useAppHost)
            return null;
        var withoutExtension = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            Path.GetFileNameWithoutExtension(targetPath));
        if (await IsVisibleFileAsync(
                workspace,
                withoutExtension,
                cancellationToken).ConfigureAwait(false))
        {
            return withoutExtension;
        }
        var windows = withoutExtension + ".exe";
        return await IsVisibleFileAsync(
                workspace,
                windows,
                cancellationToken).ConfigureAwait(false)
            ? windows
            : null;
    }

    private static async ValueTask<bool> IsCurrentAsync(
        string targetPath,
        string projectPath,
        IReadOnlyList<DotNetProjectReferenceOutput> projectReferences,
        DotNetDebugProjectKind projectKind,
        string? appHostPath,
        RuntimeWorkspaceBinding workspace,
        CancellationToken cancellationToken)
    {
        WorkspaceFileInfo? targetInfo =
            await GetWorkspaceInfoAsync(
                workspace,
                targetPath,
                cancellationToken).ConfigureAwait(false);
        if (targetInfo is not { Kind: WorkspaceEntryKind.File } ||
            targetInfo.Version.ModifiedAt is not { } targetTime)
            return false;
        if (projectKind == DotNetDebugProjectKind.Application)
        {
            var stem = Path.Combine(
                Path.GetDirectoryName(targetPath)!,
                Path.GetFileNameWithoutExtension(targetPath));
            if (!await IsVisibleFileAsync(
                    workspace,
                    stem + ".runtimeconfig.json",
                    cancellationToken).ConfigureAwait(false) ||
                !await IsVisibleFileAsync(
                    workspace,
                    stem + ".deps.json",
                    cancellationToken).ConfigureAwait(false))
                return false;
            if (appHostPath is not null)
            {
                WorkspaceFileInfo? appHostInfo =
                    await GetWorkspaceInfoAsync(
                        workspace,
                        appHostPath,
                        cancellationToken).ConfigureAwait(false);
                if (appHostInfo is not
                        { Kind: WorkspaceEntryKind.File } ||
                    appHostInfo.Version.ModifiedAt < targetTime)
                {
                    return false;
                }
            }
        }
        if (!await InputsAreCurrentAsync(
                workspace,
                projectPath,
                targetTime,
                cancellationToken).ConfigureAwait(false))
        {
            return false;
        }
        foreach (var reference in projectReferences)
        {
            WorkspaceFileInfo? referenceInfo =
                await GetWorkspaceInfoAsync(
                    workspace,
                    reference.TargetPath,
                    cancellationToken).ConfigureAwait(false);
            if (referenceInfo is not
                    { Kind: WorkspaceEntryKind.File } ||
                referenceInfo.Version.ModifiedAt is not
                    { } referenceTime)
            {
                return false;
            }
            if (!await InputsAreCurrentAsync(
                    workspace,
                    reference.ProjectPath,
                    referenceTime,
                    cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
            if (!reference.RequiresRuntimeCopy)
            {
                if (targetTime < referenceTime)
                    return false;
                continue;
            }
            var loadedCopy = Path.Combine(
                Path.GetDirectoryName(targetPath)!,
                Path.GetFileName(reference.TargetPath));
            WorkspaceFileInfo? loadedInfo =
                await GetWorkspaceInfoAsync(
                    workspace,
                    loadedCopy,
                    cancellationToken).ConfigureAwait(false);
            if (loadedInfo is not
                    { Kind: WorkspaceEntryKind.File } ||
                loadedInfo.Version.ModifiedAt < referenceTime)
                return false;
        }
        return true;
    }

    private static async ValueTask<bool> InputsAreCurrentAsync(
        RuntimeWorkspaceBinding workspace,
        string project,
        DateTimeOffset outputTime,
        CancellationToken cancellationToken)
    {
        WorkspacePath projectPath =
            DebugWorkspaceAccess.FromEffective(workspace, project);
        WorkspaceFileInfo? projectInfo =
            await workspace.Files.GetInfoAsync(
                projectPath,
                cancellationToken).ConfigureAwait(false);
        if (projectInfo is not { Kind: WorkspaceEntryKind.File } ||
            projectInfo.Version.ModifiedAt > outputTime)
            return false;
        WorkspacePath directory =
            DebugWorkspaceAccess.Parent(projectPath) ??
            DebugWorkspaceAccess.Root(projectPath.RootId);
        WorkspaceListResultSet listing =
            await workspace.Files.ListAsync(
                directory,
                new WorkspaceListOptions
                {
                    Recursive = true,
                    MaxEntries = 10_002,
                },
                cancellationToken).ConfigureAwait(false);
        WorkspaceDirectoryEntry[] inputs = listing.Entries
            .Where(entry =>
                entry.Kind == WorkspaceEntryKind.File &&
                IsSourceFile(entry.Path.RelativePath) &&
                !ContainsBuildDirectory(entry.Path.RelativePath))
            .Take(10_001)
            .ToArray();
        if (inputs.Length > 10_000)
            return false;
        foreach (WorkspaceDirectoryEntry input in inputs)
        {
            if (input.Version.ModifiedAt > outputTime)
                return false;
        }

        WorkspacePath owningRoot =
            DebugWorkspaceAccess.Root(projectPath.RootId);
        for (WorkspacePath? ancestor = directory;
             ancestor is { } current;)
        {
            foreach (var name in new[]
                     {
                         "Directory.Build.props",
                         "Directory.Build.targets"
                     })
            {
                WorkspacePath imported =
                    DebugWorkspaceAccess.ResolveRelative(
                        workspace,
                        current,
                        name);
                WorkspaceFileInfo? importedInfo =
                    await workspace.Files.GetInfoAsync(
                        imported,
                        cancellationToken).ConfigureAwait(false);
                if (importedInfo is
                        { Kind: WorkspaceEntryKind.File } &&
                    importedInfo.Version.ModifiedAt > outputTime)
                    return false;
            }
            if (current.Equals(owningRoot))
                break;
            if (DebugWorkspaceAccess.Parent(current) is not { } parent)
                break;
            ancestor = parent;
        }
        return true;
    }

    private static bool ContainsBuildDirectory(string path)
        => path.Contains("/bin/", StringComparison.Ordinal) ||
           path.Contains("/obj/", StringComparison.Ordinal) ||
           path.Contains("\\bin\\", StringComparison.Ordinal) ||
           path.Contains("\\obj\\", StringComparison.Ordinal);

    private static bool IsSourceFile(string path)
        => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".fs", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase);

    private static async ValueTask<WorkspaceFileInfo?>
        GetWorkspaceInfoAsync(
            RuntimeWorkspaceBinding workspace,
            string effectivePath,
            CancellationToken cancellationToken)
    {
        WorkspacePath? logical =
            DebugWorkspaceAccess.TryFromEffective(
                workspace,
                effectivePath);
        return logical is null
            ? null
            : await workspace.Files.GetInfoAsync(
                logical.Value,
                cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<bool> IsVisibleFileAsync(
        RuntimeWorkspaceBinding workspace,
        string effectivePath,
        CancellationToken cancellationToken) =>
        await GetWorkspaceInfoAsync(
                workspace,
                effectivePath,
                cancellationToken).ConfigureAwait(false) is
            { Kind: WorkspaceEntryKind.File };

    internal static bool IsProject(string path)
        => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);

    private sealed record QueryResult(
        IReadOnlyDictionary<string, string?> Properties,
        IReadOnlyList<DotNetEvaluatedProjectReference> ProjectReferences,
        IReadOnlyList<DotNetPackageEvidence> Packages);
}
