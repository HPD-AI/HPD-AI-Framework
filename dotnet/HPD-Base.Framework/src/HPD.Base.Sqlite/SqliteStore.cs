using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Sqlite;

/// <summary>Creates the complete SQLite authoritative store bundle for HPD Base.</summary>
public static class SqliteStore
{
    /// <summary>Creates a validated SQLite store descriptor.</summary>
    /// <param name="configure">Optional SQLite provider configuration.</param>
    /// <returns>The immutable provider descriptor selected with <see cref="HPDBaseBuilder.UseStore"/>.</returns>
    public static HPDBaseStoreProvider Configure(Action<HPDBaseSqliteOptions>? configure = null) =>
        HPDBaseStoreProviderFactory.Create(new BaseStoreProviderDescriptor
        {
            Kind = "sqlite",
            ProtocolVersion = HPDBaseStoreProviderFactory.ProtocolVersion,
            Capabilities = BaseStoreProviderCapabilities.Records |
                BaseStoreProviderCapabilities.AtomicMutations |
                BaseStoreProviderCapabilities.RequiredIndexes |
                BaseStoreProviderCapabilities.RelationalExecution |
                BaseStoreProviderCapabilities.TransactionalJournal |
                BaseStoreProviderCapabilities.HistoricalReads |
                BaseStoreProviderCapabilities.Administration |
                BaseStoreProviderCapabilities.CoLocatedVectors |
                BaseStoreProviderCapabilities.CoLocatedTextSearch,
            RegistrationIds = ["sqlite.records", "sqlite.vector"],
            SubjectReferences = BaseSubjectProviderCapabilities.BuiltIn,
            SubjectLifecycle = BaseSubjectLifecycleProviderCapabilities.BuiltIn,
            SubjectRetirement = BaseSubjectRetirementProviderCapabilities.BuiltIn,
            ModuleMutations = new BaseModuleMutationCapability
            {
                Supported = true, SerializableExecution = true, DurableReceipts = true,
                GenerationCells = true, AtomicRecordAndGenerationCommit = true,
                MaximumLimits = BaseModuleMutationPlatform.MaximumLimits,
            },
            TextSearch = BaseTextPlatform.ProviderCapability(BaseTextProviderClass.CoLocatedTransactional),
            Activations = BaseActivationCapabilityContract.BuiltIn("hpd.base.sqlite.activations.v2"),
            SemanticActivations = BaseSemanticActivationCapabilityContract.BuiltIn(durable: true),
        }, new Installer(configure));

    private sealed class Installer(Action<HPDBaseSqliteOptions>? configure) : IHPDBaseStoreInstaller
    {
        private bool _hasVectors;
        private bool _hasText;

        public HPDBaseStoreRegistrationReceipt Configure(HPDBaseStoreInstallationContext context)
        {
            CollectionDefinition[] collections = context.Collections.ToArray();
            string? storeId = null;
            context.Services.AddHPDBaseSqliteStore(options =>
            {
                configure?.Invoke(options);
                options.Collections = collections;
                options.ExportedSubjects = context.ExportedSubjects.ToArray();
                options.ModuleMutations = context.ModuleMutations.ToArray();
                options.ModuleGenerationCells = context.ModuleGenerationCells.ToArray();
                options.SemanticActivations = context.SemanticActivations.ToArray();
                options.SemanticActivationMigrations = context.SemanticActivationMigrations.ToArray();
                options.SemanticActivationApplicationId = context.ApplicationId;
                options.SemanticActivationOwnerGeneration = context.SemanticActivationOwnerGeneration;
                options.SemanticActivationDefinitionSetChecksum = context.SemanticActivationDefinitionSetChecksum.ToArray();
                options.SubjectLifecycleConsumers = context.SubjectLifecycleConsumers.ToArray();
                options.SubjectRetirementConsumers = context.SubjectRetirementConsumers.ToArray();
                options.SubjectRetirementPolicies = context.SubjectRetirementPolicies.ToArray();
                options.SubjectLifecycleInspectionAuthorities = context.SubjectLifecycleInspectionAuthorities.ToArray();
                storeId = options.StoreId;
            });
            _hasVectors = collections.SelectMany(static item => item.VectorIndexes ?? []).Any();
            _hasText = collections.SelectMany(static item => item.TextIndexes ?? []).Any();
            if (_hasVectors)
            {
                if (collections.SelectMany(static item => item.VectorIndexes ?? []).Any(static index => index.Function == BaseVectorFunction.DotProductSimilarity))
                    throw new InvalidOperationException("The SQLite vector provider does not support dot-product indexes.");
                var model = new SqliteVecModel(collections);
                context.Services.AddSingleton(model);
                context.Services.AddSingleton(static provider => new SqliteVecMutationProjection(provider.GetRequiredService<SqliteVecModel>()));
                context.Services.AddSingleton<ISqliteAtomicMutationProjection>(static provider => provider.GetRequiredService<SqliteVecMutationProjection>());
                context.Services.AddSingleton(static provider => new SqliteVecProvider(provider.GetRequiredService<SqliteRecordStore>(), provider.GetRequiredService<SqliteVecModel>(), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<BaseOpaqueTokenProtector>(), provider.GetRequiredService<HPDBaseVectorSnapshot>()));
                context.Services.AddSingleton<IBaseVectorProvider>(static provider => provider.GetRequiredService<SqliteVecProvider>());
                context.Services.AddSingleton<IBaseVectorAuthority>(static provider => provider.GetRequiredService<SqliteVecProvider>());
                context.Services.AddSingleton<IBaseVectorAdministrationProvider>(static provider => provider.GetRequiredService<SqliteVecProvider>());
            }
            if (_hasText)
            {
                var model = new SqliteTextModel(collections);
                context.Services.AddSingleton(model);
                context.Services.AddSingleton(static provider => new SqliteTextMutationProjection(provider.GetRequiredService<SqliteTextModel>()));
                context.Services.AddSingleton<ISqliteAtomicMutationProjection>(static provider => provider.GetRequiredService<SqliteTextMutationProjection>());
                context.Services.AddSingleton<SqliteTextProvider>();
                context.Services.AddSingleton<IBaseTextProvider>(static provider => provider.GetRequiredService<SqliteTextProvider>());
            }
            return context.CreateReceipt(storeId ?? throw new InvalidOperationException("base.store.providerInvalid"));
        }

        public async ValueTask InitializeAsync(HPDBaseStoreInitializationContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseSqliteStore(context.Services);
            if (!_hasVectors && !_hasText) return;
            SqliteRecordStore store = context.Services.GetRequiredService<SqliteRecordStore>();
            await using SqliteConnection connection = await store.VectorConnections.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (_hasVectors) { EnsurePlatform(); SqliteVecNative.Load(connection); await using SqliteCommand version = connection.CreateCommand(); version.CommandText = "SELECT vec_version();"; version.CommandTimeout = store.VectorCommandTimeoutSeconds; string actual = Convert.ToString(await version.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) ?? ""; if (actual != "v0.1.7-alpha.2.1") throw new InvalidOperationException("base.vector.providerUnavailable"); }
            if (_hasText) try { await ProbeTextAsync(connection, store.VectorCommandTimeoutSeconds, cancellationToken).ConfigureAwait(false); } catch { throw new InvalidOperationException(BaseTextErrorCodes.CapabilityUnavailable); }
        }

        private static async ValueTask ProbeTextAsync(SqliteConnection connection, int timeoutSeconds, CancellationToken cancellationToken)
        {
            await using (SqliteCommand create = connection.CreateCommand()) { create.CommandTimeout = timeoutSeconds; create.CommandText = "CREATE VIRTUAL TABLE temp.hpd_base_text_probe USING fts5(value, tokenize='unicode61 remove_diacritics 0'); INSERT INTO temp.hpd_base_text_probe(rowid,value) VALUES(1,'portable search'),(2,'transaction rollback');"; await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
            await using (SqliteCommand phrase = connection.CreateCommand()) { phrase.CommandTimeout = timeoutSeconds; phrase.CommandText = "SELECT COUNT(*) FROM temp.hpd_base_text_probe WHERE value MATCH '\"portable search\"';"; if (Convert.ToInt64(await phrase.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 1) throw new InvalidOperationException(); }
            await using (SqliteCommand prefix = connection.CreateCommand()) { prefix.CommandTimeout = timeoutSeconds; prefix.CommandText = "SELECT COUNT(*) FROM temp.hpd_base_text_probe WHERE value MATCH 'port*';"; if (Convert.ToInt64(await prefix.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 1) throw new InvalidOperationException(); }
            await using (SqliteTransaction transaction = connection.BeginTransaction()) { await using SqliteCommand delete = connection.CreateCommand(); delete.Transaction = transaction; delete.CommandTimeout = timeoutSeconds; delete.CommandText = "DELETE FROM temp.hpd_base_text_probe WHERE rowid=1;"; await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); }
            await using (SqliteCommand verify = connection.CreateCommand()) { verify.CommandTimeout = timeoutSeconds; verify.CommandText = "SELECT COUNT(*) FROM temp.hpd_base_text_probe WHERE rowid=1; DROP TABLE temp.hpd_base_text_probe;"; if (Convert.ToInt64(await verify.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 1) throw new InvalidOperationException(); }
        }

        private static void EnsurePlatform()
        {
            Architecture architecture = RuntimeInformation.ProcessArchitecture;
            bool supported = (OperatingSystem.IsLinux() && architecture is Architecture.X64 or Architecture.Arm64) ||
                (OperatingSystem.IsMacOS() && architecture is Architecture.X64 or Architecture.Arm64) ||
                (OperatingSystem.IsWindows() && architecture == Architecture.X64);
            if (!supported) throw new PlatformNotSupportedException("base.vector.providerUnsupportedPlatform");
        }
    }
}
