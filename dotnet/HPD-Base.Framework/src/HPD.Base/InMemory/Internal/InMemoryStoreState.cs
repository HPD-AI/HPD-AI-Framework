
using System.Collections.Immutable;

namespace HPD.Base;

internal sealed class InMemoryStoreState
{
    internal InMemoryStoreState()
    {
    }

    private InMemoryStoreState(InMemoryStoreState source)
    {
        Collections = source.Collections;
        NextRecordId = source.NextRecordId;
        NextRevision = source.NextRevision;
        GlobalMutationPosition = source.GlobalMutationPosition;
        Receipts = source.Receipts;
        ExpiredSemanticRetirementReceiptFloors = source.ExpiredSemanticRetirementReceiptFloors;
        VectorProjections = source.VectorProjections;
        TextProjections = source.TextProjections;
        TextRebuildReceipts = source.TextRebuildReceipts;
        LogicalIndexes = source.LogicalIndexes;
        SubjectContracts = source.SubjectContracts;
        SubjectLifetimes = source.SubjectLifetimes;
        SubjectTerminals = source.SubjectTerminals;
        SubjectLifecycleFacts = source.SubjectLifecycleFacts;
        SubjectLifecycleMemberships = source.SubjectLifecycleMemberships;
        SubjectLifecycleMembershipIndex = source.SubjectLifecycleMembershipIndex;
        SubjectLifecycleConsumers = source.SubjectLifecycleConsumers;
        SubjectLifecycleCheckpoints = source.SubjectLifecycleCheckpoints;
        SubjectLifecycleDeliveryEpoch = source.SubjectLifecycleDeliveryEpoch;
        SubjectRetirementBarriers = source.SubjectRetirementBarriers;
        SubjectRetirementTerminals = source.SubjectRetirementTerminals;
        SubjectRetirementPosition = source.SubjectRetirementPosition;
        SubjectRetirementPublications = source.SubjectRetirementPublications;
        ModuleGenerations = source.ModuleGenerations;
        SemanticActivationScopes = source.SemanticActivationScopes;
        Activations = source.Activations;
        ActivationPruneFloors = source.ActivationPruneFloors;
        ActivationsByProtectedScope = source.ActivationsByProtectedScope;
        DisposedActivationsByAuthority = source.DisposedActivationsByAuthority;
        Executors = source.Executors;
        Schedules = source.Schedules;
        ScheduleOccurrences = source.ScheduleOccurrences;
        ScheduleCancellations = source.ScheduleCancellations;
        ActivationInstanceReceipts = source.ActivationInstanceReceipts;
        ActivationInstanceReceiptCompactionFacts = source.ActivationInstanceReceiptCompactionFacts;
        ActivationControlReceipts = source.ActivationControlReceipts;
        ActivationInstanceReceiptChain = source.ActivationInstanceReceiptChain;
        NextExecutorGeneration = source.NextExecutorGeneration;
        ActivationIndexGeneration = source.ActivationIndexGeneration;
        ActivationYieldReservationGeneration = source.ActivationYieldReservationGeneration;
        ActivationYieldReservedUnusedSlots = source.ActivationYieldReservedUnusedSlots;
        ActivationYieldRetainedUsedSlots = source.ActivationYieldRetainedUsedSlots;
        MutationJournal = source.MutationJournal;
    }

    /// <summary>Gets the collections.</summary>
    public Dictionary<string, InMemoryCollectionState> Collections { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the next record ID.</summary>
    public long NextRecordId { get; set; }
    /// <summary>Gets or sets the next revision.</summary>
    public long NextRevision { get; set; }
    /// <summary>Gets or sets the global committed mutation position.</summary>
    public long GlobalMutationPosition { get; set; }
    /// <summary>Gets process-local atomic request receipts.</summary>
    public Dictionary<string, InMemoryMutationReceipt> Receipts { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ExpiredSemanticRetirementReceiptFloors { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets BASE-owned immutable vector projection slots by canonical collection/index key.</summary>
    public Dictionary<string, InMemoryVectorProjectionState> VectorProjections { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, InMemoryTextProjectionState> TextProjections { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, InMemoryTextRebuildReceipt> TextRebuildReceipts { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, InMemoryLogicalIndexAuthority> LogicalIndexes { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets current exported-subject contract authority by canonical contract key.</summary>
    public Dictionary<string, InMemorySubjectContractState> SubjectContracts { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets current exported-subject lifetimes by canonical subject key.</summary>
    public Dictionary<string, InMemorySubjectLifetimeState> SubjectLifetimes { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets latest terminal lifetime evidence by logical subject key.</summary>
    public Dictionary<string, InMemorySubjectTerminalState> SubjectTerminals { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets canonical durable lifecycle facts.</summary>
    public List<InMemorySubjectLifecycleFactRow> SubjectLifecycleFacts { get; } = [];
    /// <summary>Gets consumer-indexed lifecycle memberships.</summary>
    public List<InMemorySubjectLifecycleMembershipRow> SubjectLifecycleMemberships { get; } = [];
    /// <summary>Gets exact protected-scope membership seeks without cross-scope enumeration.</summary>
    public Dictionary<string, List<int>> SubjectLifecycleMembershipIndex { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, InMemorySubjectLifecycleConsumerProjection> SubjectLifecycleConsumers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, InMemorySubjectLifecycleCheckpointState> SubjectLifecycleCheckpoints { get; } = new(StringComparer.Ordinal);
    public long SubjectLifecycleDeliveryEpoch { get; set; } = 1;
    /// <summary>Gets current coordinated-retirement barriers by protected lifetime key.</summary>
    public Dictionary<string, InMemorySubjectRetirementBarrierState> SubjectRetirementBarriers { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets immutable terminal coordinated-retirement evidence.</summary>
    public Dictionary<string, InMemorySubjectRetirementTerminalState> SubjectRetirementTerminals { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets the next dedicated retirement-control position.</summary>
    public long SubjectRetirementPosition { get; set; }
    /// <summary>Gets sanitized retirement control publications in provider order.</summary>
    public List<BaseSubjectRetirementPublicationRow> SubjectRetirementPublications { get; } = [];
    /// <summary>Gets module-owned generation cells by canonical scoped key.</summary>
    public Dictionary<string, long> ModuleGenerations { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets semantic scope-directory bindings by protected exact scope.</summary>
    public Dictionary<string, BaseSemanticActivationScopeBinding> SemanticActivationScopes { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets durable semantic activation slots by stable semantic key.</summary>
    public Dictionary<string, InMemorySemanticActivationSlot> SemanticActivationSlots { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets identified process-local semantic maintenance progress and terminal receipts.</summary>
    public Dictionary<string, InMemorySemanticMaintenanceEntry> SemanticMaintenance { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets permanent per-identity replay receipts for removed semantic definitions.</summary>
    public Dictionary<string, InMemorySemanticMaintenanceEntry> RemovedSemanticMaintenanceReceipts { get; }
        = new(StringComparer.Ordinal);
    /// <summary>Gets permanently disabled semantic definition identities for this process incarnation.</summary>
    public HashSet<string> RemovedSemanticDefinitions { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets immutable published semantic-definition migration authority by source identity.</summary>
    public Dictionary<string, BaseSemanticActivationDefinitionMigrationAuthority> SemanticMigrationAuthorities { get; }
        = new(StringComparer.Ordinal);
    /// <summary>Gets byte-exact negative authority retained for every published semantic migration.</summary>
    public Dictionary<string, ImmutableArray<InMemorySemanticHistoricalAuthority>> SemanticMigrationHistory { get; }
        = new(StringComparer.Ordinal);
    /// <summary>Gets immutable terminal definition-removal publications by removed definition identity.</summary>
    public Dictionary<string, InMemorySemanticRemovedDefinitionAuthority> RemovedSemanticDefinitionAuthorities { get; }
        = new(StringComparer.Ordinal);
    /// <summary>Gets byte-exact absence authority retained after executable definition removal.</summary>
    public Dictionary<string, ImmutableArray<InMemorySemanticHistoricalAuthority>> RemovedSemanticDefinitionHistory { get; }
        = new(StringComparer.Ordinal);
    /// <summary>Gets the provider-owned installed semantic graph authority.</summary>
    public BaseSemanticActivationStoreAuthorityRequirement? SemanticActivationAuthority { get; set; }
    /// <summary>Gets durable activation rows by deterministic activation identity.</summary>
    public Dictionary<string, InMemoryActivationRow> Activations { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets non-prunable exact L51 prune authority by activation identity.</summary>
    public Dictionary<string, BaseActivationPruneEvidence> ActivationPruneFloors { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, SortedSet<string>> ActivationsByProtectedScope { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets disposed activation identities by exact protected scope and definition authority.</summary>
    public Dictionary<string, SortedSet<string>> DisposedActivationsByAuthority { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets durable executor incarnations by application/host/process key.</summary>
    public Dictionary<string, InMemoryExecutorRow> Executors { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets current durable schedule authority by ID/version.</summary>
    public Dictionary<string, BaseScheduleAuthority> Schedules { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets immutable occurrence facts by occurrence identity.</summary>
    public Dictionary<string, BaseScheduleOccurrenceFact> ScheduleOccurrences { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets durable cancel-previous maintenance state by deterministic identity.</summary>
    public Dictionary<string, InMemoryScheduleCancellationRow> ScheduleCancellations { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets durable definition-bound activation-instance receipts by identified-request key.</summary>
    public Dictionary<string, InMemoryActivationInstanceReceiptRow> ActivationInstanceReceipts { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets compact retained chain authority by original receipt sequence.</summary>
    public SortedDictionary<long, BaseActivationCompactedReceiptFact> ActivationInstanceReceiptCompactionFacts { get; } = [];
    /// <summary>Gets durable scheduler, executor, migration, and maintenance receipts by identified-request key.</summary>
    public Dictionary<string, InMemoryActivationControlReceiptRow> ActivationControlReceipts { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets ordered authority for the activation-instance receipt chain.</summary>
    public BaseActivationInstanceReceiptChainState ActivationInstanceReceiptChain { get; set; } =
        BaseActivationInstanceReceiptChainContract.Create(
            0, BaseActivationInstanceReceiptChainContract.ZeroOrderedChecksum.AsSpan(), 0);
    /// <summary>Gets the next positive executor generation.</summary>
    public long NextExecutorGeneration { get; set; }
    /// <summary>Gets or sets the generation invalidating finite due observations.</summary>
    public long ActivationIndexGeneration { get; set; }
    /// <summary>Gets or sets the durable-yield reservation-state generation.</summary>
    public long ActivationYieldReservationGeneration { get; set; }
    /// <summary>Gets or sets currently reserved but unused yield-receipt slots.</summary>
    public long ActivationYieldReservedUnusedSlots { get; set; }
    /// <summary>Gets or sets retained used yield-receipt slots.</summary>
    public long ActivationYieldRetainedUsedSlots { get; set; }
    /// <summary>Gets the shared record/control mutation journal by append position.</summary>
    public SortedDictionary<long, BaseMutationJournalEntry> MutationJournal { get; } = [];

    internal InMemoryStoreState CloneForSemanticMaintenance()
    {
        var clone = new InMemoryStoreState(this);
        foreach ((string key, InMemorySemanticActivationSlot slot) in SemanticActivationSlots)
            clone.SemanticActivationSlots.Add(key, slot.DeepClone());
        foreach ((string key, InMemorySemanticMaintenanceEntry entry) in SemanticMaintenance)
            clone.SemanticMaintenance.Add(key, entry.DeepClone());
        foreach ((string key, InMemorySemanticMaintenanceEntry entry) in RemovedSemanticMaintenanceReceipts)
            clone.RemovedSemanticMaintenanceReceipts.Add(key, entry.DeepClone());
        clone.RemovedSemanticDefinitions.UnionWith(RemovedSemanticDefinitions);
        foreach ((string key, BaseSemanticActivationDefinitionMigrationAuthority authority) in SemanticMigrationAuthorities)
            clone.SemanticMigrationAuthorities.Add(key, CloneMigrationAuthority(authority));
        foreach ((string key, ImmutableArray<InMemorySemanticHistoricalAuthority> history) in SemanticMigrationHistory)
            clone.SemanticMigrationHistory.Add(key, history.Select(static value => value.DeepClone()).ToImmutableArray());
        foreach ((string key, InMemorySemanticRemovedDefinitionAuthority authority) in RemovedSemanticDefinitionAuthorities)
            clone.RemovedSemanticDefinitionAuthorities.Add(key, authority.DeepClone());
        foreach ((string key, ImmutableArray<InMemorySemanticHistoricalAuthority> history) in RemovedSemanticDefinitionHistory)
            clone.RemovedSemanticDefinitionHistory.Add(key, history.Select(static value => value.DeepClone()).ToImmutableArray());
        clone.SemanticActivationAuthority = SemanticActivationAuthority is null ? null : SemanticActivationAuthority with
        {
            ApplicationId = new string(SemanticActivationAuthority.ApplicationId.AsSpan()),
            LogicalStoreId = new string(SemanticActivationAuthority.LogicalStoreId.AsSpan()),
            StoreInstanceId = new string(SemanticActivationAuthority.StoreInstanceId.AsSpan()),
            DefinitionSetChecksum = SemanticActivationAuthority.DefinitionSetChecksum.ToArray().ToImmutableArray(),
        };
        return clone;
    }

    /// <summary>Executes the clone operation.</summary>
    public InMemoryStoreState Clone()
    {
        var clone = new InMemoryStoreState
        {
            NextRecordId = NextRecordId,
            NextRevision = NextRevision,
            GlobalMutationPosition = GlobalMutationPosition,
            SubjectLifecycleDeliveryEpoch = SubjectLifecycleDeliveryEpoch,
            SubjectRetirementPosition = SubjectRetirementPosition,
            ActivationIndexGeneration = ActivationIndexGeneration,
            ActivationYieldReservationGeneration = ActivationYieldReservationGeneration,
            ActivationYieldReservedUnusedSlots = ActivationYieldReservedUnusedSlots,
            ActivationYieldRetainedUsedSlots = ActivationYieldRetainedUsedSlots,
            ActivationInstanceReceiptChain = ActivationInstanceReceiptChain with
            {
                OrderedChecksum = ActivationInstanceReceiptChain.OrderedChecksum.ToArray().ToImmutableArray(),
                Checksum = ActivationInstanceReceiptChain.Checksum.ToArray().ToImmutableArray(),
            },
            NextExecutorGeneration = NextExecutorGeneration,
        };

        foreach (var (id, collection) in Collections)
            clone.Collections.Add(id, collection.Clone());
        foreach (var (id, receipt) in Receipts)
            clone.Receipts.Add(id, receipt.DeepClone());
        foreach (string floor in ExpiredSemanticRetirementReceiptFloors)
            clone.ExpiredSemanticRetirementReceiptFloors.Add(new string(floor.AsSpan()));
        foreach (var (id, projection) in VectorProjections)
            clone.VectorProjections.Add(id, projection);
        foreach (var (id, projection) in TextProjections)
            clone.TextProjections.Add(id, projection.Clone());
        foreach (var (id, receipt) in TextRebuildReceipts)
            clone.TextRebuildReceipts.Add(id, receipt with { Fingerprint = [.. receipt.Fingerprint], Result = receipt.Result with { PublicationChecksum = ImmutableArray.Create(receipt.Result.PublicationChecksum.ToArray()) } });
        foreach (var (id, authority) in LogicalIndexes)
            clone.LogicalIndexes.Add(id, authority.DeepClone());
        foreach (var (id, subject) in SubjectContracts)
            clone.SubjectContracts.Add(id, subject with { });
        foreach (var (id, lifetime) in SubjectLifetimes)
            clone.SubjectLifetimes.Add(id, lifetime with { });
        foreach (var (id, terminal) in SubjectTerminals)
            clone.SubjectTerminals.Add(id, terminal with { });
        clone.SubjectLifecycleFacts.AddRange(SubjectLifecycleFacts.Select(static value => value with
        {
            Scope = value.Scope with { IndexDigest = [.. value.Scope.IndexDigest], ProtectedCanonicalValue = [.. value.Scope.ProtectedCanonicalValue] },
            Fact = value.Fact with { },
        }));
        clone.SubjectLifecycleMemberships.AddRange(SubjectLifecycleMemberships.Select(static value => value with
        {
            Scope = value.Scope with { IndexDigest = [.. value.Scope.IndexDigest], ProtectedCanonicalValue = [.. value.Scope.ProtectedCanonicalValue] },
        }));
        foreach ((string key, List<int> indexes) in SubjectLifecycleMembershipIndex)
            clone.SubjectLifecycleMembershipIndex.Add(key, [.. indexes]);
        foreach (var (id, consumer) in SubjectLifecycleConsumers) clone.SubjectLifecycleConsumers.Add(id, consumer with { Cutoff = consumer.Cutoff is null ? null : consumer.Cutoff with { } });
        foreach (var (id, checkpoint) in SubjectLifecycleCheckpoints) clone.SubjectLifecycleCheckpoints.Add(id, checkpoint with
        {
            Scope = checkpoint.Scope with { IndexDigest = [.. checkpoint.Scope.IndexDigest], ProtectedCanonicalValue = [.. checkpoint.Scope.ProtectedCanonicalValue] },
            Through = checkpoint.Through is null ? null : checkpoint.Through with { },
        });
        foreach ((string key, InMemorySubjectRetirementBarrierState barrier) in SubjectRetirementBarriers)
            clone.SubjectRetirementBarriers.Add(key, barrier.DeepClone());
        foreach ((string key, InMemorySubjectRetirementTerminalState terminal) in SubjectRetirementTerminals)
            clone.SubjectRetirementTerminals.Add(key, terminal.DeepClone());
        clone.SubjectRetirementPublications.AddRange(SubjectRetirementPublications.Select(static row => row with
        {
            Scope = row.Scope is null ? null : row.Scope with { IndexDigest = [.. row.Scope.IndexDigest], ProtectedCanonicalValue = [.. row.Scope.ProtectedCanonicalValue] },
            Fact = row.Fact with { },
        }));
        foreach ((string key, long generation) in ModuleGenerations)
            clone.ModuleGenerations.Add(key, generation);
        foreach ((string key, BaseSemanticActivationScopeBinding binding) in SemanticActivationScopes)
            clone.SemanticActivationScopes.Add(key, binding with
            {
                BindingId = binding.BindingId.ToArray().ToImmutableArray(),
                ProtectedCanonicalScope = binding.ProtectedCanonicalScope.ToArray().ToImmutableArray(),
                SeekDigest = binding.SeekDigest.ToArray().ToImmutableArray(),
                ProtectionKeyId = new string(binding.ProtectionKeyId.AsSpan()),
                Checksum = binding.Checksum.ToArray().ToImmutableArray(),
            });
        foreach ((string key, InMemorySemanticActivationSlot slot) in SemanticActivationSlots)
            clone.SemanticActivationSlots.Add(key, slot.DeepClone());
        foreach ((string key, InMemorySemanticMaintenanceEntry maintenance) in SemanticMaintenance)
            clone.SemanticMaintenance.Add(key, maintenance.DeepClone());
        foreach ((string key, InMemorySemanticMaintenanceEntry receipt) in RemovedSemanticMaintenanceReceipts)
            clone.RemovedSemanticMaintenanceReceipts.Add(key, receipt.DeepClone());
        clone.RemovedSemanticDefinitions.UnionWith(RemovedSemanticDefinitions);
        foreach ((string key, BaseSemanticActivationDefinitionMigrationAuthority authority) in SemanticMigrationAuthorities)
            clone.SemanticMigrationAuthorities.Add(key, CloneMigrationAuthority(authority));
        foreach ((string key, ImmutableArray<InMemorySemanticHistoricalAuthority> history) in SemanticMigrationHistory)
            clone.SemanticMigrationHistory.Add(key, history.Select(static value => value.DeepClone()).ToImmutableArray());
        foreach ((string key, InMemorySemanticRemovedDefinitionAuthority authority) in RemovedSemanticDefinitionAuthorities)
            clone.RemovedSemanticDefinitionAuthorities.Add(key, authority.DeepClone());
        foreach ((string key, ImmutableArray<InMemorySemanticHistoricalAuthority> history) in RemovedSemanticDefinitionHistory)
            clone.RemovedSemanticDefinitionHistory.Add(key, history.Select(static value => value.DeepClone()).ToImmutableArray());
        clone.SemanticActivationAuthority = SemanticActivationAuthority is null ? null : SemanticActivationAuthority with
        {
            ApplicationId = new string(SemanticActivationAuthority.ApplicationId.AsSpan()),
            LogicalStoreId = new string(SemanticActivationAuthority.LogicalStoreId.AsSpan()),
            StoreInstanceId = new string(SemanticActivationAuthority.StoreInstanceId.AsSpan()),
            DefinitionSetChecksum = SemanticActivationAuthority.DefinitionSetChecksum.ToArray().ToImmutableArray(),
        };
        foreach ((string key, InMemoryActivationRow activation) in Activations)
            clone.Activations.Add(key, activation.DeepClone());
        foreach ((string key, BaseActivationPruneEvidence evidence) in ActivationPruneFloors)
            clone.ActivationPruneFloors.Add(key, evidence with
            {
                Definition = evidence.Definition with { Checksum = evidence.Definition.Checksum.ToArray().ToImmutableArray() },
                TerminalControlChecksum = evidence.TerminalControlChecksum.ToArray().ToImmutableArray(),
                TerminalReceiptChecksum = evidence.TerminalReceiptChecksum.ToArray().ToImmutableArray(),
                OccurrenceChecksum = evidence.OccurrenceChecksum?.ToArray().ToImmutableArray(),
                ResultChecksum = evidence.ResultChecksum?.ToArray().ToImmutableArray(),
                PublicationAuthorityChecksum = evidence.PublicationAuthorityChecksum.ToArray().ToImmutableArray(),
                Checksum = evidence.Checksum.ToArray().ToImmutableArray(),
            });
        foreach ((string key, SortedSet<string> activationIds) in ActivationsByProtectedScope)
            clone.ActivationsByProtectedScope.Add(key, new SortedSet<string>(activationIds, StringComparer.Ordinal));
        foreach ((string key, SortedSet<string> activationIds) in DisposedActivationsByAuthority)
            clone.DisposedActivationsByAuthority.Add(key, new SortedSet<string>(activationIds, StringComparer.Ordinal));
        foreach ((string key, InMemoryExecutorRow executor) in Executors)
            clone.Executors.Add(key, executor.DeepClone());
        foreach ((string key, BaseScheduleAuthority schedule) in Schedules)
            clone.Schedules.Add(key, CloneSchedule(schedule));
        foreach ((string key, BaseScheduleOccurrenceFact occurrence) in ScheduleOccurrences)
            clone.ScheduleOccurrences.Add(key, CloneOccurrence(occurrence));
        foreach ((string key, InMemoryScheduleCancellationRow cancellation) in ScheduleCancellations)
            clone.ScheduleCancellations.Add(key, cancellation.DeepClone());
        foreach ((string key, InMemoryActivationInstanceReceiptRow receipt) in ActivationInstanceReceipts)
            clone.ActivationInstanceReceipts.Add(key, receipt.DeepClone());
        foreach ((long sequence, BaseActivationCompactedReceiptFact fact) in ActivationInstanceReceiptCompactionFacts)
            clone.ActivationInstanceReceiptCompactionFacts.Add(sequence, fact with
            {
                ReceiptKey = new string(fact.ReceiptKey.AsSpan()),
                ReceiptAuthorityChecksum = fact.ReceiptAuthorityChecksum.ToArray().ToImmutableArray(),
                PriorOrderedChecksum = fact.PriorOrderedChecksum.ToArray().ToImmutableArray(),
                OrderedChecksum = fact.OrderedChecksum.ToArray().ToImmutableArray(),
                CompactionReceiptKey = new string(fact.CompactionReceiptKey.AsSpan()),
                Checksum = fact.Checksum.ToArray().ToImmutableArray(),
            });
        foreach ((string key, InMemoryActivationControlReceiptRow receipt) in ActivationControlReceipts)
            clone.ActivationControlReceipts.Add(key, receipt.DeepClone());
        foreach ((long position, BaseMutationJournalEntry entry) in MutationJournal)
            clone.MutationJournal.Add(position, CloneJournalEntry(entry));

        return clone;
    }

    private static BaseSemanticActivationDefinitionMigrationAuthority CloneMigrationAuthority(
        BaseSemanticActivationDefinitionMigrationAuthority value) => value with
    {
        MigrationId = new string(value.MigrationId.AsSpan()),
        From = value.From with
        {
            Id = new string(value.From.Id.AsSpan()),
            Checksum = value.From.Checksum.ToArray().ToImmutableArray(),
        },
        To = value.To with
        {
            Id = new string(value.To.Id.AsSpan()),
            Checksum = value.To.Checksum.ToArray().ToImmutableArray(),
        },
        OrderedNegativeAuthorityChecksum = value.OrderedNegativeAuthorityChecksum.ToArray().ToImmutableArray(),
        ReceiptChecksum = value.ReceiptChecksum.ToArray().ToImmutableArray(),
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };

    internal void RebuildSubjectLifecycleMembershipIndex()
    {
        SubjectLifecycleMembershipIndex.Clear();
        for (int index = 0; index < SubjectLifecycleMemberships.Count; index++)
        {
            InMemorySubjectLifecycleMembershipRow membership = SubjectLifecycleMemberships[index];
            string key = $"{membership.ConsumerId}\n{membership.ConsumerVersion}\n{(int)membership.Scope.Kind}\n{Convert.ToHexString(membership.Scope.IndexDigest)}";
            if (!SubjectLifecycleMembershipIndex.TryGetValue(key, out List<int>? indexes))
                SubjectLifecycleMembershipIndex.Add(key, indexes = []);
            indexes.Add(index);
        }
    }

    private static BaseMutationJournalEntry CloneJournalEntry(BaseMutationJournalEntry entry) => new()
    {
        Kind = entry.Kind,
        Position = entry.Position,
        RecordMutation = entry.RecordMutation is null ? null : entry.RecordMutation with
        {
            Before = CloneSnapshot(entry.RecordMutation.Before),
            After = CloneSnapshot(entry.RecordMutation.After),
        },
        SubjectAuthorityPublication = entry.SubjectAuthorityPublication is null
            ? null
            : entry.SubjectAuthorityPublication with { },
    };

    private static BaseScheduleAuthority CloneSchedule(BaseScheduleAuthority value) => value with
    {
        Definition = BaseScheduleDefinitionBuilder.Create(value.Definition),
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };

    private static BaseScheduleOccurrenceFact CloneOccurrence(BaseScheduleOccurrenceFact value) => value with
    {
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
        Disposition = value.Disposition switch
        {
            BaseOccurrenceMaterialized materialized => materialized with { },
            BaseOccurrenceSkippedMisfire skipped => skipped with { },
            BaseOccurrenceSkippedOverlap skipped => skipped with { },
            BaseOccurrenceCancelled cancelled => cancelled with { },
            BaseOccurrenceSuppressedByReplacement replacement => replacement with { },
            BaseOccurrenceSuppressedByRestoreFloor floor => floor with { FloorChecksum = floor.FloorChecksum.ToArray().ToImmutableArray() },
            _ => throw new InvalidOperationException("base.activation.occurrenceInvalid"),
        },
    };

    private static RecordSnapshot? CloneSnapshot(RecordSnapshot? snapshot) => snapshot is null ? null : snapshot with
    {
        Payload = snapshot.Payload is null ? null : RecordCloneHelpers.ClonePayload(snapshot.Payload),
        Metadata = snapshot.Metadata is null ? null : RecordCloneHelpers.CloneMetadata(snapshot.Metadata),
    };
}

internal sealed record InMemorySemanticHistoricalAuthority(
    byte[] ScopeBindingId,
    byte[] KeyDigest,
    BaseSemanticActivationSlotState State,
    byte[] CanonicalAuthority)
{
    internal InMemorySemanticHistoricalAuthority DeepClone() => new(
        ScopeBindingId.ToArray(), KeyDigest.ToArray(), State, CanonicalAuthority.ToArray());
}

internal sealed record InMemorySemanticRemovedDefinitionAuthority(
    BaseSemanticActivationDefinitionKey Definition,
    BaseSemanticActivationRemovalAuthority Removal,
    long AbsenceCount,
    byte[] AbsenceChecksum,
    long PublicationGeneration,
    byte[] ReceiptChecksum,
    byte[] Checksum)
{
    internal InMemorySemanticRemovedDefinitionAuthority DeepClone() => new(
        Definition with
        {
            Id = new string(Definition.Id.AsSpan()),
            Checksum = Definition.Checksum.ToArray().ToImmutableArray(),
        },
        BaseSemanticActivationRemovalAuthorityContract.Seal(Removal), AbsenceCount,
        AbsenceChecksum.ToArray(), PublicationGeneration, ReceiptChecksum.ToArray(), Checksum.ToArray());
}

internal sealed record InMemoryLogicalIndexAuthority
{
    public required long Generation { get; init; }
    public required BaseLogicalIndexGenerationState State { get; init; }
    public required BaseSchemaAuthorityChecksum PublicationChecksum { get; init; }
    public BaseLogicalIndexDirectoryAuthority? DirectoryAuthority { get; init; }
    public BaseLogicalIndexDirectory? Directory { get; init; }

    internal InMemoryLogicalIndexAuthority DeepClone() => this with
    {
        PublicationChecksum = BaseSchemaAuthorityChecksum.Create(PublicationChecksum.ToArray()),
        DirectoryAuthority = DirectoryAuthority?.DeepClone(),
        Directory = Directory?.DeepClone(),
    };
}

internal sealed class InMemorySemanticMaintenancePlan
{
    internal required InMemorySemanticMaintenanceEntry Entry { get; init; }
    internal ImmutableArray<byte>? ReplacementDefinitionSetChecksum { get; set; }
    internal bool RemovesDefinition { get; set; }
    internal BaseSemanticActivationMigrationDefinition? Migration { get; set; }
    internal long MigrationSourceLive { get; set; }
    internal long MigrationSourceRetired { get; set; }
    internal long MigrationSourceAbsent { get; set; }
    internal ImmutableArray<InMemorySemanticHistoricalAuthority> HistoricalAuthority { get; set; } = [];
    internal BaseSemanticActivationDefinitionMigrationAuthority? MigrationAuthority { get; set; }
    internal InMemorySemanticRemovedDefinitionAuthority? RemovalAuthority { get; set; }
    internal long ExpectedPublishedRootBytes { get; set; }
    internal long CurrentPlanBytes { get; set; }
    internal long CurrentReceiptBytes { get; set; }
    internal long CurrentTransientBytes { get; set; }
    internal long MaximumMaterializedScanBytes { get; private set; }
    internal BaseSemanticActivationRecoveryBoundary? ReadLowerBoundary { get; set; }
    internal BaseSemanticActivationRecoveryBoundary? ReadUpperBoundary { get; set; }
    internal int PageReadIntervals { get; private set; }
    internal int PageIndexOperations { get; private set; }

    internal void ChargeLookup(int count = 1) =>
        PageIndexOperations = checked(PageIndexOperations + count);

    internal void ChargeWrite(int count = 1) =>
        PageIndexOperations = checked(PageIndexOperations + count);

    internal void ChargeScan(long rows)
    {
        if (rows < 0 || rows > int.MaxValue) throw new OverflowException();
        PageIndexOperations = checked(PageIndexOperations + 1 + (int)rows);
    }

    internal void ChargeFullRange(long rows)
    {
        ChargeScan(rows);
        PageReadIntervals = checked(PageReadIntervals + 1);
    }

    internal void ChargeRetainedTraversal(long rows, int passes = 1)
    {
        if (rows < 0 || rows > int.MaxValue || passes < 0) throw new OverflowException();
        PageIndexOperations = checked(PageIndexOperations + checked((int)rows * passes));
    }

    internal void ObserveMaterializedScan(long bytes)
    {
        if (bytes < 0) throw new OverflowException();
        MaximumMaterializedScanBytes = Math.Max(MaximumMaterializedScanBytes, bytes);
    }

    internal void ObserveSimultaneousMaterializedScans(long primaryBytes, long additionalBytes)
    {
        if (primaryBytes < 0 || additionalBytes < 0) throw new OverflowException();
        ObserveMaterializedScan(checked(primaryBytes + additionalBytes));
    }
}

internal sealed record InMemorySemanticActivationSlot
{
    internal required byte[] CanonicalKey { get; init; }
    internal required BaseSemanticActivationScopeBinding ScopeBinding { get; init; }
    internal BaseSemanticActivationLiveAuthority? Live { get; init; }
    internal BaseSemanticActivationRetirementAuthority? Retired { get; init; }
    internal BaseSemanticActivationAbsenceAuthority? Absent { get; init; }

    internal InMemorySemanticActivationSlot DeepClone() => this with
    {
        CanonicalKey = CanonicalKey.ToArray(),
        ScopeBinding = ScopeBinding with
        {
            BindingId = ScopeBinding.BindingId.ToArray().ToImmutableArray(),
            ProtectedCanonicalScope = ScopeBinding.ProtectedCanonicalScope.ToArray().ToImmutableArray(),
            SeekDigest = ScopeBinding.SeekDigest.ToArray().ToImmutableArray(),
            ProtectionKeyId = new string(ScopeBinding.ProtectionKeyId.AsSpan()),
            Checksum = ScopeBinding.Checksum.ToArray().ToImmutableArray(),
        },
        Live = Live is null ? null : Live with
        {
            Definition = Live.Definition with { Checksum = Live.Definition.Checksum.ToArray().ToImmutableArray() },
            KeyDigest = BaseSemanticActivationKeyDigest.Create(Live.KeyDigest.ToArray()),
            Scope = Live.Scope with { Value = Live.Scope.Value is null ? null : new string(Live.Scope.Value.AsSpan()) },
            ScopeBinding = ScopeBinding with
            {
                BindingId = ScopeBinding.BindingId.ToArray().ToImmutableArray(),
                ProtectedCanonicalScope = ScopeBinding.ProtectedCanonicalScope.ToArray().ToImmutableArray(),
                SeekDigest = ScopeBinding.SeekDigest.ToArray().ToImmutableArray(), Checksum = ScopeBinding.Checksum.ToArray().ToImmutableArray(),
            },
            ActivationDefinition = Live.ActivationDefinition with { Checksum = Live.ActivationDefinition.Checksum.ToArray().ToImmutableArray() },
            InputChecksum = Live.InputChecksum.ToArray().ToImmutableArray(), Checksum = Live.Checksum.ToArray().ToImmutableArray(),
            StoreAuthority = CloneStore(Live.StoreAuthority), SubjectLifetime = CloneLifetime(Live.SubjectLifetime),
        },
        Retired = Retired is null ? null : Retired with
        {
            Definition = Retired.Definition with { Checksum = Retired.Definition.Checksum.ToArray().ToImmutableArray() },
            KeyDigest = BaseSemanticActivationKeyDigest.Create(Retired.KeyDigest.ToArray()),
            SubjectLifetime = CloneLifetime(Retired.SubjectLifetime), TerminalActivationChecksum = Retired.TerminalActivationChecksum.ToArray().ToImmutableArray(),
            CompletionOperationChecksum = Retired.CompletionOperationChecksum.ToArray().ToImmutableArray(),
            CompletionReceiptChecksum = Retired.CompletionReceiptChecksum.ToArray().ToImmutableArray(),
            StoreAuthority = CloneStore(Retired.StoreAuthority), Checksum = Retired.Checksum.ToArray().ToImmutableArray(),
        },
        Absent = Absent is null ? null : Absent with
        {
            Definition = Absent.Definition with { Checksum = Absent.Definition.Checksum.ToArray().ToImmutableArray() },
            Key = BaseSemanticActivationKeyDigest.Create(Absent.Key.ToArray()),
            ScopeBindingId = Absent.ScopeBindingId.ToArray().ToImmutableArray(),
            StoreAuthority = CloneStore(Absent.StoreAuthority), Checksum = Absent.Checksum.ToArray().ToImmutableArray(),
        },
    };

    private static BaseSemanticActivationStoreAuthority CloneStore(BaseSemanticActivationStoreAuthority value) => value with
    {
        Requirement = value.Requirement with { DefinitionSetChecksum = value.Requirement.DefinitionSetChecksum.ToArray().ToImmutableArray() },
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };

    private static BaseSemanticActivationSubjectLifetimeBinding? CloneLifetime(BaseSemanticActivationSubjectLifetimeBinding? value) => value is null ? null : value with
    {
        ContractChecksum = value.ContractChecksum.ToArray().ToImmutableArray(), ScopeBindingId = value.ScopeBindingId.ToArray().ToImmutableArray(),
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };
}

internal sealed class InMemorySemanticMaintenanceEntry
{
    internal required byte[] Fingerprint { get; set; }
    internal required string Kind { get; set; }
    internal required BaseSemanticActivationDefinitionKey Definition { get; set; }
    internal required BaseSemanticActivationDefinitionKey? TargetDefinition { get; set; }
    internal required BaseSemanticActivationMaintenanceResult Result { get; set; }
    internal required Dictionary<string, InMemorySemanticActivationSlot> StagedSlots { get; set; }
    internal required List<byte[]> ProcessedAuthorities { get; set; }
    internal required List<long> ProcessedCanonicalBytes { get; set; }
    internal required InMemorySemanticMaintenanceAccounting Accounting { get; set; }

    internal InMemorySemanticMaintenanceEntry DeepClone() => new()
    {
        Fingerprint = Fingerprint.ToArray(),
        Kind = new string(Kind.AsSpan()),
        Definition = Definition with { Id = new string(Definition.Id.AsSpan()), Checksum = Definition.Checksum.ToArray().ToImmutableArray() },
        TargetDefinition = TargetDefinition is null ? null : TargetDefinition with
        {
            Id = new string(TargetDefinition.Id.AsSpan()),
            Checksum = TargetDefinition.Checksum.ToArray().ToImmutableArray(),
        },
        Result = CloneResult(Result),
        StagedSlots = StagedSlots.ToDictionary(static item => new string(item.Key.AsSpan()),
            static item => item.Value.DeepClone(), StringComparer.Ordinal),
        ProcessedAuthorities = ProcessedAuthorities.Select(static value => value.ToArray()).ToList(),
        ProcessedCanonicalBytes = [.. ProcessedCanonicalBytes],
        Accounting = Accounting with { },
    };

    private static BaseSemanticActivationMaintenanceResult CloneResult(BaseSemanticActivationMaintenanceResult value) => value with
    {
        ProviderIncarnation = value.ProviderIncarnation.ToArray().ToImmutableArray(),
        AuthorityChecksum = value.AuthorityChecksum.ToArray().ToImmutableArray(),
        ResultChecksum = value.ResultChecksum.ToArray().ToImmutableArray(),
        CommitObservationChecksum = value.CommitObservationChecksum.ToArray().ToImmutableArray(),
        Checkpoint = value.Checkpoint is null ? null : value.Checkpoint with
        {
            ProviderIncarnation = value.Checkpoint.ProviderIncarnation.ToArray().ToImmutableArray(),
            FenceToken = value.Checkpoint.FenceToken.ToArray().ToImmutableArray(),
            Definition = value.Checkpoint.Definition with
            {
                Id = new string(value.Checkpoint.Definition.Id.AsSpan()),
                Checksum = value.Checkpoint.Definition.Checksum.ToArray().ToImmutableArray(),
            },
            After = value.Checkpoint.After is null ? null : value.Checkpoint.After with
            {
                DefinitionId = new string(value.Checkpoint.After.DefinitionId.AsSpan()),
                ScopeBindingId = value.Checkpoint.After.ScopeBindingId.ToArray().ToImmutableArray(),
                Key = BaseSemanticActivationKeyDigest.Create(value.Checkpoint.After.Key.ToArray()),
            },
            RollingChecksum = value.Checkpoint.RollingChecksum.ToArray().ToImmutableArray(),
            RequestFingerprint = value.Checkpoint.RequestFingerprint.ToArray().ToImmutableArray(),
            Checksum = value.Checkpoint.Checksum.ToArray().ToImmutableArray(),
        },
    };
}

internal sealed record InMemorySemanticMaintenanceAccounting
{
    internal required long Rows { get; init; }
    internal required long CanonicalBytes { get; init; }
    internal required int Pages { get; init; }
    internal required int ReadIntervals { get; init; }
    internal required int IndexOperations { get; init; }
    internal required long EvidenceBytes { get; init; }
    internal required long ReceiptBytes { get; init; }
    internal required long TransientBytes { get; init; }
}

internal sealed record InMemoryScheduleCancellationRow(
    string MaintenanceId,
    string ReplacementActivationId,
    byte[] OverlapKey,
    BaseScheduleCancellationBoundary HighWater,
    BaseScheduleCancellationBoundary? After,
    bool Completed)
{
    internal InMemoryScheduleCancellationRow DeepClone() => this with
    {
        OverlapKey = OverlapKey.ToArray(),
        HighWater = HighWater with { ActivationId = new string(HighWater.ActivationId.AsSpan()) },
        After = After is null ? null : After with { ActivationId = new string(After.ActivationId.AsSpan()) },
    };
}

internal sealed record InMemoryActivationControlReceiptRow(
    string Kind,
    byte[] Fingerprint,
    byte[] Result,
    byte[] ResultChecksum,
    byte[] AuthorityChecksum)
{
    internal InMemoryActivationControlReceiptRow DeepClone() => this with
    {
        Fingerprint = Fingerprint.ToArray(),
        Result = Result.ToArray(),
        ResultChecksum = ResultChecksum.ToArray(),
        AuthorityChecksum = AuthorityChecksum.ToArray(),
    };
}

internal sealed record InMemoryActivationInstanceReceiptRow(
    string Kind,
    string ActivationId,
    BaseActivationDefinitionKey Definition,
    BaseActivationReceiptRetentionPolicy Retention,
    byte[] Fingerprint,
    byte[] Result,
    byte[] ResultChecksum,
    byte[] AuthorityChecksum,
    long CommittedAt,
    long DuplicateResolveUntil,
    long ReceiptSequence,
    byte[] PriorOrderedChecksum,
    byte[] OrderedChecksum)
{
    internal InMemoryActivationInstanceReceiptRow DeepClone() => this with
    {
        ActivationId = new string(ActivationId.AsSpan()),
        Definition = Definition with { Checksum = Definition.Checksum.ToArray().ToImmutableArray() },
        Retention = Retention with { },
        Fingerprint = Fingerprint.ToArray(),
        Result = Result.ToArray(),
        ResultChecksum = ResultChecksum.ToArray(),
        AuthorityChecksum = AuthorityChecksum.ToArray(),
        PriorOrderedChecksum = PriorOrderedChecksum.ToArray(),
        OrderedChecksum = OrderedChecksum.ToArray(),
    };
}

internal sealed record InMemoryExecutorRow(
    BaseExecutorIncarnationAuthority Authority,
    BaseExecutorHeartbeatObservation Heartbeat,
    bool Retired)
{
    internal InMemoryExecutorRow DeepClone() => new(
        Authority with
        {
            WorkerDefinitionSetChecksum = Authority.WorkerDefinitionSetChecksum.ToArray().ToImmutableArray(),
            Checksum = Authority.Checksum.ToArray().ToImmutableArray(),
        },
        Heartbeat with
        {
            ExecutorAuthorityChecksum = Heartbeat.ExecutorAuthorityChecksum.ToArray().ToImmutableArray(),
            Checksum = Heartbeat.Checksum.ToArray().ToImmutableArray(),
        },
        Retired);
}

internal sealed record InMemoryActivationRow(
    BaseActivationPayload Payload,
    BaseActivationState State,
    long Generation,
    long RequestedDueAt,
    long EffectiveDueAt,
    byte[] Fingerprint,
    byte[] ControlChecksum,
    string? OccurrenceId = null,
    int Priority = 0,
    byte[]? OverlapKey = null,
    BaseScheduleOverlapPolicy OverlapPolicy = BaseScheduleOverlapPolicy.Allow,
    bool Eligible = true,
    int AttemptNumber = 0,
    long ClaimEpoch = 0,
    BaseActivationClaimAuthority? Claim = null,
    BaseActivationLeaseObservation? Lease = null,
    byte[]? CanonicalResult = null,
    BaseEffectExecutionAuthority? Effect = null,
    byte[]? TerminalReceiptChecksum = null,
    long YieldCount = 0,
    long MaximumYields = 0,
    long ExecutionSliceOrdinal = 0,
    long? AttemptStartedAt = null,
    long? SliceStartedAt = null,
    BaseActivationYieldDisposition? YieldTerminalDisposition = null,
    string? YieldTerminalFailureCode = null)
{
    internal InMemoryActivationRow DeepClone() => new(
        Payload with
        {
            Definition = Payload.Definition with { Checksum = Payload.Definition.Checksum.ToArray().ToImmutableArray() },
            ReceiptRetention = Payload.ReceiptRetention with { },
            CanonicalInput = Payload.CanonicalInput.ToArray().ToImmutableArray(),
            InputChecksum = Payload.InputChecksum.ToArray().ToImmutableArray(),
            Scope = Payload.Scope with { },
            Checksum = Payload.Checksum.ToArray().ToImmutableArray(),
        },
        State,
        Generation,
        RequestedDueAt,
        EffectiveDueAt,
        [.. Fingerprint],
        [.. ControlChecksum],
        OccurrenceId is null ? null : new string(OccurrenceId.AsSpan()),
        Priority,
        OverlapKey is null ? null : [.. OverlapKey],
        OverlapPolicy,
        Eligible,
        AttemptNumber,
        ClaimEpoch,
        Claim is null ? null : Claim with
        {
            FencingToken = Claim.FencingToken.ToArray().ToImmutableArray(),
            DefinitionChecksum = Claim.DefinitionChecksum.ToArray().ToImmutableArray(),
        },
        Lease is null ? null : Lease with { Checksum = Lease.Checksum.ToArray().ToImmutableArray() },
        CanonicalResult is null ? null : [.. CanonicalResult],
        Effect is null ? null : Effect with
        {
            Claim = Effect.Claim with
            {
                FencingToken = Effect.Claim.FencingToken.ToArray().ToImmutableArray(),
                DefinitionChecksum = Effect.Claim.DefinitionChecksum.ToArray().ToImmutableArray(),
            },
            Executor = Effect.Executor with
            {
                WorkerDefinitionSetChecksum = Effect.Executor.WorkerDefinitionSetChecksum.ToArray().ToImmutableArray(),
                Checksum = Effect.Executor.Checksum.ToArray().ToImmutableArray(),
            },
            Checksum = Effect.Checksum.ToArray().ToImmutableArray(),
        },
        TerminalReceiptChecksum is null ? null : [.. TerminalReceiptChecksum],
        YieldCount, MaximumYields, ExecutionSliceOrdinal, AttemptStartedAt, SliceStartedAt,
        YieldTerminalDisposition,
        YieldTerminalFailureCode is null ? null : new string(YieldTerminalFailureCode.AsSpan()));
}

internal sealed record InMemorySubjectContractState(
    string ContractId,
    int ContractVersion,
    string ContractChecksum,
    BaseSubjectAuthorityEpoch AuthorityEpoch,
    long RestoreEpoch,
    long StateGeneration,
    BaseSubjectCurrentPublicationReceipt CurrentPublicationReceipt);

internal sealed record InMemorySubjectLifetimeState(
    string ContractId,
    int ContractVersion,
    BaseSubjectId SubjectId,
    BaseSubjectIncarnation Incarnation,
    long LifetimeGeneration,
    BaseSubjectLifecycleState LifecycleState,
    long SubjectSequence,
    BaseOwnedSubjectScopeEvidence Scope,
    string PrivateCollectionId,
    RecordId PrivateRecordId,
    long CreatedJournalPosition,
    long LastLifecyclePosition);

internal sealed record InMemorySubjectTerminalState(
    string ContractId,
    int ContractVersion,
    BaseSubjectId SubjectId,
    BaseOwnedSubjectScopeEvidence Scope,
    BaseSubjectAuthorityEpoch AuthorityEpoch,
    BaseSubjectIncarnation Incarnation,
    long LifetimeGeneration,
    long SubjectSequence,
    long RetiredPosition,
    long ContractStateGeneration,
    long RestoreEpoch,
    string ReceiptChecksum);
internal sealed record InMemorySubjectLifecycleFactRow(BaseProtectedSubjectScope Scope, BaseSubjectLifecycleOrderingBoundary Boundary, BaseSubjectLifecycleFact Fact);
internal sealed record InMemorySubjectLifecycleMembershipRow(string ConsumerId, int ConsumerVersion, string ConsumerChecksum, long ProjectionGeneration, BaseSubjectLifecycleState MatchedState, BaseProtectedSubjectScope Scope, int FactIndex);
internal sealed record InMemorySubjectLifecycleConsumerProjection(string ConsumerId, int ConsumerVersion, string ConsumerChecksum, string ContractId, int ContractVersion, long ProjectionGeneration, BaseSubjectLifecycleOrderingBoundary? Cutoff, long PublishedGraphGeneration, DateTimeOffset InstalledAtUtc, TimeSpan MaximumCheckpointLag);
internal sealed record InMemorySubjectLifecycleCheckpointState(string ConsumerId, int ConsumerVersion, string ConsumerChecksum, string ContractId, int ContractVersion, long ProjectionGeneration, BaseProtectedSubjectScope Scope, BaseSubjectLifecycleOrderingBoundary? Through, long Generation, DateTimeOffset AdvancedAtUtc, bool Overtaken);
internal sealed record InMemorySubjectRetirementAcknowledgement(string ConsumerId, int ConsumerVersion, string ConsumerChecksum, long ThroughSequence, BaseSubjectAcknowledgementDisposition Disposition, long Position);
internal sealed record InMemorySubjectRetirementBarrierState(BaseProtectedSubjectScope Scope, BaseSubjectRetirementBarrier Barrier, Dictionary<string, InMemorySubjectRetirementAcknowledgement> Acknowledgements)
{
    internal InMemorySubjectRetirementBarrierState DeepClone() => new(
        Scope with { IndexDigest = [.. Scope.IndexDigest], ProtectedCanonicalValue = [.. Scope.ProtectedCanonicalValue] },
        Barrier with { AuthorityEpoch = new BaseSubjectAuthorityEpoch(Barrier.AuthorityEpoch.ToArray()), Incarnation = new BaseSubjectIncarnation(Barrier.Incarnation.ToArray()) },
        Acknowledgements.ToDictionary(static pair => new string(pair.Key.AsSpan()), static pair => pair.Value with { }, StringComparer.Ordinal));
}
internal sealed record InMemorySubjectRetirementTerminalState(BaseSubjectRetirementTerminalReceipt Receipt)
{
    internal InMemorySubjectRetirementTerminalState DeepClone() => new(Receipt with
    {
        Scope=Receipt.Scope with{IndexDigest=[..Receipt.Scope.IndexDigest],ProtectedCanonicalValue=[..Receipt.Scope.ProtectedCanonicalValue]},
        AuthorityEpoch=new BaseSubjectAuthorityEpoch(Receipt.AuthorityEpoch.ToArray()),Incarnation=new BaseSubjectIncarnation(Receipt.Incarnation.ToArray()),
        Acknowledgements=[..Receipt.Acknowledgements.Select(static value=>value with{})],
    });
}

internal sealed class InMemoryVectorProjectionState
{
    internal long AppliedThrough { get; set; }
    internal long Generation { get; set; } = 1;
    internal long PurgeGeneration { get; set; }
    internal Dictionary<string, InMemoryVectorCarrier> Carriers { get; } = new(StringComparer.Ordinal);
    internal InMemoryVectorProjectionState Clone()
    {
        var clone = new InMemoryVectorProjectionState { AppliedThrough = AppliedThrough, Generation = Generation, PurgeGeneration = PurgeGeneration };
        foreach ((string id, InMemoryVectorCarrier carrier) in Carriers) clone.Carriers.Add(id, carrier.Copy());
        return clone;
    }
}

internal sealed record InMemoryVectorCarrier(RecordId RecordId, RevisionToken Revision, long Position, BaseVector Vector)
{
    internal InMemoryVectorCarrier Copy() => this with { Vector = BaseVector.Create(Vector.ToArray()) };
}

internal sealed class InMemoryTextProjectionState
{
    internal long AppliedThrough { get; set; }
    internal long Generation { get; set; } = 1;
    internal long PurgeGeneration { get; set; }
    internal Dictionary<string, InMemoryTextCarrier> Carriers { get; } = new(StringComparer.Ordinal);
    internal InMemoryTextProjectionState Clone() { var copy = new InMemoryTextProjectionState { AppliedThrough = AppliedThrough, Generation = Generation, PurgeGeneration = PurgeGeneration }; foreach ((string key, InMemoryTextCarrier value) in Carriers) copy.Carriers.Add(key, value with { }); return copy; }
}
internal sealed record InMemoryTextCarrier(RecordId RecordId, RevisionToken Revision, long Position);
internal sealed record InMemoryTextRebuildReceipt(byte[] Fingerprint, BaseTextRebuildResult Result);

internal sealed record InMemoryMutationReceipt(
    byte[] Fingerprint,
    byte[] StructuralDigest,
    BaseAtomicReceiptResult Result,
    byte[] ReceiptBytes,
    DateTimeOffset CommittedAt,
    DateTimeOffset ExpiresAt)
{
    public InMemoryMutationReceipt DeepClone() => new(
        [.. Fingerprint],
        [.. StructuralDigest],
        CloneReceipt(Result),
        [.. ReceiptBytes],
        CommittedAt,
        ExpiresAt);

    private static BaseAtomicReceiptResult CloneReceipt(BaseAtomicReceiptResult result) => new()
    {
        Kind = result.Kind,
        Mutations = result.Mutations.Select(static fact => BaseOwnedMutationFact.Freeze(fact.MaterializeOwned(), fact.CodecVersion)).ToImmutableArray(),
        SelectionMutation = result.SelectionMutation is null ? null : result.SelectionMutation with { },
        ModuleMutation = result.ModuleMutation is null ? null : result.ModuleMutation with
        {
            OperationId = new string(result.ModuleMutation.OperationId.AsSpan()),
            Generations = result.ModuleMutation.Generations.Select(static generation => generation with
            {
                CaptureId = new string(generation.CaptureId.AsSpan()),
                CellId = new string(generation.CellId.AsSpan()),
            }).ToImmutableArray(),
            CanonicalResultBytes = result.ModuleMutation.CanonicalResultBytes.ToArray().ToImmutableArray(),
            CreatedActivationIds = result.ModuleMutation.CreatedActivationIds
                .Select(static value => new string(value.AsSpan()))
                .ToImmutableArray(),
            SemanticActivation = result.ModuleMutation.SemanticActivation is null ? null : CloneSemantic(result.ModuleMutation.SemanticActivation),
        },
        SubjectLifecycleCheckpoint = result.SubjectLifecycleCheckpoint is null
            ? null
            : BaseSubjectLifecycleReceiptOwnership.Clone(result.SubjectLifecycleCheckpoint),
        SubjectLifecycleMaintenance = result.SubjectLifecycleMaintenance is null
            ? null
            : result.SubjectLifecycleMaintenance with { RollingChecksum = new string(result.SubjectLifecycleMaintenance.RollingChecksum.AsSpan()) },
        SubjectRetirement = result.SubjectRetirement is null ? null : result.SubjectRetirement with
        {
            Acknowledgement = result.SubjectRetirement.Acknowledgement is null ? null : result.SubjectRetirement.Acknowledgement with
            {
                BarrierChecksum = result.SubjectRetirement.Acknowledgement.BarrierChecksum is null ? null : new string(result.SubjectRetirement.Acknowledgement.BarrierChecksum.AsSpan()),
            },
            Timeout = result.SubjectRetirement.Timeout is null ? null : result.SubjectRetirement.Timeout with { BarrierChecksum = new string(result.SubjectRetirement.Timeout.BarrierChecksum.AsSpan()) },
            Override = result.SubjectRetirement.Override is null ? null : result.SubjectRetirement.Override with { BarrierChecksum = new string(result.SubjectRetirement.Override.BarrierChecksum.AsSpan()) },
            Purge = result.SubjectRetirement.Purge is null ? null : result.SubjectRetirement.Purge with { TerminalReceiptChecksum = new string(result.SubjectRetirement.Purge.TerminalReceiptChecksum.AsSpan()) },
            ConsumerRemoval = result.SubjectRetirement.ConsumerRemoval is null ? null : result.SubjectRetirement.ConsumerRemoval with { AcceptedConsumerSetChecksum = new string(result.SubjectRetirement.ConsumerRemoval.AcceptedConsumerSetChecksum.AsSpan()) },
            Maintenance = result.SubjectRetirement.Maintenance is null ? null : result.SubjectRetirement.Maintenance with { RollingChecksum = new string(result.SubjectRetirement.Maintenance.RollingChecksum.AsSpan()) },
        },
        ActivationCreation = result.ActivationCreation is null ? null : new BaseActivationCreationReceiptResult
        {
            ActivationIds = result.ActivationCreation.ActivationIds
                .Select(static value => new string(value.AsSpan()))
                .ToImmutableArray(),
        },
        ActivationTransactionalOperation = result.ActivationTransactionalOperation is null ? null : result.ActivationTransactionalOperation with
        {
            Generations = result.ActivationTransactionalOperation.Generations.Select(static value => value with { }).ToImmutableArray(),
            CanonicalResultBytes = result.ActivationTransactionalOperation.CanonicalResultBytes.ToArray().ToImmutableArray(),
            ActivationControlChecksum = result.ActivationTransactionalOperation.ActivationControlChecksum.ToArray().ToImmutableArray(),
        },
    };

    private static BaseSemanticActivationReceiptEvidence CloneSemantic(BaseSemanticActivationReceiptEvidence value) => new()
    {
        Operation = value.Operation,
        DefinitionId = new string(value.DefinitionId.AsSpan()),
        DefinitionVersion = value.DefinitionVersion,
        DefinitionChecksum = value.DefinitionChecksum.ToArray().ToImmutableArray(),
        Key = BaseSemanticActivationKeyDigest.Create(value.Key.ToArray()),
        State = value.State,
        SlotGeneration = value.SlotGeneration,
        EnsureDisposition = value.EnsureDisposition,
        RetirementDisposition = value.RetirementDisposition,
        ActivationId = value.ActivationId is null ? null : new string(value.ActivationId.AsSpan()),
        SlotChecksum = value.SlotChecksum.ToArray().ToImmutableArray(),
        JournalPosition = value.JournalPosition,
        CommitEvidenceChecksum = value.CommitEvidenceChecksum.ToArray().ToImmutableArray(),
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };
}

internal sealed class InMemoryCollectionState
{
    public long NextAppendPosition { get; set; }
    public long PurgeGeneration { get; set; }
    /// <summary>Gets the records by ID.</summary>
    public Dictionary<string, StoredRecord> RecordsById { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the optional immutable ordinal successor index used by vector projection scans.</summary>
    public ImmutableSortedSet<string>? RecordIdsOrdinal { get; set; }

    /// <summary>Executes the clone operation.</summary>
    public InMemoryCollectionState Clone()
    {
        var clone = new InMemoryCollectionState
        {
            NextAppendPosition = NextAppendPosition,
            PurgeGeneration = PurgeGeneration,
            RecordIdsOrdinal = RecordIdsOrdinal,
        };
        foreach (var (id, record) in RecordsById)
        {
            clone.RecordsById.Add(id, record with
            {
                Payload = RecordCloneHelpers.ClonePayload(record.Payload),
                Metadata = RecordCloneHelpers.CloneMetadata(record.Metadata)
            });
        }

        return clone;
    }
}

internal sealed record StoredRecord(
    string CollectionId,
    RecordId Id,
    RecordPayload Payload,
    RecordMetadata Metadata,
    long AppendPosition,
    long LatestMutationPosition = 0);
