using System.Text.Json;
using HPD.Base.Descriptors;
using HPD.Base.Query;
using HPD.Base.Relational.Capabilities;
using HPD.Base.Relational.Descriptors;
using HPD.Base.Relational.Planning;
using HPD.Base.Relational.Serialization;
using HPD.Base.Results;

namespace HPD.Base.Relational.Abstractions.Tests.Serialization;

public sealed class RelationalDescriptorSerializationTests
{
    [Fact]
    public void StoreDescriptorRoundTripsWithLowerCamelEnumsAndExtensionBag()
    {
        var descriptor = RelationalSamples.StoreDescriptor();
        var json = JsonSerializer.Serialize(descriptor, HPDBaseRelationalJsonSerializerContext.Default.RelationalStoreDescriptor);
        var roundTrip = JsonSerializer.Deserialize(json, HPDBaseRelationalJsonSerializerContext.Default.RelationalStoreDescriptor);

        Assert.Contains("\"kind\":\"table\"", json);
        Assert.Contains("\"payloadMappingKind\":\"hybrid\"", json);
        Assert.Equal("relational-store", roundTrip!.Id);
        Assert.Equal(RelationalPayloadMappingKind.Hybrid, roundTrip.CollectionMappings![0].PayloadMappingKind);
        Assert.Equal("v", roundTrip.Extensions!["sample"].GetString());
    }

    [Fact]
    public void QueryPlanRoundTripsWithFailClosedDefaults()
    {
        var plan = new RelationalQueryPlanDescriptor
        {
            Id = "plan-unsafe",
            StoreId = "store",
            CollectionId = "orders",
            Status = RelationalQueryPlanStatus.Unsafe,
            Residual = new RelationalResidualDescriptor
            {
                Kind = RelationalResidualKind.AfterPageUnsafe,
                Required = true,
                UnsafeReasons = ["filter would run after page"]
            },
            Count = new RelationalCountPlanDescriptor
            {
                Requested = true,
                Mode = QueryCountMode.Exact,
                UnsafeReasons = ["count would include hidden candidates"]
            },
            UnsafeReasons = ["page", "count"]
        };

        var json = JsonSerializer.Serialize(plan, HPDBaseRelationalJsonSerializerContext.Default.RelationalQueryPlanDescriptor);
        var roundTrip = JsonSerializer.Deserialize(json, HPDBaseRelationalJsonSerializerContext.Default.RelationalQueryPlanDescriptor);

        Assert.Contains("\"status\":\"unsafe\"", json);
        Assert.Contains("\"kind\":\"afterPageUnsafe\"", json);
        Assert.False(roundTrip!.ExecutableForRequestedContext);
        Assert.False(roundTrip.SafeForRequestedContext);
    }

    [Fact]
    public void SourceGeneratedContextCoversRepresentativeDtosAndResults()
    {
        Assert.NotNull(HPDBaseRelationalJsonSerializerContext.Default.RelationalStoreDescriptor);
        Assert.NotNull(HPDBaseRelationalJsonSerializerContext.Default.RelationalCapabilityDescriptor);
        Assert.NotNull(HPDBaseRelationalJsonSerializerContext.Default.RelationalQueryPlanRequest);
        Assert.NotNull(HPDBaseRelationalJsonSerializerContext.Default.RelationalQueryPlanDescriptor);
        Assert.NotNull(HPDBaseRelationalJsonSerializerContext.Default.RelationalPolicyPlanDescriptor);
        Assert.NotNull(HPDBaseRelationalJsonSerializerContext.Default.OperationResultRelationalStoreDescriptor);
        Assert.NotNull(HPDBaseRelationalJsonSerializerContext.Default.OperationResultRelationalQueryPlanDescriptor);
    }

    [Fact]
    public void OperationResultShapeSerializesWithoutReflectionFallback()
    {
        var result = new OperationResult<RelationalQueryPlanDescriptor>
        {
            Status = OperationStatus.Ok,
            Value = RelationalSamples.SafePlan()
        };

        var json = JsonSerializer.Serialize(result, HPDBaseRelationalJsonSerializerContext.Default.OperationResultRelationalQueryPlanDescriptor);

        Assert.Contains("\"status\":\"ok\"", json);
        Assert.Contains("\"executableForRequestedContext\":true", json);
    }
}

internal static class RelationalSamples
{
    public static RelationalStoreDescriptor StoreDescriptor() =>
        new()
        {
            Id = "relational-store",
            StoreId = "store",
            DescriptorVersion = "1.0",
            Provider = new RelationalProviderDescriptor { Id = "provider", Name = "Provider" },
            Databases = [new RelationalDatabaseDescriptor { Id = "db", StoreId = "store", NativeName = "main" }],
            Tables =
            [
                new RelationalTableDescriptor
                {
                    Id = "table-orders",
                    StoreId = "store",
                    NativeName = "orders",
                    ColumnRefs = ["col-id", "col-payload"],
                    MappedCollectionIds = ["orders"],
                    RowIdentityStrategy = RelationalRecordIdMappingKind.NativePrimaryKey
                }
            ],
            Columns =
            [
                new RelationalColumnDescriptor
                {
                    Id = "col-id",
                    StoreId = "store",
                    ParentObjectRef = "table-orders",
                    NativeName = "id",
                    Type = new RelationalColumnTypeDescriptor { NativeTypeName = "uuid", Family = RelationalColumnTypeFamily.Uuid },
                    Nullable = false
                }
            ],
            CollectionMappings =
            [
                new RelationalCollectionMappingDescriptor
                {
                    Id = "mapping-orders",
                    StoreId = "store",
                    CollectionId = "orders",
                    TableRef = "table-orders",
                    MappingKind = RelationalMappingKind.Table,
                    RecordIdMappingKind = RelationalRecordIdMappingKind.NativePrimaryKey,
                    PayloadMappingKind = RelationalPayloadMappingKind.Hybrid
                }
            ],
            Extensions = new Dictionary<string, JsonElement> { ["sample"] = JsonDocument.Parse("\"v\"").RootElement.Clone() }
        };

    public static RelationalQueryPlanDescriptor SafePlan() =>
        new()
        {
            Id = "plan-safe",
            StoreId = "store",
            CollectionId = "orders",
            Status = RelationalQueryPlanStatus.Supported,
            ExecutableForRequestedContext = true,
            SafeForRequestedContext = true,
            Pushdown = new RelationalQueryPushdownDescriptor
            {
                Filter = RelationalPushdownSupport.Complete,
                Sort = RelationalPushdownSupport.Complete,
                Page = RelationalPushdownSupport.Complete,
                Count = RelationalPushdownSupport.Complete,
                Select = RelationalPushdownSupport.Complete,
                Include = RelationalPushdownSupport.Unsupported,
                Policy = RelationalPushdownSupport.Complete,
                CompleteBeforeObservableArtifacts = true
            },
            Residual = new RelationalResidualDescriptor
            {
                Kind = RelationalResidualKind.None,
                SafeForRequestedContext = true,
                RunsBeforeCount = true,
                RunsBeforePage = true
            },
            Count = new RelationalCountPlanDescriptor
            {
                Requested = true,
                Mode = QueryCountMode.Exact,
                ExactCandidateSet = true,
                SafeForRequestedContext = true
            },
            Page = new RelationalPagePlanDescriptor
            {
                Requested = true,
                PageAppliedAfterAllRequiredFilters = true,
                CursorBindsPolicyContext = true,
                SafeForRequestedContext = true
            }
        };

    public static RelationalCapabilityDescriptor CapabilityDescriptor() =>
        new()
        {
            Id = "relational-capabilities",
            StoreId = "store",
            Version = "1.0",
            Metadata = new RelationalMetadataCapability
            {
                Status = CapabilityStatus.Available,
                StoreMetadata = true,
                NamespaceMetadata = true,
                TableMetadata = true,
                ColumnMetadata = true
            },
            Mapping = new RelationalMappingCapability
            {
                Status = CapabilityStatus.Available,
                CollectionMappings = true,
                FieldMappings = true,
                JsonColumnMappings = true
            },
            QueryPlanning = new RelationalQueryPlanningCapability
            {
                Status = CapabilityStatus.Available,
                ResidualSafetyDiagnostics = true,
                CountPageSafetyDiagnostics = true
            },
            JoinsIncludes = new RelationalJoinIncludeCapability
            {
                Status = CapabilityStatus.Planned,
                NativeEngineSupportsJoins = true,
                IncludePlanExplanationSupported = true,
                CallableIncludeExecutionAvailable = false
            },
            Transactions = new RelationalTransactionCapability
            {
                Status = CapabilityStatus.Planned,
                NativeEngineSupportsTransactions = true,
                CallableInterfaceAvailable = false
            },
            SchemaWrite = new RelationalSchemaWriteCapability
            {
                Status = CapabilityStatus.Unavailable,
                NativeEngineSupportsDefinitionChanges = true,
                CallableInterfaceAvailable = false,
                DefinitionChangeRunnerAvailable = false
            }
        };
}
