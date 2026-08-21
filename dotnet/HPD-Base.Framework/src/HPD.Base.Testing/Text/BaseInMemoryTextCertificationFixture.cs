using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HPD.Base;

/// <summary>Runs the public lexical certification protocol against the real built-in InMemory provider.</summary>
public class BaseTextCertificationFixture : IBaseTextCertificationFixture
{
    private readonly Action<HPDBaseBuilder>? _configureProvider;
    private readonly string _storeId;
    /// <summary>Creates a fixture over one provider selected by the supplied BASE builder configuration.</summary>
    public BaseTextCertificationFixture(string providerId, int providerVersion, BaseTextProviderClass providerClass, Action<HPDBaseBuilder>? configureProvider = null, string? storeId = null)
    {
        if (string.IsNullOrWhiteSpace(providerId) || providerVersion <= 0 || !Enum.IsDefined(providerClass)) throw new ArgumentException("The text provider identity is invalid.");
        ProviderId = providerId; ProviderVersion = providerVersion; ProviderClass = providerClass; _configureProvider = configureProvider; _storeId = storeId ?? providerId.Split('.')[0];
    }
    /// <inheritdoc />
    public string ProtocolVersion => BaseTextProviderCertification.ProtocolVersion;
    /// <inheritdoc />
    public BaseTextProviderClass ProviderClass { get; }
    /// <inheritdoc />
    public string ProviderId { get; }
    /// <inheritdoc />
    public int ProviderVersion { get; }
    /// <inheritdoc />
    public async ValueTask<IBaseTextCertificationHost> CreateAsync(BaseTextCertificationHostRequest request, CancellationToken cancellationToken)
    {
        if (request.Faults.Length != 0) throw new ArgumentException("The built-in reference fixture does not accept injected provider faults.", nameof(request));
        var services = new ServiceCollection().AddLogging(static builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddHPDBase(builder =>
        {
            builder.ConfigureSchema(options => options.PlanProtectionKey = request.TokenKeys[0].Key.ToArray());
            _configureProvider?.Invoke(builder);
            builder.ConfigureTokenProtection(options => options.ActiveKey = request.TokenKeys[0]);
            builder.AddPolicyAuthority<BaseTextCertificationAllowPolicy>(new() { Id = "base.testing.text.policy.v1", Version = 1, OwningModuleId = "hpd.base.testing", EvaluatorContractId = "base.testing.text.policy", EvaluatorContractVersion = 1, CompositionOrder = 0 });
            builder.AddStaticGrantAuthority(new() { Id = BaseTextGrants.Query, Version = 1, OwningModuleId = "hpd.base.testing", SourceContractId = "base.testing.text.grants", SourceContractVersion = 1 }, new()
            {
                Id = BaseTextGrants.Query, ApplicationId = "hpd.base.application", Audience = HPDBaseEndpointAudience.Application,
                Subject = new() { Kind = AccessSubjectKind.ServicePrincipal, Id = "base-text-certification" }, Action = BaseTextGrants.Query,
                Scope = new() { Kind = ResourceScopeKind.TextIndex, CollectionId = BaseTextCertificationSchemaRecord.Collection.Id, TextIndexId = BaseTextCertificationSchemaRecord.TextIndexes.Content.Definition.Id },
            });
            builder.AddCollection(BaseTextCertificationSchemaRecord.Collection);
        });
        ServiceProvider provider = services.BuildServiceProvider();
        if (!string.Equals(ProviderId, "inmemory.text", StringComparison.Ordinal))
        {
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = _storeId }, cancellationToken).ConfigureAwait(false);
            if (!planned.IsSuccess() || planned.Value is null) throw new InvalidOperationException(planned.Error?.Code ?? "base.testing.text.schemaFailed");
            OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = planned.Value.ProtectedArtifact }, cancellationToken).ConfigureAwait(false);
            if (!applied.IsSuccess()) throw new InvalidOperationException(applied.Error?.Code ?? "base.testing.text.schemaFailed");
        }
        OperationResult<BaseApplicationReadiness> readiness = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!readiness.IsSuccess()) { await provider.DisposeAsync().ConfigureAwait(false); throw new InvalidOperationException(readiness.Error?.Code ?? BaseTextErrorCodes.IndexUnavailable); }
        BaseTextProviderDescriptor installed = provider.GetRequiredService<IBaseTextProvider>().Descriptor;
        if (!string.Equals(installed.Id, ProviderId, StringComparison.Ordinal) || installed.Version != ProviderVersion || installed.ProviderClass != ProviderClass)
        { await provider.DisposeAsync().ConfigureAwait(false); throw new InvalidOperationException("base.testing.text.providerIdentityMismatch"); }
        return new BaseInMemoryTextCertificationHost(provider, request);
    }
}

/// <summary>Runs the public lexical certification protocol against the real built-in InMemory provider.</summary>
public sealed class BaseInMemoryTextCertificationFixture() : BaseTextCertificationFixture("inmemory.text", 1, BaseTextProviderClass.CoLocatedTransactional);

internal sealed class BaseInMemoryTextCertificationHost(ServiceProvider services, BaseTextCertificationHostRequest request) : IBaseTextCertificationHost, IBaseTextCertificationAuthorityControl, IBaseTextCertificationProviderControl
{
    private readonly object _gate = new();
    private readonly List<BaseTextCertificationObservation> _observations = [Observation(1, BaseTextCertificationOperationKind.HostCreated, State(services), OperationStatus.Ok, request.ProviderClass)];
    private long _sequence = 1;
    private readonly BaseSession _session = services.GetRequiredService<IBaseSessionFactory>().For(new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.ServicePrincipal, SubjectId = "base-text-certification" });
    public IBaseTextCertificationAuthorityControl Authority => this;
    public IBaseTextCertificationProviderControl Provider => this;

    public async ValueTask<BaseTextCertificationOperationResult> ExecuteAsync(BaseTextCertificationOperation operation, CancellationToken cancellationToken)
    {
        BaseTextCertificationProviderState before = await InspectAsync(cancellationToken).ConfigureAwait(false);
        OperationStatus status; BaseError? error = null; BaseTextHttpResult<BaseTextCertificationRecord>? query = null;
        switch (operation)
        {
            case BaseTextCertificationOperation.Query value:
                try
                {
                    BaseTextQuery lexical = Query(value.Request.Query); BaseTextCandidateConstraint filter = value.Request.Filter is null ? new BaseTextCandidateConstraint.True() : Filter(value.Request.Filter);
                    var search = new BaseTextSearch<BaseTextCertificationSchemaRecord>(_session.Collection(BaseTextCertificationSchemaRecord.Collection), BaseTextCertificationSchemaRecord.TextIndexes.Content, lexical, filter, value.Request.Take, value.Request.Cursor is null ? null : BaseTextCursor.Parse(value.Request.Cursor), new BaseTextConsistencyRequirement.Current());
                    BaseResult<BaseTextResult<BaseTextCertificationSchemaRecord>> result = await search.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    status = result.Status;
                    if (result is BaseSuccess<BaseTextResult<BaseTextCertificationSchemaRecord>> success)
                        query = new() { Matches = success.Value.Matches.Select(static match => new BaseTextHttpMatch<BaseTextCertificationRecord> { Record = ToContract(match.Record.Id.Value, match.Record.Value), Revision = match.Revision.Value, ScoreUnits = match.Score.Units.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToArray(), Next = success.Value.Next?.Encode(), ConsistencyToken = success.Value.Consistency.Encode() };
                    else error = ((BaseFailure<BaseTextResult<BaseTextCertificationSchemaRecord>>)result).Error;
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException) { status = OperationStatus.ValidationFailed; error = new() { Code = BaseTextErrorCodes.QueryInvalid, Message = "The text search query is invalid.", Category = ErrorCategory.Validation }; }
                break;
            case BaseTextCertificationOperation.Commit value:
                BaseTextCertificationCommitResult committed = await CommitAsync(value.Request, cancellationToken).ConfigureAwait(false); status = committed.Status; break;
            case BaseTextCertificationOperation.Inspect:
                status = OperationStatus.Ok; break;
            case BaseTextCertificationOperation.Rebuild value:
                OperationResult<BaseTextRebuildResult> rebuilt = await services.GetRequiredService<IBaseTextAdministration>().RebuildAsync(new() { CollectionId = BaseTextCertificationSchemaRecord.Collection.Id, TextIndexId = BaseTextCertificationSchemaRecord.TextIndexes.Content.Definition.Id, ExpectedGeneration = value.Request.ExpectedGeneration, Identity = value.Request.Identity }, cancellationToken).ConfigureAwait(false); status = rebuilt.Status; error = rebuilt.Error; break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
        BaseTextCertificationProviderState after = await InspectAsync(cancellationToken).ConfigureAwait(false); long sequence = AddObservation(operation is BaseTextCertificationOperation.Query ? BaseTextCertificationOperationKind.Query : operation is BaseTextCertificationOperation.Rebuild ? BaseTextCertificationOperationKind.Rebuild : operation is BaseTextCertificationOperation.Commit ? BaseTextCertificationOperationKind.ProjectionWrite : BaseTextCertificationOperationKind.Inspection, after, status);
        return new() { Status = status, Error = error, Query = query, Before = before, After = after, ObservationSequence = sequence };
    }

    public ValueTask<BaseTextCertificationObservationPage> ObserveAsync(BaseTextCertificationObservationRequest value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); lock (_gate)
        {
            long after = value.AfterSequence ?? 0; BaseTextCertificationObservation[] entries = _observations.Where(item => item.Sequence > after).Take(value.Take).ToArray();
            return ValueTask.FromResult(new BaseTextCertificationObservationPage { Entries = [.. entries], NextSequence = entries.Length == value.Take ? entries[^1].Sequence : null, RetainedLowSequence = 1, CapturedHighSequence = _sequence, Overtaken = after != 0 && after < 1 });
        }
    }
    public ValueTask<BaseTextCertificationShutdownResult> ShutdownAsync(BaseTextCertificationShutdownRequest value, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); AddObservation(BaseTextCertificationOperationKind.Shutdown, State(services), OperationStatus.Ok); return ValueTask.FromResult(new BaseTextCertificationShutdownResult { Completed = true, RetainedOperationCount = 0, Elapsed = TimeSpan.Zero }); }
    public async ValueTask<BaseTextCertificationSeedResult> SeedAsync(BaseTextCertificationSeedRequest value, CancellationToken cancellationToken)
    {
        foreach (BaseTextCertificationRecord record in value.Records) (await Collection.CreateAsync(new(record.Id), FromContract(record), cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseTextCertificationProviderState state = await InspectAsync(cancellationToken).ConfigureAwait(false); byte[] checksum = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', value.Records.OrderBy(static item => item.Id, StringComparer.Ordinal).Select(static item => $"{item.Id}|{item.Tenant}|{item.Active}|{item.Priority}|{item.Optional}|{item.Title}|{item.Body}"))));
        return new() { Head = state.AppliedThrough, RecordCount = value.Records.Length, StateChecksum = ImmutableArray.Create(checksum) };
    }
    public async ValueTask<BaseTextCertificationCommitResult> CommitAsync(BaseTextCertificationCommitRequest value, CancellationToken cancellationToken)
    {
        var revisions = ImmutableArray.CreateBuilder<RevisionToken>(); OperationStatus status = OperationStatus.Updated;
        foreach (BaseTextCertificationMutation mutation in value.Mutations)
        {
            switch (mutation)
            {
                case BaseTextCertificationMutation.Create create: { BaseRecord<BaseTextCertificationSchemaRecord> record = (await Collection.CreateAsync(new(create.Record.Id), FromContract(create.Record), cancellationToken).ConfigureAwait(false)).RequireValue(); revisions.Add(record.Revision!.Value); status = OperationStatus.Created; break; }
                case BaseTextCertificationMutation.Replace replace: { BaseRecord<BaseTextCertificationSchemaRecord> record = (await Collection.ReplaceAsync(new(replace.Record.Id), FromContract(replace.Record), replace.ExpectedRevision, cancellationToken).ConfigureAwait(false)).RequireValue(); revisions.Add(record.Revision!.Value); break; }
                case BaseTextCertificationMutation.Delete delete: { DeleteResult deleted = (await Collection.DeleteAsync(new(delete.RecordId), delete.ExpectedRevision, true, cancellationToken).ConfigureAwait(false)).RequireValue(); revisions.Add(deleted.Previous!.Metadata.Revision!.Value); break; }
            }
        }
        return new() { Status = status, Head = (await InspectAsync(cancellationToken).ConfigureAwait(false)).AppliedThrough, Revisions = revisions.ToImmutable() };
    }
    public async ValueTask<BaseMutationJournalPosition> CaptureHeadAsync(CancellationToken cancellationToken) => (await InspectAsync(cancellationToken).ConfigureAwait(false)).AppliedThrough;
    public async ValueTask<BaseTextCertificationRevisionResult> InspectRevisionAsync(BaseTextCertificationRevisionRequest value, CancellationToken cancellationToken)
    {
        BaseResult<BaseRecord<BaseTextCertificationSchemaRecord>> result = await Collection.GetAsync(new(value.RecordId), cancellationToken).ConfigureAwait(false);
        return result is BaseSuccess<BaseRecord<BaseTextCertificationSchemaRecord>> success && success.Value.Revision == value.Revision ? new() { Found = true, Record = ToContract(value.RecordId, success.Value.Value) } : new() { Found = false };
    }
    public ValueTask PruneHistoryAsync(BaseMutationJournalPosition through, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
    public ValueTask RestoreAsync(BaseTextCertificationRestoreRequest value, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask AdvanceAsync(BaseMutationJournalPosition through, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
    public ValueTask PublishVisibilityAsync(BaseMutationJournalPosition through, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
    public async ValueTask RebuildAsync(BaseTextCertificationRebuildRequest value, CancellationToken cancellationToken) { _ = await services.GetRequiredService<IBaseTextAdministration>().RebuildAsync(new() { CollectionId = BaseTextCertificationSchemaRecord.Collection.Id, TextIndexId = BaseTextCertificationSchemaRecord.TextIndexes.Content.Definition.Id, ExpectedGeneration = value.ExpectedGeneration, Identity = value.Identity }, cancellationToken).ConfigureAwait(false); }
    public async ValueTask<BaseTextCertificationProviderState> InspectAsync(CancellationToken cancellationToken) => State(services, (await services.GetRequiredService<IBaseTextAdministration>().GetAsync(BaseTextCertificationSchemaRecord.Collection.Id, BaseTextCertificationSchemaRecord.TextIndexes.Content.Definition.Id, cancellationToken).ConfigureAwait(false)).Value);
    public ValueTask<BaseTextCertificationFaultState> InspectFaultAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(new BaseTextCertificationFaultState { Configured = request.Faults, Consumed = [] }); }
    public ValueTask<BaseTextCertificationLateWorkResult> ReleaseLateWorkAsync(BaseTextCertificationOperationKind operationKind, int occurrence, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(new BaseTextCertificationLateWorkResult { OperationKind = operationKind, Occurrence = occurrence, WasRetained = false, Released = false, QuarantineCountAfterRelease = 0 }); }
    public async ValueTask DisposeAsync() => await services.DisposeAsync().ConfigureAwait(false);

    private BaseCollectionSession<BaseTextCertificationSchemaRecord> Collection => _session.Collection(BaseTextCertificationSchemaRecord.Collection);
    private long AddObservation(BaseTextCertificationOperationKind operation, BaseTextCertificationProviderState state, OperationStatus status) { lock (_gate) { long sequence = checked(++_sequence); _observations.Add(Observation(sequence, operation, state, status, request.ProviderClass)); return sequence; } }
    private static BaseTextCertificationObservation Observation(long sequence, BaseTextCertificationOperationKind operation, BaseTextCertificationProviderState state, OperationStatus status, BaseTextProviderClass providerClass) => new() { Sequence = sequence, Operation = operation, ProviderClass = providerClass, SnapshotDigest = ImmutableArray.Create(SHA256.HashData(Encoding.UTF8.GetBytes($"{state.Generation}:{state.AppliedThrough.Value}:{state.VisibleThrough.Value}"))), Status = status, State = state, Accounting = EmptyAccounting() };
    private static BaseTextCertificationProviderState State(IServiceProvider services, BaseTextIndexStatus? value = null) { value ??= services.GetService<IBaseTextAdministration>()?.GetAsync(BaseTextCertificationSchemaRecord.Collection.Id, BaseTextCertificationSchemaRecord.TextIndexes.Content.Definition.Id).AsTask().GetAwaiter().GetResult().Value; return value is null ? new() { Generation = 1, AppliedThrough = new(0), VisibleThrough = new(0), State = BaseTextIndexState.Ready, CarrierCount = 0, QuarantineCount = 0 } : new() { Generation = value.Generation, AppliedThrough = value.AppliedThrough, VisibleThrough = value.SearchVisibleThrough, State = value.State, CarrierCount = value.CarrierCount, QuarantineCount = 0 }; }
    private static BaseTextProviderAccounting EmptyAccounting() => new() { InputBytes = 0, QueryBytes = 0, ConstraintBytes = 0, StatementParameters = 0, AuthorizedRecordsExamined = 0, PostingsExamined = 0, PrefixExpansionCount = 0, PrefixExpansionBytes = 0, ScoreProofBytes = 0, CandidateCount = 0, OrderingBytes = 0, ExactHydrationBytes = 0, ResultBytes = 0, CursorBytes = 0, RetainedTransientBytes = 0, Elapsed = TimeSpan.Zero };
    private static BaseTextCertificationSchemaRecord FromContract(BaseTextCertificationRecord value) => new() { Tenant = value.Tenant, Active = value.Active, Priority = value.Priority, Optional = value.Optional, Title = value.Title, Body = value.Body };
    private static BaseTextCertificationRecord ToContract(string id, BaseTextCertificationSchemaRecord value) => new() { Id = id, Tenant = value.Tenant, Active = value.Active, Priority = value.Priority, Optional = value.Optional, Title = value.Title, Body = value.Body };
    private static BaseTextQuery Query(BaseTextHttpQueryNode value) => value.Kind switch { "term" => BaseTextQuery.Token(value.Value!), "prefix" => BaseTextQuery.StartsWith(value.Value!), "phrase" => BaseTextQuery.ExactPhrase(value.Terms!), "field" => BaseTextQuery.InField(value.Field!, Query(value.Child!)), "and" => BaseTextQuery.All(value.Children!.Select(Query).ToArray()), "or" => BaseTextQuery.Any(value.Children!.Select(Query).ToArray()), "not" => BaseTextQuery.Exclude(Query(value.Child!)), _ => throw new ArgumentException() };
    private static BaseTextCandidateConstraint Filter(BaseTextHttpFilter value)
    {
        if (value.Kind is "and" or "or") { BaseTextCandidateConstraint[] children = value.Children!.Select(Filter).ToArray(); return value.Kind == "and" ? new BaseTextCandidateConstraint.And([.. children]) : new BaseTextCandidateConstraint.Or([.. children]); }
        BaseTextFilterValueKind kind = value.Field switch { "active" => BaseTextFilterValueKind.Boolean, "priority" => BaseTextFilterValueKind.Integer, _ => BaseTextFilterValueKind.String }; var field = new BaseTextFilterField(value.Field!, kind);
        return value.Kind switch { "missing" => new BaseTextCandidateConstraint.IsMissing(field), "null" => new BaseTextCandidateConstraint.IsNull(field), "equal" => new BaseTextCandidateConstraint.Equal(field, Value(value.Value!, kind)), "in" => new BaseTextCandidateConstraint.In(field, value.Values!.Select(item => Value(item, kind)).ToImmutableArray()), _ => throw new ArgumentException() };
    }
    private static BaseTextFilterValue Value(BaseTextHttpFilterValue value, BaseTextFilterValueKind kind) => kind switch { BaseTextFilterValueKind.Boolean => BaseTextFilterValue.FromBoolean(value.Boolean!.Value), BaseTextFilterValueKind.Integer => BaseTextFilterValue.FromInteger(value.Integer!.Value), _ => BaseTextFilterValue.FromString(value.Text!) };
}

internal sealed class BaseTextCertificationAllowPolicy : IPolicyEvaluator { public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow()); }

[BaseCollection("base_text_certification_records", typeof(BaseTextCertificationJsonContext))]
[BaseTextIndex("base.testing.text.content.v1", Fields = [nameof(Title), nameof(Body)], Weights = [4, 1], FilterFields = [nameof(Tenant), nameof(Active), nameof(Priority), nameof(Optional)], Audience = HPDBaseEndpointAudience.Application)]
internal sealed partial record BaseTextCertificationSchemaRecord
{
    [BaseField("tenant", Operators = BaseFieldOperator.Equal)] public required string Tenant { get; init; }
    [BaseField("active", Operators = BaseFieldOperator.Equal)] public required bool Active { get; init; }
    [BaseField("priority", Operators = BaseFieldOperator.Equal)] public required long Priority { get; init; }
    [BaseField("optional", Operators = BaseFieldOperator.Equal)] public string? Optional { get; init; }
    [BaseField("title")] public required string Title { get; init; }
    [BaseField("body")] public required string Body { get; init; }
}

[JsonSerializable(typeof(BaseTextCertificationSchemaRecord))]
internal sealed partial class BaseTextCertificationJsonContext : JsonSerializerContext;
