using HPD.Base.StoreConformance;
using HPD.Base.StoreConformance.Runtime;
using HPD.Base.Tests.Volatile.TestDoubles;
using HPD.Base;

namespace HPD.Base.Tests.Volatile.Conformance;

public sealed class VolatileConformanceFixture :
    IConfigurableRuntimeStoreConformanceFixture,
    IRecordStoreConformanceSeeder,
    IStreamingRecordStoreConformanceExpectations
{
    private readonly HPDBaseVolatileStoreOptions _options = new()
    {
        StoreId = "conformance-volatile",
        DefaultPageSize = 100,
        MaxPageSize = 1_000,
        AllowClientRequestedIds = true,
        EnableStreamingCapability = true
    };

    public string ProviderName => "HPD.Base";

    public StoreCapabilityDescriptor Capabilities => new VolatileRecordStore(_options).Capabilities;

    public bool ExpectsSnapshotStreams => true;

    public bool ExpectsEnumerationCancellation => true;

    public CollectionDefinition Collection => new()
    {
        Id = "conformance-items",
        Name = "conformance-items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve,
        Operations = new CollectionOperationMatrix
        {
            List = true,
            Get = true,
            Create = true,
            Patch = true,
            Replace = true,
            Delete = true,
            Upsert = true
        }
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
        return ValueTask.FromResult<IRecordStore>(new VolatileRecordStore(_options));
    }

    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask<IServiceProvider> CreateRuntimeServicesAsync(CancellationToken cancellationToken = default)
    {
        return CreateRuntimeServicesAsync(new RuntimeStoreConformanceOptions(), cancellationToken);
    }

    public ValueTask<IServiceProvider> CreateRuntimeServicesAsync(
        RuntimeStoreConformanceOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(options);

        var services = new ServiceCollection();
        services.AddLogging();
        if (options.PolicyEvaluator is not null)
        {
            services.AddSingleton<IPolicyEvaluator>(options.PolicyEvaluator);
        }
        else
        {
            services.AddSingleton<IPolicyEvaluator, ConformanceAllowPolicyEvaluator>();
        }

        if (options.EventPublisher is not null)
        {
            services.AddSingleton(options.EventPublisher);
            services.AddSingleton<IBaseEventPublisher>(options.EventPublisher);
        }

        services.AddHPDBaseRuntime()
            .AddHPDBaseVolatileStore(options =>
            {
                options.StoreId = _options.StoreId;
                options.DefaultPageSize = _options.DefaultPageSize;
                options.MaxPageSize = _options.MaxPageSize;
                options.AllowClientRequestedIds = _options.AllowClientRequestedIds;
                options.EnableStreamingCapability = _options.EnableStreamingCapability;
                options.CollectionIds = [Collection.Id];
                options.Collections = [Collection];
            });

        var provider = services.BuildServiceProvider();
        if (options.StoreOverride is null)
        {
            provider.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseVolatileStore(provider);
        }
        else
        {
            provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
            {
                StoreId = _options.StoreId,
                Store = options.StoreOverride,
                CollectionIds = [Collection.Id]
            });
        }

        provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
        return ValueTask.FromResult<IServiceProvider>(provider);
    }

    public async ValueTask<RecordEnvelope> CreateRecordAsync(
        IRecordStore store,
        CollectionDefinition collection,
        string id,
        params (string Field, JsonElement Value)[] fields)
    {
        var request = new RecordCreateRequest
        {
            RequestedId = new RecordId(id),
            Payload = new RecordPayload
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = fields.ToDictionary(field => field.Field, field => field.Value.Clone(), StringComparer.Ordinal)
            }
        };

        var result = await VolatileMutationTestDriver.CreateAsync(
            (IRecordMutationStore)store,
            collection,
            request,
            Operation(BaseOperationKind.Create, new RecordId(id)));
        result.Status.Should().Be(OperationStatus.Created);
        return result.Value!;
    }
}
