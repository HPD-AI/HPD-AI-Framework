using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ToolHarness.Coding.Debugging;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DotNetDebugLaunchArtifactResolverTests
{
    [Fact]
    public void Slnx_resolves_nested_project_to_exact_existing_artifact()
    {
        using var fixture = new DotNetFixture();
        var project = fixture.AddProject("src/App/App.csproj", "net8.0");
        var solution = fixture.AddFile(
            "App.slnx", "<Solution><Project Path=\"src/App/App.csproj\" /></Solution>");
        var artifact = fixture.AddArtifact("src/App/bin/Debug/net8.0/App.dll");

        var result = fixture.Resolve(
            new ProjectDirectoryDebugLaunchTarget(fixture.Root),
            [solution],
            new HashSet<string>(["*.slnx"]));

        result.Should().Be(artifact);
        File.Exists(project).Should().BeTrue();
    }

    [Fact]
    public void Missing_output_requires_an_authorized_external_build()
    {
        using var fixture = new DotNetFixture();
        var project = fixture.AddProject("App.csproj", "net8.0");

        var act = () => fixture.Resolve(
            new ProjectDirectoryDebugLaunchTarget(fixture.Root),
            [project],
            new HashSet<string>(["*.csproj"]));

        act.Should().Throw<DebugStartPlanningException>()
            .Which.Kind.Should().Be("debug_build_required");
    }

    [Fact]
    public void Multiple_projects_are_rejected_without_guessing()
    {
        using var fixture = new DotNetFixture();
        var first = fixture.AddProject("One.csproj", "net8.0");
        var second = fixture.AddProject("Two.csproj", "net8.0");

        var act = () => fixture.Resolve(
            new ProjectDirectoryDebugLaunchTarget(fixture.Root),
            [first, second],
            new HashSet<string>(["*.csproj"]));

        act.Should().Throw<DebugStartPlanningException>()
            .Which.Kind.Should().Be("debug_project_ambiguous");
    }

    [Fact]
    public void Multiple_frameworks_require_explicit_disambiguation()
    {
        using var fixture = new DotNetFixture();
        var project = fixture.AddProject("App.csproj", "net8.0;net9.0", plural: true);

        var act = () => fixture.Resolve(
            new ProjectDirectoryDebugLaunchTarget(fixture.Root),
            [project],
            new HashSet<string>(["*.csproj"]));

        act.Should().Throw<DebugStartPlanningException>()
            .Which.Kind.Should().Be("debug_target_framework_ambiguous");
    }

    [Fact]
    public void Stale_artifact_requires_a_new_build()
    {
        using var fixture = new DotNetFixture();
        var project = fixture.AddProject("App.csproj", "net8.0");
        var artifact = fixture.AddArtifact("bin/Debug/net8.0/App.dll");
        var source = fixture.AddFile("Program.cs", "Console.WriteLine();");
        File.SetLastWriteTimeUtc(artifact, DateTime.UtcNow.AddMinutes(-2));
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow);

        var act = () => fixture.Resolve(
            new ProjectDirectoryDebugLaunchTarget(fixture.Root),
            [project],
            new HashSet<string>(["*.csproj"]));

        act.Should().Throw<DebugStartPlanningException>()
            .Which.Kind.Should().Be("debug_build_required");
    }

    [Fact]
    public void Build_manifest_resolves_output_name_supplied_by_imported_properties()
    {
        using var fixture = new DotNetFixture();
        var project = fixture.AddFile(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><AssemblyName>$(ImportedAssemblyName)</AssemblyName></PropertyGroup></Project>");
        var artifact = fixture.AddArtifact("custom/net8.0/Imported.App.dll");
        var runtimeConfig = Path.ChangeExtension(artifact, ".runtimeconfig.json");
        fixture.AddFile(
            "obj/Debug/net8.0/App.csproj.FileListAbsolute.txt",
            $"{artifact}{System.Environment.NewLine}{runtimeConfig}{System.Environment.NewLine}");

        var result = fixture.Resolve(
            new ProjectDirectoryDebugLaunchTarget(fixture.Root),
            [project],
            new HashSet<string>(["*.csproj"]));

        result.Should().Be(artifact);
    }

    private sealed class DotNetFixture : IDisposable
    {
        public DotNetFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"hpd-dotnet-resolver-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string AddProject(string relativePath, string frameworks, bool plural = false)
            => AddFile(relativePath,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><{(plural ? "TargetFrameworks" : "TargetFramework")}>{frameworks}</{(plural ? "TargetFrameworks" : "TargetFramework")}></PropertyGroup></Project>");

        public string AddArtifact(string relativePath)
        {
            var artifact = AddFile(relativePath, "assembly");
            AddFile(relativePath[..^4] + ".runtimeconfig.json", "{}");
            File.SetLastWriteTimeUtc(artifact, DateTime.UtcNow.AddMinutes(1));
            return artifact;
        }

        public string AddFile(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public string Resolve(
            ProjectDirectoryDebugLaunchTarget target,
            IReadOnlyList<string> matchedPaths,
            IReadOnlySet<string> markers)
            => new DotNetDebugLaunchArtifactResolver().Resolve(
                target,
                Root,
                new WorkspaceRootMarkerResolution(
                    Root, Root, markers, matchedPaths, "fixture"));

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
