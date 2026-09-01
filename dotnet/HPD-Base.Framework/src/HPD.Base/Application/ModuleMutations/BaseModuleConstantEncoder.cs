using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace HPD.Base;

internal static class BaseModuleConstantEncoder
{
    internal static byte[] Encode<TValue>(BaseModuleValueType authority, TValue value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            if (value is null) writer.WriteNullValue();
            else if (value is string text) writer.WriteStringValue(text);
            else if (value is bool boolean) writer.WriteBooleanValue(boolean);
            else if (value is int int32) writer.WriteNumberValue(int32);
            else if (value is long int64) writer.WriteNumberValue(int64);
            else if (value is uint uint32) writer.WriteNumberValue(uint32);
            else if (value is ulong uint64) writer.WriteNumberValue(uint64);
            else if (value is decimal number) writer.WriteRawValue(number.ToString(CultureInfo.InvariantCulture), skipInputValidation: false);
            else if (value is Guid guid) writer.WriteStringValue(guid.ToString("D"));
            else if (value is DateTimeOffset instant && instant.Offset == TimeSpan.Zero)
                writer.WriteStringValue(instant.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            else if (value is BaseBinary binary) writer.WriteStringValue(Convert.ToBase64String(binary.ToArray()));
            else if (value is BaseCanonicalJson canonical && canonical.IsValid) writer.WriteRawValue(canonical.Utf8.Span, skipInputValidation: false);
            else if (value is IBaseRecordIdValue recordId) writer.WriteStringValue(recordId.CanonicalValue);
            else if (BaseClosedEnumGeneratedContract.TryGetWire(typeof(TValue), value, out string wire)) writer.WriteStringValue(wire);
            else throw new InvalidOperationException("base.moduleMutation.invalid");
        }
        byte[] owned = buffer.WrittenSpan.ToArray();
        using JsonDocument document = JsonDocument.Parse(owned);
        if (document.RootElement.ValueKind == JsonValueKind.Null)
        {
            if (authority.Nullability != BaseFieldNullability.Nullable)
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }
        else
        {
            var field = new FieldDefinition
            {
                Id = "value", ApplicationName = "value", WireName = "value", Type = "scalar",
                Presence = authority.Presence, Nullability = authority.Nullability,
                ScalarKind = (BaseScalarKind)(int)authority.Kind,
                ScalarCodec = authority.Codec, ScalarConstraints = authority.Constraints,
                ScalarConstraintChecksum = authority.ConstraintChecksum,
            };
            if (BaseCanonicalRecordValidator.Validate(field, document.RootElement) is not null)
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }
        return owned;
    }
}
