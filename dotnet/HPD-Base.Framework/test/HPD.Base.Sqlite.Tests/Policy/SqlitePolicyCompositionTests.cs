using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Policy;

public sealed class SqlitePolicyCompositionTests
{
    [Fact]
    public async Task RuntimePolicyFilterIsPushedDownBeforeCountAndPage()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-policy-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = Services(path, new TenantPolicyEvaluator(SupportedTenantFilter()));
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<SqliteRecordStore>().InitializeUnacceptedSchemaForTestsAsync();
            provider.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseSqliteStore(provider);
            var runtime = provider.GetRequiredService<IBaseRecordRuntime>();

            await Create(runtime, "a1", "a");
            await Create(runtime, "a2", "a");
            await Create(runtime, "b1", "b");

            var result = await runtime.ListAsync(
                "items",
                new RecordQuery { Page = new QueryPage { Mode = QueryPaginationMode.Page, Page = 1, PerPage = 1 }, Count = QueryCountMode.Exact },
                Principal(),
                Operation(BaseOperationKind.List));

            result.Status.Should().Be(OperationStatus.Ok);
            result.Value!.Count!.Total.Should().Be(2);
            result.Value.Items.Should().ContainSingle();
            result.Value.Items[0].Payload.Fields!["tenant"].GetString().Should().Be("a");
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task UnsupportedPolicyFilterFailsClosedBeforePage()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-policy-unsupported-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = Services(path, new TenantPolicyEvaluator(new FilterExpression
            {
                Kind = FilterNodeKind.Compare,
                Field = "tenant",
                Operator = FilterOperator.Contains,
                Value = new QueryValue { Kind = QueryValueKind.String, String = "a" }
            }));
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<SqliteRecordStore>().InitializeUnacceptedSchemaForTestsAsync();
            provider.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseSqliteStore(provider);
            var runtime = provider.GetRequiredService<IBaseRecordRuntime>();

            await Create(runtime, "a1", "a");
            var result = await runtime.ListAsync(
                "items",
                new RecordQuery { Page = new QueryPage { Mode = QueryPaginationMode.Page, Page = 1, PerPage = 1 }, Count = QueryCountMode.Exact },
                Principal(),
                Operation(BaseOperationKind.List));

            result.Status.Should().BeOneOf(OperationStatus.Unsupported, OperationStatus.ValidationFailed, OperationStatus.CapabilityUnavailable);
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private static ServiceCollection Services(string path, IPolicyEvaluator evaluator)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseRuntime().UsePolicyAuthority("sqlite-policy-tests", new BasePolicyAuthorityDefinition
        {
            Id = "sqlite.policy", Version = 1, OwningModuleId = "tests",
            EvaluatorContractId = "sqlite.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        }, evaluator).AddHPDBaseSqliteStore(options =>
        {
            options.DataSource = path;
            options.StoreId = "policy-sqlite";
            options.Collections = [Collection()];
        });
        return services;
    }

    private static async Task Create(IBaseRecordRuntime runtime, string id, string tenant)
    {
        var result = await runtime.CreateAsync(
            "items",
            new RecordCreateRequest { RequestedId = new RecordId(id), Payload = Payload(id, tenant) },
            Principal(),
            Operation(BaseOperationKind.Create, new RecordId(id)));
        result.Status.Should().Be(OperationStatus.Created);
    }

    private static FilterExpression SupportedTenantFilter() => new()
    {
        Kind = FilterNodeKind.Compare,
        Field = "tenant",
        Operator = FilterOperator.Equal,
        Value = new QueryValue { Kind = QueryValueKind.String, String = "a" }
    };

    private static CollectionDefinition Collection() => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve,
        MutationMode = BaseCollectionMutationMode.Mutable,
        Fields =
        [
            new FieldDefinition { Id = "title", ApplicationName = "title", WireName = "title", Type = BaseFieldTypes.String },
            new FieldDefinition { Id = "tenant", ApplicationName = "tenant", WireName = "tenant", Type = BaseFieldTypes.String }
        ]
    };

    private static OperationContext Operation(BaseOperationKind kind, RecordId? id = null) => new() { Operation = kind, CollectionId = "items", RecordId = id?.Value, Now = DateTimeOffset.UnixEpoch };
    private static PrincipalContext Principal() => new() { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "user" };
    private static RecordPayload Payload(string title, string tenant)
    {
        using var document = JsonDocument.Parse($$"""{"title":"{{title}}","tenant":"{{tenant}}"}""");
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }

    private sealed class TenantPolicyEvaluator : IPolicyEvaluator
    {
        private readonly FilterExpression _filter;

        public TenantPolicyEvaluator(FilterExpression filter)
        {
            _filter = filter;
        }

        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed,
                Constraints = request.Operation.Operation == BaseOperationKind.List
                    ? new PolicyConstraints { RecordFilter = _filter }
                    : null
            });
        }
    }
}
