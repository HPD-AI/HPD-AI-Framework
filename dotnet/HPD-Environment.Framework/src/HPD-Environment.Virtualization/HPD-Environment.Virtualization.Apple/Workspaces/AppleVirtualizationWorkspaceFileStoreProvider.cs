namespace HPD.Environment.Virtualization.Apple.Workspaces;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime.Workspaces;

/// <summary>
/// Opens workspace access through the Apple guest process provider. File
/// operations execute in the guest and never fall back to host filesystem APIs.
/// </summary>
internal sealed class AppleVirtualizationWorkspaceFileStoreProvider(
    IProcessProvider processProvider)
    : IWorkspaceFileStoreProvider
{
    public ProviderId ProviderId =>
        AppleVirtualizationProviderDescriptor.ProviderId;

    public ValueTask<WorkspaceFileStoreAttachment> OpenAsync(
        WorkspaceFileStoreSpec spec,
        TargetHandle<ExecutionUnit>? unit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();
        if (unit is null ||
            unit.Value.Route.ProviderId is not { } provider ||
            !provider.Equals(ProviderId))
        {
            throw new InvalidOperationException(
                "AppleVirtualization.WorkspaceUnitRequired: workspace access requires an Apple Virtualization execution unit.");
        }
        if (spec.Access is not (
            AccessMode.ReadOnly or AccessMode.ReadWrite))
        {
            throw new NotSupportedException(
                $"AppleVirtualization.WorkspaceAccessUnsupported: access mode '{spec.Access}' is not supported.");
        }
        ProcessWorkspaceFileStore.ValidateExecutionRoot(spec.ExecutionPath);
        var store = new ProcessWorkspaceFileStore(
            spec.RootId,
            spec.ExecutionPath.Value,
            spec.Access,
            spec.PathSemantics,
            processProvider,
            unit.Value,
            "AppleVirtualization");
        return ValueTask.FromResult(new WorkspaceFileStoreAttachment
        {
            Files = store,
            Paths = store,
            PathSemantics = spec.PathSemantics,
            Lifetime = store,
        });
    }
}
