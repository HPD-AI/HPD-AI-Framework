using HPD.Agent.ToolHarness.Coding;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class WorkspaceRootMarkerResolverTests
{
    [Fact]
    public async Task Resolves_glob_markers_from_artifact_ancestry_deterministically()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hpd-marker-{Guid.NewGuid():N}");
        try
        {
            var artifactDirectory = Path.Combine(root, "bin", "Debug", "net8.0");
            Directory.CreateDirectory(artifactDirectory);
            var project = Path.Combine(root, "Sample.csproj");
            var artifact = Path.Combine(artifactDirectory, "Sample.dll");
            await File.WriteAllTextAsync(project, "<Project />");
            await File.WriteAllBytesAsync(artifact, []);
            var workspace = new AgentWorkspace(
                "root", root, [new AgentWorkspaceRoot("root", root)]);
            var resolver = new WorkspaceRootMarkerResolver();

            var first = await resolver.ResolveAsync(
                workspace, artifact, ["*.csproj", "global.json"]);
            var second = await resolver.ResolveAsync(
                workspace, artifact, ["global.json", "*.csproj"]);

            first.ProjectRoot.Should().Be(root);
            first.MatchedMarkers.Should().Contain("*.csproj");
            first.MatchedPaths.Should().Equal(project);
            first.Fingerprint.Should().Be(second.Fingerprint);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Never_walks_above_the_owning_workspace_root()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"hpd-marker-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "workspace");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            await File.WriteAllTextAsync(Path.Combine(parent, "outside.csproj"), "<Project />");
            var workspace = new AgentWorkspace(
                "root", root, [new AgentWorkspaceRoot("root", root)]);

            var result = await new WorkspaceRootMarkerResolver().ResolveAsync(
                workspace, Path.Combine(root, "nested"), ["*.csproj"]);

            result.MatchedPaths.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }
    }
}
