using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class CapacityRequestContractsV1Tests
{
    [Fact]
    public void Subject_union_is_closed_and_kind_bound()
    {
        CapacitySubjectV1[] subjects =
        [
            new CapacitySubjectV1.Tenant(TenantId.Create()),
            new CapacitySubjectV1.Session(SessionId.Create()),
            new CapacitySubjectV1.Participant(ParticipantId.Create()),
            new CapacitySubjectV1.Operation(OperationId.Create()),
            new CapacitySubjectV1.Provider(ProviderId.Create()),
            new CapacitySubjectV1.Custodian(CustodianDescriptorId.Create()),
            new CapacitySubjectV1.Exporter(ExportId.Create()),
            new CapacitySubjectV1.Subscriber(SubscriberId.Create()),
            new CapacitySubjectV1.Schema(SchemaId.Create()),
            new CapacitySubjectV1.Owner(OwnerSliceId.S2),
            new CapacitySubjectV1.Sink(SinkGenerationId.Create()),
        ];

        Assert.Equal(Enumerable.Range(1, 11), subjects.Select(static subject => (int)subject.Kind));
        foreach (var subject in subjects)
        {
            Span<byte> encoded = stackalloc byte[16];
            Assert.True(subject.TryWriteIdentity(encoded, out var length));
            Assert.Equal(subject is CapacitySubjectV1.Owner ? 2 : 16, length);
        }

        Assert.Throws<ArgumentException>(() => new CapacitySubjectV1.Tenant(default));
        Assert.Throws<ArgumentException>(() => new CapacitySubjectV1.Session(default));
        Assert.Throws<ArgumentException>(() => new CapacitySubjectV1.Participant(default));
        Assert.Throws<ArgumentException>(() => new CapacitySubjectV1.Operation(default));
        Assert.Throws<ArgumentException>(() => new CapacitySubjectV1.Provider(default));
        Assert.Throws<ArgumentException>(() => new CapacitySubjectV1.Custodian(default));
        Assert.Throws<ArgumentException>(() => new CapacitySubjectV1.Exporter(default));
        Assert.Throws<ArgumentException>(() => new CapacitySubjectV1.Subscriber(default));
        Assert.Throws<ArgumentException>(() => new CapacitySubjectV1.Schema(default));
        Assert.Throws<ArgumentException>(() => new CapacitySubjectV1.Owner((OwnerSliceId)ushort.MaxValue));
        Assert.Throws<ArgumentException>(() => new CapacitySubjectV1.Sink(default));
    }

    [Fact]
    public void Scope_derives_one_canonical_kind_without_generic_identity_escape()
    {
        var tenant = TenantId.Create();
        var session = SessionId.Create();
        var tenantScope = new CapacityScopeV1(tenant);
        var sessionScope = new CapacityScopeV1(tenant, session);
        var providerScope = new CapacityScopeV1(tenant, session, new CapacitySubjectV1.Provider(ProviderId.Create()));

        Assert.Equal(CapacityScopeKindV1.Tenant, tenantScope.Kind);
        Assert.Equal(CapacityScopeKindV1.Session, sessionScope.Kind);
        Assert.Equal(CapacityScopeKindV1.Provider, providerScope.Kind);
        Assert.Throws<ArgumentException>(() => new CapacityScopeV1(default));
        Assert.Throws<ArgumentException>(() => new CapacityScopeV1(tenant, default(SessionId)));
        Assert.Throws<ArgumentException>(() => new CapacityScopeV1(tenant, subject: new CapacitySubjectV1.Tenant(tenant)));
        Assert.Throws<ArgumentException>(() => new CapacityScopeV1(tenant, subject: new CapacitySubjectV1.Session(session)));

        CapacitySubjectV1[] subjects =
        [
            new CapacitySubjectV1.Participant(ParticipantId.Create()),
            new CapacitySubjectV1.Operation(OperationId.Create()),
            new CapacitySubjectV1.Provider(ProviderId.Create()),
            new CapacitySubjectV1.Custodian(CustodianDescriptorId.Create()),
            new CapacitySubjectV1.Exporter(ExportId.Create()),
            new CapacitySubjectV1.Subscriber(SubscriberId.Create()),
            new CapacitySubjectV1.Schema(SchemaId.Create()),
            new CapacitySubjectV1.Owner(OwnerSliceId.S2),
            new CapacitySubjectV1.Sink(SinkGenerationId.Create()),
        ];
        Assert.Equal(
            [CapacityScopeKindV1.Participant, CapacityScopeKindV1.Operation, CapacityScopeKindV1.Provider,
             CapacityScopeKindV1.Custodian, CapacityScopeKindV1.Exporter, CapacityScopeKindV1.Subscriber,
             CapacityScopeKindV1.Schema, CapacityScopeKindV1.Owner, CapacityScopeKindV1.Sink],
            subjects.Select(subject => new CapacityScopeV1(tenant, subject: subject).Kind));
    }

    [Fact]
    public void Charge_enforces_dimension_scope_amount_and_purpose()
    {
        var scope = new CapacityScopeV1(TenantId.Create(), subject: new CapacitySubjectV1.Participant(ParticipantId.Create()));
        var purpose = CapacityPurposeId.Create();
        var descriptor = CapacityDimensionRegistryV1.Get(CapacityDimensionsV1.MediaBytes);

        var charge = new CapacityChargeV1(descriptor.Id, scope, descriptor.MaximumPerCharge, purpose);

        Assert.Equal(descriptor.Id, charge.DimensionId);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CapacityChargeV1(descriptor.Id, scope, 0, purpose));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CapacityChargeV1(descriptor.Id, scope, descriptor.MaximumPerCharge + 1, purpose));
        Assert.Throws<ArgumentException>(() => new CapacityChargeV1(descriptor.Id, scope, 1, default));
        var exporterScope = new CapacityScopeV1(TenantId.Create(), subject: new CapacitySubjectV1.Exporter(ExportId.Create()));
        Assert.Throws<ArgumentException>(() => new CapacityChargeV1(descriptor.Id, exporterScope, 1, purpose));

        foreach (var dimension in CapacityDimensionRegistryV1.All)
        {
            var dimensionScope = ScopeFor(dimension.ScopeKinds[0]);
            Assert.Equal(dimension.MaximumPerCharge - 1, new CapacityChargeV1(dimension.Id, dimensionScope, dimension.MaximumPerCharge - 1, purpose).Amount);
            Assert.Equal(dimension.MaximumPerCharge, new CapacityChargeV1(dimension.Id, dimensionScope, dimension.MaximumPerCharge, purpose).Amount);
            Assert.Throws<ArgumentOutOfRangeException>(() => new CapacityChargeV1(dimension.Id, dimensionScope, checked(dimension.MaximumPerCharge + 1), purpose));
        }
    }

    [Fact]
    public void Request_owns_sorts_bounds_and_deduplicates_charges()
    {
        var tenant = TenantId.Create();
        var session = SessionId.Create();
        var purpose = CapacityPurposeId.Create();
        var media = new CapacityChargeV1(
            CapacityDimensionsV1.MediaBytes,
            new CapacityScopeV1(tenant, session, new CapacitySubjectV1.Participant(ParticipantId.Create())),
            20,
            purpose);
        var queue = new CapacityChargeV1(
            CapacityDimensionsV1.QueueItems,
            new CapacityScopeV1(tenant, session, new CapacitySubjectV1.Operation(OperationId.Create())),
            1,
            purpose);
        var source = new[] { queue, media };
        var request = new CapacityRequestV1(
            OperationId.Create(),
            ExpectedAuthorityVectorV1.Create(new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()), []),
            source,
            new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 10),
            CapacityPriorityV1.Normal);

        source[0] = media;
        Assert.Equal([media, queue], request.Charges);
        Assert.IsNotType<CapacityChargeV1[]>(request.Charges);
        Assert.Throws<ArgumentException>(() => new CapacityRequestV1(request.OperationId, request.Authority, [media, media], request.Deadline, request.Priority));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CapacityRequestV1(request.OperationId, request.Authority, [], request.Deadline, request.Priority));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CapacityRequestV1(request.OperationId, request.Authority, Enumerable.Repeat(media, 257), request.Deadline, request.Priority));
        Assert.Throws<ArgumentException>(() => new CapacityRequestV1(default, request.Authority, [media], request.Deadline, request.Priority));
        Assert.Throws<ArgumentException>(() => new CapacityRequestV1(request.OperationId, request.Authority, [media], default, request.Priority));
        Assert.Throws<ArgumentException>(() => new CapacityRequestV1(request.OperationId, request.Authority, [media], request.Deadline, (CapacityPriorityV1)ushort.MaxValue));
    }

    [Fact]
    public void Request_stops_enumeration_at_the_first_out_of_bound_item()
    {
        var charge = new CapacityChargeV1(CapacityDimensionsV1.MediaBytes, ScopeFor(CapacityScopeKindV1.Tenant), 1, CapacityPurposeId.Create());
        var authority = ExpectedAuthorityVectorV1.Create(new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()), []);
        var deadline = new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CapacityRequestV1(OperationId.Create(), authority, TooMany(), deadline, CapacityPriorityV1.Normal));

        IEnumerable<CapacityChargeV1> TooMany()
        {
            for (var index = 0; index < 257; index++) yield return charge with { };
            throw new InvalidOperationException("The constructor enumerated beyond the first forbidden item.");
        }
    }

    [Fact]
    public void Canonical_order_includes_full_scope_context_and_raw_purpose_bytes()
    {
        var tenantA = TenantId.FromValue(StableId128.FromBytes(Convert.FromHexString("00000000000000000000000000000001")));
        var tenantB = TenantId.FromValue(StableId128.FromBytes(Convert.FromHexString("00000000000000000000000000000002")));
        var participant = ParticipantId.FromValue(StableId128.FromBytes(Convert.FromHexString("00000000000000000000000000000003")));
        var purposeHigh = CapacityPurposeId.FromValue(StableId128.FromBytes(Convert.FromHexString("80000000000000000000000000000000")));
        var purposeLow = CapacityPurposeId.FromValue(StableId128.FromBytes(Convert.FromHexString("00000000000000000000000000000004")));
        var scopeA = new CapacityScopeV1(tenantA, subject: new CapacitySubjectV1.Participant(participant));
        var scopeB = new CapacityScopeV1(tenantB, subject: new CapacitySubjectV1.Participant(participant));
        var low = new CapacityChargeV1(CapacityDimensionsV1.MediaBytes, scopeA, 1, purposeLow);
        var high = new CapacityChargeV1(CapacityDimensionsV1.MediaBytes, scopeA, 1, purposeHigh);
        var otherContext = new CapacityChargeV1(CapacityDimensionsV1.MediaBytes, scopeB, 1, purposeLow);
        var request = new CapacityRequestV1(
            OperationId.Create(),
            ExpectedAuthorityVectorV1.Create(new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()), []),
            [high, otherContext, low],
            new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 1),
            CapacityPriorityV1.Normal);

        Assert.Equal(3, request.Charges.Count);
        Assert.True(CapacityChargeComparerV1.Instance.Compare(low, high) < 0);
        Assert.NotEqual(0, CapacityChargeComparerV1.Instance.Compare(low, otherContext));
    }

    private static CapacityScopeV1 ScopeFor(CapacityScopeKindV1 kind)
    {
        var tenant = TenantId.Create();
        var session = SessionId.Create();
        return kind switch
        {
            CapacityScopeKindV1.Tenant => new(tenant),
            CapacityScopeKindV1.Session => new(tenant, session),
            CapacityScopeKindV1.Participant => new(tenant, session, new CapacitySubjectV1.Participant(ParticipantId.Create())),
            CapacityScopeKindV1.Operation => new(tenant, session, new CapacitySubjectV1.Operation(OperationId.Create())),
            CapacityScopeKindV1.Provider => new(tenant, session, new CapacitySubjectV1.Provider(ProviderId.Create())),
            CapacityScopeKindV1.Sink => new(tenant, session, new CapacitySubjectV1.Sink(SinkGenerationId.Create())),
            CapacityScopeKindV1.Subscriber => new(tenant, session, new CapacitySubjectV1.Subscriber(SubscriberId.Create())),
            CapacityScopeKindV1.Custodian => new(tenant, session, new CapacitySubjectV1.Custodian(CustodianDescriptorId.Create())),
            CapacityScopeKindV1.Schema => new(tenant, session, new CapacitySubjectV1.Schema(SchemaId.Create())),
            CapacityScopeKindV1.Exporter => new(tenant, session, new CapacitySubjectV1.Exporter(ExportId.Create())),
            CapacityScopeKindV1.Owner => new(tenant, session, new CapacitySubjectV1.Owner(OwnerSliceId.S2)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}
