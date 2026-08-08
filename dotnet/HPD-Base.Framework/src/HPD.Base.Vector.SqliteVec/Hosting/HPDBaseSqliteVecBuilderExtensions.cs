using System.Runtime.InteropServices;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Vector.SqliteVec;

/// <summary>Installs the exact co-located sqlite-vec provider into a unified BASE builder.</summary>
public static class HPDBaseSqliteVecBuilderExtensions
{
    /// <summary>Installs the pinned sqlite-vec provider for every declared vector index.</summary>
    public static HPDBaseBuilder UseSqliteVec(this HPDBaseBuilder builder)
    { ArgumentNullException.ThrowIfNull(builder); return builder.Use(new Installer()); }

    private sealed class Installer : IHPDBaseBuilderExtension
    {
        public string Id => "vector.sqlitevec";
        public bool IsRecordProvider => false;
        public bool SupportsRequiredIndexes => false;
        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
        {
            if (collections.SelectMany(static item => item.VectorIndexes ?? []).Any(static index => index.Function == BaseVectorFunction.DotProductSimilarity)) throw new InvalidOperationException("HPD.Base.Vector.SqliteVec does not support dot-product indexes in L39.");
            var model = new SqliteVecModel(collections); services.AddSingleton(model); services.AddSingleton(static provider => new SqliteVecMutationProjection(provider.GetRequiredService<SqliteVecModel>())); services.AddSingleton<ISqliteAtomicMutationProjection>(static provider => provider.GetRequiredService<SqliteVecMutationProjection>()); services.AddSingleton(static provider => new SqliteVecProvider(provider.GetRequiredService<SqliteRecordStore>(), provider.GetRequiredService<SqliteVecModel>(), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<BaseOpaqueTokenProtector>(), provider.GetRequiredService<HPDBaseVectorSnapshot>())); services.AddSingleton<IBaseVectorProvider>(static provider => provider.GetRequiredService<SqliteVecProvider>()); services.AddSingleton<IBaseVectorAuthority>(static provider => provider.GetRequiredService<SqliteVecProvider>()); services.AddSingleton<IBaseVectorAdministrationProvider>(static provider => provider.GetRequiredService<SqliteVecProvider>());
        }
        public async ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            EnsurePlatform();
            SqliteRecordStore store = services.GetRequiredService<SqliteRecordStore>();
            await using SqliteConnection connection = await store.VectorConnections.OpenAsync(cancellationToken).ConfigureAwait(false); SqliteVecNative.Load(connection);
            await using SqliteCommand version = connection.CreateCommand(); version.CommandText = "SELECT vec_version();"; version.CommandTimeout = store.VectorCommandTimeoutSeconds; string actual = Convert.ToString(await version.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) ?? ""; if (actual != "v0.1.7-alpha.2.1") throw new InvalidOperationException("base.vector.providerUnavailable: the pinned sqlite-vec version is unavailable.");
        }
        private static void EnsurePlatform()
        {
            Architecture architecture = RuntimeInformation.ProcessArchitecture;
            bool supported = (OperatingSystem.IsLinux() && architecture is Architecture.X64 or Architecture.Arm64) || (OperatingSystem.IsMacOS() && architecture is Architecture.X64 or Architecture.Arm64) || (OperatingSystem.IsWindows() && architecture == Architecture.X64);
            if (!supported) throw new PlatformNotSupportedException("base.vector.providerUnsupportedPlatform: sqlite-vec has no certified native asset for this platform.");
        }
    }
}
