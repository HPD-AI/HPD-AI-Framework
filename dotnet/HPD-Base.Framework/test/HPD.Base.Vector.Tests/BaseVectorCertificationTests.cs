using FluentAssertions;
using HPD.Base.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace HPD.Base.Vector.Tests;

public sealed class BaseVectorCertificationTests
{
    [Fact]
    public async Task ProtocolMismatchCreatesNoHostAndReturnsSafeFailure()
    {
        var fixture = new Fixture(protocolVersion: 1);
        BaseVectorCertificationReport report = await BaseVectorProviderCertification.RunAsync(fixture, BaseVectorCertificationPlan.RequiredLocal());
        report.Succeeded.Should().BeFalse();
        report.Cases.Should().ContainSingle().Which.Code.Should().Be("base.testing.vector.protocolUnsupported");
        fixture.Created.Should().Be(0);
    }

    [Fact]
    public async Task LocalPlanCreatesExecutesAndDisposesOneFreshHostPerCase()
    {
        var fixture = new Fixture();
        BaseVectorCertificationReport report = await BaseVectorProviderCertification.RunAsync(fixture, BaseVectorCertificationPlan.RequiredLocal());
        report.Succeeded.Should().BeTrue(string.Join("; ", report.Cases.Select(static item => $"{item.CaseId}:{item.Code}:{item.Message}")));
        report.Cases.Should().HaveCount(14).And.OnlyContain(static result => result.Outcome == BaseVectorCertificationCaseOutcome.Passed);
        fixture.Created.Should().Be(14);
        fixture.Disposed.Should().Be(14);
        fixture.CapturedHeads.Should().Be(5);
        fixture.ProviderInspections.Should().Be(5);
        fixture.ObservationReads.Should().Be(14);
        fixture.ReceivedFaults.Distinct().Should().BeEquivalentTo([
            BaseVectorCertificationFaultKind.None, BaseVectorCertificationFaultKind.RebuildPublishResponseLoss,
            BaseVectorCertificationFaultKind.NonCooperativeQuery, BaseVectorCertificationFaultKind.NonCooperativeInspection,
            BaseVectorCertificationFaultKind.NonCooperativeRebuild, BaseVectorCertificationFaultKind.MalformedCandidates,
            BaseVectorCertificationFaultKind.DuplicateCandidates, BaseVectorCertificationFaultKind.OversizedCandidates,
            BaseVectorCertificationFaultKind.CredentialFailure, BaseVectorCertificationFaultKind.TerminalSchemaFailure]);
    }

    [Fact]
    public async Task DerivedPlanUsesTheSameCommittedMutationAuthorityAndNonzeroHead()
    {
        var fixture = new Fixture(providerClass: BaseVectorCertificationProviderClass.DerivedJournal);

        BaseVectorCertificationReport report = await BaseVectorProviderCertification.RunAsync(fixture, BaseVectorCertificationPlan.RequiredLocal());

        report.Succeeded.Should().BeTrue(string.Join("; ", report.Cases.Select(static item => $"{item.CaseId}:{item.Code}:{item.Message}")));
        fixture.MaximumCapturedHead.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DerivedPlanRejectsAnAuthorityDisconnectedFromSessionMutations()
    {
        var fixture = new Fixture(providerClass: BaseVectorCertificationProviderClass.DerivedJournal, disconnectDerivedHead: true);

        BaseVectorCertificationReport report = await BaseVectorProviderCertification.RunAsync(fixture, BaseVectorCertificationPlan.RequiredLocal());

        report.Succeeded.Should().BeFalse();
        report.Cases.Where(static result => !result.CaseId.Contains(".fault.", StringComparison.Ordinal))
            .Should().OnlyContain(static result => result.Code == "base.testing.vector.derivedHeadInvalid");
    }

    [Fact]
    public async Task OwningCancellationIsPreservedAndDisposesTheCurrentHost()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(cancelDuringInitialization: cancellation);

        Func<Task> action = async () => await BaseVectorProviderCertification.RunAsync(
            fixture,
            BaseVectorCertificationPlan.RequiredLocal(),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Created.Should().Be(1);
        fixture.Disposed.Should().Be(1);
    }

    [Fact]
    public async Task Control_only_adapter_without_query_evidence_fails_certification()
    {
        var fixture = new Fixture(rejectQueries: true);

        BaseVectorCertificationReport report = await BaseVectorProviderCertification.RunAsync(fixture, BaseVectorCertificationPlan.RequiredLocal());

        report.Succeeded.Should().BeFalse();
        report.Cases.Where(static result => !result.CaseId.Contains(".fault.", StringComparison.Ordinal))
            .Should().OnlyContain(static result => result.Code == "base.testing.vector.queryEvidenceInvalid");
    }

    [Fact]
    public async Task Self_reported_fault_without_external_failure_fails_certification()
    {
        var fixture = new Fixture(successfulFaultOperations: true);

        BaseVectorCertificationReport report = await BaseVectorProviderCertification.RunAsync(fixture, BaseVectorCertificationPlan.RequiredLocal());

        report.Succeeded.Should().BeFalse();
        report.Cases.Where(static result => result.CaseId.Contains(".fault.", StringComparison.Ordinal))
            .Should().OnlyContain(static result => result.Code == "base.testing.vector.faultOutcomeInvalid");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ConstructionAndDisposalFailuresAreSanitized(bool failConstruction, bool failDisposal)
    {
        var fixture = new Fixture(throwOnCreate: failConstruction, throwOnDispose: failDisposal);

        BaseVectorCertificationReport report = await BaseVectorProviderCertification.RunAsync(
            fixture,
            BaseVectorCertificationPlan.RequiredLocal());

        report.Succeeded.Should().BeFalse();
        report.Cases.Should().Contain(static result => result.Code == "base.testing.vector.adapterFailed");
        report.Cases.Select(static result => result.Message).Should().NotContain(message => message != null && message.Contains("fixture-secret", StringComparison.Ordinal));
    }

    [Fact]
    public void RequestFactoriesDeepCopyAndEnforceBounds()
    {
        float[] vector = [1, 2];
        BaseVectorCertificationField[] fields = [BaseVectorCertificationField.Create("vector", BaseVectorCertificationValue.Vector(BaseVector.Create(vector)))];
        BaseVectorCertificationRecord record = BaseVectorCertificationRecord.Create("record", fields);
        BaseVectorCertificationSeedRequest request = BaseVectorCertificationSeedRequest.Create([record]);
        vector[0] = 99;
        fields[0] = BaseVectorCertificationField.Create("other", BaseVectorCertificationValue.Null());
        request.Records[0].Fields[0].Value.VectorValue!.Value.ToArray().Should().Equal(1, 2);
        FluentActions.Invoking(() => BaseVectorCertificationObservationRequest.Create(take: 257)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => BaseVectorCertificationMutationRequest.Create([])).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ProviderAuthoredProtocolShapesExposeNoWritableProperties()
    {
        Type[] protocolShapes =
        [
            typeof(BaseVectorCertificationAuthorityHead),
            typeof(BaseVectorCertificationSeedResult),
            typeof(BaseVectorCertificationMutationResult),
            typeof(BaseVectorCertificationTransitionResult),
            typeof(BaseVectorCertificationPruneResult),
            typeof(BaseVectorCertificationAuthorityState),
            typeof(BaseVectorCertificationGenerationFact),
            typeof(BaseVectorCertificationRecordFact),
            typeof(BaseVectorCertificationAdvanceResult),
            typeof(BaseVectorCertificationVisibilityResult),
            typeof(BaseVectorCertificationRebuildResult),
            typeof(BaseVectorCertificationProviderState),
            typeof(BaseVectorCertificationIndexState),
            typeof(BaseVectorCertificationFaultState),
            typeof(BaseVectorCertificationObservationPage),
            typeof(BaseVectorCertificationObservation),
            typeof(BaseVectorCertificationObservationFact),
        ];

        protocolShapes.SelectMany(static type => type.GetProperties())
            .Should().OnlyContain(static property => property.SetMethod == null);
        protocolShapes.SelectMany(static type => type.GetConstructors())
            .Should().BeEmpty();
    }

    [Fact]
    public void FaultPlansEnforceTheClosedParameterVocabulary()
    {
        BaseVectorCertificationFaultPlan.Create(BaseVectorCertificationFaultKind.PartialBatchSuccess, partialSuccessCount: 255).PartialSuccessCount.Should().Be(255);
        BaseVectorCertificationFaultPlan.Create(BaseVectorCertificationFaultKind.DelaySearchVisibility, delay: TimeSpan.FromMilliseconds(1)).Delay.Should().Be(TimeSpan.FromMilliseconds(1));
        BaseVectorCertificationFaultPlan.Create(BaseVectorCertificationFaultKind.NonCooperativeRebuild, delay: TimeSpan.FromMilliseconds(1)).Delay.Should().Be(TimeSpan.FromMilliseconds(1));
        FluentActions.Invoking(() => BaseVectorCertificationFaultPlan.Create(BaseVectorCertificationFaultKind.None, occurrence: 2)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => BaseVectorCertificationFaultPlan.Create(BaseVectorCertificationFaultKind.FailBeforeSend, delay: TimeSpan.FromMilliseconds(1))).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => BaseVectorCertificationFaultPlan.Create(BaseVectorCertificationFaultKind.PartialBatchSuccess)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => BaseVectorCertificationFaultPlan.Create(BaseVectorCertificationFaultKind.CredentialFailure, occurrence: 17)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task MissingOrDuplicateFaultConsumptionFailsCertification(int observedOffset)
    {
        var fixture = new Fixture(faultObservationOffset: observedOffset);

        BaseVectorCertificationReport report = await BaseVectorProviderCertification.RunAsync(
            fixture,
            BaseVectorCertificationPlan.RequiredLocal());

        report.Succeeded.Should().BeFalse();
        report.Cases.Where(static result => result.CaseId.Contains(".fault.", StringComparison.Ordinal))
            .Should().OnlyContain(static result => result.Code == "base.testing.vector.faultNotConsumed");
    }

    private sealed class Fixture(
        int protocolVersion = BaseVectorProviderCertification.ProtocolVersion,
        CancellationTokenSource? cancelDuringInitialization = null,
        bool throwOnCreate = false,
        bool throwOnDispose = false,
        int faultObservationOffset = 0,
        bool rejectQueries = false,
        bool successfulFaultOperations = false,
        BaseVectorCertificationProviderClass providerClass = BaseVectorCertificationProviderClass.CoLocatedTransactional,
        bool disconnectDerivedHead = false) : IBaseVectorProviderCertificationFixture
    {
        public int Created { get; private set; }
        public int Disposed { get; private set; }
        public int CapturedHeads { get; private set; }
        public int ProviderInspections { get; private set; }
        public int ObservationReads { get; private set; }
        public long MaximumCapturedHead { get; private set; }
        public List<BaseVectorCertificationFaultKind> ReceivedFaults { get; } = [];
        public BaseVectorCertificationIdentity Identity { get; } = BaseVectorCertificationIdentity.Create(protocolVersion, "test.package", "1.0.0", "1.0.0", "1.0.0", "osx-arm64", "local", providerClass);
        public BaseVectorCertificationProviderClass ProviderClass => providerClass;
        public ValueTask<IBaseVectorCertificationHost> CreateHostAsync(BaseVectorCertificationHostRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (throwOnCreate) throw new InvalidOperationException("fixture-secret");
            request.Schema.Id.Should().Be("hpd.base.vector.certification.v1");
            ReceivedFaults.Add(request.Fault.Kind);
            Created++;
            return ValueTask.FromResult<IBaseVectorCertificationHost>(new Host(this, cancelDuringInitialization, throwOnDispose, request.Fault, faultObservationOffset, rejectQueries, request.Schema, successfulFaultOperations, providerClass, disconnectDerivedHead));
        }

        private sealed class Host : IBaseVectorCertificationHost, IBaseVectorCertificationAuthorityControl, IBaseVectorCertificationProviderControl, IBaseVectorCertificationObservationSource
        {
            private readonly Fixture _owner;
            private readonly bool _throwOnDispose;
            private readonly BaseVectorCertificationFaultPlan _fault;
            private readonly int _faultObservationOffset;
            private readonly ServiceProvider _services;
            private readonly bool _successfulFaultOperations;
            private readonly WriteOperationalState _writeState;
            private readonly BaseVectorCertificationProviderClass _providerClass;
            private readonly bool _disconnectDerivedHead;
            private readonly DerivedMutationAuthority? _derived;
            private long _position;
            private long _indexGeneration = 1;
            public Host(Fixture owner, CancellationTokenSource? cancelDuringInitialization, bool throwOnDispose, BaseVectorCertificationFaultPlan fault, int faultObservationOffset, bool rejectQueries, BaseVectorCertificationSchema schema, bool successfulFaultOperations, BaseVectorCertificationProviderClass providerClass, bool disconnectDerivedHead)
            {
                _owner = owner; _throwOnDispose = throwOnDispose; _fault = fault; _faultObservationOffset = faultObservationOffset; _successfulFaultOperations = successfulFaultOperations; _providerClass = providerClass; _disconnectDerivedHead = disconnectDerivedHead;
                bool queryFault = fault.Kind is BaseVectorCertificationFaultKind.MalformedCandidates or BaseVectorCertificationFaultKind.DuplicateCandidates or BaseVectorCertificationFaultKind.OversizedCandidates or BaseVectorCertificationFaultKind.CredentialFailure or BaseVectorCertificationFaultKind.TerminalSchemaFailure or BaseVectorCertificationFaultKind.NonCooperativeQuery;
                bool usesTestProvider = providerClass == BaseVectorCertificationProviderClass.DerivedJournal || rejectQueries || queryFault || fault.Kind is BaseVectorCertificationFaultKind.RebuildPublishResponseLoss or BaseVectorCertificationFaultKind.NonCooperativeInspection or BaseVectorCertificationFaultKind.NonCooperativeRebuild;
                bool needsObserver = providerClass == BaseVectorCertificationProviderClass.DerivedJournal || queryFault;
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddHPDBase(builder =>
                {
                    schema.Configure(builder);
                    if (usesTestProvider)
                    {
                        builder.ConfigureVector(options =>
                        {
                            if (providerClass == BaseVectorCertificationProviderClass.DerivedJournal) options.DerivedProviderDefaultConsistency = new BaseVectorConsistencyRequirement.BoundedStaleness(TimeSpan.FromMinutes(5));
                            if (fault.Kind == BaseVectorCertificationFaultKind.NonCooperativeQuery) options.ProviderTimeout = TimeSpan.FromMilliseconds(100);
                            if (fault.Kind is BaseVectorCertificationFaultKind.NonCooperativeInspection or BaseVectorCertificationFaultKind.NonCooperativeRebuild) options.AdministrationTimeout = TimeSpan.FromSeconds(1);
                        }).UseTestVectorProvider(options =>
                        {
                            options.Consistency = providerClass == BaseVectorCertificationProviderClass.DerivedJournal ? BaseVectorProviderConsistency.DerivedJournal : BaseVectorProviderConsistency.TransactionalCurrent;
                            options.SupportsRebuild = true;
                            options.CertificationFault = successfulFaultOperations ? BaseVectorCertificationFaultKind.None : fault.Kind;
                            if (fault.Kind == BaseVectorCertificationFaultKind.NonCooperativeQuery && !successfulFaultOperations) { options.SearchDelay = TimeSpan.FromMilliseconds(300); options.IgnoreSearchCancellation = true; }
                            if (fault.Kind is BaseVectorCertificationFaultKind.NonCooperativeInspection or BaseVectorCertificationFaultKind.NonCooperativeRebuild && !successfulFaultOperations) { options.AdministrationDelay = TimeSpan.FromMilliseconds(1200); options.IgnoreAdministrationCancellation = true; }
                        });
                    }
                });
                if (needsObserver)
                {
                    services.AddSingleton(new FixtureMode(providerClass == BaseVectorCertificationProviderClass.DerivedJournal));
                    services.AddSingleton<DerivedMutationAuthority>();
                    services.AddSingleton<IBaseCommittedMutationObserver>(static provider => provider.GetRequiredService<DerivedMutationAuthority>());
                }
                services.AddSingleton<WriteOperationalState>();
                services.AddSingleton<IBaseHealthContributor, WriteHealthContributor>();
                _services = services.BuildServiceProvider();
                IHPDBaseApplication application = _services.GetRequiredService<IHPDBaseApplication>();
                Application = cancelDuringInitialization is null ? application : new Application(application, cancelDuringInitialization);
                Sessions = _services.GetRequiredService<IBaseSessionFactory>();
                _derived = needsObserver ? _services.GetRequiredService<DerivedMutationAuthority>() : null;
                _writeState = _services.GetRequiredService<WriteOperationalState>();
            }
            public string StoreId => "inmemory";
            public IHPDBaseApplication Application { get; }
            public IBaseSessionFactory Sessions { get; }
            public IBaseVectorCertificationAuthorityControl Authority => this;
            public IBaseVectorCertificationProviderControl Provider => this;
            public IBaseVectorCertificationObservationSource Observations => this;
            public async ValueTask DisposeAsync() { _owner.Disposed++; await _services.DisposeAsync(); if (_throwOnDispose) throw new InvalidOperationException("fixture-secret"); }

            public ValueTask<OperationResult<BaseVectorCertificationAuthorityHead>> CaptureHeadAsync(CancellationToken cancellationToken = default) { _owner.CapturedHeads++; BaseVectorCertificationAuthorityHead head = Head(); _owner.MaximumCapturedHead = Math.Max(_owner.MaximumCapturedHead, head.HighWaterPosition); return ValueTask.FromResult(OperationResults.Ok(head)); }
            public ValueTask<OperationResult<BaseVectorCertificationAuthorityState>> InspectAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(OperationResults.Ok(BaseVectorCertificationAuthorityState.Create(Head(), [], [])));
            ValueTask<OperationResult<BaseVectorCertificationProviderState>> IBaseVectorCertificationProviderControl.InspectAsync(CancellationToken cancellationToken) { _owner.ProviderInspections++; return ValueTask.FromResult(FaultOr(ProviderState())); }
            public ValueTask<OperationResult<BaseVectorCertificationObservationPage>> ReadAsync(BaseVectorCertificationObservationRequest request, CancellationToken cancellationToken = default) { _owner.ObservationReads++; return ValueTask.FromResult(OperationResults.Ok(BaseVectorCertificationObservationPage.Create(0, request.AfterSequenceExclusive, false, []))); }
            public ValueTask<OperationResult<BaseVectorCertificationSeedResult>> SeedAsync(BaseVectorCertificationSeedRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(OperationResults.Ok(BaseVectorCertificationSeedResult.Create(request.Records.Count, Head())));
            public ValueTask<OperationResult<BaseVectorCertificationMutationResult>> CommitAsync(BaseVectorCertificationMutationRequest request, CancellationToken cancellationToken = default)
            {
                long first = _position + 1;
                _position += request.Mutations.Count;
                return ValueTask.FromResult(OperationResults.Ok(BaseVectorCertificationMutationResult.Create(request.Mutations.Count, first, _position, Head(), [])));
            }
            public ValueTask<OperationResult<BaseVectorCertificationTransitionResult>> TransitionAsync(BaseVectorCertificationTransitionRequest request, CancellationToken cancellationToken = default)
            {
                long previous = _indexGeneration++;
                return ValueTask.FromResult(OperationResults.Ok(BaseVectorCertificationTransitionResult.Create(request.Kind, request.CollectionId, request.IndexId, previous, _indexGeneration)));
            }
            public ValueTask<OperationResult<BaseVectorCertificationPruneResult>> PruneHistoryAsync(BaseVectorCertificationPruneRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(_providerClass == BaseVectorCertificationProviderClass.DerivedJournal ? OperationResults.Ok(BaseVectorCertificationPruneResult.Create(0, request.RetainFromPositionInclusive, Math.Max(request.RetainFromPositionInclusive, _derived!.Position))) : OperationResults.CapabilityUnavailable<BaseVectorCertificationPruneResult>(new BaseError { Code = "base.testing.vector.operationNotApplicable", Message = "The certification operation is not applicable.", Category = ErrorCategory.Capability }));
            public async ValueTask<OperationResult<BaseVectorCertificationAdvanceResult>> AdvanceAsync(BaseVectorCertificationAdvanceRequest request, CancellationToken cancellationToken = default)
            {
                if (_fault.Kind == BaseVectorCertificationFaultKind.NonCooperativeWrite && !_successfulFaultOperations)
                {
                    _writeState.Enter();
                    try { await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken.None).ConfigureAwait(false); }
                    finally { _writeState.Exit(); }
                }
                if (_fault.Kind != BaseVectorCertificationFaultKind.None && (_fault.Kind is >= BaseVectorCertificationFaultKind.FailBeforeSend and <= BaseVectorCertificationFaultKind.FencingLoss || _fault.Kind == BaseVectorCertificationFaultKind.NonCooperativeWrite)) return FaultOr(BaseVectorCertificationAdvanceResult.Create(0, 0, 0, 0));
                if (_providerClass != BaseVectorCertificationProviderClass.DerivedJournal) return OperationResults.CapabilityUnavailable<BaseVectorCertificationAdvanceResult>(new BaseError { Code = "base.testing.vector.operationNotApplicable", Message = "The certification operation is not applicable.", Category = ErrorCategory.Capability });
                long previous = _derived!.AppliedPosition;
                _derived.AdvanceTo(request.ThroughPositionInclusive);
                return OperationResults.Ok(BaseVectorCertificationAdvanceResult.Create(checked((int)(request.ThroughPositionInclusive - previous)), previous, request.ThroughPositionInclusive, _derived.SearchVisiblePosition));
            }
            public ValueTask<OperationResult<BaseVectorCertificationVisibilityResult>> PublishVisibilityAsync(BaseVectorCertificationVisibilityRequest request, CancellationToken cancellationToken = default)
            {
                if (_providerClass != BaseVectorCertificationProviderClass.DerivedJournal) return ValueTask.FromResult(OperationResults.CapabilityUnavailable<BaseVectorCertificationVisibilityResult>(new BaseError { Code = "base.testing.vector.operationNotApplicable", Message = "The certification operation is not applicable.", Category = ErrorCategory.Capability }));
                if (_fault.Kind == BaseVectorCertificationFaultKind.DelaySearchVisibility && !_successfulFaultOperations) return ValueTask.FromResult(FaultOr(BaseVectorCertificationVisibilityResult.Create(0, 0, 0)));
                long previous = _derived!.SearchVisiblePosition;
                _derived.PublishThrough(request.ThroughPositionInclusive);
                return ValueTask.FromResult(OperationResults.Ok(BaseVectorCertificationVisibilityResult.Create(previous, request.ThroughPositionInclusive, _derived.AppliedPosition)));
            }
            public ValueTask<OperationResult<BaseVectorCertificationRebuildResult>> RebuildAsync(BaseVectorCertificationRebuildRequest request, CancellationToken cancellationToken = default) { long previous = _indexGeneration++; return ValueTask.FromResult(FaultOr(BaseVectorCertificationRebuildResult.Create(request.CollectionId, request.IndexId, previous, _indexGeneration, Head()))); }
            public ValueTask<OperationResult<BaseVectorCertificationFaultState>> InspectFaultAsync(CancellationToken cancellationToken = default)
            {
                int observed = _fault.Kind == BaseVectorCertificationFaultKind.None ? 0 : Math.Max(0, _fault.Occurrence + _faultObservationOffset);
                return ValueTask.FromResult(OperationResults.Ok(BaseVectorCertificationFaultState.Create(_fault.Kind, _fault.Occurrence, observed, _fault.Kind != BaseVectorCertificationFaultKind.None && observed >= _fault.Occurrence, false)));
            }
            private OperationResult<T> FaultOr<T>(T value) => _fault.Kind != BaseVectorCertificationFaultKind.None && !_successfulFaultOperations
                ? OperationResults.StoreError<T>(new BaseError { Code = "base.testing.vector.fault." + string.Concat(_fault.Kind.ToString().Select((character, index) => char.IsUpper(character) && index != 0 ? "-" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString())), Message = "The certification fault was externally observed.", Category = ErrorCategory.Store })
                : OperationResults.Ok(value);
            private BaseVectorCertificationAuthorityHead Head() => BaseVectorCertificationAuthorityHead.Create(new string('a', 64), 0, 0, 0, _disconnectDerivedHead ? 0 : _derived?.Position ?? _position, DateTimeOffset.UnixEpoch);
            private BaseVectorCertificationProviderState ProviderState()
            {
                if (_providerClass != BaseVectorCertificationProviderClass.DerivedJournal || _derived is null) return BaseVectorCertificationProviderState.Create([]);
                return BaseVectorCertificationProviderState.Create([.. DerivedMutationAuthority.IndexIds.Select(index => BaseVectorCertificationIndexState.Create(DerivedMutationAuthority.CollectionId, index, _indexGeneration, 0, _derived.AppliedPosition, _derived.SearchVisiblePosition, _derived.CarrierCount, BaseVectorIndexState.Ready))]);
            }
        }

        private sealed record FixtureMode(bool Derived);

        private sealed class WriteOperationalState
        {
            private int _active;
            public bool Active => Volatile.Read(ref _active) != 0;
            public void Enter() => Volatile.Write(ref _active, 1);
            public void Exit() => Volatile.Write(ref _active, 0);
        }

        private sealed class WriteHealthContributor(WriteOperationalState state) : IBaseHealthContributor
        {
            public string Id => "hpd.base.vector.certification.write";
            public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool active = state.Active;
                return ValueTask.FromResult<HealthDescriptor[]>([new HealthDescriptor { Id = Id, Scope = HealthScope.Module, Status = active ? HealthStatus.Degraded : HealthStatus.Healthy, CheckedAt = DateTimeOffset.UnixEpoch, Summary = active ? "A certification write is quarantined." : "Certification writes are ready.", PublicSafe = false, Visibility = VisibilityLevel.Admin, Metrics = [new HealthMetric { Name = "quarantinedOperations", Kind = HealthMetricValueKind.Number, NumberValue = active ? 1 : 0 }] }]);
            }
        }

        private sealed class DerivedMutationAuthority(BaseTestVectorStore store, FixtureMode mode) : IBaseCommittedMutationObserver
        {
            private readonly object _gate = new();
            private readonly Dictionary<string, BaseTestVectorEntry> _records = new(StringComparer.Ordinal);
            private readonly SortedDictionary<long, BaseTestVectorEntry[]> _history = [];
            private long _position;
            private long _applied;
            private long _visible;
            public long Position { get { lock (_gate) return _position; } }
            public long AppliedPosition { get { lock (_gate) return _applied; } }
            public long SearchVisiblePosition { get { lock (_gate) return _visible; } }
            public int CarrierCount { get { lock (_gate) return _visible == 0 ? 0 : _history[_visible].Length; } }

            public ValueTask ObserveAsync(BaseRecordMutationEvent mutation, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(mutation.Resource.CollectionId, Collection, StringComparison.Ordinal)) return ValueTask.CompletedTask;
                lock (_gate)
                {
                    _position++;
                    if (mutation.After is { Payload.Fields: not null, Metadata.Revision: not null } after)
                        _records[after.Id.Value] = Entry(after, after.Payload.Fields);
                    else if (mutation.Resource.RecordId is { } id) _records.Remove(id.Value);
                    _history[_position] = _records.Values.ToArray();
                    if (mode.Derived) PublishAuthority(); else PublishCurrent();
                }
                return ValueTask.CompletedTask;
            }

            public void AdvanceTo(long target)
            {
                lock (_gate)
                {
                    if (target < _applied || target > _position) throw new ArgumentOutOfRangeException(nameof(target));
                    foreach (string index in Indexes)
                    {
                        store.SetDerivedState(Collection, index, _position, _applied, _visible, DateTimeOffset.UtcNow);
                        for (long position = _applied + 1; position <= target; position++) store.ApplyDerivedPosition(Collection, index, position, DateTimeOffset.UtcNow);
                    }
                    _applied = target;
                }
            }

            public void PublishThrough(long target)
            {
                lock (_gate)
                {
                    if (target < _visible || target > _applied) throw new ArgumentOutOfRangeException(nameof(target));
                    foreach (string index in Indexes)
                    {
                        store.Seed(Collection, index, target == 0 ? [] : _history[target]);
                        store.SetDerivedState(Collection, index, _position, _applied, target, DateTimeOffset.UtcNow);
                    }
                    _visible = target;
                }
            }

            private void PublishAuthority()
            {
                foreach (string index in Indexes)
                {
                    if (_position == 1) store.Seed(Collection, index, []);
                    store.SetDerivedState(Collection, index, _position, _applied, _visible, DateTimeOffset.UtcNow);
                }
            }

            private void PublishCurrent()
            {
                foreach (string index in Indexes) store.Seed(Collection, index, _records.Values);
                _applied = _visible = _position;
            }

            private static BaseTestVectorEntry Entry(RecordSnapshot after, Dictionary<string, JsonElement> fields)
            {
                float[] vector = fields["Embedding"].EnumerateArray().Select(static item => item.GetSingle()).ToArray();
                var filters = new Dictionary<string, BaseVectorFilterValue>(StringComparer.Ordinal)
                {
                    ["hpd.base.vector.certification.tenant"] = BaseVectorFilterValue.FromString(fields["Tenant"].GetString()!),
                    ["hpd.base.vector.certification.active"] = BaseVectorFilterValue.FromBoolean(fields["Active"].GetBoolean()),
                    ["hpd.base.vector.certification.priority"] = BaseVectorFilterValue.FromInteger(fields["Priority"].GetInt64()),
                    ["hpd.base.vector.certification.optional"] = fields["Optional"].ValueKind == JsonValueKind.Null ? BaseVectorFilterValue.Null() : BaseVectorFilterValue.FromString(fields["Optional"].GetString()!),
                };
                return new BaseTestVectorEntry { Record = new RecordEnvelope { CollectionId = after.CollectionId, Id = after.Id, Payload = after.Payload!, Metadata = after.Metadata! }, Vector = BaseVector.Create(vector), Filters = filters };
            }

            internal const string CollectionId = "hpd.base.vector.certification.records";
            internal static readonly string[] IndexIds = ["hpd.base.vector.certification.cosine", "hpd.base.vector.certification.dot", "hpd.base.vector.certification.euclidean"];
            private const string Collection = CollectionId;
            private static readonly string[] Indexes = IndexIds;
        }

        private sealed class Application(IHPDBaseApplication inner, CancellationTokenSource cancelDuringInitialization) : IHPDBaseApplication
        {
            public BaseApplicationReadiness CurrentReadiness => inner.CurrentReadiness;
            public IHPDBaseAdministration Administration => inner.Administration;
            public ValueTask<OperationResult<BaseApplicationReadiness>> InitializeAsync(CancellationToken cancellationToken = default)
            {
                cancelDuringInitialization.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return inner.InitializeAsync(cancellationToken);
            }
        }
    }
}
