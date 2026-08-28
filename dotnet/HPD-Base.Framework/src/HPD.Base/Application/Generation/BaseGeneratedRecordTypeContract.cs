namespace HPD.Base;

/// <summary>Registers source-generated record-type identity without reflection.</summary>
public static class BaseGeneratedRecordTypeContract
{
    private static readonly Dictionary<Type, string> CollectionIds = [];
    private static readonly Dictionary<Type, string> RecordIdCollectionIds = [];
    private static readonly Lock Sync = new();

    /// <summary>Registers the stable collection identity for one generated record type.</summary>
    /// <typeparam name="TRecord">The generated persisted record type.</typeparam>
    /// <param name="collectionId">The stable collection identity.</param>
    public static void Register<TRecord>(string collectionId)
    {
        BaseApplicationId.Validate(collectionId, nameof(collectionId));
        lock (Sync)
        {
            if (CollectionIds.TryGetValue(typeof(TRecord), out string? existing)
                && !string.Equals(existing, collectionId, StringComparison.Ordinal))
                throw new InvalidOperationException("base.moduleMutation.invalid");
            CollectionIds[typeof(TRecord)] = new string(collectionId.AsSpan());
            RecordIdCollectionIds[typeof(BaseRecordId<TRecord>)] = new string(collectionId.AsSpan());
        }
    }

    internal static string GetCollectionId<TRecord>()
    {
        lock (Sync)
            return CollectionIds.TryGetValue(typeof(TRecord), out string? collectionId)
                ? collectionId
                : throw new InvalidOperationException("base.moduleMutation.invalid");
    }

    internal static bool MatchesCollectionId(Type recordType, string? collectionId)
    {
        ArgumentNullException.ThrowIfNull(recordType);
        if (collectionId is null) return false;
        lock (Sync)
            return CollectionIds.TryGetValue(recordType, out string? registered)
                && string.Equals(registered, collectionId, StringComparison.Ordinal);
    }

    internal static bool MatchesRecordIdType(Type recordIdType, string? collectionId)
    {
        ArgumentNullException.ThrowIfNull(recordIdType);
        if (collectionId is null) return false;
        lock (Sync)
            return RecordIdCollectionIds.TryGetValue(recordIdType, out string? registered)
                && string.Equals(registered, collectionId, StringComparison.Ordinal);
    }
}
