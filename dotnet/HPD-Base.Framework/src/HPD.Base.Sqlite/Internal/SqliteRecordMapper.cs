using HPD.Base;
using HPD.Base.Records;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite.Internal;

internal static class SqliteRecordMapper
{
    public static RecordEnvelope ReadEnvelope(SqliteDataReader reader, string storeId)
    {
        var collectionId = reader.GetString(0);
        var recordId = reader.GetString(1);
        var revision = reader.GetInt64(2);
        var createdAt = DateTimeOffset.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind);
        var updatedAt = DateTimeOffset.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind);
        var payload = SqliteRecordSerializer.Deserialize(reader.GetString(5));

        return new RecordEnvelope
        {
            CollectionId = collectionId,
            Id = new RecordId(recordId),
            Payload = payload,
            Metadata = Metadata(revision, createdAt, updatedAt, storeId)
        };
    }

    public static RecordMetadata Metadata(long revision, DateTimeOffset createdAt, DateTimeOffset updatedAt, string storeId) =>
        new()
        {
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Revision = Token(revision),
            ETag = $"\"sqlite:{revision}\"",
            StoreId = storeId
        };

    public static RevisionToken Token(long revision) => new($"sqlite:{revision}");

    public static bool TryParseRevision(RevisionToken? token, out long revision)
    {
        revision = 0;
        if (token is null)
        {
            return true;
        }

        var value = token.Value.Value;
        if (!value.StartsWith("sqlite:", StringComparison.Ordinal))
        {
            revision = -1;
            return true;
        }

        return long.TryParse(value.AsSpan("sqlite:".Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out revision)
            && revision > 0;
    }
}
