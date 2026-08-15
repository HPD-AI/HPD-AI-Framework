using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.Logging;
using HPD.Base.StoreConformance;
using HPD.Base.StoreConformance.Runtime;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Conformance;

public sealed class SqliteConformanceFixture : IConfigurableRuntimeStoreConformanceFixture, IRecordStoreConformanceSeeder
{
    private readonly string _dataSource = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-conformance-" + Guid.NewGuid().ToString("N") + ".db");

    private HPDBaseSqliteOptions Options => new()
    {
        StoreId = "conformance-sqlite",
        DataSource = _dataSource,
        DefaultPageSize = 100,
        MaxPageSize = 1_000,
        AllowClientRequestedIds = true,
        Collections = [Collection]
    };

    public string ProviderName => "HPD.Base.Sqlite";

    public StoreCapabilityDescriptor Capabilities => SqliteTestFactory.Create(Options).Capabilities;

    public CollectionDefinition Collection => new()
    {
        Id = "conformance-items",
        Name = "conformance-items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve,
        Fields =
        [
            new FieldDefinition { Id = "title", ApplicationName = "title", WireName = "title", Type = BaseFieldTypes.String },
            new FieldDefinition { Id = "status", ApplicationName = "status", WireName = "status", Type = BaseFieldTypes.String },
            new FieldDefinition { Id = "rank", ApplicationName = "rank", WireName = "rank", Type = BaseFieldTypes.Integer },
            new FieldDefinition { Id = "enabled", ApplicationName = "enabled", WireName = "enabled", Type = BaseFieldTypes.Boolean },
            new FieldDefinition { Id = "tags", ApplicationName = "tags", WireName = "tags", Type = BaseFieldTypes.Array },
            new FieldDefinition { Id = "profile", ApplicationName = "profile", WireName = "profile", Type = BaseFieldTypes.Object },
            new FieldDefinition { Id = "nullable", ApplicationName = "nullable", WireName = "nullable", Type = BaseFieldTypes.String, Nullable = true }
        ],
        MutationMode = BaseCollectionMutationMode.Mutable
    };

    public OperationContext Operation(BaseOperationKind operation, RecordId? id = null) => new()
    {
        Operation = operation,
        CollectionId = Collection.Id,
        RecordId = id?.Value,
        Now = DateTimeOffset.UnixEpoch
    };

    public ValueTask<IRecordStore> CreateStoreAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IRecordStore>(SqliteTestFactory.Create(Options));
    }

    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteIfExists(_dataSource);
        DeleteIfExists(_dataSource + "-wal");
        DeleteIfExists(_dataSource + "-shm");
        return ValueTask.CompletedTask;
    }

    public ValueTask<IServiceProvider> CreateRuntimeServicesAsync(CancellationToken cancellationToken = default) =>
        CreateRuntimeServicesAsync(new RuntimeStoreConformanceOptions(), cancellationToken);

    public ValueTask<IServiceProvider> CreateRuntimeServicesAsync(RuntimeStoreConformanceOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var services = new ServiceCollection();
        services.AddLogging();
        IPolicyEvaluator policy = options.PolicyEvaluator ?? new ConformanceAllowPolicyEvaluator();
        if (options.EventPublisher is not null)
        {
            services.AddSingleton(options.EventPublisher);
            services.AddSingleton<IBaseEventPublisher>(options.EventPublisher);
        }

        services.AddHPDBaseRuntime()
            .UsePolicyAuthority("sqlite-conformance", PolicyDefinition(), policy)
            .AddHPDBaseSqliteStore(sqlite =>
        {
            var configured = Options;
            sqlite.StoreId = configured.StoreId;
            sqlite.DataSource = configured.DataSource;
            sqlite.DefaultPageSize = configured.DefaultPageSize;
            sqlite.MaxPageSize = configured.MaxPageSize;
            sqlite.AllowClientRequestedIds = configured.AllowClientRequestedIds;
            sqlite.Collections = configured.Collections;
        });

        var provider = services.BuildServiceProvider();
        if (options.StoreOverride is null)
        {
            provider.GetRequiredService<SqliteRecordStore>().InitializeUnacceptedSchemaForTestsAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
            provider.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseSqliteStore(provider);
        }
        else
        {
            provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration { StoreId = Options.StoreId, Store = options.StoreOverride, CollectionIds = [Collection.Id] });
        }

        provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
        return ValueTask.FromResult<IServiceProvider>(provider);
    }

    private static BasePolicyAuthorityDefinition PolicyDefinition() => new()
    {
        Id = "sqlite-conformance.policy", Version = 1,
        OwningModuleId = "sqlite-conformance",
        EvaluatorContractId = "sqlite-conformance.policy-evaluator",
        EvaluatorContractVersion = 1, CompositionOrder = 0,
    };

    public async ValueTask<RecordEnvelope> CreateRecordAsync(IRecordStore store, CollectionDefinition collection, string id, params (string Field, JsonElement Value)[] fields)
    {
        var result = await store.CreateAsync(
            collection,
            new RecordCreateRequest
            {
                RequestedId = new RecordId(id),
                Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields.ToDictionary(field => field.Field, field => field.Value.Clone(), StringComparer.Ordinal) }
            },
            Operation(BaseOperationKind.Create, new RecordId(id)));

        result.Status.Should().Be(OperationStatus.Created);
        return result.Value!;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
