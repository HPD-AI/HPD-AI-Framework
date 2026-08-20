using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal enum BaseSubjectLifecycleTokenKind : byte { Cursor = 1, Checkpoint = 2 }
internal enum BaseSubjectLifecycleTokenReadStatus : byte { Valid = 0, Invalid = 1, Expired = 2 }

internal sealed record BaseSubjectLifecycleTokenPayload(
    string StoreInstanceId,
    long RestoreEpoch,
    long DeliveryEpoch,
    long ProjectionGeneration,
    long CheckpointGeneration,
    BaseSubjectLifecycleOrderingBoundary? Boundary,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

internal sealed class BaseSubjectLifecycleTokenCodec(BaseOpaqueTokenProtector tokens, TimeProvider timeProvider)
{
    private const byte Version = 1;

    internal BaseSubjectLifecycleCursor ProtectCursor(BaseSubjectLifecycleTokenPayload payload, ReadOnlySpan<byte> binding) =>
        new(Encoding.ASCII.GetBytes(tokens.Protect("hpd.base.subject-lifecycle.cursor.v1", Version, Encode(payload), binding)));

    internal BaseSubjectLifecycleCheckpoint ProtectCheckpoint(BaseSubjectLifecycleTokenPayload payload, ReadOnlySpan<byte> binding) =>
        new(Encoding.ASCII.GetBytes(tokens.Protect("hpd.base.subject-lifecycle.checkpoint.v1", Version, Encode(payload), binding)));

    internal bool TryReadCursor(BaseSubjectLifecycleCursor cursor, ReadOnlySpan<byte> binding, BaseSubjectIdKind idKind, out BaseSubjectLifecycleTokenPayload? payload) =>
        ReadCursor(cursor, binding, idKind, out payload) == BaseSubjectLifecycleTokenReadStatus.Valid;

    internal bool TryReadCheckpoint(BaseSubjectLifecycleCheckpoint checkpoint, ReadOnlySpan<byte> binding, BaseSubjectIdKind idKind, out BaseSubjectLifecycleTokenPayload? payload) =>
        ReadCheckpoint(checkpoint, binding, idKind, out payload) == BaseSubjectLifecycleTokenReadStatus.Valid;

    internal BaseSubjectLifecycleTokenReadStatus ReadCursor(BaseSubjectLifecycleCursor cursor, ReadOnlySpan<byte> binding, BaseSubjectIdKind idKind, out BaseSubjectLifecycleTokenPayload? payload) =>
        Read("hpd.base.subject-lifecycle.cursor.v1", Encoding.ASCII.GetString(cursor.ToArray()), binding, idKind, out payload);

    internal BaseSubjectLifecycleTokenReadStatus ReadCheckpoint(BaseSubjectLifecycleCheckpoint checkpoint, ReadOnlySpan<byte> binding, BaseSubjectIdKind idKind, out BaseSubjectLifecycleTokenPayload? payload) =>
        Read("hpd.base.subject-lifecycle.checkpoint.v1", Encoding.ASCII.GetString(checkpoint.ToArray()), binding, idKind, out payload);

    internal static byte[] Binding(string applicationId, BaseSubjectLifecycleConsumerDefinition consumer, string consumerChecksum, string contractChecksum, BaseOwnedSubjectScopeEvidence scope)
    {
        string canonical = $"base.subjectLifecycle.token.binding.v1\0{applicationId}\0{consumer.Id}\0{consumer.Version}\0{consumerChecksum}\0{consumer.ContractId}\0{consumer.ContractVersion}\0{contractChecksum}\0{(int)scope.Kind}\0{scope.Value}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    private BaseSubjectLifecycleTokenReadStatus Read(string purpose, string text, ReadOnlySpan<byte> binding, BaseSubjectIdKind idKind, out BaseSubjectLifecycleTokenPayload? payload)
    {
        payload = null; BaseOpaqueTokenResult result = tokens.Unprotect(purpose, Version, text, 49, 2048, binding);
        if (result.Status != BaseOpaqueTokenStatus.Valid || result.Plaintext is null) return BaseSubjectLifecycleTokenReadStatus.Invalid;
        try
        {
            payload = Decode(result.Plaintext, idKind);
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (payload.ExpiresAtUtc <= now) { payload = null; return BaseSubjectLifecycleTokenReadStatus.Expired; }
            if (payload.IssuedAtUtc > now) { payload = null; return BaseSubjectLifecycleTokenReadStatus.Invalid; }
            return BaseSubjectLifecycleTokenReadStatus.Valid;
        }
        catch { payload = null; return BaseSubjectLifecycleTokenReadStatus.Invalid; }
    }

    private static byte[] Encode(BaseSubjectLifecycleTokenPayload payload)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        Write(writer, payload.StoreInstanceId); writer.Write(payload.RestoreEpoch); writer.Write(payload.DeliveryEpoch); writer.Write(payload.ProjectionGeneration);
        writer.Write(payload.CheckpointGeneration); writer.Write(payload.IssuedAtUtc.UtcTicks); writer.Write(payload.ExpiresAtUtc.UtcTicks); writer.Write(payload.Boundary is not null);
        if (payload.Boundary is { } boundary)
        {
            writer.Write(boundary.CommitPosition.Value); Write(writer, boundary.SubjectId.Value); writer.Write(boundary.AuthorityEpoch.ToArray()); writer.Write(boundary.Incarnation.ToArray()); writer.Write(boundary.SubjectSequence);
        }
        writer.Flush(); return stream.ToArray();
    }

    private static BaseSubjectLifecycleTokenPayload Decode(byte[] bytes, BaseSubjectIdKind idKind)
    {
        using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        string store = Read(reader); long restore = reader.ReadInt64(); long delivery = reader.ReadInt64(); long projection = reader.ReadInt64();
        long checkpointGeneration = reader.ReadInt64(); DateTimeOffset issued = new(reader.ReadInt64(), TimeSpan.Zero); DateTimeOffset expires = new(reader.ReadInt64(), TimeSpan.Zero);
        BaseSubjectLifecycleOrderingBoundary? boundary = null;
        if (reader.ReadBoolean()) boundary = new() { CommitPosition = new(reader.ReadInt64()), SubjectId = BaseSubjectId.Create(Read(reader), idKind), AuthorityEpoch = new(reader.ReadBytes(16)), Incarnation = new(reader.ReadBytes(24)), SubjectSequence = reader.ReadInt64() };
        if (stream.Position != stream.Length || restore < 0 || delivery < 1 || projection < 1) throw new FormatException();
        return new(store, restore, delivery, projection, checkpointGeneration, boundary, issued, expires);
    }

    private static void Write(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
    private static string Read(BinaryReader reader) { int length = reader.ReadInt32(); if (length is < 1 or > 256) throw new FormatException(); byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException(); return Encoding.UTF8.GetString(bytes); }
}
