#nullable enable

using System.Xml.Linq;

namespace HPD.Media.Architecture.Tests;

public sealed class RealtimeMediaDependencyDagTests
{
    [Fact]
    public void SourceProjects_KeepExpectedDirectProjectReferences()
    {
        Dictionary<string, ProjectInfo> projects = LoadProjects();
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["HPD.Buffers"] = [],
            ["HPD.Audio.Primitives"] = ["HPD.Buffers"],
            ["HPD.Audio.Connections"] = ["HPD.Audio.Primitives"],
            ["HPD.Audio.Codecs"] = ["HPD.Audio.Primitives", "HPD.Buffers"],
            ["HPD.Audio.Codecs.G711"] = ["HPD.Audio.Codecs", "HPD.Audio.Primitives", "HPD.Media.Diagnostics"],
            ["HPD.Audio.Codecs.Opus"] = ["HPD.Audio.Codecs", "HPD.Audio.Primitives"],
            ["HPD.Audio.WebRTC"] = ["HPD.Audio.Codecs", "HPD.Audio.Primitives", "HPD.Media.Rtp.Audio", "HPD.Media.Rtp.Audio.Sdp", "HPD.Media.Sdp", "HPD.Media.Transport", "HPD.Media.WebRTC"],
            ["HPD.Media.Transport"] = ["HPD.Buffers"],
            ["HPD.Media.Rtp"] = ["HPD.Buffers", "HPD.Media.Diagnostics"],
            ["HPD.Media.Rtp.Audio"] = ["HPD.Audio.Codecs", "HPD.Media.Rtp"],
            ["HPD.Media.Rtp.Audio.Sdp"] = ["HPD.Audio.Codecs", "HPD.Media.Rtp.Audio", "HPD.Media.Sdp"],
            ["HPD.Media.Rtp.Repair"] = ["HPD.Media.Rtcp.Feedback", "HPD.Media.Rtp"],
            ["HPD.Media.Rtcp"] = ["HPD.Media.Diagnostics", "HPD.Media.Rtp"],
            ["HPD.Media.Rtcp.Feedback"] = ["HPD.Media.Rtcp", "HPD.Media.Rtp"],
            ["HPD.Media.Rtcp.Twcc"] = ["HPD.Media.Rtcp", "HPD.Media.Rtp"],
            ["HPD.Media.Sdp"] = [],
            ["HPD.Media.Srtp"] = ["HPD.Media.Diagnostics", "HPD.Media.Transport"],
            ["HPD.Media.WebRTC"] = ["HPD.Media.Rtcp", "HPD.Media.Rtp", "HPD.Media.Sdp", "HPD.Media.Transport"],
            ["HPD.Media.Diagnostics"] = ["HPD-Events"]
        };

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), projects.Keys.Order(StringComparer.Ordinal));
        foreach ((string projectName, string[] expectedReferences) in expected)
        {
            Assert.True(projects.TryGetValue(projectName, out ProjectInfo? project), $"Missing project {projectName}.");
            Assert.Equal(
                expectedReferences.Order(StringComparer.Ordinal),
                project.DirectReferences.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void SourceProjects_DoNotCreateForbiddenTransitiveDependencies()
    {
        Dictionary<string, ProjectInfo> projects = LoadProjects();

        AssertNoTransitiveReference(projects, "HPD.Buffers", IsRealtimeMediaProject);
        AssertNoTransitiveReference(projects, "HPD.Audio.Primitives", name => name.StartsWith("HPD.Media.", StringComparison.Ordinal));
        AssertNoTransitiveReference(projects, "HPD.Audio.Primitives", name => name.StartsWith("HPD.Audio.Codecs", StringComparison.Ordinal));
        AssertNoTransitiveReference(projects, "HPD.Media.Rtp", name => name is "HPD.Media.Rtcp" or "HPD.Media.Srtp" or "HPD.Media.WebRTC" or "HPD.Audio.Codecs");
        AssertNoTransitiveReference(projects, "HPD.Media.Rtcp", name => name is "HPD.Media.Rtcp.Feedback" or "HPD.Media.Rtcp.Twcc" or "HPD.Media.Rtp.Repair" or "HPD.Media.WebRTC" or "HPD.Media.Srtp" or "HPD.Audio.Codecs");
        AssertNoTransitiveReference(projects, "HPD.Media.Sdp", _ => true);
        AssertNoTransitiveReference(projects, "HPD.Media.Srtp", name => name is "HPD.Media.WebRTC" or "HPD.Media.Sdp" or "HPD.Media.Rtp" || name.StartsWith("HPD.Audio.", StringComparison.Ordinal));
        AssertNoTransitiveReference(projects, "HPD.Media.WebRTC", name => name is "HPD.Audio.Codecs.Opus" or "HPD.Media.Rtp.Audio" or "HPD-Events");
        AssertNoTransitiveReference(projects, "HPD.Audio.Codecs.Opus", name => name.StartsWith("HPD.Media.", StringComparison.Ordinal));
        AssertNoTransitiveReference(projects, "HPD.Audio.WebRTC", name => name is "HPD.Audio.Codecs.Opus" or "HPD-Events");
    }

    [Fact]
    public void SourceProjects_HaveNativeAotAnalyzerFlagsEnabled()
    {
        foreach (ProjectInfo project in LoadProjects().Values)
        {
            Assert.True(project.IsAotCompatible, $"{project.Name} must set IsAotCompatible=true.");
            Assert.True(project.EnableTrimAnalyzer, $"{project.Name} must set EnableTrimAnalyzer=true.");
            Assert.True(project.EnableSingleFileAnalyzer, $"{project.Name} must set EnableSingleFileAnalyzer=true.");
        }
    }

    [Fact]
    public void OnlyDiagnosticsDependsOnSemanticEventsAssembly()
    {
        Dictionary<string, ProjectInfo> projects = LoadProjects();
        string[] eventConsumers = projects.Values
            .Where(project => project.DirectReferences.Contains("HPD-Events", StringComparer.Ordinal))
            .Select(project => project.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["HPD.Media.Diagnostics"], eventConsumers);
    }

    [Fact]
    public void SourceProjectGraph_IsAcyclic()
    {
        Dictionary<string, ProjectInfo> projects = LoadProjects();
        foreach (string project in projects.Keys)
        {
            AssertNoCycle(projects, project, [], []);
        }
    }

    private static void AssertNoTransitiveReference(
        Dictionary<string, ProjectInfo> projects,
        string projectName,
        Func<string, bool> forbidden)
    {
        HashSet<string> references = GetTransitiveRealtimeReferences(projects, projectName);
        string[] violations = references
            .Where(forbidden)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(violations.Length == 0, $"{projectName} has forbidden references: {string.Join(", ", violations)}.");
    }

    private static HashSet<string> GetTransitiveRealtimeReferences(
        Dictionary<string, ProjectInfo> projects,
        string projectName)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(projects[projectName].DirectReferences.Where(projects.ContainsKey));
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (string reference in projects[current].DirectReferences)
            {
                if (projects.ContainsKey(reference))
                {
                    stack.Push(reference);
                }
            }
        }

        return visited;
    }

    private static void AssertNoCycle(
        Dictionary<string, ProjectInfo> projects,
        string projectName,
        HashSet<string> visited,
        HashSet<string> path)
    {
        if (!path.Add(projectName))
        {
            Assert.Fail($"Project reference cycle detected through {projectName}.");
        }

        if (visited.Add(projectName))
        {
            foreach (string reference in projects[projectName].DirectReferences)
            {
                if (projects.ContainsKey(reference))
                {
                    AssertNoCycle(projects, reference, visited, path);
                }
            }
        }

        path.Remove(projectName);
    }

    private static Dictionary<string, ProjectInfo> LoadProjects()
    {
        DirectoryInfo srcRoot = FindSourceRoot();
        return Directory.EnumerateFiles(srcRoot.FullName, "*.csproj", SearchOption.AllDirectories)
            .Select(ReadProject)
            .ToDictionary(project => project.Name, StringComparer.Ordinal);
    }

    private static ProjectInfo ReadProject(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath);
        string name = Path.GetFileNameWithoutExtension(projectPath);
        string[] directReferences = document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(GetProjectReferenceName)
            .ToArray();

        string? property(string propertyName)
        {
            return document
                .Descendants(propertyName)
                .Select(element => element.Value.Trim())
                .FirstOrDefault(value => value.Length > 0);
        }

        return new ProjectInfo(
            name,
            directReferences,
            string.Equals(property("IsAotCompatible"), "true", StringComparison.OrdinalIgnoreCase),
            string.Equals(property("EnableTrimAnalyzer"), "true", StringComparison.OrdinalIgnoreCase),
            string.Equals(property("EnableSingleFileAnalyzer"), "true", StringComparison.OrdinalIgnoreCase));
    }

    private static DirectoryInfo FindSourceRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = new DirectoryInfo(Path.Combine(
                current.FullName,
                "HPD-AI-Framework",
                "dotnet",
                "shared",
                "src",
                "HPD-RealtimeMedia",
                "src"));
            if (candidate.Exists)
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate HPD-RealtimeMedia source root.");
    }

    private static bool IsRealtimeMediaProject(string name)
    {
        return name.StartsWith("HPD.Audio.", StringComparison.Ordinal) ||
            name.StartsWith("HPD.Media.", StringComparison.Ordinal);
    }

    private static string GetProjectReferenceName(string include)
    {
        string normalized = include.Replace('\\', '/');
        string fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        return Path.GetFileNameWithoutExtension(fileName);
    }

    private sealed record ProjectInfo(
        string Name,
        IReadOnlyCollection<string> DirectReferences,
        bool IsAotCompatible,
        bool EnableTrimAnalyzer,
        bool EnableSingleFileAnalyzer);
}
