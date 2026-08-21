using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal enum BaseTextCursorReadStatus { Valid, Invalid, Expired, ScopeMismatch }

internal sealed class BaseTextCursorCodec(BaseOpaqueTokenProtector tokens, TimeProvider timeProvider)
{
    private const string Purpose = "base.text.cursor.v1"; private const byte Version = 1;
    private static readonly byte[] ProtectionScope = SHA256.HashData("HPDB-TEXT-CURSOR-PROTECTION-1\0"u8);
    internal BaseTextCursor Issue(BaseTextAuthoritySnapshot snapshot, ReadOnlySpan<byte> queryDigest, ReadOnlySpan<byte> constraintDigest, ReadOnlySpan<byte> authorityDigest, ReadOnlySpan<byte> boundary)
    {
        DateTimeOffset issued = timeProvider.GetUtcNow(), expires = checked(issued + TimeSpan.FromHours(24)); using var stream = new MemoryStream();
        Write(stream, issued.UtcTicks); Write(stream, expires.UtcTicks); WriteString(stream, snapshot.StoreIdentityDigest); Write(stream, snapshot.RestoreEpoch); Write(stream, snapshot.SchemaGeneration); WriteString(stream, snapshot.CollectionId); Write(stream, snapshot.PurgeGeneration); WriteString(stream, snapshot.TextIndexId); Write(stream, snapshot.TextIndexVersion); Write(stream, snapshot.TextIndexGeneration); Write(stream, snapshot.AuthoritativeHead.Value); Write(stream, snapshot.AppliedThrough.Value); Write(stream, snapshot.SearchVisibleThrough.Value); WriteBytes(stream, snapshot.AnalyzerReceipt.AsSpan()); WriteBytes(stream, snapshot.ScoringReceipt.AsSpan()); WriteBytes(stream, queryDigest); WriteBytes(stream, constraintDigest); WriteBytes(stream, authorityDigest); WriteBytes(stream, boundary);
        string protectedText = tokens.Protect(Purpose, Version, stream.ToArray(), ProtectionScope); return BaseTextCursor.Create(System.Collections.Immutable.ImmutableArray.Create(Encoding.ASCII.GetBytes(protectedText)));
    }
    internal BaseTextCursorReadStatus Read(BaseTextCursor cursor, BaseTextAuthoritySnapshot snapshot, ReadOnlySpan<byte> queryDigest, ReadOnlySpan<byte> constraintDigest, ReadOnlySpan<byte> authorityDigest, out System.Collections.Immutable.ImmutableArray<byte> boundary)
    {
        boundary = []; BaseOpaqueTokenResult result = tokens.Unprotect(Purpose, Version, cursor.Encode(), 80, 32 * 1024, ProtectionScope); if (result.Status != BaseOpaqueTokenStatus.Valid || result.Plaintext is null) return BaseTextCursorReadStatus.Invalid;
        try
        {
            using var stream = new MemoryStream(result.Plaintext, writable: false); long issued = ReadLong(stream), expires = ReadLong(stream); string store = ReadString(stream); long restore = ReadLong(stream), schema = ReadLong(stream); string collection = ReadString(stream); long purge = ReadLong(stream); string index = ReadString(stream); int version = checked((int)ReadLong(stream)); long generation = ReadLong(stream); long head = ReadLong(stream), applied = ReadLong(stream), visible = ReadLong(stream); byte[] analyzer = ReadBytes(stream), scoring = ReadBytes(stream), query = ReadBytes(stream), constraint = ReadBytes(stream), authority = ReadBytes(stream), bytes = ReadBytes(stream);
            if (stream.Position != stream.Length || issued > timeProvider.GetUtcNow().UtcTicks) return BaseTextCursorReadStatus.Invalid;
            if (expires <= timeProvider.GetUtcNow().UtcTicks) return BaseTextCursorReadStatus.Expired;
            if (store != snapshot.StoreIdentityDigest || restore != snapshot.RestoreEpoch || schema != snapshot.SchemaGeneration || collection != snapshot.CollectionId || purge != snapshot.PurgeGeneration || index != snapshot.TextIndexId || version != snapshot.TextIndexVersion || generation != snapshot.TextIndexGeneration || head != snapshot.AuthoritativeHead.Value || applied != snapshot.AppliedThrough.Value || visible != snapshot.SearchVisibleThrough.Value || !analyzer.AsSpan().SequenceEqual(snapshot.AnalyzerReceipt.AsSpan()) || !scoring.AsSpan().SequenceEqual(snapshot.ScoringReceipt.AsSpan()) || !query.AsSpan().SequenceEqual(queryDigest) || !constraint.AsSpan().SequenceEqual(constraintDigest) || !authority.AsSpan().SequenceEqual(authorityDigest)) return BaseTextCursorReadStatus.ScopeMismatch;
            boundary = System.Collections.Immutable.ImmutableArray.Create(bytes); return BaseTextCursorReadStatus.Valid;
        }
        catch { return BaseTextCursorReadStatus.Invalid; }
    }
    private static void Write(Stream stream, long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); stream.Write(bytes); }
    private static void WriteString(Stream stream, string value) => WriteBytes(stream, Encoding.UTF8.GetBytes(value));
    private static void WriteBytes(Stream stream, ReadOnlySpan<byte> value) { Span<byte> count = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(count, checked((uint)value.Length)); stream.Write(count); stream.Write(value); }
    private static long ReadLong(Stream stream) { Span<byte> bytes = stackalloc byte[8]; stream.ReadExactly(bytes); return BinaryPrimitives.ReadInt64BigEndian(bytes); }
    private static byte[] ReadBytes(Stream stream) { Span<byte> count = stackalloc byte[4]; stream.ReadExactly(count); int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(count)); if (length < 0 || length > 16 * 1024) throw new InvalidDataException(); byte[] value = new byte[length]; stream.ReadExactly(value); return value; }
    private static string ReadString(Stream stream) => Encoding.UTF8.GetString(ReadBytes(stream));
}
