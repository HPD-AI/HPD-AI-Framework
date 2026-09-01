namespace HPD.Base.Tests.Subjects;

public sealed class L45SubjectControlDispatcherTests
{
    [Fact]
    public async Task Degraded_subject_control_state_refuses_new_live_query_admission()
    {
        var state = new BaseSubjectControlOperationalState();
        state.MarkDegraded();
        var coordinator = new DefaultBaseLiveQueryCoordinator(
            new BaseLiveQueryOptions(),
            state);

        Func<Task> subscribe = async () => await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "subject-dependent",
            ExecuteAsync = _ => ValueTask.FromResult(new BaseLiveQueryEvaluation<int>
            {
                Value = 1,
                Dependencies = new BaseDependencySet { References = [] },
            }),
        });

        BaseLiveQueryException failure = (await subscribe.Should().ThrowAsync<BaseLiveQueryException>()).Which;
        failure.Code.Should().Be(BaseSubjectErrorCodes.ValidationUnavailable);
    }

    [Fact]
    public async Task StartupReconcilesOnlyControlEntriesAndDuplicateScansAreIdempotent()
    {
        var store = new ControlStore(
        [
            Entry(1, 0, 1, BaseSubjectAuthorityPublicationKind.InitialInstallation),
            new BaseMutationJournalEntry
            {
                Kind = BaseMutationJournalEntryKind.RecordMutation,
                Position = new BaseMutationJournalPosition(2),
                RecordMutation = new BaseRecordMutationJournalEntry
                {
                    EventId = "record-event",
                    Type = "record.updated",
                    SchemaVersion = "1",
                    OccurredAt = DateTimeOffset.UnixEpoch,
                    Operation = BaseOperationKind.Patch,
                    CollectionId = "records",
                    RecordId = RecordId.Create("one"),
                },
            },
            Entry(3, 1, 2, BaseSubjectAuthorityPublicationKind.EpochRotation),
        ], Current(1, 2, 3, BaseSubjectAuthorityPublicationKind.EpochRotation));
        var registry = new DefaultRecordStoreRegistry();
        registry.Add(new RecordStoreRegistration { StoreId = "store", Store = store, CollectionIds = ["records"] });
        var coordinator = new CapturingLiveQueryCoordinator();
        var hub = new BaseSubjectLiveControlHub();
        using BaseSubjectLiveControlHub.Lease live = hub.Subscribe(
            new HashSet<(string ContractId, int ContractVersion)> { ("example.subject", 1) });
        var dispatcher = new BaseSubjectControlDispatcher(registry, new DeterministicDependencyFactory(), coordinator, hub);

        await dispatcher.InitializeAsync(CancellationToken.None);
        await dispatcher.ReconcileAsync(CancellationToken.None);

        coordinator.Invalidations.Should().ContainSingle();
        BaseDependencyInvalidation invalidation = coordinator.Invalidations[0];
        invalidation.Reason.Should().Be(BaseDependencyInvalidationReasons.SubjectAuthorityChanged);
        invalidation.References.Should().ContainSingle(reference =>
            reference.TemplateId == BaseDependencyIds.SubjectContract
            && reference.Value.Contains("generation=1", StringComparison.Ordinal));
        store.RecordEntriesObserved.Should().Be(0, "historical record rows never enter the post-commit observer path");
        (await live.Reader.ReadAsync()).PublishedStateGeneration.Should().Be(2);
        live.Reader.TryRead(out _).Should().BeFalse("a duplicate scan cannot republish the same process-local control");
    }

    [Fact]
    public async Task PrunedCurrentPublicationReceiptReconcilesWithoutFabricatingHistory()
    {
        var store = new ControlStore([], Current(3, 4, 8, BaseSubjectAuthorityPublicationKind.EpochRotation), earliest: 9, highWater: 8);
        var registry = new DefaultRecordStoreRegistry();
        registry.Add(new RecordStoreRegistration { StoreId = "store", Store = store, CollectionIds = ["records"] });
        var coordinator = new CapturingLiveQueryCoordinator();
        var dispatcher = new BaseSubjectControlDispatcher(registry, new DeterministicDependencyFactory(), coordinator);

        await dispatcher.InitializeAsync(CancellationToken.None);

        coordinator.Invalidations.Should().ContainSingle();
        coordinator.Invalidations[0].References[0].Value.Should().Contain("generation=3");
    }

    [Fact]
    public async Task Corrupt_current_publication_receipt_maintenance_closes_instead_of_guessing()
    {
        BaseSubjectCurrentPublicationState valid = Current(1, 2, 3, BaseSubjectAuthorityPublicationKind.EpochRotation);
        BaseSubjectCurrentPublicationState corrupt = valid with
        {
            Receipt = valid.Receipt with { PublicationDigest = new string('f', 64) },
        };
        var store = new ControlStore(
            [Entry(1, 0, 1, BaseSubjectAuthorityPublicationKind.InitialInstallation), Entry(3, 1, 2, BaseSubjectAuthorityPublicationKind.EpochRotation)],
            corrupt,
            highWater: 3);
        var registry = new DefaultRecordStoreRegistry();
        registry.Add(new RecordStoreRegistration { StoreId = "store", Store = store, CollectionIds = ["records"] });
        var dispatcher = new BaseSubjectControlDispatcher(registry, new DeterministicDependencyFactory(), new CapturingLiveQueryCoordinator());

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.InitializeAsync(CancellationToken.None).AsTask());

        failure.Message.Should().Be(BaseSubjectErrorCodes.ProviderContractInvalid);
    }

    [Fact]
    public async Task Conflicting_duplicate_publication_position_is_rejected()
    {
        BaseMutationJournalEntry conflicting = Entry(3, 1, 3, BaseSubjectAuthorityPublicationKind.EpochRotation);
        var store = new ControlStore(
            [Entry(1, 0, 1, BaseSubjectAuthorityPublicationKind.InitialInstallation), Entry(3, 1, 2, BaseSubjectAuthorityPublicationKind.EpochRotation), conflicting],
            Current(1, 2, 3, BaseSubjectAuthorityPublicationKind.EpochRotation),
            highWater: 3);
        var registry = new DefaultRecordStoreRegistry();
        registry.Add(new RecordStoreRegistration { StoreId = "store", Store = store, CollectionIds = ["records"] });
        var dispatcher = new BaseSubjectControlDispatcher(registry, new DeterministicDependencyFactory(), new CapturingLiveQueryCoordinator());

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.InitializeAsync(CancellationToken.None).AsTask());

        failure.Message.Should().Be(BaseSubjectErrorCodes.ProviderContractInvalid);
    }

    [Fact]
    public async Task Noncooperative_sink_is_quarantined_and_late_completion_recovers_without_duplicate_delivery()
    {
        var store = new ControlStore(
            [Entry(1, 0, 1, BaseSubjectAuthorityPublicationKind.InitialInstallation), Entry(2, 1, 2, BaseSubjectAuthorityPublicationKind.EpochRotation)],
            Current(1, 2, 2, BaseSubjectAuthorityPublicationKind.EpochRotation),
            highWater: 2);
        var registry = new DefaultRecordStoreRegistry();
        registry.Add(new RecordStoreRegistration { StoreId = "store", Store = store, CollectionIds = ["records"] });
        var state = new BaseSubjectControlOperationalState();
        var coordinator = new BlockingLiveQueryCoordinator();
        var hub = new BaseSubjectLiveControlHub(state);
        var dispatcher = new BaseSubjectControlDispatcher(
            registry,
            new DeterministicDependencyFactory(),
            coordinator,
            hub,
            state,
            TimeSpan.FromMilliseconds(20));

        InvalidOperationException timeout = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.InitializeAsync(CancellationToken.None).AsTask());
        timeout.Message.Should().Be(BaseSubjectErrorCodes.ValidationUnavailable);
        state.Degraded.Should().BeTrue();
        state.Quarantined.Should().Be(1);
        coordinator.Calls.Should().Be(1);

        coordinator.Complete();
        await WaitUntilAsync(() => state.AdmitsLiveState && state.Quarantined == 0);

        coordinator.Calls.Should().Be(1, "late acknowledgment is retained and must not invoke the sink twice");
    }

    [Fact]
    public async Task Live_subscription_registration_reconciles_the_latest_published_generation_without_a_race()
    {
        var hub = new BaseSubjectLiveControlHub();
        BaseSubjectAuthorityPublicationFact publication = Entry(
            7,
            1,
            2,
            BaseSubjectAuthorityPublicationKind.EpochRotation).SubjectAuthorityPublication!;

        hub.Publish(publication);
        using BaseSubjectLiveControlHub.Lease matching = hub.Subscribe(
            new HashSet<(string ContractId, int ContractVersion)> { ("example.subject", 1) });
        using BaseSubjectLiveControlHub.Lease unrelated = hub.Subscribe(
            new HashSet<(string ContractId, int ContractVersion)> { ("other.subject", 1) });

        BaseSubjectAuthorityPublicationFact reconciled = await matching.Reader.ReadAsync();
        reconciled.Should().BeEquivalentTo(publication);
        unrelated.Reader.TryRead(out _).Should().BeFalse();

        Action conflictingReplay = () => hub.Publish(publication with
        {
            Position = new BaseMutationJournalPosition(8),
        });
        conflictingReplay.Should().Throw<InvalidOperationException>()
            .WithMessage(BaseSubjectErrorCodes.ProviderContractInvalid);
    }

    private static BaseMutationJournalEntry Entry(
        long position,
        long previous,
        long published,
        BaseSubjectAuthorityPublicationKind kind) => new()
    {
        Kind = BaseMutationJournalEntryKind.SubjectAuthorityPublication,
        Position = new BaseMutationJournalPosition(position),
        SubjectAuthorityPublication = new BaseSubjectAuthorityPublicationFact
        {
            Position = new BaseMutationJournalPosition(position),
            ContractId = "example.subject",
            ContractVersion = 1,
            PreviousStateGeneration = previous,
            PublishedStateGeneration = published,
            RestoreEpoch = 0,
            Kind = kind,
        },
    };

    private static BaseSubjectCurrentPublicationState Current(
        long previous,
        long published,
        long position,
        BaseSubjectAuthorityPublicationKind kind)
    {
        const string contractId = "example.subject";
        const int contractVersion = 1;
        string checksum = new('a', 64);
        var epoch = new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)1, 16).ToArray());
        var publicationPosition = new BaseMutationJournalPosition(position);
        return new BaseSubjectCurrentPublicationState
        {
            ContractId = contractId,
            ContractVersion = contractVersion,
            ContractChecksum = checksum,
            AuthorityEpoch = epoch,
            Receipt = new BaseSubjectCurrentPublicationReceipt
            {
                PreviousStateGeneration = previous,
                PublishedStateGeneration = published,
                RestoreEpoch = 0,
                Kind = kind,
                OriginalPublicationPosition = publicationPosition,
                PublicationDigest = BaseSubjectPublicationIntegrity.Compute(
                    contractId,
                    contractVersion,
                    checksum,
                    previous,
                    published,
                    0,
                    kind,
                    publicationPosition,
                    epoch),
            },
        };
    }

    private sealed class ControlStore(
        BaseMutationJournalEntry[] entries,
        BaseSubjectCurrentPublicationState current,
        long earliest = 1,
        long? highWater = null) : IRecordStore, ITransactionalMutationJournalStore, IBaseSubjectPublicationStore
    {
        public int RecordEntriesObserved { get; private set; }
        public StoreCapabilityDescriptor Capabilities { get; } = new global::HPD.Base.Tests.FakeRecordStore("store").Capabilities;

        public ValueTask<BaseMutationJournalBounds> GetMutationJournalBoundsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new BaseMutationJournalBounds(
                new BaseMutationJournalPosition(earliest),
                new BaseMutationJournalPosition(highWater ?? entries.LastOrDefault()?.Position.Value ?? 0),
                0));

        public ValueTask<BaseMutationJournalPage> ReadMutationJournalAsync(BaseMutationJournalReadRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BaseMutationJournalEntry[] page = entries
                .Where(entry => entry.Position.Value > request.After.Value
                    && entry.Position.Value <= (request.Through?.Value ?? long.MaxValue))
                .Take(request.Limit)
                .ToArray();
            return ValueTask.FromResult(new BaseMutationJournalPage
            {
                Entries = page,
                Earliest = new BaseMutationJournalPosition(earliest),
                HighWatermark = new BaseMutationJournalPosition(highWater ?? entries.LastOrDefault()?.Position.Value ?? 0),
                HasMore = false,
            });
        }

        public ValueTask<BaseMutationJournalEntry?> FindMutationJournalEntryAsync(string eventId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<BaseMutationJournalEntry?>(entries.FirstOrDefault(entry =>
                string.Equals(entry.RecordMutation?.EventId, eventId, StringComparison.Ordinal)));

        public ValueTask<OperationResult<BaseSubjectCurrentPublicationState[]>> ReadCurrentSubjectPublicationsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.Ok(new[] { current }));

        public ValueTask<OperationResult<RecordPage>> ListAsync(CollectionDefinition collection, RecordQuery query, OperationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.Ok(new RecordPage { Items = [], Page = new PageInfo() }));
        public ValueTask<OperationResult<RecordEnvelope>> GetAsync(CollectionDefinition collection, RecordId id, OperationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.NotFound<RecordEnvelope>(new BaseError { Code = "not-found", Message = "Not found.", Category = ErrorCategory.NotFound }));
    }

    private sealed class DeterministicDependencyFactory : IBaseDependencyReferenceFactory
    {
        public BaseDependencyReference Create(string templateId, params BaseDependencyParameter[] parameters) => new()
        {
            TemplateId = templateId,
            Value = string.Join(";", parameters.Select(static parameter => parameter.Name + "=" + parameter.Value)),
        };

        public BaseDependencySet CreateSet(params BaseDependencyReference[] references) => new() { References = references };
    }

    private sealed class CapturingLiveQueryCoordinator : IBaseLiveQueryCoordinator
    {
        public List<BaseDependencyInvalidation> Invalidations { get; } = [];
        public ValueTask<IBaseLiveQuerySubscription<T>> SubscribeAsync<T>(BaseLiveQueryRequest<T> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask InvalidateAsync(BaseDependencyInvalidation invalidation, CancellationToken cancellationToken = default)
        {
            Invalidations.Add(invalidation);
            return ValueTask.CompletedTask;
        }
        public ValueTask InvalidateSubjectAuthorityAsync(
            BaseSubjectAuthorityPublicationFact publication,
            BaseDependencyInvalidation invalidation,
            CancellationToken cancellationToken = default)
        {
            Invalidations.Add(invalidation);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingLiveQueryCoordinator : IBaseLiveQueryCoordinator
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public void Complete() => _completion.TrySetResult();
        public ValueTask<IBaseLiveQuerySubscription<T>> SubscribeAsync<T>(BaseLiveQueryRequest<T> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask InvalidateAsync(BaseDependencyInvalidation invalidation, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
        public ValueTask InvalidateSubjectAuthorityAsync(
            BaseSubjectAuthorityPublicationFact publication,
            BaseDependencyInvalidation invalidation,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return new ValueTask(_completion.Task);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
            await Task.Delay(10, timeout.Token);
    }
}
