using System.Text.Json;
using FluentAssertions;
using HPD.Base.Application.DependencyInjection;
using HPD.Base.Application.Batches;
using HPD.Base.Application.Records;
using HPD.Base.Application.Results;
using HPD.Base.Application.Sessions;
using HPD.Base.Application.Tests.Generation;
using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Application.Tests.Sessions;

public sealed class BaseSessionTests
{
    [Fact]
    public async Task CreateBindsPrincipalScopeTimeAndTypedPayload()
    {
        var runtime = new RecordingRuntime
        {
            CreateResult = Success(Envelope(
                new GeneratedProject
                {
                    OrganizationId = "org_1",
                    Name = "created",
                })),
        };
        var principal = Principal();
        var time = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero));
        using var services = Services(runtime, time);
        var sessions = services.GetRequiredService<IBaseSessionFactory>();
        var session = sessions.For(principal, options =>
        {
            options.ProjectId = "project_1";
            options.CorrelationId = "corr_1";
        });

        var result = await session.Collection(GeneratedProject.Collection).CreateAsync(
            new RecordId("record_1"),
            new GeneratedProject
            {
                OrganizationId = "org_1",
                Name = "created",
            },
            idempotencyKey: "idem_1");

        result.Should().BeOfType<BaseSuccess<Application.Records.BaseRecord<GeneratedProject>>>();
        result.RequireValue().Value.Name.Should().Be("created");
        runtime.Principal.Should().BeEquivalentTo(principal);
        runtime.Principal.Should().NotBeSameAs(principal);
        runtime.Operation.Should().BeEquivalentTo(new
        {
            Operation = BaseOperationKind.Create,
            CollectionId = "projects",
            RecordId = "record_1",
            TenantId = "tenant_1",
            ProjectId = "project_1",
            Mode = OperationMode.User,
            CorrelationId = "corr_1",
            Now = time.GetUtcNow(),
        });
        runtime.CreateRequest!.RequestedId.Should().Be(new RecordId("record_1"));
        runtime.CreateRequest.IdempotencyKey.Should().Be("idem_1");
        JsonSerializer.Deserialize(
            runtime.CreateRequest.Payload.Json,
            GeneratedProject.Collection.JsonTypeInfo)!.Name.Should().Be("created");
    }

    [Fact]
    public async Task GetMapsCanonicalFailureToClosedFailureCase()
    {
        var runtime = new RecordingRuntime
        {
            GetResult = new OperationResult<RecordEnvelope>
            {
                Status = OperationStatus.NotFound,
                Error = new BaseError
                {
                    Code = "base.record.notFound",
                    Message = "Record was not found.",
                    Category = ErrorCategory.NotFound,
                },
            },
        };
        using var services = Services(runtime, TimeProvider.System);
        var session = services.GetRequiredService<IBaseSessionFactory>().For(Principal());

        var result = await session.Collection(GeneratedProject.Collection)
            .GetAsync(new RecordId("missing"));

        var failure = result.Should().BeOfType<BaseFailure<Application.Records.BaseRecord<GeneratedProject>>>()
            .Subject;
        failure.Error.Code.Should().Be("base.record.notFound");
        var action = () => result.RequireValue();
        action.Should().Throw<BaseOperationException>()
            .Where(exception => exception.Status == OperationStatus.NotFound);
    }

    [Fact]
    public async Task GetDecodesPolicyProjectedFieldMapWithoutReflection()
    {
        var runtime = new RecordingRuntime
        {
            GetResult = Success(new RecordEnvelope
            {
                CollectionId = "projects",
                Id = new RecordId("record_1"),
                Payload = new RecordPayload
                {
                    Kind = RecordPayloadKind.FieldMap,
                    Fields = new Dictionary<string, JsonElement>
                    {
                        ["organizationId"] = JsonSerializer.SerializeToElement("org_1"),
                        ["name"] = JsonSerializer.SerializeToElement("visible"),
                    },
                },
                Metadata = new RecordMetadata(),
                Policy = new RecordPolicyMetadata { Redacted = true },
            }),
        };
        using var services = Services(runtime, TimeProvider.System);
        var session = services.GetRequiredService<IBaseSessionFactory>().For(Principal());

        var record = (await session.Collection(GeneratedProject.Collection)
            .GetAsync(new RecordId("record_1"))).RequireValue();

        record.Value.Name.Should().Be("visible");
        record.Redacted.Should().BeTrue();
    }

    [Fact]
    public async Task QueryLowersTypedFieldsIntoBoundedCanonicalAst()
    {
        var runtime = new RecordingRuntime
        {
            ListResult = new OperationResult<RecordPage>
            {
                Status = OperationStatus.Ok,
                Value = new RecordPage
                {
                    Items =
                    [
                        Envelope(new GeneratedProject
                        {
                            OrganizationId = "org_1",
                            Name = "visible",
                        }),
                    ],
                    Page = new PageInfo
                    {
                        Offset = 0,
                        Limit = 25,
                        HasMore = false,
                    },
                },
            },
        };
        using var services = Services(runtime, TimeProvider.System);
        var session = services.GetRequiredService<IBaseSessionFactory>().For(Principal());

        var page = (await session.Collection(GeneratedProject.Collection)
            .Query()
            .Where(GeneratedProject.Fields.OrganizationId, "org_1")
            .OrderBy(GeneratedProject.Fields.Name)
            .Take(25)
            .PageAsync()).RequireValue();

        page.Items.Should().ContainSingle();
        runtime.Query!.Page!.Limit.Should().Be(25);
        runtime.Query.Filter.Should().Match<FilterExpression>(filter =>
            filter.Kind == FilterNodeKind.Compare &&
            filter.Field == "organizationId" &&
            filter.Operator == FilterOperator.Equal &&
            filter.Value!.String == "org_1");
        runtime.Query.Sort.Should().ContainSingle()
            .Which.Field.Should().Be("name");
        runtime.Operation!.Operation.Should().Be(BaseOperationKind.Query);
    }

    [Fact]
    public async Task EnsureReadsExistingRecordWithoutReportingAnUpdate()
    {
        var runtime = new RecordingRuntime
        {
            UpsertResult = new OperationResult<RecordUpsertResult>
            {
                Status = OperationStatus.Conflict,
                Error = new BaseError
                {
                    Code = "base.record.exists",
                    Message = "Record already exists.",
                    Category = ErrorCategory.Conflict,
                },
            },
            GetResult = new OperationResult<RecordEnvelope>
            {
                Status = OperationStatus.Ok,
                Value = Envelope(new GeneratedProject
                {
                    OrganizationId = "org_1",
                    Name = "existing",
                }),
            },
        };
        using var services = Services(runtime, TimeProvider.System);
        var collection = services.GetRequiredService<IBaseSessionFactory>()
            .For(Principal())
            .Collection(GeneratedProject.Collection);

        var ensured = (await collection.EnsureAsync(
            new RecordId("record_1"),
            new GeneratedProject
            {
                OrganizationId = "org_1",
                Name = "new",
            })).RequireValue();

        ensured.Outcome.Should().Be(BaseEnsureOutcome.AlreadyExisted);
        ensured.Record.Value.Name.Should().Be("existing");
        runtime.UpsertRequest!.Condition.Should()
            .Be(RecordUpsertExistenceCondition.CreateOnly);
    }

    [Fact]
    public async Task AtomicBatchReturnsTypedRecordsOnlyAfterCommitProof()
    {
        var firstEnvelope = Envelope(new GeneratedProject
        {
            OrganizationId = "org_1",
            Name = "first",
        });
        var secondEnvelope = Envelope(new GeneratedProject
        {
            OrganizationId = "org_1",
            Name = "second",
        }) with
        {
            Id = new RecordId("record_2"),
        };
        var runtime = new RecordingRuntime
        {
            BatchResult = new OperationResult<BaseRecordBatchResult>
            {
                Status = OperationStatus.Ok,
                Value = new BaseRecordBatchResult
                {
                    Outcome = BaseRecordBatchOutcome.Committed,
                    Items =
                    [
                        new BaseRecordBatchItemResult
                        {
                            ItemId = "item_0000",
                            Index = 0,
                            Kind = BaseRecordMutationKind.Create,
                            Disposition = BaseRecordBatchItemDisposition.Committed,
                            Status = OperationStatus.Created,
                            Record = firstEnvelope,
                        },
                        new BaseRecordBatchItemResult
                        {
                            ItemId = "item_0001",
                            Index = 1,
                            Kind = BaseRecordMutationKind.Create,
                            Disposition = BaseRecordBatchItemDisposition.Committed,
                            Status = OperationStatus.Created,
                            Record = secondEnvelope,
                        },
                    ],
                },
            },
        };
        using var services = Services(runtime, TimeProvider.System);
        var session = services.GetRequiredService<IBaseSessionFactory>().For(Principal());
        var batch = session.Atomic();
        var first = batch.Create(
            GeneratedProject.Collection,
            new RecordId("record_1"),
            new GeneratedProject { OrganizationId = "org_1", Name = "first" });
        batch.Create(
            GeneratedProject.Collection,
            new RecordId("record_2"),
            new GeneratedProject { OrganizationId = "org_1", Name = "second" });

        BaseCommittedBatch committed =
            (await batch.CommitAsync()).RequireValue().RequireCommitted();

        committed.Record(first).Value.Name.Should().Be("first");
        runtime.BatchRequest!.Mode.Should().Be(BaseRecordBatchExecutionMode.Atomic);
        runtime.BatchRequest.Operations.Select(item => item.ItemId)
            .Should().Equal("item_0000", "item_0001");
        runtime.Operation!.CollectionId.Should().Be("base");
    }

    private static ServiceProvider Services(
        RecordingRuntime runtime,
        TimeProvider timeProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseRecordRuntime>(runtime);
        services.AddSingleton(timeProvider);
        services.AddHPDBaseApplication();
        return services.BuildServiceProvider();
    }

    private static PrincipalContext Principal() =>
        new()
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = "subject_1",
            SubjectKind = AccessSubjectKind.User,
            CurrentTenantId = "tenant_1",
        };

    private static OperationResult<RecordEnvelope> Success(RecordEnvelope envelope) =>
        new()
        {
            Status = OperationStatus.Created,
            Value = envelope,
        };

    private static RecordEnvelope Envelope(GeneratedProject value) =>
        new()
        {
            CollectionId = "projects",
            Id = new RecordId("record_1"),
            Payload = new RecordPayload
            {
                Kind = RecordPayloadKind.Json,
                Json = JsonSerializer.SerializeToElement(
                    value,
                    GeneratedProject.Collection.JsonTypeInfo),
            },
            Metadata = new RecordMetadata
            {
                Revision = new RevisionToken("revision_1"),
            },
        };

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class RecordingRuntime : IBaseRecordRuntime
    {
        public OperationResult<RecordEnvelope>? GetResult { get; init; }
        public OperationResult<RecordEnvelope>? CreateResult { get; init; }
        public OperationResult<RecordPage>? ListResult { get; init; }
        public OperationResult<RecordUpsertResult>? UpsertResult { get; init; }
        public OperationResult<BaseRecordBatchResult>? BatchResult { get; init; }
        public PrincipalContext? Principal { get; private set; }
        public OperationContext? Operation { get; private set; }
        public RecordCreateRequest? CreateRequest { get; private set; }
        public RecordQuery? Query { get; private set; }
        public RecordUpsertRequest? UpsertRequest { get; private set; }
        public BaseRecordBatchRequest? BatchRequest { get; private set; }

        public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
            string collectionId,
            RecordId id,
            PrincipalContext principal,
            OperationContext operation,
            CancellationToken cancellationToken = default)
        {
            Capture(principal, operation);
            return ValueTask.FromResult(GetResult!);
        }

        public ValueTask<OperationResult<RecordEnvelope>> CreateAsync(
            string collectionId,
            RecordCreateRequest request,
            PrincipalContext principal,
            OperationContext operation,
            CancellationToken cancellationToken = default)
        {
            CreateRequest = request;
            Capture(principal, operation);
            return ValueTask.FromResult(CreateResult!);
        }

        public ValueTask<OperationResult<RecordPage>> ListAsync(
            string collectionId,
            RecordQuery? query,
            PrincipalContext principal,
            OperationContext operation,
            CancellationToken cancellationToken = default)
        {
            Query = query;
            Capture(principal, operation);
            return ValueTask.FromResult(ListResult!);
        }

        public ValueTask<OperationResult<RecordEnvelope>> PatchAsync(
            string collectionId,
            RecordId id,
            RecordPatchRequest request,
            PrincipalContext principal,
            OperationContext operation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(
            string collectionId,
            RecordId id,
            RecordReplaceRequest request,
            PrincipalContext principal,
            OperationContext operation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<DeleteResult>> DeleteAsync(
            string collectionId,
            RecordId id,
            RecordDeleteRequest request,
            PrincipalContext principal,
            OperationContext operation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<RecordUpsertResult>> UpsertAsync(
            string collectionId,
            RecordUpsertRequest request,
            PrincipalContext principal,
            OperationContext operation,
            CancellationToken cancellationToken = default)
        {
            UpsertRequest = request;
            Capture(principal, operation);
            return ValueTask.FromResult(UpsertResult!);
        }

        public ValueTask<OperationResult<BaseRecordBatchResult>> BatchAsync(
            BaseRecordBatchRequest request,
            PrincipalContext principal,
            OperationContext operation,
            CancellationToken cancellationToken = default)
        {
            BatchRequest = request;
            Capture(principal, operation);
            return ValueTask.FromResult(BatchResult!);
        }

        private void Capture(
            PrincipalContext principal,
            OperationContext operation)
        {
            Principal = principal;
            Operation = operation;
        }
    }
}
