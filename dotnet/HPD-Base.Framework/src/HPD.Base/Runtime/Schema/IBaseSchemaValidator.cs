
namespace HPD.Base;

/// <summary>Defines the ibase schema validator contract.</summary>
public interface IBaseSchemaValidator
{
    /// <summary>Executes the validate create async operation.</summary>
    ValueTask<OperationResult<BaseValidatedPayload>> ValidateCreateAsync(
        BasePayloadValidationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the validate patch async operation.</summary>
    ValueTask<OperationResult<BaseValidatedPayload>> ValidatePatchAsync(
        BasePayloadValidationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the validate replace async operation.</summary>
    ValueTask<OperationResult<BaseValidatedPayload>> ValidateReplaceAsync(
        BasePayloadValidationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Represents a base payload validation request.</summary>
public sealed record BasePayloadValidationRequest
{
    /// <summary>Gets or sets the collection.</summary>
    public required CollectionDefinition Collection { get; init; }
    /// <summary>Gets or sets the principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets or sets the operation.</summary>
    public required OperationContext Operation { get; init; }
    /// <summary>Gets or sets the existing record.</summary>
    public RecordEnvelope? ExistingRecord { get; init; }
    /// <summary>Gets or sets the payload.</summary>
    public RecordPayload? Payload { get; init; }
    /// <summary>Gets or sets the patch.</summary>
    public RecordPayload? Patch { get; init; }
    /// <summary>Gets the stable field identifiers removed by a patch.</summary>
    public System.Collections.Immutable.ImmutableArray<string> RemovedFieldIds { get; init; } = [];
    /// <summary>Gets or sets the write mask.</summary>
    public FieldMask? WriteMask { get; init; }
}

/// <summary>Represents a base validated payload.</summary>
public sealed record BaseValidatedPayload
{
    /// <summary>Gets or sets the payload.</summary>
    public required RecordPayload Payload { get; init; }
    /// <summary>Gets or sets the changed fields.</summary>
    public string[]? ChangedFields { get; init; }
    /// <summary>Gets or sets the proposed record.</summary>
    public RecordEnvelope? ProposedRecord { get; init; }
}
