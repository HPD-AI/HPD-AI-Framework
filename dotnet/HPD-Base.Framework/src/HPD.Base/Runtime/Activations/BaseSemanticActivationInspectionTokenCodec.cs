using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed record BaseSemanticActivationInspectionTokenPayload(
    string ApplicationId,
    string LogicalStoreId,
    long RestoreEpoch,
    BaseSemanticActivationDefinitionKey Definition,
    BaseSemanticActivationSlotState? State,
    int Take,
    BaseSemanticActivationProviderInspectionBoundary Boundary,
    DateTimeOffset ExpiresAtUtc);

internal sealed class BaseSemanticActivationInspectionTokenCodec(
    BaseOpaqueTokenProtector tokens,
    TimeProvider timeProvider)
{
    private const byte Version = 1;
    private const string Purpose = "hpd.base.semantic-activation.inspection.v1";

    internal BaseSemanticActivationInspectionToken Protect(
        BaseSemanticActivationInspectionTokenPayload payload)
    {
        byte[] binding = Binding(payload.ApplicationId, payload.LogicalStoreId, payload.Definition,
            payload.State, payload.Take);
        return new(tokens.Protect(Purpose, Version, Encode(payload), binding));
    }

    internal bool TryRead(
        BaseSemanticActivationInspectionToken token,
        string applicationId,
        string logicalStoreId,
        BaseSemanticActivationDefinitionKey definition,
        BaseSemanticActivationSlotState? state,
        int take,
        out BaseSemanticActivationInspectionTokenPayload? payload)
    {
        payload = null;
        if (token.Value.Length is < 1 or > 2048 || token.Value.Any(static value =>
                !(value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            return false;
        BaseOpaqueTokenResult result = tokens.Unprotect(Purpose, Version, token.Value, 128, 2048,
            Binding(applicationId, logicalStoreId, definition, state, take));
        if (result.Status != BaseOpaqueTokenStatus.Valid || result.Plaintext is null) return false;
        try
        {
            BaseSemanticActivationInspectionTokenPayload value = Decode(result.Plaintext);
            if (value.ApplicationId != applicationId || value.LogicalStoreId != logicalStoreId
                || value.Definition.Id != definition.Id || value.Definition.Version != definition.Version
                || !CryptographicOperations.FixedTimeEquals(value.Definition.Checksum.AsSpan(), definition.Checksum.AsSpan())
                || value.State != state || value.Take != take || value.RestoreEpoch < 0
                || value.ExpiresAtUtc <= timeProvider.GetUtcNow()) return false;
            payload = value; return true;
        }
        catch { return false; }
    }

    private static byte[] Binding(string applicationId, string logicalStoreId,
        BaseSemanticActivationDefinitionKey definition, BaseSemanticActivationSlotState? state, int take)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        Write(writer, "base.semanticActivation.inspectionBinding.v1"); Write(writer, applicationId); Write(writer, logicalStoreId);
        Definition(writer, definition); writer.Write(state is not null); if (state is not null) writer.Write((int)state.Value); writer.Write(take);
        writer.Flush(); return SHA256.HashData(stream.ToArray());
    }

    private static byte[] Encode(BaseSemanticActivationInspectionTokenPayload value)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        Write(writer, value.ApplicationId); Write(writer, value.LogicalStoreId); writer.Write(value.RestoreEpoch);
        Definition(writer, value.Definition); writer.Write(value.State is not null); if (value.State is not null) writer.Write((int)value.State.Value);
        writer.Write(value.Take); Write(writer, value.Boundary.DefinitionId); writer.Write(value.Boundary.ScopeBindingId.ToArray());
        Span<byte> key = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; value.Boundary.Key.CopyTo(key); writer.Write(key);
        writer.Write(value.Boundary.CapturedAuthorityGeneration); writer.Write(value.Boundary.RuntimeBoundaryChecksum.ToArray());
        writer.Write(value.ExpiresAtUtc.UtcTicks); writer.Flush(); return stream.ToArray();
    }

    private static BaseSemanticActivationInspectionTokenPayload Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        string application = Read(reader); string store = Read(reader); long restore = reader.ReadInt64();
        BaseSemanticActivationDefinitionKey definition = ReadDefinition(reader);
        BaseSemanticActivationSlotState? state = reader.ReadBoolean() ? (BaseSemanticActivationSlotState)reader.ReadInt32() : null;
        int take = reader.ReadInt32(); string boundaryDefinition = Read(reader); byte[] binding = reader.ReadBytes(32); byte[] key = reader.ReadBytes(32);
        long generation = reader.ReadInt64(); byte[] checksum = reader.ReadBytes(32); long expiry = reader.ReadInt64();
        if (stream.Position != stream.Length || restore < 0 || take is < 1 or > 256 || binding.Length != 32 || key.Length != 32
            || generation <= 0 || checksum.Length != 32 || state is not null && !Enum.IsDefined(state.Value)) throw new FormatException();
        return new(application, store, restore, definition, state, take, new()
        {
            DefinitionId = boundaryDefinition, ScopeBindingId = binding.ToImmutableArray(), Key = BaseSemanticActivationKeyDigest.Create(key),
            CapturedAuthorityGeneration = generation, RuntimeBoundaryChecksum = checksum.ToImmutableArray(),
        }, new DateTimeOffset(expiry, TimeSpan.Zero));
    }

    private static void Definition(BinaryWriter writer, BaseSemanticActivationDefinitionKey value)
    { Write(writer, value.Id); writer.Write(value.Version); writer.Write(value.Checksum.ToArray()); }
    private static BaseSemanticActivationDefinitionKey ReadDefinition(BinaryReader reader)
    { string id = Read(reader); int version = reader.ReadInt32(); byte[] checksum = reader.ReadBytes(32); if (version <= 0 || checksum.Length != 32) throw new FormatException(); return new() { Id = id, Version = version, Checksum = checksum.ToImmutableArray() }; }
    private static void Write(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
    private static string Read(BinaryReader reader) { int length = reader.ReadInt32(); if (length is < 1 or > 256) throw new FormatException(); byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException(); return Encoding.UTF8.GetString(bytes); }
}
