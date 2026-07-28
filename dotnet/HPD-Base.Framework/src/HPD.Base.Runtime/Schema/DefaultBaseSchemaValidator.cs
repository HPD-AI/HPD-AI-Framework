using System.Text.Json;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;
using HPD.Base.Runtime.Serialization;
using HPD.Base.Schema;

namespace HPD.Base.Runtime.Schema;

internal sealed class DefaultBaseSchemaValidator : IBaseSchemaValidator
{
    public ValueTask<OperationResult<BaseValidatedPayload>> ValidateCreateAsync(
        BasePayloadValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ValidatePayload(
            request.Collection,
            request.Payload,
            request.Operation,
            SchemaValidationOperation.Create));
    }

    public ValueTask<OperationResult<BaseValidatedPayload>> ValidatePatchAsync(
        BasePayloadValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Patch?.Kind != RecordPayloadKind.FieldMap)
        {
            return ValueTask.FromResult(OperationResults.Unsupported<BaseValidatedPayload>(UnsupportedPatchError()));
        }

        if (request.Patch.Fields is null || request.Patch.Fields.Count == 0)
        {
            return ValueTask.FromResult(OperationResults.ValidationFailed<BaseValidatedPayload>(new BaseError
            {
                Code = "base.runtime.patch.empty",
                Message = "Patch must contain at least one top-level field.",
                Category = ErrorCategory.Validation
            }));
        }

        return ValueTask.FromResult(ValidatePayload(
            request.Collection,
            request.Patch,
            request.Operation,
            SchemaValidationOperation.Patch,
            request.Patch.Fields?.Keys.ToArray()));
    }

    public ValueTask<OperationResult<BaseValidatedPayload>> ValidateReplaceAsync(
        BasePayloadValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ValidatePayload(
            request.Collection,
            request.Payload,
            request.Operation,
            SchemaValidationOperation.Replace));
    }

    private static OperationResult<BaseValidatedPayload> ValidatePayload(
        CollectionDefinition collection,
        RecordPayload? payload,
        OperationContext operationContext,
        SchemaValidationOperation operation,
        string[]? changedFields = null)
    {
        if (payload is null)
        {
            return OperationResults.ValidationFailed<BaseValidatedPayload>(new BaseError
            {
                Code = "base.runtime.payload.required",
                Message = "A record payload is required.",
                Category = ErrorCategory.Validation
            });
        }

        var fieldValues = ExtractFieldValues(payload, out var extractionError);
        if (extractionError is not null)
        {
            return OperationResults.ValidationFailed<BaseValidatedPayload>(extractionError);
        }

        var runtimeWrittenFields = ApplyRuntimeDefaultsAndGeneration(collection, fieldValues, operationContext, operation);
        var validationError = ValidateFields(collection, fieldValues, operation, runtimeWrittenFields);
        if (validationError is not null)
        {
            return OperationResults.ValidationFailed<BaseValidatedPayload>(validationError);
        }

        return OperationResults.Ok(new BaseValidatedPayload
        {
            Payload = NormalizedPayload(fieldValues),
            ChangedFields = changedFields is null
                ? runtimeWrittenFields.Count == 0 ? null : runtimeWrittenFields.ToArray()
                : changedFields.Concat(runtimeWrittenFields).Distinct(StringComparer.Ordinal).ToArray()
        });
    }

    private static Dictionary<string, JsonElement> ExtractFieldValues(RecordPayload payload, out BaseError? error)
    {
        error = null;
        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            return payload.Fields ?? [];
        }

        if (payload.Json.ValueKind != JsonValueKind.Object)
        {
            error = new BaseError
            {
                Code = "base.runtime.payload.objectRequired",
                Message = "JSON payloads must be objects.",
                Category = ErrorCategory.Validation
            };
            return [];
        }

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in payload.Json.EnumerateObject())
        {
            values[property.Name] = property.Value.Clone();
        }

        return values;
    }

    private static BaseError? ValidateFields(
        CollectionDefinition collection,
        Dictionary<string, JsonElement> fieldValues,
        SchemaValidationOperation operation,
        HashSet<string> runtimeWrittenFields)
    {
        var fields = (collection.Fields ?? []).ToDictionary(field => field.Name, StringComparer.Ordinal);

        if (collection.UnknownFields == UnknownFieldPolicy.Reject)
        {
            var unknown = fieldValues.Keys.FirstOrDefault(name => !fields.ContainsKey(name));
            if (unknown is not null)
            {
                return ValidationError("base.runtime.payload.unknownField", $"Unknown field '{unknown}'.", unknown);
            }
        }

        if (operation is SchemaValidationOperation.Create or SchemaValidationOperation.Replace)
        {
            foreach (var field in fields.Values)
            {
                if (field.Required && !fieldValues.ContainsKey(field.Name))
                {
                    return ValidationError("base.runtime.payload.requiredField", $"Required field '{field.Name}' is missing.", field.Name);
                }
            }
        }

        foreach (var (name, value) in fieldValues)
        {
            if (!fields.TryGetValue(name, out var field))
            {
                continue;
            }

            if (!field.Nullable && value.ValueKind == JsonValueKind.Null)
            {
                return ValidationError("base.runtime.payload.nonNullable", $"Field '{name}' cannot be null.", name);
            }

            var updateOperation = operation is SchemaValidationOperation.Patch or SchemaValidationOperation.Replace;
            var runtimeWritten = runtimeWrittenFields.Contains(name);
            if (!runtimeWritten && (field.ReadOnly || field.System || (updateOperation && field.Visibility?.HiddenInUpdate == true)))
            {
                return ValidationError("base.runtime.payload.readOnlyField", $"Field '{name}' cannot be written.", name);
            }

            if (!runtimeWritten && operation == SchemaValidationOperation.Create && field.Visibility?.HiddenInCreate == true)
            {
                return ValidationError("base.runtime.payload.hiddenInCreate", $"Field '{name}' cannot be written on create.", name);
            }

            if (!runtimeWritten && field.Visibility?.AdminOnly == true)
            {
                return ValidationError("base.runtime.payload.adminOnlyField", $"Field '{name}' cannot be written by the default runtime validator.", name);
            }
        }

        return null;
    }

    private static HashSet<string> ApplyRuntimeDefaultsAndGeneration(
        CollectionDefinition collection,
        Dictionary<string, JsonElement> fieldValues,
        OperationContext operationContext,
        SchemaValidationOperation operation)
    {
        var runtimeWrittenFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in collection.Fields ?? [])
        {
            if (!fieldValues.ContainsKey(field.Name)
                && operation is SchemaValidationOperation.Create or SchemaValidationOperation.Replace
                && field.Default is { Owner: EnforcementOwner.Runtime, Kind: DefaultValueKind.Literal } defaultValue)
            {
                fieldValues[field.Name] = defaultValue.Value.Clone();
                runtimeWrittenFields.Add(field.Name);
            }

            if (field.Generated is not { Owner: EnforcementOwner.Runtime } generated)
            {
                continue;
            }

            var applies = operation switch
            {
                SchemaValidationOperation.Create => generated.OnCreate,
                SchemaValidationOperation.Patch or SchemaValidationOperation.Replace => generated.OnUpdate,
                _ => false
            };
            if (!applies || !TryGenerateValue(generated, operationContext, out var generatedValue))
            {
                continue;
            }

            fieldValues[field.Name] = generatedValue;
            runtimeWrittenFields.Add(field.Name);
        }

        return runtimeWrittenFields;
    }

    private static bool TryGenerateValue(
        GenerationDescriptor generated,
        OperationContext operationContext,
        out JsonElement value)
    {
        switch (generated.Kind)
        {
            case GenerationKind.Id when operationContext.RecordId is not null:
                value = StringElement(operationContext.RecordId);
                return true;
            case GenerationKind.Timestamp:
                value = StringElement(operationContext.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                return true;
            default:
                value = default;
                return false;
        }
    }

    private static RecordPayload NormalizedPayload(Dictionary<string, JsonElement> fieldValues) => new()
    {
        Kind = RecordPayloadKind.FieldMap,
        Fields = fieldValues.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.Ordinal)
    };

    private static JsonElement StringElement(string value)
        => JsonSerializer.SerializeToElement(
            value,
            HPDBaseRuntimeJsonSerializerContext.Default.String);

    private static BaseError ValidationError(string code, string message, string target) => new()
    {
        Code = code,
        Message = message,
        Category = ErrorCategory.Validation,
        Target = target,
        Validation =
        [
            new ValidationIssue
            {
                Path = target,
                Code = code,
                Message = message
            }
        ]
    };

    private static BaseError UnsupportedPatchError() => new()
    {
        Code = "base.runtime.patch.unsupportedShape",
        Message = "Portable patch requires a field-map payload.",
        Category = ErrorCategory.Unsupported
    };

    private enum SchemaValidationOperation
    {
        Create,
        Patch,
        Replace
    }
}
