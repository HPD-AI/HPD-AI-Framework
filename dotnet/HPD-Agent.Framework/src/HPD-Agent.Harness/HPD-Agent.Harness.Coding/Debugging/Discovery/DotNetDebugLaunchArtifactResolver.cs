using System.Xml.Linq;
using HPD.Agent.ToolHarness.Coding;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal interface IDotNetDebugLaunchArtifactResolver
{
    string Resolve(
        ProjectDirectoryDebugLaunchTarget target,
        string projectDirectory,
        WorkspaceRootMarkerResolution evidence);
}

internal sealed class DotNetDebugLaunchArtifactResolver : IDotNetDebugLaunchArtifactResolver
{
    public string Resolve(
        ProjectDirectoryDebugLaunchTarget target,
        string projectDirectory,
        WorkspaceRootMarkerResolution evidence)
    {
        ValidateConfiguration(target.Configuration);
        var projects = evidence.MatchedPaths
            .Where(IsProject)
            .Where(path => IsUnder(path, projectDirectory))
            .Concat(ProjectsFromSolutions(evidence.MatchedPaths, projectDirectory))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (target.ProjectPath is not null)
        {
            var requestedProject = Path.GetFullPath(Path.Combine(projectDirectory, target.ProjectPath));
            if (!IsUnder(requestedProject, projectDirectory))
                throw new DebugStartPlanningException(
                    "invalid_request", "The selected project is outside the project directory.");
            projects = projects.Where(path =>
                string.Equals(path, requestedProject, StringComparison.Ordinal)).ToArray();
        }
        if (projects.Length == 0)
            throw new DebugStartPlanningException(
                "debug_project_not_found", "No supported .NET project was found.");
        if (projects.Length > 1)
            throw new DebugStartPlanningException(
                "debug_project_ambiguous", "Multiple .NET projects match the target.");

        var metadata = ReadProjectMetadata(projects[0]);
        var selectedFramework = SelectFramework(target.TargetFramework, metadata.Frameworks);
        var configurationRoot = Path.Combine(
            Path.GetDirectoryName(projects[0])!, "bin", target.Configuration);
        var artifacts = DiscoverFromBuildFileLists(
                projects[0], target.Configuration, metadata.AssemblyName, selectedFramework)
            .Concat(Directory.Exists(configurationRoot)
                ? DiscoverArtifacts(configurationRoot, metadata.AssemblyName, selectedFramework)
                : [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (artifacts.Length == 0)
            throw BuildRequired();
        if (selectedFramework is null)
        {
            var frameworkDirectories = artifacts
                .Select(path => Path.GetRelativePath(configurationRoot, Path.GetDirectoryName(path)!))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (frameworkDirectories.Length > 1)
                throw new DebugStartPlanningException(
                    "debug_target_framework_ambiguous",
                    "Existing outputs were found for multiple target frameworks.");
        }
        if (artifacts.Length > 1)
            throw new DebugStartPlanningException(
                "debug_artifact_ambiguous", "Multiple build artifacts match the project.");
        if (!IsCurrentArtifact(artifacts[0], projects[0]))
            throw new DebugStartPlanningException(
                "debug_build_required",
                "The exact build artifact is older than the project inputs. Build the project and retry.");
        return artifacts[0];
    }

    private static void ValidateConfiguration(string configuration)
    {
        if (configuration.Length is < 1 or > 64 ||
            configuration.Any(character => !(char.IsLetterOrDigit(character) ||
                character is '-' or '_' or '.')))
            throw new DebugStartPlanningException(
                "invalid_request", "The build configuration is invalid.");
    }

    private static (string AssemblyName, string[] Frameworks) ReadProjectMetadata(string project)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(project, LoadOptions.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            throw new DebugStartPlanningException(
                "debug_project_not_found", "The .NET project metadata could not be read.");
        }
        var properties = document.Descendants()
            .Where(element => element.Name.LocalName is
                "AssemblyName" or "TargetFramework" or "TargetFrameworks")
            .Where(element => !element.Ancestors().Any(ancestor =>
                ancestor.Name.LocalName == "Target"))
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value.Trim(), StringComparer.Ordinal);
        var assemblyName = properties.GetValueOrDefault("AssemblyName");
        if (string.IsNullOrWhiteSpace(assemblyName) || assemblyName.Contains("$(", StringComparison.Ordinal))
            assemblyName = Path.GetFileNameWithoutExtension(project);
        var frameworks = properties.GetValueOrDefault("TargetFrameworks")?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? (properties.GetValueOrDefault("TargetFramework") is { Length: > 0 } framework
                ? [framework]
                : []);
        return (assemblyName, frameworks);
    }

    private static string? SelectFramework(string? requested, string[] frameworks)
    {
        if (requested is null && frameworks.Length == 1)
            return frameworks[0];
        if (requested is null && frameworks.Length > 1)
            throw new DebugStartPlanningException(
                "debug_target_framework_ambiguous", "The project has multiple target frameworks.");
        if (requested is not null && frameworks.Length > 0 &&
            !frameworks.Contains(requested, StringComparer.Ordinal))
            throw new DebugStartPlanningException(
                "debug_target_framework_ambiguous",
                "The requested target framework is not declared by the project.");
        return requested;
    }

    private static IEnumerable<string> ProjectsFromSolutions(
        IReadOnlyList<string> markerPaths,
        string projectDirectory)
    {
        foreach (var solution in markerPaths.Where(path =>
            path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)))
        {
            IEnumerable<string> relativeProjects;
            try
            {
                relativeProjects = solution.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
                    ? XDocument.Load(solution, LoadOptions.None).Descendants()
                        .Where(element => element.Name.LocalName == "Project")
                        .Select(element => element.Attribute("Path")?.Value)
                        .OfType<string>()
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                    : File.ReadLines(solution)
                        .Select(line => line.Split(',').ElementAtOrDefault(1)?.Trim().Trim('"'))
                        .OfType<string>()
                        .Where(IsProject);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                continue;
            }
            foreach (var relative in relativeProjects)
            {
                var candidate = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(solution)!, relative!));
                if (IsUnder(candidate, projectDirectory) && File.Exists(candidate))
                    yield return candidate;
            }
        }
    }

    private static IEnumerable<string> DiscoverArtifacts(
        string configurationRoot,
        string assemblyName,
        string? targetFramework)
    {
        var runtimeConfigs = Directory.EnumerateFiles(
                configurationRoot, "*.runtimeconfig.json", SearchOption.AllDirectories)
            .Take(257).ToArray();
        if (runtimeConfigs.Length > 256)
            throw new DebugStartPlanningException(
                "debug_artifact_ambiguous",
                "Build artifact discovery exceeded its bounded candidate limit.");
        foreach (var runtimeConfig in runtimeConfigs)
        {
            if (targetFramework is not null &&
                !PathSegments(runtimeConfig).Contains(targetFramework, StringComparer.Ordinal))
                continue;
            var candidate = runtimeConfig[..^".runtimeconfig.json".Length] + ".dll";
            if (File.Exists(candidate) &&
                (string.Equals(Path.GetFileNameWithoutExtension(candidate), assemblyName,
                     StringComparison.Ordinal) ||
                 runtimeConfigs.Length == 1))
                yield return candidate;
        }
        if (runtimeConfigs.Length == 0)
        {
            foreach (var candidate in Directory.EnumerateFiles(
                         configurationRoot, $"{assemblyName}.dll", SearchOption.AllDirectories)
                     .Where(path => targetFramework is null ||
                         PathSegments(path).Contains(targetFramework, StringComparer.Ordinal))
                     .Take(257))
                yield return candidate;
        }
    }

    private static IEnumerable<string> DiscoverFromBuildFileLists(
        string project,
        string configuration,
        string assemblyName,
        string? targetFramework)
    {
        var projectDirectory = Path.GetDirectoryName(project)!;
        var intermediateRoot = Path.Combine(projectDirectory, "obj", configuration);
        if (!Directory.Exists(intermediateRoot))
            yield break;
        var lists = Directory.EnumerateFiles(
                intermediateRoot, "*.FileListAbsolute.txt", SearchOption.AllDirectories)
            .Take(65).ToArray();
        if (lists.Length > 64)
            throw new DebugStartPlanningException(
                "debug_artifact_ambiguous",
                "Build manifest discovery exceeded its bounded candidate limit.");
        foreach (var list in lists)
        {
            if (targetFramework is not null &&
                !PathSegments(list).Contains(targetFramework, StringComparer.Ordinal))
                continue;
            foreach (var line in File.ReadLines(list).Take(4097))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var candidate = Path.GetFullPath(line.Trim());
                if (!IsUnder(candidate, projectDirectory) ||
                    !candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(candidate))
                    continue;
                var runtimeConfig = Path.ChangeExtension(candidate, ".runtimeconfig.json");
                if (!File.Exists(runtimeConfig))
                    continue;
                if (string.Equals(Path.GetFileNameWithoutExtension(candidate), assemblyName,
                        StringComparison.Ordinal) ||
                    File.ReadLines(list).Any(entry =>
                        string.Equals(Path.GetFullPath(entry.Trim()), runtimeConfig,
                            StringComparison.Ordinal)))
                    yield return candidate;
            }
        }
    }

    private static bool IsCurrentArtifact(string artifact, string project)
    {
        var artifactWrite = File.GetLastWriteTimeUtc(artifact);
        var projectDirectory = Path.GetDirectoryName(project)!;
        var newestInput = File.GetLastWriteTimeUtc(project);
        foreach (var pattern in new[]
                 { "*.cs", "*.fs", "*.vb", "Directory.Build.props", "Directory.Build.targets" })
        {
            foreach (var input in Directory.EnumerateFiles(
                         projectDirectory, pattern, SearchOption.AllDirectories).Take(10_001))
            {
                if (input.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal) ||
                    input.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal))
                    continue;
                var write = File.GetLastWriteTimeUtc(input);
                if (write > newestInput)
                    newestInput = write;
            }
        }
        return artifactWrite >= newestInput;
    }

    private static bool IsProject(string path)
        => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative != ".." && !Path.IsPathRooted(relative) &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string[] PathSegments(string path)
        => path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

    private static DebugStartPlanningException BuildRequired()
        => new("debug_build_required",
            "No exact current build artifact was found. Build the project and retry.");
}
