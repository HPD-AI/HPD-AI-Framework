using HPD.Base.Sqlite;

namespace HPD.Base.Sqlite;

internal sealed class SqliteNames
{
    public SqliteNames(HPDBaseSqliteOptions options)
    {
        Prefix = options.SchemaPrefix;
        Records = Prefix + "records";
        Collections = Prefix + "collections";
        ProviderState = Prefix + "provider_state";
        MutationJournal = Prefix + "mutation_journal";
        RecordsUpdatedIndex = "ix_" + Prefix + "records_collection_updated";
        MutationJournalScopeIndex = "ix_" + Prefix + "mutation_journal_scope_position";
    }

    public string Prefix { get; }
    public string Records { get; }
    public string Collections { get; }
    public string ProviderState { get; }
    public string MutationJournal { get; }
    public string RecordsUpdatedIndex { get; }
    public string MutationJournalScopeIndex { get; }
}
