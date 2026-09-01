using System.Collections.Immutable;
using HPD.Base.Testing;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Schema;

public sealed class BaseLogicalIndexCertificationHostTests
{
    [Fact]
    public async Task InMemory_fixture_executes_membership_order_and_complete_point_mutation()
    {
        await ExerciseFixtureAsync(InMemoryProviderInstaller.Create(null), null);
    }

    [Fact]
    public async Task Sqlite_fixture_executes_membership_order_and_complete_point_mutation()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-base-l80-{Guid.NewGuid():N}.db");
        try
        {
            await ExerciseFixtureAsync(SqliteStore.Configure(options =>
            {
                options.DataSource = database;
                options.StoreId = "base-cert-sqlite";
            }), "base-cert-sqlite");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(database)) File.Delete(database);
        }
    }

    [Fact]
    public async Task InMemory_fixture_executes_atomic_key_swap_conflict_and_delete()
    {
        await ExerciseAtomicLifecycleAsync(InMemoryProviderInstaller.Create(null), null);
    }

    [Fact]
    public async Task Sqlite_fixture_executes_atomic_key_swap_conflict_and_delete()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-base-l80-lifecycle-{Guid.NewGuid():N}.db");
        try
        {
            await ExerciseAtomicLifecycleAsync(SqliteStore.Configure(options =>
            {
                options.DataSource = database;
                options.StoreId = "base-cert-sqlite";
            }), "base-cert-sqlite");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(database)) File.Delete(database);
        }
    }

    [Fact]
    public async Task InMemory_bounded_fixture_accepts_maximum_and_rejects_maximum_plus_one()
    {
        await ExerciseBoundedCapacityAsync(InMemoryProviderInstaller.Create(options =>
            options.LogicalIndexCertificationCapability =
                GraphBoundedCapability()), null);
    }

    [Fact]
    public async Task Sqlite_bounded_fixture_accepts_maximum_and_rejects_maximum_plus_one()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-base-l80-bounded-{Guid.NewGuid():N}.db");
        try
        {
            await ExerciseBoundedCapacityAsync(SqliteStore.Configure(options =>
            {
                options.DataSource = database;
                options.StoreId = "base-cert-sqlite";
                options.LogicalIndexCertificationCapability =
                    GraphBoundedCapability();
            }), "base-cert-sqlite");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(database)) File.Delete(database);
        }
    }

    [Fact]
    public async Task InMemory_hostile_member_set_fails_closed_and_quarantines()
    {
        await ExerciseHostileMemberSetAsync(InMemoryProviderInstaller.Create(null), null);
    }

    [Fact]
    public async Task Sqlite_hostile_member_set_fails_closed_and_quarantines()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-base-l80-hostile-{Guid.NewGuid():N}.db");
        try
        {
            await ExerciseHostileMemberSetAsync(SqliteStore.Configure(options =>
            {
                options.DataSource = database;
                options.StoreId = "base-cert-sqlite";
            }), "base-cert-sqlite");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(database)) File.Delete(database);
        }
    }

    [Fact]
    public async Task InMemory_capture_reports_exact_point_hit_miss_and_scan_fallback_evidence()
    {
        await ExerciseCaptureEvidenceAsync(InMemoryProviderInstaller.Create(null), null);
    }

    [Fact]
    public async Task Sqlite_capture_reports_exact_point_hit_miss_and_scan_fallback_evidence()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-base-l80-capture-{Guid.NewGuid():N}.db");
        try
        {
            await ExerciseCaptureEvidenceAsync(SqliteStore.Configure(options =>
            {
                options.DataSource = database;
                options.StoreId = "base-cert-sqlite";
            }), "base-cert-sqlite");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(database)) File.Delete(database);
        }
    }

    [Fact]
    public async Task InMemory_hostile_returned_evidence_is_rejected_and_quarantined_at_prepare()
    {
        await ExerciseHostileResultOwnershipAsync(InMemoryProviderInstaller.Create(null), null);
    }

    [Fact]
    public async Task Sqlite_hostile_returned_evidence_is_rejected_and_quarantined_at_prepare()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-base-l80-hostile-result-{Guid.NewGuid():N}.db");
        try
        {
            await ExerciseHostileResultOwnershipAsync(SqliteStore.Configure(options =>
            {
                options.DataSource = database;
                options.StoreId = "base-cert-sqlite";
            }), "base-cert-sqlite");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(database)) File.Delete(database);
        }
    }

    [Fact]
    public async Task InMemory_policy_conjunct_completes_the_exact_point_without_hidden_influence()
    {
        await ExercisePolicyPointAsync(InMemoryProviderInstaller.Create(null), null);
    }

    [Fact]
    public async Task Sqlite_policy_conjunct_completes_the_exact_point_without_hidden_influence()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-base-l80-policy-{Guid.NewGuid():N}.db");
        try
        {
            await ExercisePolicyPointAsync(SqliteStore.Configure(options =>
            {
                options.DataSource = database;
                options.StoreId = "base-cert-sqlite";
            }), "base-cert-sqlite");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(database)) File.Delete(database);
        }
    }

    [Fact]
    public async Task InMemory_stale_point_capture_conflicts_without_selection_mutation()
    {
        await ExerciseStalePointCaptureAsync(InMemoryProviderInstaller.Create(null), null);
    }

    [Fact]
    public async Task Sqlite_write_ownership_conflict_prevents_point_reselection()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-base-l80-generation-{Guid.NewGuid():N}.db");
        try
        {
            await using ServiceProvider provider = BaseLogicalIndexCertificationHost.Create(
                SqliteStore.Configure(options =>
                {
                    options.DataSource = database;
                    options.StoreId = "base-cert-sqlite";
                    options.BusyTimeout = TimeSpan.FromMilliseconds(1);
                }));
            BaseCollectionSession<BaseLogicalIndexCertificationItem> collection =
                await InitializeFixtureAsync(provider, "base-cert-sqlite");
            (await collection.CreateAsync(Id(1), Item("a", "x", 1))).RequireValue();

            await using var writer = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = database }.ToString());
            await writer.OpenAsync();
            await using SqliteCommand acquire = writer.CreateCommand();
            acquire.CommandText = "BEGIN IMMEDIATE;";
            await acquire.ExecuteNonQueryAsync();

            BaseResult<BaseSelectionMutationResult> selection = await collection.Query()
                .Where(BasePredicate<BaseLogicalIndexCertificationItem>.And(
                    BaseLogicalIndexCertificationItem.Fields.Tenant.Equal("a"),
                    BaseLogicalIndexCertificationItem.Fields.Code.Equal("x")))
                .OrderBy(BaseLogicalIndexCertificationItem.Fields.Sequence)
                .ThenByRecordId()
                .Take(4)
                .PatchSelectedAsync(
                    collection.GetMergePatchSelectionProfile(
                        BaseLogicalIndexCertificationHost.ProfileIdentity()),
                    BaseLogicalIndexCertificationHost.SequencePatch(9),
                    BasePreviousStateRequirement.None,
                    BaseLogicalIndexCertificationHost.Identity("point-generation-conflict"));
            BaseFailure<BaseSelectionMutationResult> conflict =
                Assert.IsType<BaseFailure<BaseSelectionMutationResult>>(selection);
            Assert.Equal(OperationStatus.Conflict, conflict.Status);
            Assert.Equal(BaseSelectionErrorCodes.TransactionConflict, conflict.Error.Code);

            await using SqliteCommand release = writer.CreateCommand();
            release.CommandText = "ROLLBACK;";
            await release.ExecuteNonQueryAsync();
            (await collection.ReplaceAsync(Id(1), Item("a", "z", 1))).RequireValue();
            BaseRecord<BaseLogicalIndexCertificationItem> stored =
                (await collection.GetAsync(Id(1))).RequireValue();
            Assert.Equal("z", stored.Value.Code);
            Assert.Equal(1, stored.Value.Sequence);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(database)) File.Delete(database);
        }
    }

    private static async Task ExerciseStalePointCaptureAsync(
        HPDBaseStoreProvider storeProvider, string? schemaStoreId)
    {
        var evaluator = new PausingSelectionItemPolicyEvaluator();
        await using ServiceProvider provider = BaseLogicalIndexCertificationHost.Create(
            storeProvider, certificationEvaluator: evaluator);
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection =
            await InitializeFixtureAsync(provider, schemaStoreId);
        (await collection.CreateAsync(Id(1), Item("a", "x", 1))).RequireValue();

        Task<BaseResult<BaseSelectionMutationResult>> selection = collection.Query()
            .Where(BasePredicate<BaseLogicalIndexCertificationItem>.And(
                BaseLogicalIndexCertificationItem.Fields.Tenant.Equal("a"),
                BaseLogicalIndexCertificationItem.Fields.Code.Equal("x")))
            .OrderBy(BaseLogicalIndexCertificationItem.Fields.Sequence)
            .ThenByRecordId()
            .Take(4)
            .PatchSelectedAsync(
                collection.GetMergePatchSelectionProfile(
                    BaseLogicalIndexCertificationHost.ProfileIdentity()),
                BaseLogicalIndexCertificationHost.SequencePatch(9),
                BasePreviousStateRequirement.None,
                BaseLogicalIndexCertificationHost.Identity("point-generation-conflict"))
            .AsTask();

        await evaluator.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));
        (await collection.ReplaceAsync(Id(1), Item("a", "z", 1))).RequireValue();
        evaluator.Release.SetResult();

        BaseFailure<BaseSelectionMutationResult> conflict =
            Assert.IsType<BaseFailure<BaseSelectionMutationResult>>(await selection);
        Assert.True(conflict.Status == OperationStatus.Conflict,
            $"{conflict.Status}:{conflict.Error.Code}:{conflict.Error.Category}");
        Assert.Equal(BaseSelectionErrorCodes.TransactionConflict, conflict.Error.Code);
        BaseRecord<BaseLogicalIndexCertificationItem> stored =
            (await collection.GetAsync(Id(1))).RequireValue();
        Assert.Equal("z", stored.Value.Code);
        Assert.Equal(1, stored.Value.Sequence);
    }

    private static async Task ExerciseFixtureAsync(
        HPDBaseStoreProvider storeProvider, string? schemaStoreId)
    {
        await using ServiceProvider provider = BaseLogicalIndexCertificationHost.Create(storeProvider);
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection =
            await InitializeFixtureAsync(provider, schemaStoreId);
        BaseLogicalIndexDefinition[] indexes = collection.Contract.Definition.Indexes!;
        BaseLogicalIndexDefinition unique = indexes.Single(index =>
            index.Id.ToString() == "base.cert.logicalIndex.tenantCode.v1");
        BaseLogicalIndexDefinition ordered = indexes.Single(index =>
            index.Id.ToString() == "base.cert.logicalIndex.tenantSequence.v1");
        var inspection = (IBaseLogicalIndexCertificationInspection)provider
            .GetRequiredService<IRecordStore>();
        BaseLogicalIndexCertificationSnapshot empty = await inspection
            .InspectLogicalIndexForCertificationAsync(collection.Contract.Id, unique.Checksum);
        Assert.Empty(empty.Directory.EqualityPostings);

        (await collection.CreateAsync(Id(1), Item("a", "x", 1))).RequireValue();
        (await collection.CreateAsync(Id(2), Item("a", "y", 2))).RequireValue();
        (await collection.CreateAsync(Id(3), Item("b", null, 3))).RequireValue();
        (await collection.CreateAsync(Id(4), Item("b", "x", 4))).RequireValue();

        BaseLogicalIndexCertificationSnapshot membership = await inspection
            .InspectLogicalIndexForCertificationAsync(collection.Contract.Id, unique.Checksum);
        Assert.Equal(3, membership.Directory.Accounting.Postings);
        Assert.DoesNotContain(membership.Directory.ComparatorEntries,
            entry => entry.RecordId == Id(3).Value);
        Assert.Equal(Id(1).Value, FindPosting(membership, Point(collection, "a", "x")));
        BaseLogicalIndexCertificationSnapshot comparator = await inspection
            .InspectLogicalIndexForCertificationAsync(collection.Contract.Id, ordered.Checksum);
        Assert.Equal([Id(3).Value, Id(4).Value, Id(1).Value, Id(2).Value],
            comparator.Directory.ComparatorEntries.Select(entry => entry.RecordId));

        BaseSelectionMutationResult patched = (await collection.Query()
            .Where(BasePredicate<BaseLogicalIndexCertificationItem>.And(
                BaseLogicalIndexCertificationItem.Fields.Tenant.Equal("a"),
                BaseLogicalIndexCertificationItem.Fields.Code.Equal("x")))
            .OrderBy(BaseLogicalIndexCertificationItem.Fields.Sequence)
            .ThenByRecordId()
            .Take(4)
            .PatchSelectedAsync(
                collection.GetMergePatchSelectionProfile(
                    BaseLogicalIndexCertificationHost.ProfileIdentity()),
                BaseLogicalIndexCertificationHost.SequencePatch(9),
                BasePreviousStateRequirement.None,
                BaseLogicalIndexCertificationHost.Identity("point-hit"))).RequireValue();

        Assert.Equal(1, patched.SelectedCount);
        Assert.Equal(1, patched.MutatedCount);
        Assert.Equal(9, (await collection.GetAsync(Id(1))).RequireValue().Value.Sequence);
    }

    private static async Task ExerciseAtomicLifecycleAsync(
        HPDBaseStoreProvider storeProvider, string? schemaStoreId)
    {
        await using ServiceProvider provider = BaseLogicalIndexCertificationHost.Create(storeProvider);
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection =
            await InitializeFixtureAsync(provider, schemaStoreId);
        BaseLogicalIndexDefinition unique = collection.Contract.Definition.Indexes!.Single(index =>
            index.Id.ToString() == "base.cert.logicalIndex.tenantCode.v1");
        var inspection = (IBaseLogicalIndexCertificationInspection)provider.GetRequiredService<IRecordStore>();

        (await collection.CreateAsync(Id(1), Item("a", "x", 1))).RequireValue();
        (await collection.CreateAsync(Id(2), Item("a", "y", 2))).RequireValue();
        BaseLogicalIndexCertificationSnapshot beforeSwap = await inspection
            .InspectLogicalIndexForCertificationAsync(collection.Contract.Id, unique.Checksum);

        (await collection.ReplaceAsync(Id(1), Item("a", "z", 1))).RequireValue();
        BaseLogicalIndexCertificationSnapshot moved = await inspection
            .InspectLogicalIndexForCertificationAsync(collection.Contract.Id, unique.Checksum);
        Assert.True(beforeSwap.Authority.DirectoryPublicationChecksum.AsSpan().SequenceEqual(
            moved.Authority.PreviousDirectoryPublicationChecksum.AsSpan()));
        Assert.DoesNotContain(moved.Directory.EqualityPostings, posting =>
            posting.EqualityKey.AsSpan().SequenceEqual(Point(collection, "a", "x").EqualityKey.AsSpan()));
        Assert.Equal(Id(1).Value, FindPosting(moved, Point(collection, "a", "z")));
        (await collection.ReplaceAsync(Id(1), Item("a", "x", 1))).RequireValue();
        beforeSwap = await inspection.InspectLogicalIndexForCertificationAsync(
            collection.Contract.Id, unique.Checksum);

        BaseBatchBuilder swap = collection.Session.Atomic(BaseLogicalIndexCertificationHost.Identity("key-swap"));
        swap.Replace(collection.Contract, Id(1), Item("a", "y", 1));
        swap.Replace(collection.Contract, Id(2), Item("a", "x", 2));
        (await swap.CommitAsync()).RequireValue();

        BaseLogicalIndexCertificationSnapshot swapped = await inspection
            .InspectLogicalIndexForCertificationAsync(collection.Contract.Id, unique.Checksum);
        Assert.NotEqual(beforeSwap.Authority.MemberSetChecksum, swapped.Authority.MemberSetChecksum);
        Assert.Equal([Id(1).Value, Id(2).Value], swapped.Directory.EqualityPostings
            .SelectMany(posting => posting.RecordIds).Order(StringComparer.Ordinal));
        Assert.Equal(Id(2).Value, FindPosting(swapped, Point(collection, "a", "x")));
        Assert.Equal(Id(1).Value, FindPosting(swapped, Point(collection, "a", "y")));
        Assert.Equal("y", (await collection.GetAsync(Id(1))).RequireValue().Value.Code);
        Assert.Equal("x", (await collection.GetAsync(Id(2))).RequireValue().Value.Code);

        BaseLogicalIndexCertificationSnapshot beforeConflict = swapped.DeepClone();
        BaseResult<BaseRecord<BaseLogicalIndexCertificationItem>> conflict = await collection.ReplaceAsync(
            Id(2), Item("a", "y", 2));
        BaseFailure<BaseRecord<BaseLogicalIndexCertificationItem>> failure =
            Assert.IsType<BaseFailure<BaseRecord<BaseLogicalIndexCertificationItem>>>(conflict);
        Assert.Equal(BaseSchemaErrorCodes.UniqueConstraintViolated, failure.Error.Code);
        BaseLogicalIndexCertificationSnapshot afterConflict = await inspection
            .InspectLogicalIndexForCertificationAsync(collection.Contract.Id, unique.Checksum);
        Assert.Equal("x", (await collection.GetAsync(Id(2))).RequireValue().Value.Code);
        Assert.True(beforeConflict.Directory.CanonicalEncoding.AsSpan().SequenceEqual(
            afterConflict.Directory.CanonicalEncoding.AsSpan()),
            $"directory changed: {Convert.ToHexString(beforeConflict.Directory.CanonicalEncoding.AsSpan())} -> {Convert.ToHexString(afterConflict.Directory.CanonicalEncoding.AsSpan())}");
        Assert.True(beforeConflict.Authority.MemberSetChecksum.AsSpan().SequenceEqual(
            afterConflict.Authority.MemberSetChecksum.AsSpan()),
            $"member set changed: {Convert.ToHexString(beforeConflict.Authority.MemberSetChecksum.AsSpan())} -> {Convert.ToHexString(afterConflict.Authority.MemberSetChecksum.AsSpan())}");
        Assert.True(beforeConflict.Authority.DirectoryPublicationChecksum.AsSpan().SequenceEqual(
            afterConflict.Authority.DirectoryPublicationChecksum.AsSpan()));

        (await collection.DeleteAsync(Id(1))).RequireValue();
        BaseLogicalIndexCertificationSnapshot deleted = await inspection
            .InspectLogicalIndexForCertificationAsync(collection.Contract.Id, unique.Checksum);
        Assert.DoesNotContain(deleted.Directory.ComparatorEntries, entry => entry.RecordId == Id(1).Value);
        Assert.Single(deleted.Directory.EqualityPostings);
        Assert.Equal(Id(2).Value, deleted.Directory.EqualityPostings[0].RecordIds.Single());
        Assert.Equal(Id(2).Value, FindPosting(deleted, Point(collection, "a", "x")));
    }

    private static async Task ExerciseBoundedCapacityAsync(
        HPDBaseStoreProvider storeProvider, string? schemaStoreId)
    {
        await using ServiceProvider provider = BaseLogicalIndexCertificationHost.Create(storeProvider);
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection =
            await InitializeFixtureAsync(provider, schemaStoreId);
        BaseLogicalIndexDefinition unique = collection.Contract.Definition.Indexes!.Single(index =>
            index.Id.ToString() == "base.cert.logicalIndex.tenantCode.v1");
        var inspection = (IBaseLogicalIndexCertificationInspection)provider.GetRequiredService<IRecordStore>();
        Assert.True(GraphBoundedCapability().Checksum.AsSpan()
            .SequenceEqual(inspection.LogicalIndexCertificationCapability.Checksum.AsSpan()));

        for (int ordinal = 1; ordinal <= 4; ordinal++)
            (await collection.CreateAsync(Id(ordinal), Item($"t{ordinal}", $"v{ordinal}", ordinal)))
                .RequireValue();
        BaseLogicalIndexCertificationSnapshot maximum = await inspection
            .InspectLogicalIndexForCertificationAsync(collection.Contract.Id, unique.Checksum);
        Assert.Equal(4, maximum.Directory.Accounting.Records);
        Assert.Equal(4, maximum.Directory.Accounting.Postings);

        BaseResult<BaseRecord<BaseLogicalIndexCertificationItem>> overflow = await collection.CreateAsync(
            Id(5), Item("t5", "v5", 5));
        BaseFailure<BaseRecord<BaseLogicalIndexCertificationItem>> failure =
            Assert.IsType<BaseFailure<BaseRecord<BaseLogicalIndexCertificationItem>>>(overflow);
        Assert.Equal(OperationStatus.CapabilityUnavailable, failure.Status);
        Assert.Equal(BaseSchemaErrorCodes.CapabilityUnavailable, failure.Error.Code);
        BaseLogicalIndexCertificationSnapshot after = await inspection
            .InspectLogicalIndexForCertificationAsync(collection.Contract.Id, unique.Checksum);
        Assert.True(maximum.Directory.CanonicalEncoding.AsSpan().SequenceEqual(
            after.Directory.CanonicalEncoding.AsSpan()));
        Assert.True(maximum.Authority.DirectoryPublicationChecksum.AsSpan().SequenceEqual(
            after.Authority.DirectoryPublicationChecksum.AsSpan()));
        Assert.IsType<BaseFailure<BaseRecord<BaseLogicalIndexCertificationItem>>>(
            await collection.GetAsync(Id(5)));
    }

    private static async Task ExerciseHostileMemberSetAsync(
        HPDBaseStoreProvider storeProvider, string? schemaStoreId)
    {
        await using ServiceProvider provider = BaseLogicalIndexCertificationHost.Create(storeProvider);
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection =
            await InitializeFixtureAsync(provider, schemaStoreId);
        BaseLogicalIndexDefinition selected = collection.Contract.Definition.Indexes!.Single(index =>
            index.Id.ToString() == "base.cert.logicalIndex.aTenantCode.v1");
        var inspection = (IBaseLogicalIndexCertificationInspection)provider.GetRequiredService<IRecordStore>();
        (await collection.CreateAsync(Id(1), Item("a", "x", 1))).RequireValue();
        await inspection.CorruptLogicalIndexMemberSetForCertificationAsync(
            collection.Contract.Id, selected.Checksum);

        BasePredicate<BaseLogicalIndexCertificationItem> pointPredicate =
            BasePredicate<BaseLogicalIndexCertificationItem>.And(
                BaseLogicalIndexCertificationItem.Fields.Tenant.Equal("a"),
                BaseLogicalIndexCertificationItem.Fields.Code.Equal("x"));
        Assert.NotNull(BaseLogicalIndexPointPlanContract.Derive(
            collection.Contract.Definition, pointPredicate.Expression));
        BaseResult<BaseSelectionMutationResult> result = await collection.Query()
            .Where(pointPredicate)
            .OrderBy(BaseLogicalIndexCertificationItem.Fields.Sequence)
            .ThenByRecordId()
            .Take(4)
            .PatchSelectedAsync(
                collection.GetMergePatchSelectionProfile(
                    BaseLogicalIndexCertificationHost.ProfileIdentity()),
                BaseLogicalIndexCertificationHost.SequencePatch(9),
                BasePreviousStateRequirement.None,
                BaseLogicalIndexCertificationHost.Identity("hostile-member-set"));

        BaseFailure<BaseSelectionMutationResult> failure =
            Assert.IsType<BaseFailure<BaseSelectionMutationResult>>(result);
        Assert.Equal(OperationStatus.StoreError, failure.Status);
        Assert.Equal(BaseSchemaErrorCodes.ProviderEvidenceInvalid, failure.Error.Code);
        Assert.False(inspection.LogicalIndexesReady);
        Assert.Equal(1, (await collection.GetAsync(Id(1))).RequireValue().Value.Sequence);
    }

    private static async Task ExerciseCaptureEvidenceAsync(
        HPDBaseStoreProvider storeProvider, string? schemaStoreId)
    {
        await using ServiceProvider provider = BaseLogicalIndexCertificationHost.Create(storeProvider);
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection =
            await InitializeFixtureAsync(provider, schemaStoreId);
        for (int ordinal = 1; ordinal <= 4; ordinal++)
        {
            BaseLogicalIndexCertificationItem item = ordinal switch
            {
                1 => Item("a", "x", 1),
                2 => Item("a", "y", 2),
                3 => Item("b", null, 3),
                _ => Item("b", "x", 4),
            };
            (await collection.CreateAsync(Id(ordinal), item)).RequireValue();
        }

        BasePredicate<BaseLogicalIndexCertificationItem> hitPredicate =
            BasePredicate<BaseLogicalIndexCertificationItem>.And(
                BaseLogicalIndexCertificationItem.Fields.Tenant.Equal("a"),
                BaseLogicalIndexCertificationItem.Fields.Code.Equal("x"));
        BaseCapturedAtomicExecution hit = await CaptureAsync(provider, collection,
            Query(hitPredicate.Expression));
        Assert.NotNull(hit.Selection!.LogicalIndexEvidence);
        Assert.Equal(BaseIndexAccessShape.LogicalIndexPoint,
            hit.Selection.LogicalIndexEvidence!.AccessShape);
        Assert.Equal([Id(1).Value], hit.Selection.Records.Select(record =>
            record.MaterializeOwned().Id.Value));
        Assert.Single(hit.ReadIntervals);
        Assert.Equal(hit.Selection.LogicalIndexEvidence.ReadInterval.LogicalAccessPathId,
            hit.ReadIntervals[0].LogicalAccessPathId);
        byte[] expectedMemberSet = hit.Selection.LogicalIndexEvidence.MemberSetChecksum.ToArray();
        byte[] hostileMemberSet = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(
            hit.Selection.LogicalIndexEvidence.MemberSetChecksum)
            ?? throw new InvalidOperationException("base.logicalIndex.certificationInvalid");
        hostileMemberSet[0] ^= 0x01;
        BaseCapturedAtomicExecution ownedRecapture = await CaptureAsync(provider, collection,
            Query(hitPredicate.Expression));
        Assert.True(expectedMemberSet.AsSpan().SequenceEqual(
            ownedRecapture.Selection!.LogicalIndexEvidence!.MemberSetChecksum.AsSpan()));
        Assert.True(((IBaseLogicalIndexCertificationInspection)provider.GetRequiredService<IRecordStore>())
            .LogicalIndexesReady);

        BasePredicate<BaseLogicalIndexCertificationItem> missPredicate =
            BasePredicate<BaseLogicalIndexCertificationItem>.And(
                BaseLogicalIndexCertificationItem.Fields.Tenant.Equal("a"),
                BaseLogicalIndexCertificationItem.Fields.Code.Equal("z"));
        BaseCapturedAtomicExecution miss = await CaptureAsync(provider, collection,
            Query(missPredicate.Expression));
        Assert.NotNull(miss.Selection!.LogicalIndexEvidence);
        Assert.Empty(miss.Selection.Records);
        Assert.Equal(0, miss.Selection.LogicalIndexEvidence!.ExaminedPostings);

        BasePredicate<BaseLogicalIndexCertificationItem> scanPredicate =
            BaseLogicalIndexCertificationItem.Fields.Sequence.GreaterThan(1);
        BaseCapturedAtomicExecution scan = await CaptureAsync(provider, collection,
            Query(scanPredicate.Expression));
        Assert.Null(scan.Selection!.LogicalIndexEvidence);
        Assert.Equal([Id(2).Value, Id(3).Value, Id(4).Value], scan.Selection.Records.Select(record =>
            record.MaterializeOwned().Id.Value));
        Assert.Single(scan.ReadIntervals);
        Assert.StartsWith("collection:", scan.ReadIntervals[0].LogicalAccessPathId,
            StringComparison.Ordinal);
    }

    private static async Task ExerciseHostileResultOwnershipAsync(
        HPDBaseStoreProvider storeProvider, string? schemaStoreId)
    {
        await using ServiceProvider provider = BaseLogicalIndexCertificationHost.Create(storeProvider);
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection =
            await InitializeFixtureAsync(provider, schemaStoreId);
        (await collection.CreateAsync(Id(1), Item("a", "x", 1))).RequireValue();

        BasePredicate<BaseLogicalIndexCertificationItem> predicate =
            BasePredicate<BaseLogicalIndexCertificationItem>.And(
                BaseLogicalIndexCertificationItem.Fields.Tenant.Equal("a"),
                BaseLogicalIndexCertificationItem.Fields.Code.Equal("x"));
        (BaseAtomicExecutionRequest request, BaseAtomicMutationExecutionLimits limits) =
            await CreateCaptureRequestAsync(provider, collection, Query(predicate.Expression));
        var probe = new HostileResultOwnershipProbe(request, limits);
        RecordMutationExecutionResult execution = await provider
            .GetRequiredService<IAtomicRecordStore>()
            .ExecuteAtomicAsync(probe, new RecordMutationExecutionRequest
            {
                AcquisitionTimeout = TimeSpan.FromSeconds(1),
                TransactionTimeout = TimeSpan.FromSeconds(1),
                CommitCompletionTimeout = TimeSpan.FromSeconds(1),
            });

        Assert.Equal(RecordMutationExecutionOutcome.RollbackConfirmed, execution.Outcome);
        Assert.NotNull(probe.PrepareResult);
        Assert.Equal(OperationStatus.StoreError, probe.PrepareResult!.Status);
        Assert.Equal(BaseSchemaErrorCodes.ProviderEvidenceInvalid, probe.PrepareResult.Error!.Code);
        Assert.False(((IBaseLogicalIndexCertificationInspection)provider
            .GetRequiredService<IRecordStore>()).LogicalIndexesReady);
        Assert.Equal(1, (await collection.GetAsync(Id(1))).RequireValue().Value.Sequence);
    }

    private static async Task ExercisePolicyPointAsync(
        HPDBaseStoreProvider storeProvider, string? schemaStoreId)
    {
        await using ServiceProvider provider = BaseLogicalIndexCertificationHost.Create(
            storeProvider, constrainToTenantA: true);
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection =
            await InitializeFixtureAsync(provider, schemaStoreId);
        (await collection.CreateAsync(Id(1), Item("a", "x", 1))).RequireValue();
        (await collection.CreateAsync(Id(4), Item("b", "x", 4))).RequireValue();

        BaseSelectionMutationResult result = (await collection.Query()
            .Where(BaseLogicalIndexCertificationItem.Fields.Code.Equal("x"))
            .OrderBy(BaseLogicalIndexCertificationItem.Fields.Sequence)
            .ThenByRecordId()
            .Take(4)
            .PatchSelectedAsync(
                collection.GetMergePatchSelectionProfile(
                    BaseLogicalIndexCertificationHost.ProfileIdentity()),
                BaseLogicalIndexCertificationHost.SequencePatch(9),
                BasePreviousStateRequirement.None,
                BaseLogicalIndexCertificationHost.Identity("point-policy"))).RequireValue();

        Assert.Equal(1, result.SelectedCount);
        Assert.Equal(1, result.MutatedCount);
        Assert.Equal(9, (await collection.GetAsync(Id(1))).RequireValue().Value.Sequence);
        Assert.Equal(4, (await collection.GetAsync(Id(4))).RequireValue().Value.Sequence);
    }

    private static RecordQuery Query(FilterExpression filter) => new()
    {
        Filter = filter,
        Sort =
        [
            new QuerySort(BaseLogicalIndexCertificationItem.Fields.Sequence.Id),
            new QuerySort("id"),
        ],
        Page = new QueryPage
        {
            Mode = QueryPaginationMode.Offset,
            Offset = 0,
            Limit = 4,
        },
        Count = QueryCountMode.None,
    };

    private static async Task<BaseCapturedAtomicExecution> CaptureAsync(
        ServiceProvider provider,
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection,
        RecordQuery applicationQuery)
    {
        IAtomicRecordStore store = provider.GetRequiredService<IAtomicRecordStore>();
        (BaseAtomicExecutionRequest request, _) = await CreateCaptureRequestAsync(
            provider, collection, applicationQuery);
        var processor = new CaptureProbeProcessor(request);
        RecordMutationExecutionResult execution = await store.ExecuteAtomicAsync(
            processor, new RecordMutationExecutionRequest
            {
                AcquisitionTimeout = TimeSpan.FromSeconds(1),
                TransactionTimeout = TimeSpan.FromSeconds(1),
                CommitCompletionTimeout = TimeSpan.FromSeconds(1),
            });
        Assert.NotEqual(RecordMutationExecutionOutcome.Committed, execution.Outcome);
        OperationResult<BaseCapturedAtomicExecution> captured = Assert.IsType<OperationResult<BaseCapturedAtomicExecution>>(
            processor.Captured);
        Assert.True(captured.IsSuccess(), captured.Error?.Code);
        return Assert.IsType<BaseCapturedAtomicExecution>(captured.Value);
    }

    private static async Task<(BaseAtomicExecutionRequest Request, BaseAtomicMutationExecutionLimits Limits)>
        CreateCaptureRequestAsync(
            ServiceProvider provider,
            BaseCollectionSession<BaseLogicalIndexCertificationItem> collection,
            RecordQuery applicationQuery)
    {
        IAtomicRecordStore store = provider.GetRequiredService<IAtomicRecordStore>();
        BaseSelectionOperationProfile profile = BaseLogicalIndexCertificationHost.Profile();
        BaseAtomicMutationExecutionLimits limits = BaseAtomicSchemaContract.AttachLimits(
            DefaultBaseSelectionMutationRuntime.CreateExecutionLimits(profile.Limits),
            [collection.Contract.Definition]);
        BaseAtomicMutationAuthorityRequirement authority = (await store
            .CaptureAtomicMutationAuthorityRequirementAsync(
                BaseLogicalIndexCertificationHost.ApplicationId,
                [collection.Contract.Definition], limits)).Value
            ?? throw new InvalidOperationException("base.logicalIndex.certificationInvalid");
        BaseLogicalIndexPointSelection? point = BaseLogicalIndexPointPlanContract.Derive(
            collection.Contract.Definition, applicationQuery.Filter);
        RecordQuery providerQuery = BaseQueryFieldResolver.ToStoredNames(
            collection.Contract.Definition, applicationQuery);
        return (new BaseAtomicExecutionRequest
        {
            Kind = BaseAtomicMutationExecutionKind.SelectionMutation,
            Intent = new BaseAtomicMutationIntent
            {
                IntentDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    "base.logicalIndex.certificationCapture.v1"u8)),
                Authority = authority,
                Items = [],
            },
            Selection = new BaseSelectionMutationCaptureExtension
            {
                OperationProfileId = profile.Id,
                OperationProfileVersion = profile.Version,
                OperationProfileChecksum = BaseSelectionProfileChecksum.Compute(profile),
                Selection = new BaseAtomicSelectionRequest
                {
                    Collection = collection.Contract.Definition,
                    Query = providerQuery,
                    CanonicalRecordCodecVersion = 1,
                    LogicalIndexPoint = point,
                },
            },
            Schema = BaseAtomicSchemaContract.CaptureRequest(
                authority, [collection.Contract.Definition], limits),
            Limits = limits,
        }, limits);
    }

    private sealed class CaptureProbeProcessor(BaseAtomicExecutionRequest request)
        : IAtomicMutationProcessor
    {
        internal OperationResult<BaseCapturedAtomicExecution>? Captured { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            Captured = await session.CaptureAtomicExecutionAsync(request, cancellationToken);
            return new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.Failed,
                [],
                new BaseError
                {
                    Code = "base.logicalIndex.certificationProbeComplete",
                    Message = "The certification capture probe completed.",
                    Category = ErrorCategory.Validation,
                });
        }
    }

    private sealed class HostileResultOwnershipProbe(
        BaseAtomicExecutionRequest request,
        BaseAtomicMutationExecutionLimits limits) : IAtomicMutationProcessor
    {
        internal OperationResult<BasePreparedAtomicExecution>? PrepareResult { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            OperationResult<BaseCapturedAtomicExecution> result = await session
                .CaptureAtomicExecutionAsync(request, cancellationToken);
            if (!result.IsSuccess() || result.Value?.Selection?.LogicalIndexEvidence is null)
                return Failure(result.Error);
            BaseCapturedAtomicExecution captured = result.Value;
            byte[] retained = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(
                captured.Selection.LogicalIndexEvidence.MemberSetChecksum)
                ?? throw new InvalidOperationException("base.logicalIndex.certificationInvalid");
            retained[0] ^= 0x01;
            ImmutableArray<BaseAtomicMutationPlanItem> items = captured.Selection.Records
                .Select((owned, ordinal) =>
                {
                    RecordEnvelope record = owned.MaterializeOwned();
                    return new BaseAtomicMutationPlanItem
                    {
                        Ordinal = ordinal,
                        ItemId = $"selection:{ordinal}",
                        EventId = $"base-logical-index-hostile-result-{ordinal}",
                        Collection = request.Selection!.Selection.Collection,
                        Kind = BaseCommittedRecordMutationKind.Patch,
                        RequestedKind = BaseRecordMutationKind.Patch,
                        RecordId = record.Id,
                        ProposedPayload = record.Payload,
                        RemovedFieldIds = [],
                        Current = record,
                        ChangedFields = [],
                        Operation = new OperationContext
                        {
                            ApplicationId = request.Intent.Authority.ApplicationId,
                            Operation = BaseOperationKind.SelectionMutation,
                            CollectionId = request.Selection.Selection.Collection.Id,
                            RecordId = record.Id.Value,
                            Now = DateTimeOffset.UnixEpoch,
                        },
                    };
                })
                .ToImmutableArray();
            PrepareResult = await session.PrepareAtomicExecutionAsync(captured,
                new BaseFinalizedAtomicExecutionPlan
                {
                    Kind = BaseAtomicMutationExecutionKind.SelectionMutation,
                    PlanDigest = "base.logicalIndex.hostileResultOwnership.v1",
                    IntentDigest = request.Intent.IntentDigest,
                    CaptureDigest = captured.CaptureDigest,
                    PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]),
                    Authority = request.Intent.Authority,
                    Items = items,
                    SubjectValidations = [],
                    Schema = null,
                    Limits = limits,
                }, cancellationToken);
            return Failure(PrepareResult.Error);
        }

        private static AtomicMutationProcessingResult Failure(BaseError? error) => new(
            AtomicMutationProcessingOutcome.Failed,
            [],
            error ?? new BaseError
            {
                Code = "base.logicalIndex.certificationInvalid",
                Message = "The hostile-result ownership proof did not fail closed.",
                Category = ErrorCategory.Store,
            });
    }

    private sealed class PausingSelectionItemPolicyEvaluator : IPolicyEvaluator
    {
        internal TaskCompletionSource Captured { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.Operation.Operation == BaseOperationKind.SelectionMutation
                && request.Resource.Kind == PolicyResourceKind.UpdatePayload
                && request.Resource.ExistingRecord is not null)
            {
                Captured.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed,
                Audit = new PolicyAuditInfo
                {
                    MatchedGrantIds = [BaseLogicalIndexCertificationHost.GrantId],
                },
            };
        }
    }


    private static async Task<BaseCollectionSession<BaseLogicalIndexCertificationItem>> InitializeFixtureAsync(
        ServiceProvider provider, string? schemaStoreId)
    {
        if (schemaStoreId is not null)
        {
            IBaseSchemaManager manager = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await manager.PlanAsync(new BaseSchemaPlanRequest
            {
                StoreId = schemaStoreId,
            });
            Assert.True(planned.IsSuccess(), planned.Error?.Code);
            BaseSchemaPlan plan = Assert.IsType<BaseSchemaPlan>(planned.Value);
            OperationResult<BaseSchemaApplyResult> applied = await manager.ApplyAsync(new BaseSchemaApplyRequest
            {
                ProtectedArtifact = plan.ProtectedArtifact,
            });
            Assert.True(applied.IsSuccess(), applied.Error?.Code);
        }
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection =
            await BaseLogicalIndexCertificationHost.InitializeAsync(provider);
        return collection;
    }

    private static BaseLogicalIndexCertificationItem Item(
        string tenant, string? code, long sequence) => new()
    {
        Tenant = tenant,
        Code = code,
        Sequence = sequence,
    };

    private static BaseLogicalIndexProviderCapability GraphBoundedCapability() =>
        BaseLogicalIndexProviderContract.SealCapability(
            BaseLogicalIndexProviderContract.BuiltInCapability() with
            {
                MaximumIndexedRecordsPerCollection = 4,
                MaximumPostingsPerIndex = 4,
                MaximumPostingRecordsPerKey = 4,
                MaximumPostingsPerStore = 12,
                MaximumDirectoryPredicateEvaluationsPerPublication = 4,
                Checksum = [],
            });

    private static RecordId Id(int ordinal) => RecordId.Create(
        $"00000000-0000-0000-0000-{ordinal:000000000000}");

    private static BaseLogicalIndexPointSelection Point(
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection,
        string tenant,
        string code)
    {
        BasePredicate<BaseLogicalIndexCertificationItem> predicate =
            BasePredicate<BaseLogicalIndexCertificationItem>.And(
                BaseLogicalIndexCertificationItem.Fields.Tenant.Equal(tenant),
                BaseLogicalIndexCertificationItem.Fields.Code.Equal(code));
        return BaseLogicalIndexPointPlanContract.Derive(
            collection.Contract.Definition, predicate.Expression)
            ?? throw new InvalidOperationException("base.logicalIndex.certificationInvalid");
    }

    private static string FindPosting(
        BaseLogicalIndexCertificationSnapshot snapshot,
        BaseLogicalIndexPointSelection point) => snapshot.Directory.EqualityPostings.Single(posting =>
            posting.EqualityKey.AsSpan().SequenceEqual(point.EqualityKey.AsSpan())).RecordIds.Single();
}
