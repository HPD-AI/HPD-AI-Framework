using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HPD.Base;

internal static class BaseReadCanonicalJsonAuthorityContract
{
    private static ReadOnlySpan<byte> Purpose => "hpd.base.read.canonical-json-authority.v1\0"u8;

    internal static BaseReadCanonicalJsonAuthority Create(string collectionId, FieldDefinition field)
    {
        ArgumentNullException.ThrowIfNull(field);
        BaseScalarConstraintSet constraints = field.ScalarConstraints
            ?? throw new InvalidOperationException("base.relational.read.invalid");
        if (field.ScalarKind != BaseScalarKind.CanonicalJson ||
            field.ScalarConstraintChecksum is not { IsValid: true } checksum ||
            constraints.MaximumCanonicalJsonBytes is not > 0 ||
            constraints.JsonShape is not { } shape || !Enum.IsDefined(shape) ||
            constraints.MaximumJsonDepth is not > 0 ||
            constraints.MaximumJsonArrayItems is not > 0 ||
            constraints.MaximumJsonObjectProperties is not > 0 ||
            constraints.MaximumJsonTotalNodes is not > 0 ||
            constraints.MaximumJsonTotalStringUtf8Bytes is not > 0 ||
            constraints.MaximumJsonTotalNameUtf8Bytes is not > 0)
            throw new InvalidOperationException("base.relational.read.invalid");

        var authority = new BaseReadCanonicalJsonAuthority
        {
            CollectionId = new string(collectionId.AsSpan()),
            FieldId = new string(field.Id.AsSpan()),
            ConstraintChecksum = checksum,
            MaximumCanonicalJsonBytes = constraints.MaximumCanonicalJsonBytes.Value,
            JsonShape = shape,
            MaximumJsonDepth = constraints.MaximumJsonDepth.Value,
            MaximumJsonArrayItems = constraints.MaximumJsonArrayItems.Value,
            MaximumJsonObjectProperties = constraints.MaximumJsonObjectProperties.Value,
            MaximumJsonTotalNodes = constraints.MaximumJsonTotalNodes.Value,
            MaximumJsonTotalStringUtf8Bytes = constraints.MaximumJsonTotalStringUtf8Bytes.Value,
            MaximumJsonTotalNameUtf8Bytes = constraints.MaximumJsonTotalNameUtf8Bytes.Value,
            AuthorityChecksum = default,
        };
        return authority with { AuthorityChecksum = Checksum(authority) };
    }

    internal static BaseCanonicalJsonLimits Limits(BaseReadCanonicalJsonAuthority authority) => new()
    {
        MaximumCanonicalBytes = authority.MaximumCanonicalJsonBytes,
        MaximumDepth = authority.MaximumJsonDepth,
        MaximumArrayItemsPerContainer = authority.MaximumJsonArrayItems,
        MaximumObjectPropertiesPerContainer = authority.MaximumJsonObjectProperties,
        MaximumTotalNodes = authority.MaximumJsonTotalNodes,
        MaximumTotalStringUtf8Bytes = authority.MaximumJsonTotalStringUtf8Bytes,
        MaximumTotalNameUtf8Bytes = authority.MaximumJsonTotalNameUtf8Bytes,
    };

    internal static bool Valid(BaseReadCanonicalJsonAuthority authority) =>
        authority.AuthorityChecksum.IsValid && authority.AuthorityChecksum == Checksum(authority);

    private static BaseSchemaAuthorityChecksum Checksum(BaseReadCanonicalJsonAuthority authority)
    {
        var writer = new ArrayBufferWriter<byte>();
        Write(writer, Purpose);
        Text(writer, authority.CollectionId);
        Text(writer, authority.FieldId);
        Write(writer, authority.ConstraintChecksum.ToArray());
        I32(writer, authority.MaximumCanonicalJsonBytes);
        I32(writer, (int)authority.JsonShape);
        I32(writer, authority.MaximumJsonDepth);
        I32(writer, authority.MaximumJsonArrayItems);
        I32(writer, authority.MaximumJsonObjectProperties);
        I32(writer, authority.MaximumJsonTotalNodes);
        I32(writer, authority.MaximumJsonTotalStringUtf8Bytes);
        I32(writer, authority.MaximumJsonTotalNameUtf8Bytes);
        return BaseSchemaAuthorityChecksum.Create(SHA256.HashData(writer.WrittenSpan));
    }

    private static void Text(IBufferWriter<byte> writer, string value)
    {
        byte[] bytes = BaseStrictUtf8.Encode(value);
        U32(writer, checked((uint)bytes.Length));
        Write(writer, bytes);
    }

    private static void I32(IBufferWriter<byte> writer, int value)
    {
        Span<byte> span = writer.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        writer.Advance(4);
    }

    private static void U32(IBufferWriter<byte> writer, uint value)
    {
        Span<byte> span = writer.GetSpan(4);
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        writer.Advance(4);
    }

    private static void Write(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        value.CopyTo(writer.GetSpan(value.Length));
        writer.Advance(value.Length);
    }
}
