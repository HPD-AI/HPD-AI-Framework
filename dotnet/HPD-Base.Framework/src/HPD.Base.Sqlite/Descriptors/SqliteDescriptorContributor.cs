using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite;

internal sealed class SqliteDescriptorContributor : IBaseDescriptorContributor
{
    private readonly HPDBaseSqliteOptions _options;
    private readonly SqliteRecordStore _store;

    public SqliteDescriptorContributor(IOptions<HPDBaseSqliteOptions> options, SqliteRecordStore store)
    {
        _options = options.Value;
        _store = store;
    }

    public string Id => _options.ModuleId;

    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        if (_options.ContributeModuleDescriptor)
        {
            builder.AddModule(new BaseModuleDescriptor
            {
                Id = _options.ModuleId,
                Name = _options.ModuleName,
                Kind = BaseModuleKind.Store,
                Version = _options.StoreVersion,
                Status = ModuleStatus.Installed,
                ContributedCapabilities = _options.ContributeCapabilities ? FeatureIds() : null,
                ContributedHealthRefIds = _options.ContributeHealth ? [_options.HealthRefId] : null,
                ContributedDiagnosticIds = _options.ContributeDiagnostics ? [_options.DiagnosticRefId] : null,
                Visibility = VisibilityLevel.Admin
            });
        }

        if (_options.ContributeHealth)
        {
            builder.AddHealthRef(new HealthRefDescriptor { Id = _options.HealthRefId, Scope = HealthScope.Store, TargetRef = _options.StoreId, Visibility = VisibilityLevel.Admin });
        }

        if (_options.ContributeDiagnostics)
        {
            builder.AddDiagnosticRef(new DiagnosticRefDescriptor { Id = _options.DiagnosticRefId, Visibility = VisibilityLevel.Admin });
        }

        foreach (var collection in _options.Collections ?? [])
        {
            builder.AddCollection(BindCollection(collection));
        }

        if (_options.ContributeCapabilities)
        {
            builder.AddCapabilities(SqliteCapabilityDescriptorFactory.Create(_options, _store.Capabilities));
        }
    }

    private CollectionDefinition BindCollection(CollectionDefinition collection)
    {
        if (!string.IsNullOrWhiteSpace(collection.Store?.StoreId) && !string.Equals(collection.Store.StoreId, _options.StoreId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SQLite collection contributions must be unbound or bound to the configured SQLite store id.");
        }

        return collection with
        {
            Store = collection.Store is null ? new StoreAnnotation { StoreId = _options.StoreId, Owner = EnforcementOwner.Store } : collection.Store with { StoreId = _options.StoreId }
        };
    }

    private string[] FeatureIds()
    {
        var features = new List<string>
        {
            BaseFeatureIds.RecordsList,
            BaseFeatureIds.RecordsGet,
            BaseFeatureIds.RecordsCreate,
            BaseFeatureIds.RecordsPatch,
            BaseFeatureIds.RecordsReplace,
            BaseFeatureIds.RecordsDelete,
            BaseFeatureIds.RecordsRevision,
            BaseFeatureIds.StoreBatchAtomic,
            BaseFeatureIds.StoreBatchCrossCollection
        };
        if (_store.Capabilities.Upsert is not null)
            features.Add(BaseFeatureIds.StoreRecordUpsertAtomic);

        return features.ToArray();
    }
}
