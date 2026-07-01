using HPD.Base.Sqlite.Configuration;

namespace HPD.Base.Sqlite.Internal;

internal sealed class SqliteNames
{
    public SqliteNames(HPDBaseSqliteOptions options)
    {
        Prefix = options.SchemaPrefix;
        Records = Prefix + "records";
        Collections = Prefix + "collections";
        ProviderState = Prefix + "provider_state";
        RecordsUpdatedIndex = "ix_" + Prefix + "records_collection_updated";
    }

    public string Prefix { get; }
    public string Records { get; }
    public string Collections { get; }
    public string ProviderState { get; }
    public string RecordsUpdatedIndex { get; }
}
