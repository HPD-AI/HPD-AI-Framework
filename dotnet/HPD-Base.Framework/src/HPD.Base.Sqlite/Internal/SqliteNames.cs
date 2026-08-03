using HPD.Base.Sqlite;

namespace HPD.Base.Sqlite;

internal sealed class SqliteNames
{
    /// <summary>Initializes a new instance.</summary>
    public SqliteNames(HPDBaseSqliteOptions options)
    {
        Prefix = options.SchemaPrefix;
        Collections = Prefix + "collections";
        ProviderState = Prefix + "provider_state";
        MutationJournal = Prefix + "mutation_journal";
        OperationReceipts = Prefix + "operation_receipts";
        SchemaIdentity = Prefix + "schema_identity";
        SchemaBaseline = Prefix + "schema_baseline";
        SchemaAssets = Prefix + "schema_assets";
        SchemaHistory = Prefix + "schema_history";
        SchemaLease = Prefix + "schema_lease";
        MutationJournalScopeIndex = "ix_" + Prefix + "mutation_journal_scope_position";
    }

    /// <summary>Gets the prefix.</summary>
    public string Prefix { get; }
    /// <summary>Gets the collections.</summary>
    public string Collections { get; }
    /// <summary>Gets the provider state.</summary>
    public string ProviderState { get; }
    /// <summary>Gets the mutation journal.</summary>
    public string MutationJournal { get; }
    /// <summary>Gets the durable atomic request receipt table.</summary>
    public string OperationReceipts { get; }
    /// <summary>Gets the physical store identity.</summary>
    public string SchemaIdentity { get; }
    /// <summary>Gets the schema baseline.</summary>
    public string SchemaBaseline { get; }
    /// <summary>Gets the schema assets.</summary>
    public string SchemaAssets { get; }
    /// <summary>Gets the schema history.</summary>
    public string SchemaHistory { get; }
    /// <summary>Gets the schema lease.</summary>
    public string SchemaLease { get; }
    /// <summary>Gets the mutation journal scope index.</summary>
    public string MutationJournalScopeIndex { get; }
}
