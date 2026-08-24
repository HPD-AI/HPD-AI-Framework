using System.Collections.Immutable;
using Xunit;

namespace HPD.Base.Tests.Studio;

public sealed class BaseStudioEvidenceRuntimeTests
{
    [Fact]
    public async Task Runtime_rejects_hostile_page_checksum()
    {
        BaseStudioEvidenceRequirement requirement = Requirement(); var provider = new HostileProvider();
        OperationResult<BaseStudioEvidencePage> result = await new DefaultBaseStudioEvidenceRuntime().ReadPageAsync(provider,
            requirement, Scope(requirement), new BaseStudioEvidencePageRequest { Take = 1 });
        Assert.Equal(OperationStatus.StoreError, result.Status); Assert.Equal("base.studio.corruptEvidence", result.Error?.Code);
    }

    [Fact]
    public async Task Runtime_rejects_maximum_plus_one_before_provider_influence()
    {
        BaseStudioEvidenceRequirement requirement = Requirement(); var provider = new HostileProvider();
        await Assert.ThrowsAsync<ArgumentException>(() => new DefaultBaseStudioEvidenceRuntime().ReadPageAsync(provider,
            requirement, Scope(requirement), new BaseStudioEvidencePageRequest { Take = 3 }).AsTask());
        Assert.Equal(0, provider.Captures);
    }

    [Fact]
    public async Task Provider_authority_is_single_session_and_provider_bound()
    {
        BaseStudioEvidenceRequirement requirement = Requirement(); var first = new HostileProvider(); var second = new HostileProvider();
        OperationResult<BaseCapturedStudioEvidenceAuthority> captured = await first.CaptureAuthorityAsync(requirement, Scope(requirement));
        Assert.Equal(OperationStatus.PolicyDenied, (await second.OpenSessionAsync(captured.Value!)).Status);
        Assert.True((await first.OpenSessionAsync(captured.Value!)).IsSuccess());
        Assert.Equal(OperationStatus.PolicyDenied, (await first.OpenSessionAsync(captured.Value!)).Status);
    }

    [Fact]
    public async Task Runtime_normalizes_hostile_provider_exception()
    {
        OperationResult<BaseStudioEvidencePage> result = await new DefaultBaseStudioEvidenceRuntime().ReadPageAsync(
            new ThrowingProvider(), Requirement(), Scope(Requirement()), new BaseStudioEvidencePageRequest { Take = 1 });
        Assert.Equal(OperationStatus.StoreError, result.Status); Assert.Equal("base.studio.unexpected", result.Error?.Code);
    }

    [Fact]
    public async Task Runtime_never_discloses_provider_failure_code_or_message()
    {
        OperationResult<BaseStudioEvidencePage> result = await new DefaultBaseStudioEvidenceRuntime().ReadPageAsync(
            new NativeFailureProvider(), Requirement(), Scope(Requirement()), new BaseStudioEvidencePageRequest { Take = 1 });
        Assert.Equal("base.studio.unexpected", result.Error?.Code);
        Assert.DoesNotContain("secret", result.Error?.Message ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(BlockingPhase.Capability)]
    [InlineData(BlockingPhase.Capture)]
    [InlineData(BlockingPhase.Open)]
    [InlineData(BlockingPhase.Read)]
    [InlineData(BlockingPhase.Dispose)]
    public async Task Runtime_places_true_outer_deadline_around_synchronous_provider_work(BlockingPhase phase)
    {
        BaseStudioEvidenceRequirement requirement = Requirement() with { Limits = Requirement().Limits with
        { AcquisitionDeadline = TimeSpan.FromMilliseconds(20), SessionDeadline = TimeSpan.FromMilliseconds(20), PageDeadline = TimeSpan.FromMilliseconds(20) } };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        OperationResult<BaseStudioEvidencePage> result = await new DefaultBaseStudioEvidenceRuntime().ReadPageAsync(
            new BlockingProvider(phase), requirement, Scope(requirement), new BaseStudioEvidencePageRequest { Take = 1 });
        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(250));
        if (phase != BlockingPhase.Dispose) Assert.Equal("base.studio.deadlineExceeded", result.Error?.Code);
        else Assert.True(result.IsSuccess());
    }

    [Fact]
    public async Task InMemory_scope_index_excludes_large_foreign_scope_from_tiny_budget()
    {
        var state = new InMemoryStoreState { GlobalMutationPosition = 1_001 };
        for (long position = 1; position <= 1_000; position++) state.AddJournalEntry(Journal(position, "foreign", position.ToString()));
        state.AddJournalEntry(Journal(1_001, "orders", "1"));
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { StoreId = "evidence" });
        typeof(InMemoryRecordStore).GetField("_publishedState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(store, state);
        BaseStudioEvidenceRequirement requirement = Requirement() with { Limits = Requirement().Limits with { MaximumRowsRead = 2 } };
        OperationResult<BaseStudioEvidencePage> result = await new DefaultBaseStudioEvidenceRuntime().ReadPageAsync(store, requirement,
            Scope(requirement), new BaseStudioEvidencePageRequest { Take = 1 });
        Assert.True(result.IsSuccess()); Assert.Single(result.Value!.Items); Assert.Equal(1, result.Value.Accounting.RowsRead);
        Assert.Single(state.StudioRecordEvidenceIndex[BaseStudioEvidenceContract.RecordIndexKey(requirement.Scope, "orders", new RecordId("1"))]);
    }

    [Theory]
    [InlineData(BaseSubjectScopeKind.Global, null, "global")]
    [InlineData(BaseSubjectScopeKind.Tenant, "tenant-a", "tenant")]
    [InlineData(BaseSubjectScopeKind.Project, "project-a", "project")]
    public async Task InMemory_evidence_separates_exact_scope(BaseSubjectScopeKind kind, string? value, string expected)
    {
        var state = new InMemoryStoreState { GlobalMutationPosition = 3 };
        state.AddJournalEntry(Journal(1, "orders", "1", new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global }, "global"));
        state.AddJournalEntry(Journal(2, "orders", "1", new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" }, "tenant"));
        state.AddJournalEntry(Journal(3, "orders", "1", new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Project, Value = "project-a" }, "project"));
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { StoreId = "evidence" });
        typeof(InMemoryRecordStore).GetField("_publishedState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(store, state);
        BaseStudioEvidenceRequirement requirement = Requirement() with { Scope = new BaseOwnedSubjectScopeEvidence { Kind = kind, Value = value } };
        OperationResult<BaseStudioEvidencePage> result = await new DefaultBaseStudioEvidenceRuntime().ReadPageAsync(store, requirement,
            new BaseOwnedScopeSeekAuthority { Kind = kind, ProtectedIndexDigest = requirement.ProtectedScopeSeekChecksum }, new BaseStudioEvidencePageRequest { Take = 2 });
        BaseStudioRecordMutationEvidenceItem item = Assert.IsType<BaseStudioRecordMutationEvidenceItem>(Assert.Single(result.Value!.Items));
        Assert.Equal(expected, item.EvidenceId);
    }

    private static BaseStudioEvidenceRequirement Requirement() => new()
    {
        ApplicationId = "app", Kind = BaseStudioEvidenceKind.RecordMutation,
        Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
        Parent = new BaseStudioRecordEvidenceSubject { CollectionId = "orders", InstalledCollectionChecksum = ImmutableArray.CreateRange(new byte[32]), RecordId = new RecordId("1") },
        ProtectedScopeSeekChecksum = ImmutableArray.CreateRange(Enumerable.Repeat((byte)1, 32)),
        Limits = new BaseStudioEvidenceLimits { MaximumItems = 2, MaximumRowsRead = 3, MaximumIntervals = 1,
            MaximumEvidenceBytes = 1024, MaximumTransientBytes = 1024, AcquisitionDeadline = TimeSpan.FromSeconds(1),
            SessionDeadline = TimeSpan.FromSeconds(2), PageDeadline = TimeSpan.FromSeconds(1) },
    };
    private static BaseOwnedScopeSeekAuthority Scope(BaseStudioEvidenceRequirement value) => new()
    { Kind = BaseSubjectScopeKind.Global, ProtectedIndexDigest = value.ProtectedScopeSeekChecksum };
    private static BaseMutationJournalEntry Journal(long position, string collection, string record,
        BaseOwnedSubjectScopeEvidence? scope = null, string? evidenceId = null) => new()
    {
        Kind = BaseMutationJournalEntryKind.RecordMutation, Position = new BaseMutationJournalPosition(position),
        RecordMutation = new BaseRecordMutationJournalEntry { EventId = evidenceId ?? "event-" + position, Type = "base.record.updated",
            Scope = scope ?? new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
            SchemaVersion = "1", OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(position), Operation = BaseOperationKind.Patch,
            CollectionId = collection, RecordId = new RecordId(record) }
    };

    private sealed class HostileProvider : IBaseStudioEvidenceStore
    {
        public BaseStudioEvidenceCapability EvidenceCapability { get; } = BaseStudioEvidenceContract.RecordMutationCapability();
        public int Captures { get; private set; }
        public ValueTask<OperationResult<BaseCapturedStudioEvidenceAuthority>> CaptureAuthorityAsync(BaseStudioEvidenceRequirement request,
            BaseOwnedScopeSeekAuthority scope, CancellationToken cancellationToken = default)
        {
            Captures++; var receipt = new BaseStudioEvidenceCaptureReceipt { ApplicationId = request.ApplicationId, Kind = request.Kind,
                StoreIdentity = "fake", RestoreEpoch = 0, IndexGeneration = 1, LogicalAccessPathId = "base.studio.evidence.record-mutation.v1",
                ProtectedScopeSeekChecksum = request.ProtectedScopeSeekChecksum,
                AuthorityChecksum = EvidenceHash.Authority(request, "fake", 0, 1, "base.studio.evidence.record-mutation.v1") };
            return ValueTask.FromResult(OperationResults.Ok<BaseCapturedStudioEvidenceAuthority>(new Captured(this, receipt)));
        }
        public ValueTask<OperationResult<IBaseStudioEvidenceSession>> OpenSessionAsync(BaseCapturedStudioEvidenceAuthority authority, CancellationToken cancellationToken = default)
        {
            if (authority is not Captured value || !ReferenceEquals(value.Owner, this) || !value.TryOpen())
                return ValueTask.FromResult(OperationResults.PolicyDenied<IBaseStudioEvidenceSession>(new BaseError { Code = "mismatch", Message = "mismatch", Category = ErrorCategory.Authorization }));
            return ValueTask.FromResult(OperationResults.Ok<IBaseStudioEvidenceSession>(new HostileSession()));
        }
        private sealed class Captured(HostileProvider owner, BaseStudioEvidenceCaptureReceipt receipt) : BaseCapturedStudioEvidenceAuthority(receipt)
        { private int _opened; internal HostileProvider Owner { get; } = owner; internal bool TryOpen() => Interlocked.Exchange(ref _opened, 1) == 0; }
        private sealed class HostileSession : IBaseStudioEvidenceSession
        {
            public ValueTask<OperationResult<BaseStudioEvidencePage>> ReadPageAsync(BaseStudioEvidencePageRequest request, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(OperationResults.Ok(new BaseStudioEvidencePage { Items = [], Next = null, IndexGeneration = 1,
                    Intervals = [new BaseStudioEvidenceReadInterval { LogicalAccessPathId = "base.studio.evidence.record-mutation.v1",
                        ProtectedScopeSeekChecksum = ImmutableArray.CreateRange(new byte[32]),
                        LowerInclusive = ImmutableArray.CreateRange(new byte[8]), UpperExclusive = ImmutableArray.CreateRange(new byte[] { 0,0,0,0,0,0,0,2 }),
                        Checksum = ImmutableArray.CreateRange(new byte[32]) }],
                    Accounting = new BaseStudioEvidenceProviderAccounting { RowsRead = 0, Intervals = 1, EvidenceBytes = 0, TransientBytes = 0 },
                    PageChecksum = ImmutableArray.CreateRange(new byte[32]) }));
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class ThrowingProvider : IBaseStudioEvidenceStore
    {
        public BaseStudioEvidenceCapability EvidenceCapability { get; } = BaseStudioEvidenceContract.RecordMutationCapability();
        public ValueTask<OperationResult<BaseCapturedStudioEvidenceAuthority>> CaptureAuthorityAsync(BaseStudioEvidenceRequirement request,
            BaseOwnedScopeSeekAuthority scope, CancellationToken cancellationToken = default) => throw new InvalidOperationException("native secret");
        public ValueTask<OperationResult<IBaseStudioEvidenceSession>> OpenSessionAsync(BaseCapturedStudioEvidenceAuthority authority,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
    private sealed class NativeFailureProvider : IBaseStudioEvidenceStore
    {
        public BaseStudioEvidenceCapability EvidenceCapability { get; } = BaseStudioEvidenceContract.RecordMutationCapability();
        public ValueTask<OperationResult<BaseCapturedStudioEvidenceAuthority>> CaptureAuthorityAsync(BaseStudioEvidenceRequirement request, BaseOwnedScopeSeekAuthority scope, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.StoreError<BaseCapturedStudioEvidenceAuthority>(new BaseError { Code = "native.secret", Message = "secret native failure", Category = ErrorCategory.Store }));
        public ValueTask<OperationResult<IBaseStudioEvidenceSession>> OpenSessionAsync(BaseCapturedStudioEvidenceAuthority authority, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    public enum BlockingPhase { Capability, Capture, Open, Read, Dispose }
    private sealed class BlockingProvider(BlockingPhase phase) : IBaseStudioEvidenceStore
    {
        public BaseStudioEvidenceCapability EvidenceCapability
        { get { Block(BlockingPhase.Capability); return BaseStudioEvidenceContract.RecordMutationCapability(); } }
        public ValueTask<OperationResult<BaseCapturedStudioEvidenceAuthority>> CaptureAuthorityAsync(BaseStudioEvidenceRequirement request, BaseOwnedScopeSeekAuthority scope, CancellationToken cancellationToken = default)
        {
            Block(BlockingPhase.Capture); var receipt = new BaseStudioEvidenceCaptureReceipt { ApplicationId = request.ApplicationId, Kind = request.Kind, StoreIdentity = "blocking", RestoreEpoch = 0, IndexGeneration = 1,
                LogicalAccessPathId = BaseStudioEvidenceContract.RecordMutationPath, ProtectedScopeSeekChecksum = [.. request.ProtectedScopeSeekChecksum],
                AuthorityChecksum = BaseStudioEvidenceContract.AuthorityChecksum(request, "blocking", 0, 1, BaseStudioEvidenceContract.RecordMutationPath) };
            return ValueTask.FromResult(OperationResults.Ok<BaseCapturedStudioEvidenceAuthority>(new Captured(receipt)));
        }
        public ValueTask<OperationResult<IBaseStudioEvidenceSession>> OpenSessionAsync(BaseCapturedStudioEvidenceAuthority authority, CancellationToken cancellationToken = default)
        { Block(BlockingPhase.Open); return ValueTask.FromResult(OperationResults.Ok<IBaseStudioEvidenceSession>(new Session(this))); }
        private void Block(BlockingPhase expected) { if (phase == expected) Thread.Sleep(100); }
        private sealed class Captured(BaseStudioEvidenceCaptureReceipt receipt) : BaseCapturedStudioEvidenceAuthority(receipt);
        private sealed class Session(BlockingProvider owner) : IBaseStudioEvidenceSession
        {
            public ValueTask<OperationResult<BaseStudioEvidencePage>> ReadPageAsync(BaseStudioEvidencePageRequest request, CancellationToken cancellationToken = default)
            {
                owner.Block(BlockingPhase.Read); ImmutableArray<byte> lower = BaseStudioEvidenceContract.Tuple(0); ImmutableArray<byte> upper = BaseStudioEvidenceContract.Tuple(2);
                ImmutableArray<byte> scope = ImmutableArray.CreateRange(Enumerable.Repeat((byte)1, 32));
                ImmutableArray<BaseStudioEvidenceReadInterval> intervals = [new() { LogicalAccessPathId = BaseStudioEvidenceContract.RecordMutationPath,
                    ProtectedScopeSeekChecksum = scope, LowerInclusive = lower, UpperExclusive = upper,
                    Checksum = BaseStudioEvidenceContract.IntervalChecksum(BaseStudioEvidenceContract.RecordMutationPath, scope, lower, upper) }];
                var accounting = new BaseStudioEvidenceProviderAccounting { RowsRead = 0, Intervals = 1, EvidenceBytes = 0, TransientBytes = 0 };
                var page = new BaseStudioEvidencePage { Items = [], Next = null, IndexGeneration = 1, Intervals = intervals, Accounting = accounting, PageChecksum = [] };
                return ValueTask.FromResult(OperationResults.Ok(page with { PageChecksum = BaseStudioEvidenceContract.PageChecksum([], 1, null, intervals, accounting) }));
            }
            public ValueTask DisposeAsync() { owner.Block(BlockingPhase.Dispose); return ValueTask.CompletedTask; }
        }
    }

    private static class EvidenceHash
    {
        public static ImmutableArray<byte> Authority(BaseStudioEvidenceRequirement request, string store, long restore, long generation, string path)
        {
            // Mirrors the frozen public receipt vector without exposing provider implementation helpers.
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            Span<byte> length = stackalloc byte[4];
            foreach (string value in new[] { "base-studio-evidence-binary-v1", request.ApplicationId, ((byte)request.Kind).ToString(), store,
                restore.ToString(), generation.ToString(), path, Convert.ToHexString(request.ProtectedScopeSeekChecksum.AsSpan()),
                $"record:orders:1:{Convert.ToHexString(((BaseStudioRecordEvidenceSubject)request.Parent).InstalledCollectionChecksum.AsSpan())}" })
            { byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value); System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); hash.AppendData(length); hash.AppendData(bytes); }
            return [.. hash.GetHashAndReset()];
        }
    }
}
