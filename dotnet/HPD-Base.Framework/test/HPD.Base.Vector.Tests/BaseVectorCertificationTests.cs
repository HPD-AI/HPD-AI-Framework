using FluentAssertions;
using HPD.Base.Testing;
using Microsoft.Extensions.DependencyInjection;
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
        report.Cases.Should().HaveCount(12).And.OnlyContain(static result => result.Outcome == BaseVectorCertificationCaseOutcome.Passed);
        fixture.Created.Should().Be(12);
        fixture.Disposed.Should().Be(12);
        fixture.CapturedHeads.Should().Be(5);
        fixture.ProviderInspections.Should().Be(10);
        fixture.ObservationReads.Should().Be(12);
        fixture.ReceivedFaults.Distinct().Should().BeEquivalentTo(Enum.GetValues<BaseVectorCertificationFaultKind>()
            .Where(static kind => kind is BaseVectorCertificationFaultKind.None or >= BaseVectorCertificationFaultKind.RebuildPublishResponseLoss));
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
        bool successfulFaultOperations = false) : IBaseVectorProviderCertificationFixture
    {
        public int Created { get; private set; }
        public int Disposed { get; private set; }
        public int CapturedHeads { get; private set; }
        public int ProviderInspections { get; private set; }
        public int ObservationReads { get; private set; }
        public List<BaseVectorCertificationFaultKind> ReceivedFaults { get; } = [];
        public BaseVectorCertificationIdentity Identity { get; } = BaseVectorCertificationIdentity.Create(protocolVersion, "test.package", "1.0.0", "1.0.0", "1.0.0", "osx-arm64", "local", BaseVectorCertificationProviderClass.CoLocatedTransactional);
        public BaseVectorCertificationProviderClass ProviderClass => BaseVectorCertificationProviderClass.CoLocatedTransactional;
        public ValueTask<IBaseVectorCertificationHost> CreateHostAsync(BaseVectorCertificationHostRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (throwOnCreate) throw new InvalidOperationException("fixture-secret");
            request.Schema.Id.Should().Be("hpd.base.vector.certification.v1");
            ReceivedFaults.Add(request.Fault.Kind);
            Created++;
            return ValueTask.FromResult<IBaseVectorCertificationHost>(new Host(this, cancelDuringInitialization, throwOnDispose, request.Fault, faultObservationOffset, rejectQueries, request.Schema, successfulFaultOperations));
        }

        private sealed class Host : IBaseVectorCertificationHost, IBaseVectorCertificationAuthorityControl, IBaseVectorCertificationProviderControl, IBaseVectorCertificationObservationSource
        {
            private readonly Fixture _owner;
            private readonly bool _throwOnDispose;
            private readonly BaseVectorCertificationFaultPlan _fault;
            private readonly int _faultObservationOffset;
            private readonly ServiceProvider _services;
            private readonly bool _successfulFaultOperations;
            private long _position;
            private long _indexGeneration = 1;
            public Host(Fixture owner, CancellationTokenSource? cancelDuringInitialization, bool throwOnDispose, BaseVectorCertificationFaultPlan fault, int faultObservationOffset, bool rejectQueries, BaseVectorCertificationSchema schema, bool successfulFaultOperations)
            {
                _owner = owner; _throwOnDispose = throwOnDispose; _fault = fault; _faultObservationOffset = faultObservationOffset; _successfulFaultOperations = successfulFaultOperations;
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddHPDBase(builder =>
                {
                    schema.Configure(builder);
                    if (rejectQueries) builder.UseTestVectorProvider();
                });
                _services = services.BuildServiceProvider();
                IHPDBaseApplication application = _services.GetRequiredService<IHPDBaseApplication>();
                Application = cancelDuringInitialization is null ? application : new Application(application, cancelDuringInitialization);
                Sessions = _services.GetRequiredService<IBaseSessionFactory>();
            }
            public string StoreId => "inmemory";
            public IHPDBaseApplication Application { get; }
            public IBaseSessionFactory Sessions { get; }
            public IBaseVectorCertificationAuthorityControl Authority => this;
            public IBaseVectorCertificationProviderControl Provider => this;
            public IBaseVectorCertificationObservationSource Observations => this;
            public async ValueTask DisposeAsync() { _owner.Disposed++; await _services.DisposeAsync(); if (_throwOnDispose) throw new InvalidOperationException("fixture-secret"); }
            public ValueTask<OperationResult<BaseVectorCertificationAuthorityHead>> CaptureHeadAsync(CancellationToken cancellationToken = default) { _owner.CapturedHeads++; return ValueTask.FromResult(OperationResults.Ok(Head())); }
            public ValueTask<OperationResult<BaseVectorCertificationAuthorityState>> InspectAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(OperationResults.Ok(BaseVectorCertificationAuthorityState.Create(Head(), [], [])));
            ValueTask<OperationResult<BaseVectorCertificationProviderState>> IBaseVectorCertificationProviderControl.InspectAsync(CancellationToken cancellationToken) { _owner.ProviderInspections++; return ValueTask.FromResult(FaultOr(BaseVectorCertificationProviderState.Create([]))); }
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
            public ValueTask<OperationResult<BaseVectorCertificationPruneResult>> PruneHistoryAsync(BaseVectorCertificationPruneRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(OperationResults.CapabilityUnavailable<BaseVectorCertificationPruneResult>(new BaseError { Code = "base.testing.vector.operationNotApplicable", Message = "The certification operation is not applicable.", Category = ErrorCategory.Capability }));
            public ValueTask<OperationResult<BaseVectorCertificationAdvanceResult>> AdvanceAsync(BaseVectorCertificationAdvanceRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(_fault.Kind == BaseVectorCertificationFaultKind.None ? OperationResults.CapabilityUnavailable<BaseVectorCertificationAdvanceResult>(new BaseError { Code = "base.testing.vector.operationNotApplicable", Message = "The certification operation is not applicable.", Category = ErrorCategory.Capability }) : FaultOr(BaseVectorCertificationAdvanceResult.Create(0, 0, 0, 0)));
            public ValueTask<OperationResult<BaseVectorCertificationVisibilityResult>> PublishVisibilityAsync(BaseVectorCertificationVisibilityRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(FaultOr(BaseVectorCertificationVisibilityResult.Create(0, 0, 0)));
            public ValueTask<OperationResult<BaseVectorCertificationRebuildResult>> RebuildAsync(BaseVectorCertificationRebuildRequest request, CancellationToken cancellationToken = default) { long previous = _indexGeneration++; return ValueTask.FromResult(FaultOr(BaseVectorCertificationRebuildResult.Create(request.CollectionId, request.IndexId, previous, _indexGeneration, Head()))); }
            public ValueTask<OperationResult<BaseVectorCertificationFaultState>> InspectFaultAsync(CancellationToken cancellationToken = default)
            {
                int observed = _fault.Kind == BaseVectorCertificationFaultKind.None ? 0 : Math.Max(0, _fault.Occurrence + _faultObservationOffset);
                return ValueTask.FromResult(OperationResults.Ok(BaseVectorCertificationFaultState.Create(_fault.Kind, _fault.Occurrence, observed, _fault.Kind != BaseVectorCertificationFaultKind.None && observed >= _fault.Occurrence, false)));
            }
            private OperationResult<T> FaultOr<T>(T value) => _fault.Kind != BaseVectorCertificationFaultKind.None && !_successfulFaultOperations
                ? OperationResults.StoreError<T>(new BaseError { Code = "base.testing.vector.faultObserved", Message = "The certification fault was externally observed.", Category = ErrorCategory.Store })
                : OperationResults.Ok(value);
            private BaseVectorCertificationAuthorityHead Head() => BaseVectorCertificationAuthorityHead.Create(new string('a', 64), 0, 0, 0, _position, DateTimeOffset.UnixEpoch);
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
