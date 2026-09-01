using System.Collections.Immutable;
using System.Security.Cryptography;
using HPD.Gateway;

namespace HPD.Gateway.ControlPlane;

public sealed record GatewayRevisionMutation(
    string NamespaceId,
    string TargetNodeId,
    string IdempotencyKey,
    GatewayManagementActor Actor,
    string CorrelationId,
    ImmutableArray<byte> Utf8Configuration,
    string? Description,
    string? ExpectedDesiredStateToken,
    bool Activate,
    string SourceKind,
    string SourceId);

public sealed record GatewayRollbackMutation(
    string NamespaceId,
    string TargetNodeId,
    string RevisionId,
    string IdempotencyKey,
    GatewayManagementActor Actor,
    string CorrelationId,
    string? Description,
    string? ExpectedDesiredStateToken);

public sealed record GatewayRevisionComparison(
    string LeftRevisionId,
    string RightRevisionId,
    bool Equivalent,
    ImmutableArray<GatewayRevisionDifference> Differences,
    bool IsTruncated);

public sealed record GatewayRevisionDifference(string Path, string Kind);

public sealed record GatewayRevisionExport(
    string SchemaVersion,
    string RevisionId,
    string ContentHashAlgorithm,
    string ContentHashValue,
    ImmutableArray<byte> Utf8Configuration);

public enum GatewayApplicationReadState : byte { Found, NotFound, Gone, Invalid }

public sealed record GatewayApplicationReadResult<T>(GatewayApplicationReadState State, string Code, T? Value = default);

public interface IGatewayManagementApplication
{
    ValueTask<GatewayManagementCommandResult> ImportAsync(GatewayRevisionMutation mutation, CancellationToken cancellationToken = default);
    ValueTask<GatewayManagementCommandResult> RollbackAsync(GatewayRollbackMutation mutation, CancellationToken cancellationToken = default);
    ValueTask<GatewayApplicationReadResult<GatewayRevisionComparison>> CompareAsync(
        string namespaceId, string targetNodeId, string leftRevisionId, string rightRevisionId, CancellationToken cancellationToken = default);
    ValueTask<GatewayApplicationReadResult<GatewayRevisionExport>> ExportAsync(
        string namespaceId, string targetNodeId, string revisionId, CancellationToken cancellationToken = default);
}

internal sealed class GatewayManagementApplication(
    IGatewayManagementCommandCoordinator commands,
    IGatewayManagementReader reader) : IGatewayManagementApplication
{
    public ValueTask<GatewayManagementCommandResult> ImportAsync(
        GatewayRevisionMutation mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        return commands.SubmitAsync(new GatewaySubmitCommand(
            mutation.NamespaceId, mutation.TargetNodeId, mutation.IdempotencyKey,
            mutation.Actor, mutation.CorrelationId, mutation.SourceKind, mutation.SourceId,
            mutation.Description, mutation.Utf8Configuration,
            mutation.ExpectedDesiredStateToken, mutation.Activate), cancellationToken);
    }

    public async ValueTask<GatewayManagementCommandResult> RollbackAsync(
        GatewayRollbackMutation mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        GatewayManagedRecord<GatewayAcceptedRevision>? revision = await reader.GetRevisionAsync(
            mutation.NamespaceId, mutation.TargetNodeId, mutation.RevisionId, cancellationToken).ConfigureAwait(false);
        if (revision is null)
            return new(GatewayManagementCommandState.Invalid, "management.revision.not-found");
        return await commands.ActivateRevisionAsync(new GatewayActivateRevisionCommand(
            mutation.NamespaceId, mutation.TargetNodeId, mutation.RevisionId,
            mutation.IdempotencyKey, mutation.Actor, mutation.CorrelationId,
            mutation.ExpectedDesiredStateToken, GatewayRevisionActivationKind.Rollback),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<GatewayApplicationReadResult<GatewayRevisionComparison>> CompareAsync(
        string namespaceId, string targetNodeId, string leftRevisionId, string rightRevisionId,
        CancellationToken cancellationToken = default)
    {
        GatewayManagedRecord<GatewayAcceptedRevision>? left = await reader.GetRevisionAsync(namespaceId, targetNodeId, leftRevisionId, cancellationToken).ConfigureAwait(false);
        GatewayManagedRecord<GatewayAcceptedRevision>? right = await reader.GetRevisionAsync(namespaceId, targetNodeId, rightRevisionId, cancellationToken).ConfigureAwait(false);
        if (left is null || right is null)
            return new(GatewayApplicationReadState.NotFound, "management.revision.not-found");
        bool equivalent = CryptographicOperations.FixedTimeEquals(
            left.Value.CanonicalConfigurationUtf8.ToArray(), right.Value.CanonicalConfigurationUtf8.ToArray());
        ImmutableArray<GatewayRevisionDifference> differences = equivalent
            ? []
            : [new("$", "configuration.changed")];
        return new(GatewayApplicationReadState.Found, "management.compare.completed",
            new(leftRevisionId, rightRevisionId, equivalent, differences, false));
    }

    public async ValueTask<GatewayApplicationReadResult<GatewayRevisionExport>> ExportAsync(
        string namespaceId, string targetNodeId, string revisionId, CancellationToken cancellationToken = default)
    {
        GatewayManagedRecord<GatewayAcceptedRevision>? revision = await reader.GetRevisionAsync(namespaceId, targetNodeId, revisionId, cancellationToken).ConfigureAwait(false);
        if (revision is null)
            return new(GatewayApplicationReadState.NotFound, "management.revision.not-found");
        byte[] bytes = revision.Value.CanonicalConfigurationUtf8.ToArray();
        if (bytes.Length == 0)
            return new(GatewayApplicationReadState.Gone, "management.revision.content-gone");
        return new(GatewayApplicationReadState.Found, "management.export.completed", new(
            "hpd.gateway.export/v1", revisionId, revision.Value.ContentHashAlgorithm,
            revision.Value.ContentHashValue, ImmutableArray.Create(bytes)));
    }
}
