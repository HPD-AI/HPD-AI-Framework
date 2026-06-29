using System.Text.Json;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;
using HPD.Base.Runtime.Schema;

namespace HPD.Base.Runtime.Tests;

internal sealed class NormalizingSchemaValidator : IBaseSchemaValidator
{
    public ValueTask<OperationResult<BaseValidatedPayload>> ValidateCreateAsync(
        BasePayloadValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = request;
        return SuccessAsync("create-normalized");
    }

    public ValueTask<OperationResult<BaseValidatedPayload>> ValidatePatchAsync(
        BasePayloadValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = request;
        return SuccessAsync("patch-normalized", ["normalized"]);
    }

    public ValueTask<OperationResult<BaseValidatedPayload>> ValidateReplaceAsync(
        BasePayloadValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = request;
        return SuccessAsync("replace-normalized");
    }

    private static ValueTask<OperationResult<BaseValidatedPayload>> SuccessAsync(
        string value,
        string[]? changedFields = null) =>
        ValueTask.FromResult(OperationResults.Ok(new BaseValidatedPayload
        {
            Payload = FieldMapPayload(value),
            ChangedFields = changedFields
        }));

    private static RecordPayload FieldMapPayload(string value)
    {
        using var document = JsonDocument.Parse($$"""{"normalized":"{{value}}"}""");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = new Dictionary<string, JsonElement>
            {
                ["normalized"] = document.RootElement.GetProperty("normalized").Clone()
            }
        };
    }
}
