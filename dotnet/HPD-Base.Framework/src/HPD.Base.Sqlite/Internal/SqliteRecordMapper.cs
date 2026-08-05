using HPD.Base;

namespace HPD.Base.Sqlite;

internal static class SqliteRecordMapper
{
    /// <summary>Executes the metadata operation.</summary>
    public static RecordMetadata Metadata(long revision, DateTimeOffset createdAt, DateTimeOffset updatedAt, string storeId) =>
        new()
        {
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Revision = Token(revision),
            ETag = $"\"sqlite:{revision}\"",
            StoreId = storeId
        };

    /// <summary>Executes the token operation.</summary>
    public static RevisionToken Token(long revision) => new($"sqlite:{revision}");

    /// <summary>Executes the try parse revision operation.</summary>
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
