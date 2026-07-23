using HPD.Agent.ToolHarness.Coding;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugResolvedLaunchProgram(
    string Program,
    DebugAdapterProgramKind ProgramKind);

internal interface IDebugAdapterLaunchTargetResolver
{
    DebugResolvedLaunchProgram Resolve(
        DebugLaunchTarget requested,
        string requestedPath,
        DebugTargetKind requestedKind,
        DebugAdapterDescriptor descriptor,
        WorkspaceRootMarkerResolution evidence);
}

internal sealed class BuiltInDebugAdapterLaunchTargetResolver(
    IDotNetDebugLaunchArtifactResolver dotNetArtifacts)
    : IDebugAdapterLaunchTargetResolver
{
    public DebugResolvedLaunchProgram Resolve(
        DebugLaunchTarget requested,
        string requestedPath,
        DebugTargetKind requestedKind,
        DebugAdapterDescriptor descriptor,
        WorkspaceRootMarkerResolution evidence)
    {
        if (requested is not ProjectDirectoryDebugLaunchTarget project)
            return new(requestedPath, DirectProgramKind(requestedKind));
        if ((descriptor.ProgramKinds & DebugAdapterProgramKind.ProjectDirectory) != 0)
            return new(requestedPath, DebugAdapterProgramKind.ProjectDirectory);
        if (string.Equals(descriptor.Id, "netcoredbg", StringComparison.Ordinal))
            return new(
                dotNetArtifacts.Resolve(project, requestedPath, evidence),
                DebugAdapterProgramKind.ExecutableFile);
        if (descriptor.Id is "debugpy" or "javascript" or "rdbg")
            throw new DebugStartPlanningException(
                "project_target_requires_entrypoint",
                "The selected project requires a source entry point.");
        throw new DebugStartPlanningException(
            "adapter_program_kind_unsupported",
            "The selected debug adapter cannot consume a project directory.");
    }

    private static DebugAdapterProgramKind DirectProgramKind(DebugTargetKind kind) => kind switch
    {
        DebugTargetKind.SourceFile => DebugAdapterProgramKind.SourceFile,
        DebugTargetKind.Executable => DebugAdapterProgramKind.ExecutableFile,
        DebugTargetKind.ProjectDirectory => DebugAdapterProgramKind.ProjectDirectory,
        _ => DebugAdapterProgramKind.None
    };
}
