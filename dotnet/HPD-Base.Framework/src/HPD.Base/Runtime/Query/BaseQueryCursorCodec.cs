using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal enum BaseQueryCursorStatus
{
    Valid, Invalid, ScopeMismatch, QueryMismatch, Expired, VersionUnsupported,
    SchemaChanged, RestoreInvalidated, GuaranteeUnavailable, DirectionUnsupported,
    KeyTooLarge
}

internal readonly record struct BaseQueryCursorKey(bool Present, string Json);

internal sealed record BaseQueryCursorPayload
{
    public required QueryCursorGuarantee Guarantee { get; init; }
    public required QueryCursorDirection Direction { get; init; }
    public required long RestoreEpoch { get; init; }
    public required long SchemaGeneration { get; init; }
    public required long AppendHighWater { get; init; }
    public required long PurgeGeneration { get; init; }
    public required BaseQueryCursorKey[] Keys { get; init; }
    public required string RecordId { get; init; }
}

internal readonly record struct BaseQueryCursorReadResult(
    BaseQueryCursorStatus Status,
    BaseQueryCursorPayload? Payload);

internal sealed class BaseQueryCursorCodec(
    BaseOpaqueTokenProtector tokens,
    TimeProvider timeProvider)
{
    private const string Purpose = "hpd.base.query.cursor";
    private const byte Version = 1;
    private const int MinimumPlaintextLength = 110;
    private const int MaximumPlaintextLength = 8_192;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public string Protect(
        BaseQueryCursorPayload payload,
        RecordQuery query,
        int limit,
        string storeId,
        string collectionId,
        OperationContext context)
    {
        byte[] scope = ScopeDigest(storeId, collectionId, context);
        byte[] queryDigest = QueryDigest(query, limit);
        byte[] plaintext = Encode(payload, scope, queryDigest, timeProvider.GetUtcNow());
        try
        {
            return tokens.Protect(Purpose, Version, plaintext, new byte[32]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(scope);
            CryptographicOperations.ZeroMemory(queryDigest);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public BaseQueryCursorReadResult Unprotect(
        string token,
        RecordQuery query,
        int limit,
        string storeId,
        string collectionId,
        OperationContext context,
        long restoreEpoch,
        long schemaGeneration,
        QueryCursorGuarantee availableGuarantee,
        long purgeGeneration)
    {
        BaseOpaqueTokenResult unprotected = tokens.Unprotect(
            Purpose, Version, token, MinimumPlaintextLength, MaximumPlaintextLength, new byte[32]);
        if (unprotected.Status == BaseOpaqueTokenStatus.VersionUnsupported)
            return new(BaseQueryCursorStatus.VersionUnsupported, null);
        if (unprotected.Status != BaseOpaqueTokenStatus.Valid || unprotected.Plaintext is null)
            return new(BaseQueryCursorStatus.Invalid, null);

        byte[] plaintext = unprotected.Plaintext;
        byte[] scope = ScopeDigest(storeId, collectionId, context);
        byte[] queryDigest = QueryDigest(query, limit);
        try
        {
            if (!TryDecode(plaintext, out BaseQueryCursorPayload? payload, out DateTimeOffset issuedAt, out ReadOnlySpan<byte> storedScope, out ReadOnlySpan<byte> storedQuery))
                return new(BaseQueryCursorStatus.Invalid, null);
            if (!CryptographicOperations.FixedTimeEquals(storedScope, scope))
                return new(BaseQueryCursorStatus.ScopeMismatch, null);
            if (!CryptographicOperations.FixedTimeEquals(storedQuery, queryDigest))
                return new(BaseQueryCursorStatus.QueryMismatch, null);
            TimeSpan age = timeProvider.GetUtcNow() - issuedAt;
            if (age < TimeSpan.Zero) return new(BaseQueryCursorStatus.Invalid, null);
            if (age > Lifetime) return new(BaseQueryCursorStatus.Expired, null);
            if (payload!.RestoreEpoch != restoreEpoch) return new(BaseQueryCursorStatus.RestoreInvalidated, null);
            if (payload.SchemaGeneration != schemaGeneration) return new(BaseQueryCursorStatus.SchemaChanged, null);
            if (payload.Guarantee > availableGuarantee) return new(BaseQueryCursorStatus.GuaranteeUnavailable, null);
            if (payload.Direction != query.Page?.CursorDirection) return new(BaseQueryCursorStatus.DirectionUnsupported, null);
            if (payload.Guarantee == QueryCursorGuarantee.StableHistory && payload.PurgeGeneration != purgeGeneration)
                return new(BaseQueryCursorStatus.Expired, null);
            return new(BaseQueryCursorStatus.Valid, payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(scope);
            CryptographicOperations.ZeroMemory(queryDigest);
        }
    }

    private static byte[] Encode(BaseQueryCursorPayload payload, byte[] scope, byte[] query, DateTimeOffset issuedAt)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(issuedAt.ToUnixTimeSeconds());
        writer.Write(payload.RestoreEpoch); writer.Write(payload.SchemaGeneration);
        writer.Write(payload.AppendHighWater); writer.Write(payload.PurgeGeneration);
        writer.Write((byte)payload.Guarantee); writer.Write((byte)payload.Direction);
        writer.Write(scope); writer.Write(query);
        if (payload.Keys.Length > 8) throw new ArgumentOutOfRangeException(nameof(payload));
        writer.Write((byte)payload.Keys.Length);
        foreach (BaseQueryCursorKey key in payload.Keys)
        {
            writer.Write(key.Present);
            WriteBounded(writer, key.Json, 4_096);
        }
        WriteBounded(writer, payload.RecordId, 1_024);
        writer.Flush();
        if (stream.Length > MaximumPlaintextLength)
            throw new BaseQueryCursorKeyTooLargeException();
        return stream.ToArray();
    }

    private static bool TryDecode(
        byte[] bytes,
        out BaseQueryCursorPayload? payload,
        out DateTimeOffset issuedAt,
        out ReadOnlySpan<byte> scope,
        out ReadOnlySpan<byte> query)
    {
        payload = null; issuedAt = default; scope = default; query = default;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(reader.ReadInt64());
            long restore = reader.ReadInt64(); long schema = reader.ReadInt64();
            long high = reader.ReadInt64(); long purge = reader.ReadInt64();
            var guarantee = (QueryCursorGuarantee)reader.ReadByte();
            var direction = (QueryCursorDirection)reader.ReadByte();
            scope = bytes.AsSpan(42, 32); query = bytes.AsSpan(74, 32);
            stream.Position = 106;
            int count = reader.ReadByte();
            if (count > 8 || !Enum.IsDefined(guarantee) || !Enum.IsDefined(direction) || restore < 0 || schema < 0 || high < 0 || purge < 0)
                return false;
            var keys = new BaseQueryCursorKey[count];
            for (int index = 0; index < count; index++)
                keys[index] = new BaseQueryCursorKey(reader.ReadBoolean(), ReadBounded(reader, 4_096));
            string recordId = ReadBounded(reader, 1_024);
            if (stream.Position != stream.Length || string.IsNullOrEmpty(recordId)) return false;
            payload = new BaseQueryCursorPayload
            {
                Guarantee = guarantee, Direction = direction, RestoreEpoch = restore,
                SchemaGeneration = schema, AppendHighWater = high, PurgeGeneration = purge,
                Keys = keys, RecordId = recordId
            };
            return true;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or ArgumentException)
        {
            return false;
        }
    }

    private static void WriteBounded(BinaryWriter writer, string value, int maximumBytes)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > maximumBytes) throw new BaseQueryCursorKeyTooLargeException();
        writer.Write((ushort)bytes.Length); writer.Write(bytes);
    }

    private static string ReadBounded(BinaryReader reader, int maximumBytes)
    {
        int length = reader.ReadUInt16();
        if (length > maximumBytes) throw new InvalidDataException();
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private static byte[] ScopeDigest(string storeId, string collectionId, OperationContext context) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', storeId, collectionId, context.TenantId ?? "", context.ProjectId ?? "")));

    private static byte[] QueryDigest(RecordQuery query, int limit)
    {
        RecordQuery normalized = query with
        {
            Page = new QueryPage
            {
                Mode = QueryPaginationMode.Cursor,
                Limit = limit,
                CursorDirection = QueryCursorDirection.After
            },
            Count = QueryCountMode.None
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(normalized, HPDBaseJsonSerializerContext.Default.RecordQuery);
        return SHA256.HashData(json);
    }
}

internal sealed class BaseQueryCursorKeyTooLargeException : Exception;
