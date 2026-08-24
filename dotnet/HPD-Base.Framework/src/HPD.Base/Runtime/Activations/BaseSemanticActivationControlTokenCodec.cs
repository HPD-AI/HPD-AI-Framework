using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal enum BaseSemanticActivationControlTokenKind : byte
{
    Compact = 1,
    Remove = 2,
    ResumeCompact = 3,
    ResumeRemove = 4,
    ResolveCompact = 5,
    ResolveRemove = 6,
}

internal sealed record BaseSemanticActivationControlTokenPayload(
    BaseSemanticActivationControlTokenKind Kind,
    string ApplicationId,
    string LogicalStoreId,
    long RestoreEpoch,
    BaseSemanticActivationDefinitionKey Definition,
    ImmutableArray<byte> DefinitionSetChecksum,
    long SemanticAuthorityGeneration,
    long LiveCount,
    long RetiredCount,
    long AbsenceCount,
    ImmutableArray<byte> RetiredAuthorityChecksum,
    ImmutableArray<byte> DefinitionStateChecksum,
    ImmutableArray<byte> AbsenceAuthorityChecksum,
    BaseSemanticActivationMaintenanceLimits Limits,
    string? IdempotencyKey,
    DateTimeOffset ExpiresAtUtc);

internal sealed class BaseSemanticActivationControlTokenCodec(
    BaseOpaqueTokenProtector tokens,
    TimeProvider timeProvider)
{
    private const string Purpose = "hpd.base.semantic-activation.control.v1";
    private const byte Version = 1;

    internal BaseSemanticActivationControlToken Protect(BaseSemanticActivationControlTokenPayload payload) =>
        new(tokens.Protect(Purpose, Version, Encode(payload), Binding(payload.ApplicationId, payload.LogicalStoreId)));

    internal bool TryRead(BaseSemanticActivationControlToken token, string applicationId, string logicalStoreId,
        out BaseSemanticActivationControlTokenPayload? payload)
    {
        payload = null;
        BaseOpaqueTokenResult result = tokens.Unprotect(Purpose, Version, token.Value, 256, 4096,
            Binding(applicationId, logicalStoreId));
        if (result.Status != BaseOpaqueTokenStatus.Valid || result.Plaintext is null) return false;
        try
        {
            BaseSemanticActivationControlTokenPayload value = Decode(result.Plaintext);
            if (value.ApplicationId != applicationId || value.LogicalStoreId != logicalStoreId
                || value.ExpiresAtUtc <= timeProvider.GetUtcNow()) return false;
            payload = value; return true;
        }
        catch { return false; }
    }

    private static byte[] Binding(string applicationId, string storeId)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.controlBinding.v1\0"u8);
        Add(applicationId); Add(storeId); return hash.GetHashAndReset();
        void Add(string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); hash.AppendData(length); hash.AppendData(bytes); }
    }

    private static byte[] Encode(BaseSemanticActivationControlTokenPayload value)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)value.Kind); Text(writer, value.ApplicationId); Text(writer, value.LogicalStoreId); writer.Write(value.RestoreEpoch);
        Definition(writer, value.Definition); Bytes(writer, value.DefinitionSetChecksum.AsSpan()); writer.Write(value.SemanticAuthorityGeneration);
        writer.Write(value.LiveCount); writer.Write(value.RetiredCount); writer.Write(value.AbsenceCount);
        Bytes(writer, value.RetiredAuthorityChecksum.AsSpan()); Bytes(writer, value.DefinitionStateChecksum.AsSpan()); Bytes(writer, value.AbsenceAuthorityChecksum.AsSpan());
        writer.Write(value.Limits.PageSize); writer.Write(value.Limits.MaximumPages); writer.Write(value.Limits.MaximumRows);
        writer.Write(value.Limits.MaximumBytes); writer.Write(value.Limits.Deadline.Ticks);
        writer.Write(value.IdempotencyKey is not null); if (value.IdempotencyKey is not null) Text(writer, value.IdempotencyKey);
        writer.Write(value.ExpiresAtUtc.UtcTicks); writer.Flush(); return stream.ToArray();
    }

    internal static ImmutableArray<byte> CanonicalPayloadChecksum(BaseSemanticActivationControlTokenPayload value) =>
        SHA256.HashData(Encode(value)).ToImmutableArray();

    private static BaseSemanticActivationControlTokenPayload Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        var kind = (BaseSemanticActivationControlTokenKind)reader.ReadByte(); string application = Text(reader); string store = Text(reader);
        long restore = reader.ReadInt64(); BaseSemanticActivationDefinitionKey definition = Definition(reader); byte[] definitionSet = Bytes(reader, 32);
        long generation = reader.ReadInt64(), live = reader.ReadInt64(), retired = reader.ReadInt64(), absent = reader.ReadInt64();
        byte[] retiredChecksum = Bytes(reader, 32), stateChecksum = Bytes(reader, 32), absenceChecksum = Bytes(reader, 32);
        var limits = new BaseSemanticActivationMaintenanceLimits { PageSize = reader.ReadInt32(), MaximumPages = reader.ReadInt32(), MaximumRows = reader.ReadInt64(), MaximumBytes = reader.ReadInt64(), Deadline = TimeSpan.FromTicks(reader.ReadInt64()) };
        string? idempotency = reader.ReadBoolean() ? Text(reader) : null; long expires = reader.ReadInt64();
        if (stream.Position != stream.Length || !Enum.IsDefined(kind) || restore < 0 || generation <= 0 || live < 0 || retired < 0 || absent < 0
            || limits.PageSize is < 1 or > 256 || limits.MaximumPages <= 0 || limits.MaximumRows < 0 || limits.MaximumBytes < 0 || limits.Deadline <= TimeSpan.Zero)
            throw new FormatException();
        return new(kind, application, store, restore, definition, definitionSet.ToImmutableArray(), generation, live, retired, absent,
            retiredChecksum.ToImmutableArray(), stateChecksum.ToImmutableArray(), absenceChecksum.ToImmutableArray(), limits,
            idempotency, new DateTimeOffset(expires, TimeSpan.Zero));
    }

    private static void Definition(BinaryWriter writer, BaseSemanticActivationDefinitionKey value)
    { Text(writer, value.Id); writer.Write(value.Version); Bytes(writer, value.Checksum.AsSpan()); }
    private static BaseSemanticActivationDefinitionKey Definition(BinaryReader reader)
    { string id = Text(reader); int version = reader.ReadInt32(); byte[] checksum = Bytes(reader, 32); if (version <= 0) throw new FormatException(); return new() { Id = id, Version = version, Checksum = checksum.ToImmutableArray() }; }
    private static void Text(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); if (bytes.Length is < 1 or > 512) throw new FormatException(); writer.Write(bytes.Length); writer.Write(bytes); }
    private static string Text(BinaryReader reader) { int length = reader.ReadInt32(); if (length is < 1 or > 512) throw new FormatException(); byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException(); return Encoding.UTF8.GetString(bytes); }
    private static void Bytes(BinaryWriter writer, ReadOnlySpan<byte> value) { writer.Write(value.Length); writer.Write(value); }
    private static byte[] Bytes(BinaryReader reader, int exact) { int length = reader.ReadInt32(); if (length != exact) throw new FormatException(); byte[] value = reader.ReadBytes(length); if (value.Length != length) throw new EndOfStreamException(); return value; }
}
