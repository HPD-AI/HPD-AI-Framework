using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ToolHarness.Coding.Debugging;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugAdapterLaunchTargetResolverTests
{
    private readonly BuiltInDebugAdapterLaunchTargetResolver _resolver =
        new(new DotNetDebugLaunchArtifactResolver());

    [Theory]
    [InlineData("debugpy")]
    [InlineData("javascript")]
    [InlineData("rdbg")]
    public void Project_context_requires_an_explicit_entrypoint_when_adapter_program_is_a_file(
        string adapterId)
    {
        var act = () => _resolver.Resolve(
            new ProjectDirectoryDebugLaunchTarget("/workspace"),
            "/workspace",
            DebugTargetKind.ProjectDirectory,
            Descriptor(adapterId, DebugAdapterProgramKind.SourceFile),
            Evidence());

        act.Should().Throw<DebugStartPlanningException>()
            .Which.Kind.Should().Be("project_target_requires_entrypoint");
    }

    [Fact]
    public void Delve_preserves_a_supported_project_directory_program()
    {
        var result = _resolver.Resolve(
            new ProjectDirectoryDebugLaunchTarget("/workspace"),
            "/workspace",
            DebugTargetKind.ProjectDirectory,
            Descriptor("delve", DebugAdapterProgramKind.ProjectDirectory),
            Evidence());

        result.Program.Should().Be("/workspace");
        result.ProgramKind.Should().Be(DebugAdapterProgramKind.ProjectDirectory);
    }

    [Fact]
    public void Source_file_programs_remain_direct_and_typed()
    {
        var result = _resolver.Resolve(
            new SourceFileDebugLaunchTarget("/workspace/main.py"),
            "/workspace/main.py",
            DebugTargetKind.SourceFile,
            Descriptor("debugpy", DebugAdapterProgramKind.SourceFile),
            Evidence());

        result.Program.Should().Be("/workspace/main.py");
        result.ProgramKind.Should().Be(DebugAdapterProgramKind.SourceFile);
    }

    private static DebugAdapterDescriptor Descriptor(
        string id,
        DebugAdapterProgramKind programKinds) => new()
    {
        Id = id,
        Languages = [],
        FileExtensions = [],
        RootMarkers = [],
        TargetKinds = DebugTargetKind.SourceFile |
            DebugTargetKind.ProjectDirectory |
            DebugTargetKind.Executable,
        ProgramKinds = programKinds,
        Provenance = new() { PackageId = id, PackageVersion = "1", AssemblyName = "tests" }
    };

    private static WorkspaceRootMarkerResolution Evidence() => new(
        "/workspace",
        "/workspace",
        new HashSet<string>(),
        [],
        "none");
}
