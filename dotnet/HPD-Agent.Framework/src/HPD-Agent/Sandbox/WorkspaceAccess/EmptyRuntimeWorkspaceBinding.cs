namespace HPD.Agent.Sandbox;

using HPD.Environment.Contracts;

internal static class EmptyRuntimeWorkspaceBinding
{
    public static RuntimeWorkspaceBinding Create(ProviderId providerId)
    {
        var workingDirectory = new WorkspacePath(
            new WorkspaceRootId("workspace"),
            string.Empty);
        return new RuntimeWorkspaceBinding
        {
            Files = UnavailableFileStore.Instance,
            Paths = UnavailablePathMapper.Instance,
            WorkingDirectory = workingDirectory,
            Roots = [],
            Content = new WorkspaceContentIdentity(
                $"{providerId.Value}:unbound-workspace"),
        };
    }

    private sealed class UnavailableFileStore : IWorkspaceFileStore
    {
        public static UnavailableFileStore Instance { get; } = new();

        public ValueTask<WorkspaceFileInfo?> GetInfoAsync(
            WorkspacePath path,
            CancellationToken cancellationToken = default) =>
            Fail<WorkspaceFileInfo?>();

        public ValueTask<WorkspaceTextSnapshot?> ReadTextAsync(
            WorkspacePath path,
            WorkspaceTextReadOptions options,
            CancellationToken cancellationToken = default) =>
            Fail<WorkspaceTextSnapshot?>();

        public ValueTask<WorkspaceBinarySnapshot?> ReadBytesAsync(
            WorkspacePath path,
            WorkspaceByteReadOptions options,
            CancellationToken cancellationToken = default) =>
            Fail<WorkspaceBinarySnapshot?>();

        public ValueTask<WorkspaceMutationResult> WriteTextAsync(
            WorkspacePath path,
            string content,
            WorkspaceWriteCondition condition,
            CancellationToken cancellationToken = default) =>
            Fail<WorkspaceMutationResult>();

        public ValueTask<WorkspaceMutationResult> WriteBytesAsync(
            WorkspacePath path,
            ReadOnlyMemory<byte> content,
            WorkspaceWriteCondition condition,
            CancellationToken cancellationToken = default) =>
            Fail<WorkspaceMutationResult>();

        public ValueTask<WorkspaceMutationResult> DeleteAsync(
            WorkspacePath path,
            WorkspaceWriteCondition condition,
            CancellationToken cancellationToken = default) =>
            Fail<WorkspaceMutationResult>();

        public ValueTask CreateDirectoryAsync(
            WorkspacePath path,
            CancellationToken cancellationToken = default) =>
            Fail();

        public ValueTask<WorkspaceListResultSet>
            ListAsync(
                WorkspacePath directory,
                WorkspaceListOptions options,
                CancellationToken cancellationToken = default) =>
            Fail<WorkspaceListResultSet>();

        public ValueTask<WorkspaceSearchResultSet>
            SearchTextAsync(
                WorkspaceSearchRequest request,
                CancellationToken cancellationToken = default) =>
            Fail<WorkspaceSearchResultSet>();

        private static ValueTask<T> Fail<T>() =>
            ValueTask.FromException<T>(Error());

        private static ValueTask Fail() =>
            ValueTask.FromException(Error());

        private static NotSupportedException Error() =>
            new(
                "The sandbox session has no bound workspace file store. A workspace projection and file-store implementation is required.");
    }

    private sealed class UnavailablePathMapper : IWorkspacePathMapper
    {
        public static UnavailablePathMapper Instance { get; } = new();

        public WorkspaceExecutionPath ToExecutionPath(
            WorkspacePath path) =>
            throw Error();

        public WorkspacePath? TryFromExecutionPath(
            WorkspaceExecutionPath path) =>
            throw Error();

        public Uri ToExecutionFileUri(WorkspacePath path) =>
            throw Error();

        public WorkspacePath? TryFromExecutionFileUri(Uri uri) =>
            throw Error();

        private static NotSupportedException Error() =>
            new(
                "The sandbox session has no workspace execution-path mapping.");
    }
}
