namespace HPD.Agent.Sandbox;

using HPD.Environment.Contracts;

/// <summary>Associates one session workspace root with its provider attachment.</summary>
internal sealed record WorkspaceFileStoreRegistration(
    WorkspaceRootId RootId,
    WorkspaceFileStoreAttachment Attachment);

/// <summary>Routes logical file operations to the attachment for their root.</summary>
internal sealed class CompositeWorkspaceFileStore(
    IReadOnlyList<WorkspaceFileStoreRegistration> registrations)
    : IWorkspaceFileStore
{
    private readonly IReadOnlyDictionary<
        WorkspaceRootId,
        WorkspaceFileStoreRegistration> _byRoot =
        registrations.ToDictionary(
            registration => registration.RootId);

    public ValueTask<WorkspaceFileInfo?> GetInfoAsync(
        WorkspacePath path,
        CancellationToken cancellationToken = default) =>
        Resolve(path).GetInfoAsync(path, cancellationToken);

    public ValueTask<WorkspaceTextSnapshot?> ReadTextAsync(
        WorkspacePath path,
        WorkspaceTextReadOptions options,
        CancellationToken cancellationToken = default) =>
        Resolve(path).ReadTextAsync(
            path,
            options,
            cancellationToken);

    public ValueTask<WorkspaceBinarySnapshot?> ReadBytesAsync(
        WorkspacePath path,
        WorkspaceByteReadOptions options,
        CancellationToken cancellationToken = default) =>
        Resolve(path).ReadBytesAsync(
            path,
            options,
            cancellationToken);

    public ValueTask<WorkspaceMutationResult> WriteTextAsync(
        WorkspacePath path,
        string content,
        WorkspaceWriteCondition condition,
        CancellationToken cancellationToken = default) =>
        Resolve(path).WriteTextAsync(
            path,
            content,
            condition,
            cancellationToken);

    public ValueTask<WorkspaceMutationResult> WriteBytesAsync(
        WorkspacePath path,
        ReadOnlyMemory<byte> content,
        WorkspaceWriteCondition condition,
        CancellationToken cancellationToken = default) =>
        Resolve(path).WriteBytesAsync(
            path,
            content,
            condition,
            cancellationToken);

    public ValueTask<WorkspaceMutationResult> DeleteAsync(
        WorkspacePath path,
        WorkspaceWriteCondition condition,
        CancellationToken cancellationToken = default) =>
        Resolve(path).DeleteAsync(
            path,
            condition,
            cancellationToken);

    public ValueTask CreateDirectoryAsync(
        WorkspacePath path,
        CancellationToken cancellationToken = default) =>
        Resolve(path).CreateDirectoryAsync(
            path,
            cancellationToken);

    public ValueTask<WorkspaceListResultSet> ListAsync(
        WorkspacePath directory,
        WorkspaceListOptions options,
        CancellationToken cancellationToken = default) =>
        Resolve(directory).ListAsync(
            directory,
            options,
            cancellationToken);

    public ValueTask<WorkspaceSearchResultSet> SearchTextAsync(
        WorkspaceSearchRequest request,
        CancellationToken cancellationToken = default) =>
        Resolve(request.Root).SearchTextAsync(
            request,
            cancellationToken);

    private IWorkspaceFileStore Resolve(WorkspacePath path) =>
        (_byRoot.TryGetValue(path.RootId, out var registration)
            ? registration
            : throw new UnauthorizedAccessException(
                $"Workspace root '{path.RootId.Value}' is not available in this session."))
        .Attachment.Files;
}

/// <summary>Routes trusted execution-path mapping across all session roots.</summary>
internal sealed class CompositeWorkspacePathMapper(
    IReadOnlyList<WorkspaceFileStoreRegistration> registrations)
    : IWorkspacePathMapper
{
    private readonly IReadOnlyDictionary<
        WorkspaceRootId,
        WorkspaceFileStoreRegistration> _byRoot =
        registrations.ToDictionary(
            registration => registration.RootId);
    private readonly IReadOnlyList<WorkspaceFileStoreRegistration>
        _registrations = registrations;

    public WorkspaceExecutionPath ToExecutionPath(
        WorkspacePath path) =>
        (_byRoot.TryGetValue(path.RootId, out var registration)
            ? registration
            : throw new UnauthorizedAccessException(
                $"Workspace root '{path.RootId.Value}' is not available in this session."))
        .Attachment.Paths.ToExecutionPath(path);

    public WorkspacePath? TryFromExecutionPath(
        WorkspaceExecutionPath path)
    {
        foreach (WorkspaceFileStoreRegistration registration in
                 _registrations)
        {
            if (registration.Attachment.Paths
                    .TryFromExecutionPath(path) is { } logical)
            {
                return logical;
            }
        }
        return null;
    }

    public Uri ToExecutionFileUri(WorkspacePath path) =>
        (_byRoot.TryGetValue(path.RootId, out var registration)
            ? registration
            : throw new UnauthorizedAccessException(
                $"Workspace root '{path.RootId.Value}' is not available in this session."))
        .Attachment.Paths.ToExecutionFileUri(path);

    public WorkspacePath? TryFromExecutionFileUri(Uri uri)
    {
        foreach (WorkspaceFileStoreRegistration registration in
                 _registrations)
        {
            if (registration.Attachment.Paths
                    .TryFromExecutionFileUri(uri) is { } logical)
            {
                return logical;
            }
        }
        return null;
    }
}

/// <summary>Projection ownership retained by a composed sandbox session.</summary>
internal sealed record SandboxWorkspaceProjection(
    ResourceRef<ContentProjection> Projection,
    FinalizationPolicy FinalizationPolicy);
