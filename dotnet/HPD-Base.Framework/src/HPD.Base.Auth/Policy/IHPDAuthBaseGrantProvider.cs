using HPD.Base;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base.Auth;

/// <summary>
/// Provides BASE grants derived from HPD.Auth-backed host state.
/// </summary>
public interface IHPDBaseAuthGrantProvider
{
    /// <summary>
    /// Gets grants for the supplied BASE policy evaluation request.
    /// </summary>
    /// <param name="request">The grant request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The grants available for the request.</returns>
    ValueTask<IReadOnlyList<AccessGrant>> GetGrantsAsync(
        HPDBaseAuthGrantRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Describes the context used to load HPD.Auth-derived BASE grants.
/// </summary>
public sealed record HPDBaseAuthGrantRequest
{
    /// <summary>
    /// Gets the BASE principal.
    /// </summary>
    public required PrincipalContext Principal { get; init; }

    /// <summary>
    /// Gets the BASE operation.
    /// </summary>
    public required OperationContext Operation { get; init; }

    /// <summary>
    /// Gets the target collection.
    /// </summary>
    public required CollectionDefinition Collection { get; init; }

    /// <summary>
    /// Gets the policy resource.
    /// </summary>
    public required PolicyResource Resource { get; init; }

    /// <summary>
    /// Gets the normalized BASE operation kind.
    /// </summary>
    public BaseOperationKind Action => Operation.Operation;

    /// <summary>
    /// Gets the target collection identifier.
    /// </summary>
    public string CollectionId => Collection.Id;

    /// <summary>
    /// Gets the target record identifier when the operation addresses one record.
    /// </summary>
    public string? TargetRecordId => Resource.RecordId ?? Operation.RecordId;

    /// <summary>
    /// Gets the existing record loaded by the runtime for update/delete operations.
    /// </summary>
    public RecordEnvelope? ExistingRecord => Resource.ExistingRecord;

    /// <summary>
    /// Gets the proposed payload for create/update operations.
    /// </summary>
    public RecordPayload? ProposedPayload => Resource.ProposedPayload;

    /// <summary>
    /// Gets the proposed record for update operations when the runtime can compute it.
    /// </summary>
    public RecordEnvelope? ProposedRecord => Resource.ProposedRecord;

    /// <summary>
    /// Gets the normalized subject identifier.
    /// </summary>
    public string? SubjectId => Principal.SubjectId;

    /// <summary>
    /// Gets the normalized tenant identifier.
    /// </summary>
    public string? TenantId => Principal.CurrentTenantId;

    /// <summary>
    /// Gets a stable non-reversible fingerprint for safe grant diagnostics.
    /// </summary>
    public string SubjectFingerprint
    {
        get
        {
            var material = $"{TenantId ?? string.Empty}\n{SubjectId ?? string.Empty}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
