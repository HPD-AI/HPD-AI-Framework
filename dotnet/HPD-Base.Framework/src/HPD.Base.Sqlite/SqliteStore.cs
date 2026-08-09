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
                BaseStoreProviderCapabilities.CoLocatedVectors,
            RegistrationIds = ["sqlite.records", "sqlite.vector"],
        }, new Installer(configure));

    private sealed class Installer(Action<HPDBaseSqliteOptions>? configure) : IHPDBaseStoreInstaller
    {
        private bool _hasVectors;

        public HPDBaseStoreRegistrationReceipt Configure(HPDBaseStoreInstallationContext context)
        {
            CollectionDefinition[] collections = context.Collections.ToArray();
            string? storeId = null;
            context.Services.AddHPDBaseSqliteStore(options =>
            {
                configure?.Invoke(options);
                options.Collections = collections;
                storeId = options.StoreId;
            });
            _hasVectors = collections.SelectMany(static item => item.VectorIndexes ?? []).Any();
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
            return context.CreateReceipt(storeId ?? throw new InvalidOperationException("base.store.providerInvalid"));
        }

        public async ValueTask InitializeAsync(HPDBaseStoreInitializationContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseSqliteStore(context.Services);
            if (!_hasVectors) return;
            EnsurePlatform();
            SqliteRecordStore store = context.Services.GetRequiredService<SqliteRecordStore>();
            await using SqliteConnection connection = await store.VectorConnections.OpenAsync(cancellationToken).ConfigureAwait(false);
            SqliteVecNative.Load(connection);
            await using SqliteCommand version = connection.CreateCommand();
            version.CommandText = "SELECT vec_version();";
            version.CommandTimeout = store.VectorCommandTimeoutSeconds;
            string actual = Convert.ToString(await version.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (actual != "v0.1.7-alpha.2.1") throw new InvalidOperationException("base.vector.providerUnavailable");
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
