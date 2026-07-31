
namespace HPD.Base;

public interface IBaseSchemaValidator
{
    ValueTask<OperationResult<BaseValidatedPayload>> ValidateCreateAsync(
        BasePayloadValidationRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<BaseValidatedPayload>> ValidatePatchAsync(
        BasePayloadValidationRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<BaseValidatedPayload>> ValidateReplaceAsync(
        BasePayloadValidationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record BasePayloadValidationRequest
{
    public required CollectionDefinition Collection { get; init; }
    public required PrincipalContext Principal { get; init; }
    public required OperationContext Operation { get; init; }
    public RecordEnvelope? ExistingRecord { get; init; }
    public RecordPayload? Payload { get; init; }
    public RecordPayload? Patch { get; init; }
    public FieldMask? WriteMask { get; init; }
}

public sealed record BaseValidatedPayload
{
    public required RecordPayload Payload { get; init; }
    public string[]? ChangedFields { get; init; }
    public RecordEnvelope? ProposedRecord { get; init; }
}
