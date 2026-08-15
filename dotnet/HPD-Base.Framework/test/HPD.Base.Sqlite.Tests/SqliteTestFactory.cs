using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Base.Sqlite.Tests;

internal static class SqliteTestFactory
{
    public static SqliteRecordStore Create(
        HPDBaseSqliteOptions? options = null,
        TimeProvider? timeProvider = null,
        ISqliteTransactionController? transactions = null,
        ISqliteSessionOperationController? sessionOperations = null,
        ISqliteTransactionResourceDisposer? transactionResourceDisposer = null,
        ISqliteSchemaCommandController? schemaCommands = null,
        ISqliteAdministrationOperationController? administrationOperations = null,
        BaseOpaqueTokenProtector? tokenProtector = null,
        IEnumerable<ISqliteAtomicMutationProjection>? mutationProjectionContributors = null,
        bool initializeSchema = true)
    {
        options ??= new HPDBaseSqliteOptions { Collections = [Collection()] };
        if (!options.Collections.Any())
        {
            options.Collections = [Collection()];
        }
        var store = timeProvider is null
            && transactions is null
            && sessionOperations is null
            && transactionResourceDisposer is null
            && schemaCommands is null
            && administrationOperations is null
            && tokenProtector is null
            && mutationProjectionContributors is null
            ? new SqliteRecordStore(options, NullLoggerFactory.Instance)
            : new SqliteRecordStore(
                options,
                NullLoggerFactory.Instance,
                timeProvider ?? TimeProvider.System,
                transactions,
                sessionOperations,
                transactionResourceDisposer,
                schemaCommands,
                administrationOperations,
                tokenProtector,
                mutationProjectionContributors);
        if (initializeSchema)
            store.InitializeUnacceptedSchemaForTestsAsync().AsTask().GetAwaiter().GetResult();
        return store;
    }

    public static CollectionDefinition Collection(string id = "items") => new()
    {
        Id = id,
        Name = id,
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve,
        MutationMode = BaseCollectionMutationMode.Mutable
    };
}
