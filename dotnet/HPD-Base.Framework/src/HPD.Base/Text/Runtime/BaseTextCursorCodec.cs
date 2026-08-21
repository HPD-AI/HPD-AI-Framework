using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed class BaseTextCursorCodec(BaseOpaqueTokenProtector tokens, TimeProvider timeProvider)
{
    private const string Purpose = "base.text.cursor.v1"; private const byte Version = 1;
    internal BaseTextCursor Issue(BaseTextAuthoritySnapshot snapshot, ReadOnlySpan<byte> queryDigest, ReadOnlySpan<byte> constraintDigest, ReadOnlySpan<byte> boundary)
    {
        DateTimeOffset issued = timeProvider.GetUtcNow(), expires = checked(issued + TimeSpan.FromHours(24)); using var stream = new MemoryStream();
        Write(stream, issued.UtcTicks); Write(stream, expires.UtcTicks); WriteString(stream, snapshot.StoreIdentityDigest); Write(stream, snapshot.RestoreEpoch); Write(stream, snapshot.SchemaGeneration); WriteString(stream, snapshot.CollectionId); WriteString(stream, snapshot.TextIndexId); Write(stream, snapshot.TextIndexVersion); Write(stream, snapshot.TextIndexGeneration); WriteBytes(stream, queryDigest); WriteBytes(stream, constraintDigest); WriteBytes(stream, boundary);
        byte[] scope = Scope(snapshot.CollectionId, snapshot.TextIndexId, queryDigest, constraintDigest); string protectedText = tokens.Protect(Purpose, Version, stream.ToArray(), scope); return BaseTextCursor.Create(System.Collections.Immutable.ImmutableArray.Create(Encoding.ASCII.GetBytes(protectedText)));
    }
    internal bool TryRead(BaseTextCursor cursor, BaseTextAuthoritySnapshot snapshot, ReadOnlySpan<byte> queryDigest, ReadOnlySpan<byte> constraintDigest, out System.Collections.Immutable.ImmutableArray<byte> boundary)
    {
        boundary = []; BaseOpaqueTokenResult result = tokens.Unprotect(Purpose, Version, cursor.Encode(), 80, 32 * 1024, Scope(snapshot.CollectionId, snapshot.TextIndexId, queryDigest, constraintDigest)); if (result.Status != BaseOpaqueTokenStatus.Valid || result.Plaintext is null) return false;
        try
        {
            using var stream = new MemoryStream(result.Plaintext, writable: false); long issued = ReadLong(stream), expires = ReadLong(stream); string store = ReadString(stream); long restore = ReadLong(stream), schema = ReadLong(stream); string collection = ReadString(stream), index = ReadString(stream); int version = checked((int)ReadLong(stream)); long generation = ReadLong(stream); byte[] query = ReadBytes(stream), constraint = ReadBytes(stream), bytes = ReadBytes(stream);
            if (issued > timeProvider.GetUtcNow().UtcTicks || expires <= timeProvider.GetUtcNow().UtcTicks || store != snapshot.StoreIdentityDigest || restore != snapshot.RestoreEpoch || schema != snapshot.SchemaGeneration || collection != snapshot.CollectionId || index != snapshot.TextIndexId || version != snapshot.TextIndexVersion || generation != snapshot.TextIndexGeneration || !query.AsSpan().SequenceEqual(queryDigest) || !constraint.AsSpan().SequenceEqual(constraintDigest) || stream.Position != stream.Length) return false;
            boundary = System.Collections.Immutable.ImmutableArray.Create(bytes); return true;
        }
        catch { return false; }
    }
    private static byte[] Scope(string collection, string index, ReadOnlySpan<byte> query, ReadOnlySpan<byte> constraint) => SHA256.HashData([.. Encoding.UTF8.GetBytes(collection), 0, .. Encoding.UTF8.GetBytes(index), 0, .. query, .. constraint]);
    private static void Write(Stream stream, long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); stream.Write(bytes); }
    private static void WriteString(Stream stream, string value) => WriteBytes(stream, Encoding.UTF8.GetBytes(value));
    private static void WriteBytes(Stream stream, ReadOnlySpan<byte> value) { Span<byte> count = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(count, checked((uint)value.Length)); stream.Write(count); stream.Write(value); }
    private static long ReadLong(Stream stream) { Span<byte> bytes = stackalloc byte[8]; stream.ReadExactly(bytes); return BinaryPrimitives.ReadInt64BigEndian(bytes); }
    private static byte[] ReadBytes(Stream stream) { Span<byte> count = stackalloc byte[4]; stream.ReadExactly(count); int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(count)); if (length < 0 || length > 16 * 1024) throw new InvalidDataException(); byte[] value = new byte[length]; stream.ReadExactly(value); return value; }
    private static string ReadString(Stream stream) => Encoding.UTF8.GetString(ReadBytes(stream));
}
