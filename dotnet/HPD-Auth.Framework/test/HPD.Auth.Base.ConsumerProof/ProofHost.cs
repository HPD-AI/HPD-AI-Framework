using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.Auth.Base.ConsumerProof;

internal static class ProofHost
{
    internal static async Task RunAsync(bool sqlite, bool l81Only = false)
    {
        string? dataSource = sqlite ? Path.Combine(Path.GetTempPath(), $"hpd-auth-l3b-{Guid.NewGuid():N}.db") : null;
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var clock = new ProofTimeProvider(DateTimeOffset.UnixEpoch);
            services.AddSingleton<TimeProvider>(clock);
            services.AddHPDBase(builder => Configure(builder, dataSource));
            await using ServiceProvider provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true });
            if (sqlite)
            {
                IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
                BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest
                {
                    StoreId = "proof.sqlite",
                })).Value ?? throw new InvalidOperationException("The L3B SQLite schema plan was not produced.");
                OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(new BaseSchemaApplyRequest
                {
                    ProtectedArtifact = plan.ProtectedArtifact,
                });
                if (!applied.IsSuccess())
                    throw new InvalidOperationException("The L3B SQLite schema apply failed: " + applied.Error?.Code);
            }

            OperationResult<BaseApplicationReadiness> readiness =
                await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
            if (!readiness.IsSuccess())
                throw new InvalidOperationException("The L3B proving host was not ready: " + readiness.Error?.Code);

            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Service,
                SubjectKind = AccessSubjectKind.ServicePrincipal,
                SubjectId = "proof-service",
                CurrentTenantId = "tenant-a",
            });
            if (l81Only)
            {
                await ExecuteLifecycleAsync(session, sqlite ? "sqlite" : "inmemory");
                return;
            }
            await ExecuteIdentityAndGenerationAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteRequestControlAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteStaticSetAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteL30PatchFormsAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteGuardedSelectionAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteGuardedL50ChildAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecutePresenceAndRemovalAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteRegisteredReadsAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteCrossMarkerStorageAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteLifecycleAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteScheduleAsync(session, sqlite ? "sqlite" : "inmemory", clock);
            await ExecuteDurableContinuationAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteDurableYieldAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteClaimFenceRejectionAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteInvalidHandlerResultAsync(session, sqlite ? "sqlite" : "inmemory");
            await ExecuteSemanticLifecycleAsync(session,
                provider.GetRequiredService<IHPDBaseAdministration>(),
                provider.GetRequiredService<IBaseSessionFactory>(),
                sqlite ? "proof.sqlite" : "inmemory",
                sqlite ? "sqlite" : "inmemory", clock);
        }
        finally
        {
            if (dataSource is not null)
                foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
                    if (File.Exists(dataSource + suffix)) File.Delete(dataSource + suffix);
        }
    }

    private static async Task ExecuteInvalidHandlerResultAsync(BaseSession session, string provider)
    {
        BaseInstalledActivationHandle<ProofActivationInput, ProofActivationResult> activation =
            session.Activations.Get(ProofActivation.Registration.Identity);
        BaseInstalledActivationWorkerHandle<ProofActivationInput, ProofActivationResult> worker =
            session.Activations.GetWorker(ProofActivation.Registration.Identity);
        OperationResult<BaseActivationEnqueueResult> enqueued = await activation.EnqueueAsync(
            new ProofActivationInput { Value = "invalid-handler-result" },
            BaseMutationRequestIdentity.Create("proof-activation", "invalid-handler-result", provider,
                BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(provider + ":invalid-handler-result")))));
        if (!enqueued.IsSuccess())
            throw new InvalidOperationException("The invalid-handler-result proof could not enqueue: " + enqueued.Error?.Code);
        BaseActivationDispatchResult dispatched = await DispatchAsync(worker, enqueued.Value!.ActivationId);
        if (dispatched.State != BaseActivationState.Exhausted)
            throw new InvalidOperationException("Invalid handler output did not terminate nonretryably.");
    }

    private static async Task ExecuteClaimFenceRejectionAsync(BaseSession session, string provider)
    {
        BaseInstalledActivationHandle<ProofActivationInput, ProofActivationResult> activation =
            session.Activations.Get(ProofActivation.Registration.Identity);
        BaseInstalledActivationWorkerHandle<ProofActivationInput, ProofActivationResult> worker =
            session.Activations.GetWorker(ProofActivation.Registration.Identity);
        BaseMutationRequestFingerprint enqueueFingerprint = BaseMutationRequestFingerprint.Create(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("claim-fence-enqueue:" + provider)));
        OperationResult<BaseActivationEnqueueResult> enqueued = await activation.EnqueueAsync(
            new ProofActivationInput { Value = "claim-fence:" + provider },
            BaseMutationRequestIdentity.Create("proof-claim-fence", "enqueue", provider, enqueueFingerprint));
        if (!enqueued.IsSuccess())
            throw new InvalidOperationException("The L3B claim/fence enqueue failed: " + enqueued.Error?.Code);

        OperationResult<BaseActivationDueObservation> observed = await worker.ObserveDueAsync();
        if (!observed.IsSuccess() || observed.Value is null)
            throw new InvalidOperationException("The L3B claim/fence observation failed: " + observed.Error?.Code);
        BaseMutationRequestFingerprint claimFingerprint = BaseMutationRequestFingerprint.Create(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("claim-fence-claim:" + provider)));
        OperationResult<BaseActivationDelivery<ProofActivationInput>?> claimed = await worker.TryClaimAsync(
            observed.Value.Token,
            BaseMutationRequestIdentity.Create("proof-claim-fence", "claim", provider, claimFingerprint));
        BaseActivationDelivery<ProofActivationInput>? delivery = claimed.Value;
        if (!claimed.IsSuccess() || delivery is null
            || !string.Equals(delivery.ActivationId, enqueued.Value!.ActivationId, StringComparison.Ordinal))
            throw new InvalidOperationException("The L3B claim/fence claim failed: " + claimed.Error?.Code);

        BaseActivationDelivery<ProofActivationInput> hostile = delivery with
        {
            Claim = delivery.Claim with
            {
                FencingToken = System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("hostile-fence:" + provider)).ToImmutableArray(),
            },
        };
        BaseMutationRequestFingerprint hostileFingerprint = BaseMutationRequestFingerprint.Create(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("claim-fence-hostile:" + provider)));
        OperationResult<BaseActivationTransitionResult> rejected = await worker.CompleteAsync(
            hostile, new ProofActivationResult { Value = "must-not-commit" },
            BaseMutationRequestIdentity.Create("proof-claim-fence", "hostile-complete", provider, hostileFingerprint));
        if (rejected.IsSuccess() || rejected.Status != OperationStatus.Conflict
            || !string.Equals(rejected.Error?.Code, "base.activation.claimLost", StringComparison.Ordinal))
            throw new InvalidOperationException("The L3B hostile activation fence was not rejected safely: "
                + rejected.Status + "/" + rejected.Error?.Code);

        BaseMutationRequestFingerprint completeFingerprint = BaseMutationRequestFingerprint.Create(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("claim-fence-complete:" + provider)));
        OperationResult<BaseActivationTransitionResult> completed = await worker.CompleteAsync(
            delivery, new ProofActivationResult { Value = "claim-fence-complete" },
            BaseMutationRequestIdentity.Create("proof-claim-fence", "complete", provider, completeFingerprint));
        if (!completed.IsSuccess())
            throw new InvalidOperationException("The valid activation fence did not commit: " + completed.Error?.Code);
    }

    private static async Task ExecuteLifecycleAsync(BaseSession session, string provider)
    {
        _ = LifecycleProof.Drain();
        _ = LifecycleProof.DrainErrors();
        string subjectId = "lifecycle-subject-" + provider;
        BaseInstalledModuleMutationHandle<LifecycleSubjectCreateRequest, LifecycleSubjectCreateResult> create =
            session.ModuleMutations.Get(LifecycleModuleMutationProof.Identity);
        BaseMutationRequestFingerprint createFingerprint = BaseMutationRequestFingerprint.Create(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("lifecycle-create:" + subjectId)));
        BaseResult<BaseModuleMutationExecutionResult<LifecycleSubjectCreateResult>> createResult = await create.ExecuteAsync(
            new LifecycleSubjectCreateRequest { SubjectId = subjectId, Tenant = "tenant-a" },
            BaseMutationRequestIdentity.Create(
                "proof-lifecycle", "create", provider, createFingerprint));
        if (createResult is BaseFailure<BaseModuleMutationExecutionResult<LifecycleSubjectCreateResult>> createFailure)
            throw new InvalidOperationException("The L81 L50 lifecycle-subject create failed for " + provider + ": "
                + createFailure.Error.Code + "/" + createFailure.Error.Message);
        BaseModuleMutationExecutionResult<LifecycleSubjectCreateResult> created = createResult.RequireValue();
        BaseSubjectReference<ConsumerSubject> subject = (await session.Reads.ToArrayAsync(
            ConsumerSubjectAcquire.Handle,
            new ConsumerSubjectAcquire { SubjectId = BaseRecordId<ConsumerPrivateSubject>.Create(subjectId) }))
            .RequireValue().Single().Reference;
        BaseMutationRequestFingerprint tombstoneFingerprint = BaseMutationRequestFingerprint.Create(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("tombstone:" + subjectId)));
        BaseResult<BaseSubjectTombstoneResult<ConsumerSubject>> tombstoned = await session
            .GetExportedSubjectContract<ConsumerSubject>(ConsumerSubject.HPDBaseSubjectRegistration)
            .TombstoneAsync(new BaseSubjectTombstoneRequest<ConsumerSubject>
            {
                Subject = subject,
                ExpectedPrivateRevision = created.Result.Revision,
                Identity = BaseMutationRequestIdentity.Create("proof-lifecycle", "tombstone", provider, tombstoneFingerprint),
            });
        if (tombstoned is not BaseSuccess<BaseSubjectTombstoneResult<ConsumerSubject>>)
            throw new InvalidOperationException("The L3B lifecycle tombstone failed for " + provider + ":"
                + ((BaseFailure<BaseSubjectTombstoneResult<ConsumerSubject>>)tombstoned).Error.Code);

        BaseInstalledActivationHandle<ProofActivationInput, ProofActivationResult> activation =
            session.Activations.Get(ProofActivation.Registration.Identity);
        BaseInstalledActivationWorkerHandle<ProofActivationInput, ProofActivationResult> worker =
            session.Activations.GetWorker(ProofActivation.Registration.Identity);
        BaseMutationRequestFingerprint activationFingerprint = BaseMutationRequestFingerprint.Create(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("lifecycle:" + subjectId)));
        OperationResult<BaseActivationEnqueueResult> enqueued = await activation.EnqueueAsync(
            new ProofActivationInput { Value = "lifecycle" },
            BaseMutationRequestIdentity.Create("proof-lifecycle", "activation", provider, activationFingerprint));
        if (!enqueued.IsSuccess())
            throw new InvalidOperationException("The L3B lifecycle activation enqueue failed: " + enqueued.Error?.Code);
        BaseActivationDispatchResult dispatched = await DispatchAsync(worker, enqueued.Value!.ActivationId);
        var observations = LifecycleProof.Drain();
        string[] lifecycleErrors = LifecycleProof.DrainErrors();
        if (dispatched.State != BaseActivationState.Succeeded || observations.Length != 2
            || observations[0].Acknowledgement.Outcome != BaseSubjectRetirementMutationOutcome.Applied
            || observations[0].Checkpoint.Duplicate
            || observations[1].Acknowledgement.Outcome != BaseSubjectRetirementMutationOutcome.Duplicate
            || !observations[1].Checkpoint.Duplicate)
            throw new InvalidOperationException("The L3B guarded L47/L48 proof failed for " + provider + ":"
                + dispatched.State + ":" + string.Join(';', observations.Select(value =>
                    value.Acknowledgement.Outcome + "/" + value.Checkpoint.Duplicate))
                + ":" + string.Join(';', lifecycleErrors));
    }

    private static async Task ExecuteScheduleAsync(
        BaseSession session,
        string provider,
        ProofTimeProvider clock)
    {
        BaseInstalledScheduleHandle schedule = session.Activations.GetSchedule(ProofActivation.ScheduleIdentity);
        BaseMutationRequestFingerprint createFingerprint = BaseMutationRequestFingerprint.Create(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("schedule-create:" + provider)));
        BaseMutationRequestIdentity createIdentity = BaseMutationRequestIdentity.Create(
            "proof-schedule", "create", provider, createFingerprint);
        OperationResult<BaseScheduleMutationResult> created = await schedule.CreateAsync(createIdentity);
        OperationResult<BaseScheduleMutationResult> replayed = await schedule.CreateAsync(createIdentity);
        if (!created.IsSuccess() || !replayed.IsSuccess()
            || replayed.Value!.Disposition != BaseMutationRequestDisposition.Duplicate)
            throw new InvalidOperationException("The L3B schedule creation/replay proof failed for " + provider + ".");

        clock.Advance(TimeSpan.FromMilliseconds(2));

        BaseMutationRequestIdentity advanceIdentity = BaseMutationRequestIdentity.Create(
            "proof-schedule", "advance", provider,
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("schedule-advance:" + provider))));
        OperationResult<BaseScheduleMaintenancePage> advanced = await schedule.AdvanceAsync(advanceIdentity);
        BaseOccurrenceMaterialized materialized = advanced.Value?.Occurrences.SingleOrDefault()?.Disposition
            as BaseOccurrenceMaterialized
            ?? throw new InvalidOperationException("The L3B schedule did not materialize its static input for " + provider + ".");
        BaseInstalledActivationWorkerHandle<ProofActivationInput, ProofActivationResult> worker =
            session.Activations.GetWorker(ProofActivation.Registration.Identity);
        BaseActivationDispatchResult dispatched = await DispatchAsync(worker, materialized.ActivationId);
        if (dispatched.State != BaseActivationState.Succeeded)
            throw new InvalidOperationException("The L3B scheduled activation failed for " + provider + ".");
    }

    private static async Task ExecuteDurableContinuationAsync(BaseSession session, string provider)
    {
        _ = ProofActivation.DrainContinuations();
        string targetId = "continuation-" + provider;
        BaseRecordId<ProofOwner> owner = BaseRecordId<ProofOwner>.Create(
            Guid.Parse("00112233-4455-6677-8899-aabbccddeeff").ToString("D"));
        BaseCollectionSession<ProofWorkItem> work = session.Collection(ProofWorkItem.Collection);
        _ = (await work.CreateAsync(RecordId.Create(targetId),
            new ProofWorkItem { OwnerId = owner, Name = "before-continuation" })).RequireValue();

        BaseInstalledActivationHandle<ProofActivationInput, ProofActivationResult> activation =
            session.Activations.Get(ProofActivation.Registration.Identity);
        BaseInstalledActivationWorkerHandle<ProofActivationInput, ProofActivationResult> worker =
            session.Activations.GetWorker(ProofActivation.Registration.Identity);
        string command = "continuation-root:" + targetId;
        OperationResult<BaseActivationEnqueueResult> enqueued = await activation.EnqueueAsync(
            new ProofActivationInput { Value = command },
            BaseMutationRequestIdentity.Create("proof-continuation", "enqueue", provider,
                BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(command)))));
        if (!enqueued.IsSuccess())
            throw new InvalidOperationException("The L3B continuation root enqueue failed: " + enqueued.Error?.Code);
        BaseActivationDispatchResult root = await DispatchAsync(worker, enqueued.Value!.ActivationId);
        if (root.State != BaseActivationState.Succeeded)
            throw new InvalidOperationException("The L3B continuation root failed for " + provider + ":"
                + string.Join(';', ProofActivation.DrainContinuations()));

        string[] observed = ProofActivation.DrainContinuations();
        for (int attempt = 0; observed.Length == 0 && attempt < 32; attempt++)
        {
            OperationResult<BaseActivationDispatchResult> child = await worker.RunOneAsync();
            if (!child.IsSuccess() || child.Value is null || child.Value.Empty
                || child.Value.State != BaseActivationState.Succeeded)
                throw new InvalidOperationException("The L3B durable continuation dispatch failed for " + provider + ".");
            observed = ProofActivation.DrainContinuations();
        }
        if (observed is not [var only]
            || !string.Equals(only, "continuation-child:" + targetId, StringComparison.Ordinal)
            || (await work.GetAsync(RecordId.Create(targetId))).RequireValue().Value.Name != "continued")
            throw new InvalidOperationException("The L3B atomic state-plus-continuation proof failed for " + provider + ".");
    }

    private static async Task ExecuteDurableYieldAsync(BaseSession session, string provider)
    {
        _ = ProofActivation.DrainContinuations();
        BaseInstalledActivationHandle<ProofActivationInput, ProofActivationResult> activation =
            session.Activations.Get(ProofYieldActivation.Registration.Identity);
        BaseInstalledActivationWorkerHandle<ProofActivationInput, ProofActivationResult> worker =
            session.Activations.GetWorker(ProofYieldActivation.Registration.Identity);
        string command = "yield:6";
        OperationResult<BaseActivationEnqueueResult> enqueued = await activation.EnqueueAsync(
            new ProofActivationInput { Value = command },
            BaseMutationRequestIdentity.Create("proof-yield", "enqueue", provider,
                BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(provider + ":" + command)))));
        if (!enqueued.IsSuccess())
            throw new InvalidOperationException("The L76 durable-yield proof could not enqueue: " + enqueued.Error?.Code);

        BaseActivationState state = BaseActivationState.Pending;
        for (int slice = 0; slice < 7; slice++)
        {
            BaseActivationDispatchResult dispatched;
            try
            {
                dispatched = await DispatchAsync(worker, enqueued.Value!.ActivationId);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"The L76 durable-yield slice {slice.ToString(System.Globalization.CultureInfo.InvariantCulture)} failed for {provider}.",
                    exception);
            }
            state = dispatched.State
                ?? throw new InvalidOperationException("The L76 durable-yield slice omitted its state.");
        }
        string[] observed = ProofActivation.DrainContinuations();
        if (state != BaseActivationState.Succeeded
            || !observed.SequenceEqual(
                ["yield:0", "yield:1", "yield:2", "yield:3", "yield:4", "yield:5", "yield:6"],
                StringComparer.Ordinal))
            throw new InvalidOperationException("The L76 durable-yield proof failed for " + provider + ".");
    }

    private static async Task ExecuteRegisteredReadsAsync(BaseSession session, string provider)
    {
        BaseCanonicalJson payload = BaseCanonicalJson.ParseAndValidate("{\"enabled\":true}"u8,
            new BaseCanonicalJsonLimits
            {
                MaximumCanonicalBytes = 128, MaximumDepth = 4, MaximumArrayItemsPerContainer = 8,
                MaximumObjectPropertiesPerContainer = 8, MaximumTotalNodes = 16,
                MaximumTotalStringUtf8Bytes = 64, MaximumTotalNameUtf8Bytes = 64,
            });
        _ = (await session.Collection(ProofJsonItem.Collection).CreateAsync(
            RecordId.Create("json-" + provider), new ProofJsonItem { Payload = payload })).RequireValue();
        _ = (await session.Collection(ProofCountAlpha.Collection).CreateAsync(
            RecordId.Create("alpha-1-" + provider), new ProofCountAlpha { Enabled = true })).RequireValue();
        _ = (await session.Collection(ProofCountAlpha.Collection).CreateAsync(
            RecordId.Create("alpha-2-" + provider), new ProofCountAlpha { Enabled = true })).RequireValue();
        _ = (await session.Collection(ProofCountBeta.Collection).CreateAsync(
            RecordId.Create("beta-1-" + provider), new ProofCountBeta { Enabled = true })).RequireValue();

        ProofJsonRead.Row[] json = (await session.Reads.ToArrayAsync(
            ProofJsonRead.Handle, new ProofJsonRead { Payload = payload })).RequireValue();
        ProofCountSummary.Row[] counts = (await session.Reads.ToArrayAsync(
            ProofCountSummary.Handle, new ProofCountSummary { Enabled = true })).RequireValue();
        ProofCountSummary.Row[] zeroCounts = (await session.Reads.ToArrayAsync(
            ProofCountSummary.Handle, new ProofCountSummary { Enabled = false })).RequireValue();
        if (json.Length != 1 || !json[0].Payload.Equals(payload)
            || !json[0].Payload.Utf8.Span.SequenceEqual("{\"enabled\":true}"u8)
            || counts.Length != 2 || counts[0] is not { Kind: "alpha", Count: 2 }
            || counts[1] is not { Kind: "beta", Count: 1 }
            || zeroCounts.Length != 2 || zeroCounts[0] is not { Kind: "alpha", Count: 0 }
            || zeroCounts[1] is not { Kind: "beta", Count: 0 })
            throw new InvalidOperationException("The L3B L62/L63 registered-read proof failed for " + provider + ".");
    }

    private static async Task ExecuteCrossMarkerStorageAsync(BaseSession session, string provider)
    {
        string firstId = "stored-first-" + provider;
        string secondId = "stored-second-" + provider;
        _ = (await session.Collection(ConsumerPrivateSubject.Collection).CreateAsync(
            RecordId.Create(firstId),
            new ConsumerPrivateSubject { Active = true, Tombstoned = false, Tenant = "tenant-a" })).RequireValue();
        _ = (await session.Collection(ConsumerOtherPrivateSubject.Collection).CreateAsync(
            RecordId.Create(secondId),
            new ConsumerOtherPrivateSubject { Active = true, Tombstoned = false, Tenant = "tenant-a" })).RequireValue();
        BaseSubjectReference<ConsumerSubject> first = (await session.Reads.ToArrayAsync(
            ConsumerSubjectAcquire.Handle,
            new ConsumerSubjectAcquire { SubjectId = BaseRecordId<ConsumerPrivateSubject>.Create(firstId) }))
            .RequireValue().Single().Reference;
        BaseSubjectReference<ConsumerOtherSubject> second = (await session.Reads.ToArrayAsync(
            ConsumerOtherSubjectAcquire.Handle,
            new ConsumerOtherSubjectAcquire { SubjectId = BaseRecordId<ConsumerOtherPrivateSubject>.Create(secondId) }))
            .RequireValue().Single().Reference;
        _ = (await session.Collection(ConsumerStoredSubject.Collection).CreateAsync(
            RecordId.Create("stored-pair-" + provider),
            new ConsumerStoredSubject { Reference = first, OtherReference = second })).RequireValue();
        ConsumerStoredSubjectRead.Row row = (await session.Reads.ToArrayAsync(
            ConsumerStoredSubjectRead.Handle, new ConsumerStoredSubjectRead())).RequireValue().Single();
        if (!row.Reference.Equals(first) || !row.OtherReference.Equals(second))
            throw new InvalidOperationException("The L3B cross-marker stored-reference proof failed for " + provider + ".");
    }

    private static async Task ExecuteGuardedL50ChildAsync(BaseSession session, string provider)
    {
        string targetId = "l50-child-" + provider;
        BaseRecordId<ProofOwner> owner = BaseRecordId<ProofOwner>.Create(
            Guid.Parse("00112233-4455-6677-8899-aabbccddeeff").ToString("D"));
        BaseCollectionSession<ProofWorkItem> work = session.Collection(ProofWorkItem.Collection);
        _ = (await work.CreateAsync(RecordId.Create(targetId),
            new ProofWorkItem { OwnerId = owner, Name = "before-child" })).RequireValue();
        BaseInstalledActivationHandle<ProofActivationInput, ProofActivationResult> activation =
            session.Activations.Get(ProofActivation.Registration.Identity);
        BaseInstalledActivationWorkerHandle<ProofActivationInput, ProofActivationResult> worker =
            session.Activations.GetWorker(ProofActivation.Registration.Identity);
        string command = "l50-child:" + targetId;
        OperationResult<BaseActivationEnqueueResult> enqueued = await activation.EnqueueAsync(
            new ProofActivationInput { Value = command },
            BaseMutationRequestIdentity.Create("proof-l50", "enqueue", provider,
                BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(provider + ":" + command)))));
        if (!enqueued.IsSuccess())
            throw new InvalidOperationException("The L3B guarded L50 enqueue failed: " + enqueued.Error?.Code);
        BaseActivationDispatchResult dispatched = await DispatchAsync(worker, enqueued.Value!.ActivationId);
        if (dispatched.State != BaseActivationState.Succeeded
            || (await work.GetAsync(RecordId.Create(targetId))).RequireValue().Value.Name != "activation-child")
            throw new InvalidOperationException("The L3B guarded L50 child proof failed for " + provider + ".");
    }

    private static async Task ExecuteGuardedSelectionAsync(BaseSession session, string provider)
    {
        BaseCollectionSession<ProofSelectionItem> work = session.Collection(ProofSelectionItem.Collection);
        for (int index = 0; index < 2; index++)
            _ = (await work.CreateAsync(RecordId.Create($"l43-{provider}-{index}"),
                new ProofSelectionItem { Name = "l43-positive" })).RequireValue();

        BaseInstalledActivationHandle<ProofActivationInput, ProofActivationResult> activation =
            session.Activations.Get(ProofActivation.Registration.Identity);
        BaseInstalledActivationWorkerHandle<ProofActivationInput, ProofActivationResult> worker =
            session.Activations.GetWorker(ProofActivation.Registration.Identity);
        foreach ((string cohort, string ordinal) in new[]
        {
            ("l43-positive", "positive"), ("l43-empty", "zero"),
        })
        {
            string command = "l43-delete:" + cohort;
            OperationResult<BaseActivationEnqueueResult> enqueued = await activation.EnqueueAsync(
                new ProofActivationInput { Value = command },
                BaseMutationRequestIdentity.Create("proof-l43", "enqueue", provider + ":" + ordinal,
                    BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(provider + ":" + command)))));
            if (!enqueued.IsSuccess())
                throw new InvalidOperationException("The L3B guarded L43 enqueue failed: " + enqueued.Error?.Code);
            BaseActivationDispatchResult dispatched = await DispatchAsync(worker, enqueued.Value!.ActivationId);
            if (dispatched.State != BaseActivationState.Succeeded)
                throw new InvalidOperationException("The L3B guarded L43 dispatch failed: "
                    + cohort + "/" + dispatched.State + "/" + dispatched.ActivationId + "/"
                    + ProofSelectionItem.Collection.Definition.MutationMode + "/"
                    + string.Join(',', ProofActivation.DrainContinuations()));
        }
        for (int index = 0; index < 2; index++)
            if (await work.GetAsync(RecordId.Create($"l43-{provider}-{index}")) is BaseSuccess<BaseRecord<ProofSelectionItem>>)
                throw new InvalidOperationException("The L3B guarded L43 positive cohort was not deleted.");
    }


    private static async Task ExecuteL30PatchFormsAsync(
        BaseSession session,
        string providerName)
    {
        BaseCollectionSession<ProofOwner> owners = session.Collection(ProofOwner.Collection);
        string assignmentId = "l30-assignment";
        string directRemovalId = "l30-direct-removal";
        string guardedRemovalId = "l30-guarded-removal";
        foreach (string id in new[] { assignmentId, directRemovalId, guardedRemovalId })
            _ = (await owners.CreateAsync(RecordId.Create(id), new ProofOwner { Name = "before", Note = "remove" })).RequireValue();

        BaseRecord<ProofOwner> assigned = (await owners.PatchAsync(
            RecordId.Create(assignmentId),
            new ProofOwnerPatch { Name = "after" },
            ProofOwnerJsonSerializerContext.Default.ProofOwnerPatch)).RequireValue();
        BaseRecord<ProofOwner> removed = (await owners.PatchRemovingAsync(
            RecordId.Create(directRemovalId),
            new ProofOwnerPatch(),
            ProofOwnerJsonSerializerContext.Default.ProofOwnerPatch,
            [ProofOwner.Fields.Note.Removal()])).RequireValue();
        if (assigned.Value is not { Name: "after", Note: "remove" } || removed.Value.Note is not null)
            throw new InvalidOperationException("The L3B direct L30 patch proof failed for " + providerName + ".");

        BaseInstalledActivationHandle<ProofActivationInput, ProofActivationResult> activation =
            session.Activations.Get(ProofActivation.Registration.Identity);
        BaseInstalledActivationWorkerHandle<ProofActivationInput, ProofActivationResult> worker =
            session.Activations.GetWorker(ProofActivation.Registration.Identity);
        string command = "l30-remove:" + guardedRemovalId;
        OperationResult<BaseActivationEnqueueResult> enqueued = await activation.EnqueueAsync(
            new ProofActivationInput { Value = command },
            BaseMutationRequestIdentity.Create("proof-l30", "enqueue", guardedRemovalId,
                BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(command)))));
        if (!enqueued.IsSuccess())
            throw new InvalidOperationException("The L3B guarded L30 enqueue failed: " + enqueued.Error?.Code);
        BaseActivationDispatchResult dispatched = await DispatchAsync(worker, enqueued.Value!.ActivationId);
        if (dispatched.State != BaseActivationState.Succeeded
            || (await owners.GetAsync(RecordId.Create(guardedRemovalId))).RequireValue().Value.Note is not null)
            throw new InvalidOperationException("The L3B activation-guarded L30 removal proof failed.");
    }

    private static async Task<BaseActivationDispatchResult> DispatchAsync(
        BaseInstalledActivationWorkerHandle<ProofActivationInput, ProofActivationResult> worker,
        string activationId)
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            OperationResult<BaseActivationDispatchResult> result = await worker.RunOneAsync();
            if (!result.IsSuccess() || result.Value is null)
                throw new InvalidOperationException("The L3B activation dispatch failed: " + result.Error?.Code);
            if (!result.Value.Empty && string.Equals(result.Value.ActivationId, activationId, StringComparison.Ordinal))
                return result.Value;
            if (!result.Value.Empty && result.Value.State is not BaseActivationState.Succeeded)
                throw new InvalidOperationException("An unrelated proving activation did not succeed: "
                    + result.Value.ActivationId + "/" + result.Value.State);
            await Task.Yield();
        }
        throw new InvalidOperationException("The expected proving activation did not become due.");
    }

    private static void Configure(HPDBaseBuilder builder, string? dataSource)
    {
        SemanticProof.EnableCompaction = dataSource is not null;
        builder.ConfigureSchema(options =>
        {
            options.ApplicationId = "hpd.auth.base.consumer-proof";
            options.PlanProtectionKey = System.Security.Cryptography.SHA256.HashData("proof-plan-key"u8);
        });
        builder.ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
        {
            Id = 1, Key = System.Security.Cryptography.SHA256.HashData("proof-token-key"u8),
            IssueNotBefore = DateTimeOffset.UnixEpoch,
        });
        builder.ConfigureSelectionMutations(new HPDBaseSelectionMutationOptions
        {
            HostMaxima = SelectionProof.Profile.Limits,
            MaximumReceiptIdentityBytes = 512,
            MaximumEvidenceTokenBytes = 512,
            MaximumRouteNameBytes = 96,
            MaximumRequestBodyBytes = 1_048_576,
        });
        if (dataSource is not null)
        {
            foreach (BaseCollection<JsonElement> collection in AuthorityCollections())
                builder.AddCollection(collection);
        }
        builder.AddPolicyAuthority(new BasePolicyAuthorityDefinition
        {
            Id = "proof.allow", Version = 1, OwningModuleId = "proof.module",
            EvaluatorContractId = "proof.allow.policy", EvaluatorContractVersion = 1,
            CompositionOrder = 0,
        }, new ProofAllowPolicyEvaluator());
        foreach (string grantId in new[]
        {
            "proof.identity-and-generation.execute", "proof.request-control.execute", "proof.static-set.execute",
            "proof.presence-and-removal.execute",
            "proof.lifecycle-subject.create.execute", "consumer.subject.source",
            SelectionProof.GrantId,
            "proof.owner.source", "proof.work.source",
            "proof.selection.source",
            ReadProofGrants.Json, ReadProofGrants.Count,
            "consumer.subject.acquire", "consumer.subject.validate", "consumer.subject.admin",
            "consumer.other-subject.acquire", "consumer.other-subject.validate", "consumer.other-subject.admin",
            "consumer.subject.read",
            LifecycleProof.DeliveryGrant, LifecycleProof.AcknowledgementGrant,
            ProofActivation.ScheduleManageGrant, ProofActivation.ScheduleMaterializeGrant,
            "base.subjectLifecycle.tombstone", "base.subjectLifecycle.feed.read",
            "base.subjectLifecycle.feed.checkpoint", "base.subjectRetirement.acknowledge",
            "base.subjectRetirement.purge", "consumer.subject.retirement.purge.source",
            "proof.semantic.ensure.execute", "proof.semantic.retire.execute",
            SemanticProof.EnsureGrant, SemanticProof.RetireGrant, SemanticProof.MaintainGrant,
            SemanticProof.LifecycleRetirementGrant,
        }.Concat(ProofActivation.GrantIds).Concat(ProofYieldActivation.GrantIds))
        {
            builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
            {
                Id = grantId, Version = 1,
                OwningModuleId = grantId is "proof.lifecycle-subject.create.execute" or "consumer.subject.source"
                    ? "consumer.module"
                    : "proof.module",
                SourceContractId = "proof.static-grants", SourceContractVersion = 1,
            }, Grant(grantId));
        }
        builder.AddCollection(ProofOwner.Collection);
        builder.AddCollection(ProofWorkItem.Collection);
        builder.AddCollection(ProofSelectionItem.Collection);
        builder.AddCollection(ConsumerPrivateSubject.Collection);
        builder.AddCollection(ConsumerOtherPrivateSubject.Collection);
        builder.AddCollection(ConsumerStoredSubject.Collection);
        builder.AddCollection(ProofJsonItem.Collection);
        builder.AddCollection(ProofCountAlpha.Collection);
        builder.AddCollection(ProofCountBeta.Collection);
        builder.AddExportedSubject(ConsumerSubject.HPDBaseSubjectRegistration);
        builder.AddExportedSubject(ConsumerOtherSubject.HPDBaseSubjectRegistration);
        builder.AddSubjectLifecycleConsumer(LifecycleProof.LifecycleIdentity);
        builder.AddSubjectRetirementConsumer(LifecycleProof.RetirementIdentity);
        builder.AddSubjectRetirementPolicy(LifecycleProof.Policy);
        builder.AddRead(ConsumerSubjectAcquire.Definition);
        builder.AddRead(ConsumerOtherSubjectAcquire.Definition);
        builder.AddRead(ConsumerStoredSubjectRead.Definition);
        builder.AddRead(ProofJsonRead.Definition);
        builder.AddRead(ProofCountSummary.Definition);
        builder.AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
        {
            Id = "consumer.subject.acquire.v1",
            Version = 1,
            ContractId = "consumer.subject",
            ContractVersion = 1,
            RegisteredReadId = ConsumerSubjectAcquire.Definition.Id,
            RequiredGrantId = "consumer.subject.acquire",
            Audience = HPDBaseEndpointAudience.Application,
            MaximumResults = 1,
        });
        builder.AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
        {
            Id = "consumer.other-subject.acquire.v1",
            Version = 1,
            ContractId = "consumer.other-subject",
            ContractVersion = 1,
            RegisteredReadId = ConsumerOtherSubjectAcquire.Definition.Id,
            RequiredGrantId = "consumer.other-subject.acquire",
            Audience = HPDBaseEndpointAudience.Application,
            MaximumResults = 1,
        });
        builder.AddSelectionOperationProfile(SelectionProof.Profile);
        builder.AddModuleGenerationCell(IdentityAndGenerationProof.GenerationCell);
        builder.AddModuleMutation(IdentityAndGenerationProof.Definition, IdentityAndGenerationProof.Identity);
        builder.AddModuleMutation(RequestControlProof.Definition, RequestControlProof.Identity);
        builder.AddModuleMutation(StaticSetProof.Definition, StaticSetProof.Identity);
        builder.AddModuleMutation(PresenceAndRemovalProof.Definition, PresenceAndRemovalProof.Identity);
        builder.AddModuleMutation(LifecycleModuleMutationProof.Definition, LifecycleModuleMutationProof.Identity);
        builder.AddActivation(ProofActivation.Registration);
        builder.AddActivation(ProofYieldActivation.Registration);
        builder.AddSchedule(ProofActivation.Schedule);
        builder.AddModuleMutation(SemanticEnsureProof.Definition, SemanticEnsureProof.Identity);
        builder.AddModuleMutation(SemanticRetireProof.Definition, SemanticRetireProof.Identity);
        builder.AddSemanticActivation(SemanticProof.Registration);
        builder.SetSemanticActivationRestoreSelection(new BaseSemanticActivationRestoreSelection
        {
            LogicalStoreId = dataSource is null ? "inmemory" : "proof.sqlite",
            EnabledRestoreMode = dataSource is null ? null : BaseActivationRestoreMode.InPlaceRecovery,
            SelectionGeneration = 1,
            Identity = BaseMutationRequestIdentity.Create("proof", "semantic-restore", "selection-1",
                BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                    "proof.semantic.restore-selection"u8))),
            Checksum = [],
        });
        if (dataSource is not null)
        {
            builder.UseStore(SqliteStore.Configure(options =>
            {
                options.StoreId = "proof.sqlite";
                options.DataSource = dataSource;
                options.AdministrationEnabled = true;
            }));
        }
    }

    private static async Task ExecuteIdentityAndGenerationAsync(BaseSession session, string provider)
    {
        Guid ownerId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        Guid patchId = Guid.Parse("10112233-4455-6677-8899-aabbccddeeff");
        Guid createId = Guid.Parse("20112233-4455-6677-8899-aabbccddeeff");
        Guid deleteId = Guid.Parse("30112233-4455-6677-8899-aabbccddeeff");
        BaseCollectionSession<ProofOwner> owners = session.Collection(ProofOwner.Collection);
        BaseCollectionSession<ProofWorkItem> work = session.Collection(ProofWorkItem.Collection);
        _ = (await owners.CreateAsync(RecordId.Create(ownerId.ToString("D")),
            new ProofOwner { Name = "owner", Note = "remove-me" })).RequireValue();
        var ownerReference = BaseRecordId<ProofOwner>.Create(ownerId.ToString("D"));
        _ = (await work.CreateAsync(RecordId.Create(patchId.ToString("D")),
            new ProofWorkItem { OwnerId = ownerReference, Name = "before" })).RequireValue();
        _ = (await work.CreateAsync(RecordId.Create(deleteId.ToString("D")),
            new ProofWorkItem { OwnerId = ownerReference, Name = "delete" })).RequireValue();

        IdentityAndGenerationRequest request = new()
        {
            CreateId = createId, PatchId = patchId.ToString("D"), DeleteId = deleteId,
            OwnerId = ownerId, GenerationKey = ownerId, Name = "after",
        };
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "proof", "identity-and-generation", provider,
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("proof.identity-and-generation." + provider))));
        BaseInstalledModuleMutationHandle<IdentityAndGenerationRequest, IdentityAndGenerationResult> operation =
            session.ModuleMutations.Get(IdentityAndGenerationProof.Identity);
        BaseResult<BaseModuleMutationExecutionResult<IdentityAndGenerationResult>> commitResult =
            await operation.ExecuteAsync(request, identity);
        if (commitResult is BaseFailure<BaseModuleMutationExecutionResult<IdentityAndGenerationResult>> failure)
            throw new InvalidOperationException($"The L3B identity operation failed for {provider}: {failure.Status}/{failure.Error.Code}/{failure.Error.Message}/{failure.Error.Detail}");
        BaseModuleMutationExecutionResult<IdentityAndGenerationResult> committed = commitResult.RequireValue();
        BaseModuleMutationExecutionResult<IdentityAndGenerationResult> duplicate =
            (await operation.ExecuteAsync(request, identity)).RequireValue();

        if (committed.Disposition != BaseMutationRequestDisposition.Committed
            || duplicate.Disposition != BaseMutationRequestDisposition.Duplicate
            || committed.Result.Generation.ToCanonicalString() != "1"
            || committed.Result.CreatedId != createId
            || (await work.GetAsync(RecordId.Create(createId.ToString("D")))).RequireValue().Value.OwnerId != ownerReference
            || (await work.GetAsync(RecordId.Create(patchId.ToString("D")))).RequireValue().Value.Name != "after"
            || await work.GetAsync(RecordId.Create(deleteId.ToString("D"))) is BaseSuccess<BaseRecord<ProofWorkItem>>)
            throw new InvalidOperationException("The L3B identity-and-generation proof failed for " + provider + ".");

    }

    private static async Task ExecuteRequestControlAsync(BaseSession session, string provider)
    {
        Guid ownerId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        string targetId = Guid.Parse("20112233-4455-6677-8899-aabbccddeeff").ToString("D");
        RequestControlRequest request = new()
        {
            Accepted = true, EnableHostile = false, HostileId = " ", Left = 1, Right = 2,
            Name = "controlled", OptionalNote = null, OwnerId = ownerId, TargetId = targetId,
        };
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "proof", "request-control", provider,
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("proof.request-control." + provider))));
        BaseInstalledModuleMutationHandle<RequestControlRequest, RequestControlResult> operation =
            session.ModuleMutations.Get(RequestControlProof.Identity);
        BaseModuleMutationExecutionResult<RequestControlResult> committed =
            (await operation.ExecuteAsync(request, identity)).RequireValue();
        if (!committed.Result.Accepted || committed.Disposition != BaseMutationRequestDisposition.Committed
            || (await session.Collection(ProofWorkItem.Collection).GetAsync(RecordId.Create(targetId))).RequireValue().Value.Name != "controlled")
            throw new InvalidOperationException("The L3B request-control proof failed for " + provider + ".");

        RequestControlRequest hostile = request with { EnableHostile = true };
        BaseResult<BaseModuleMutationExecutionResult<RequestControlResult>> hostileResult =
            await operation.ExecuteAsync(hostile, BaseMutationRequestIdentity.Create(
                "proof", "request-control-hostile", provider,
                BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("proof.request-control-hostile." + provider)))));
        if (hostileResult is not BaseFailure<BaseModuleMutationExecutionResult<RequestControlResult>>
            { Status: OperationStatus.ValidationFailed })
            throw new InvalidOperationException("The L3B enabled hostile capture was not rejected for " + provider + ".");
    }

    private static async Task ExecuteStaticSetAsync(BaseSession session, string provider)
    {
        Guid ownerId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        BaseCollectionSession<ProofWorkItem> work = session.Collection(ProofWorkItem.Collection);
        BaseRecordId<ProofOwner> ownerReference = BaseRecordId<ProofOwner>.Create(ownerId.ToString("D"));
        for (int index = 0; index < StaticSetProof.CohortSize; index++)
        {
            _ = (await work.CreateAsync(RecordId.Create($"prior-{index:D3}"),
                new ProofWorkItem { OwnerId = ownerReference, Name = $"prior-{index:D3}" })).RequireValue();
        }
        StaticSetRequest request = new()
        {
            NewCount = StaticSetProof.CohortSize, PriorCount = StaticSetProof.CohortSize,
            OwnerId = ownerId, StaticAuthority = "authority",
        };
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "proof", "static-set", provider,
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("proof.static-set." + provider))));
        BaseInstalledModuleMutationHandle<StaticSetRequest, StaticSetResult> operation =
            session.ModuleMutations.Get(StaticSetProof.Identity);
        BaseModuleMutationExecutionResult<StaticSetResult> committed =
            (await operation.ExecuteAsync(request, identity)).RequireValue();
        BaseModuleMutationExecutionResult<StaticSetResult> duplicate =
            (await operation.ExecuteAsync(request, identity)).RequireValue();
        if (committed.Result.NewCount != 64 || committed.Result.PriorCount != 64
            || committed.Disposition != BaseMutationRequestDisposition.Committed
            || duplicate.Disposition != BaseMutationRequestDisposition.Duplicate)
            throw new InvalidOperationException("The L3B static-set result failed for " + provider + ".");
        BaseResult<BaseModuleMutationExecutionResult<StaticSetResult>> oversized =
            await operation.ExecuteAsync(request with { NewCount = StaticSetProof.CohortSize + 1 },
                BaseMutationRequestIdentity.Create(
                    "proof", "static-set-oversized", provider,
                    BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes("proof.static-set-oversized." + provider)))));
        if (oversized is not BaseFailure<BaseModuleMutationExecutionResult<StaticSetResult>>
            { Status: OperationStatus.ValidationFailed })
            throw new InvalidOperationException("The L3B maximum-plus-one static set was not rejected for " + provider + ".");
        for (int index = 0; index < StaticSetProof.CohortSize; index++)
        {
            if (await work.GetAsync(RecordId.Create($"prior-{index:D3}")) is BaseSuccess<BaseRecord<ProofWorkItem>>
                || await work.GetAsync(RecordId.Create($"new-{index:D3}")) is not BaseSuccess<BaseRecord<ProofWorkItem>>)
                throw new InvalidOperationException("The L3B static-set cohort failed for " + provider + ".");
        }
    }

    private static async Task ExecutePresenceAndRemovalAsync(BaseSession session, string provider)
    {
        string recordId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff").ToString("D");
        BaseCollectionSession<ProofOwner> owners = session.Collection(ProofOwner.Collection);
        var request = new PresenceAndRemovalRequest
        {
            RecordId = recordId, Value = "present",
        };
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "proof", "presence-and-removal", provider,
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("proof.presence-and-removal." + provider))));
        BaseInstalledModuleMutationHandle<PresenceAndRemovalRequest, PresenceAndRemovalResult> operation =
            session.ModuleMutations.Get(PresenceAndRemovalProof.Identity);
        BaseModuleMutationExecutionResult<PresenceAndRemovalResult> committed =
            (await operation.ExecuteAsync(request, identity)).RequireValue();
        BaseModuleMutationExecutionResult<PresenceAndRemovalResult> duplicate =
            (await operation.ExecuteAsync(request, identity)).RequireValue();
        BaseRecord<ProofOwner> stored = (await owners.GetAsync(RecordId.Create(recordId))).RequireValue();
        if (committed.Disposition != BaseMutationRequestDisposition.Committed
            || duplicate.Disposition != BaseMutationRequestDisposition.Duplicate
            || committed.Result.Missing is not null || committed.Result.Null is not null
            || committed.Result.Value != "present" || stored.Value.Note is not null)
            throw new InvalidOperationException("The L3B presence-and-removal proof failed for " + provider + ".");
    }

    private static async Task ExecuteSemanticLifecycleAsync(BaseSession session,
        IHPDBaseAdministration administration, IBaseSessionFactory sessions,
        string storeId, string provider, ProofTimeProvider clock)
    {
        _ = SemanticProofObservations.Drain();
        string subjectId = "semantic-subject-" + provider;
        BaseCollectionSession<ConsumerPrivateSubject> subjects = session.Collection(ConsumerPrivateSubject.Collection);
        BaseRecord<ConsumerPrivateSubject> created = (await subjects.CreateAsync(RecordId.Create(subjectId), new ConsumerPrivateSubject
        {
            Active = true, Tombstoned = false, Tenant = "tenant-a",
        })).RequireValue();
        BaseSubjectReference<ConsumerSubject> subject = (await session.Reads.ToArrayAsync(
            ConsumerSubjectAcquire.Handle,
            new ConsumerSubjectAcquire
            {
                SubjectId = BaseRecordId<ConsumerPrivateSubject>.Create(subjectId),
            })).RequireValue().Single().Reference;
        SemanticProofRequests.Retain(subjectId, subject);
        BaseInstalledActivationHandle<ProofActivationInput, ProofActivationResult> activation =
            session.Activations.Get(ProofActivation.Registration.Identity);
        BaseInstalledActivationWorkerHandle<ProofActivationInput, ProofActivationResult> worker =
            session.Activations.GetWorker(ProofActivation.Registration.Identity);

        async ValueTask<string> Enqueue(string value, string ordinal)
        {
            OperationResult<BaseActivationEnqueueResult> result = await activation.EnqueueAsync(
                new ProofActivationInput { Value = value },
                BaseMutationRequestIdentity.Create("proof-semantic", "enqueue", provider + ":" + ordinal,
                    BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(provider + ":" + value + ":" + ordinal)))));
            if (!result.IsSuccess())
                throw new InvalidOperationException("The L3B semantic enqueue failed: " + result.Error?.Code);
            return result.Value!.ActivationId;
        }

        async ValueTask<object[]> RetireAndResolveAsync()
        {
            string retirementId = await Enqueue("retire:" + subjectId, "retire-1");
            BaseActivationDispatchResult retired = await DispatchAsync(worker, retirementId);
            if (retired.State != BaseActivationState.Succeeded)
            {
                object[] failures = SemanticProofObservations.Drain();
                throw new InvalidOperationException("The L3B semantic retirement failed for " + provider
                    + ": " + retired.State + ":" + string.Join(',', failures.OfType<BaseError>()
                        .Select(value => value.Code)));
            }
            string retiredEnsure = await Enqueue("ensure:" + subjectId, "ensure-retired");
            BaseActivationDispatchResult resolvedRetired = await DispatchAsync(worker, retiredEnsure);
            if (resolvedRetired.State != BaseActivationState.Succeeded)
                throw new InvalidOperationException("The L3B retired semantic resolution failed for " + provider + ".");
            return SemanticProofObservations.Drain();
        }

        string firstEnsure = await Enqueue("ensure:" + subjectId, "ensure-1");
        BaseActivationDispatchResult first = await DispatchAsync(worker, firstEnsure);
        object[] firstObservations = SemanticProofObservations.Drain();
        SemanticEnsureProofResult firstEnsureResult = firstObservations
            .OfType<SemanticEnsureProofResult>().Single();
        if (first.State != BaseActivationState.Succeeded || firstEnsureResult.ActivationId is null)
            throw new InvalidOperationException("The L3B initial semantic result was incomplete.");
        BaseActivationDispatchResult semanticChildDispatch = await DispatchAsync(
            worker, firstEnsureResult.ActivationId);
        if (semanticChildDispatch.State != BaseActivationState.Succeeded)
            throw new InvalidOperationException("The L3B semantic child did not become terminal for " + provider + ".");

        string secondEnsure = await Enqueue("ensure:" + subjectId, "ensure-2");
        BaseActivationDispatchResult second = await DispatchAsync(worker, secondEnsure);
        if (second.State != BaseActivationState.Succeeded)
            throw new InvalidOperationException("The L3B semantic ensure dispatch failed for " + provider + ".");
        object[] initialObservations = [.. firstObservations, .. SemanticProofObservations.Drain()];
        SemanticEnsureProofResult[] initialEnsures = initialObservations.OfType<SemanticEnsureProofResult>().ToArray();
        if (initialEnsures.Length != 2)
            throw new InvalidOperationException("The L3B initial semantic results were incomplete.");

        var adminPrincipal = new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Admin,
            SubjectKind = AccessSubjectKind.Admin,
            SubjectId = "proof-admin",
            CurrentTenantId = "tenant-a",
        };
        BaseSession adminSession = sessions.For(adminPrincipal, options =>
        {
            options.Audience = HPDBaseEndpointAudience.ControlPlane;
            options.Mode = OperationMode.System;
        });
        var scope = new BaseOwnedSubjectScopeEvidence
        {
            Kind = BaseSubjectScopeKind.Tenant,
            Value = "tenant-a",
        };
        BaseActivationAdministrationItem? semanticChild = null;
        if (provider == "sqlite")
        {
            BaseActivationAdministrationBoundary? after = null;
            do
            {
                BaseResult<BaseActivationAdministrationPage> pageResult = await administration.ReadActivationsAsync(
                    new BaseActivationAdministrationReadRequest
                    {
                        StoreId = storeId, Principal = adminPrincipal, Scope = scope,
                        DefinitionId = ProofActivation.Registration.Definition.Id,
                        DefinitionVersion = ProofActivation.Registration.Definition.Version,
                        States = BaseActivationStateSelector.Terminal, Take = 8, After = after,
                    });
                if (pageResult is not BaseSuccess<BaseActivationAdministrationPage> page)
                    throw new InvalidOperationException("The L53 terminal activation read failed: "
                        + ((BaseFailure<BaseActivationAdministrationPage>)pageResult).Error.Code);
                semanticChild = page.Value.Items.FirstOrDefault(value =>
                    string.Equals(value.ActivationId, initialEnsures[0].ActivationId, StringComparison.Ordinal));
                after = page.Value.Next;
            }
            while (semanticChild is null && after is not null);
            if (semanticChild is null)
                throw new InvalidOperationException("The L53 semantic child was not retained.");
            BaseResult<BaseActivationTransitionResult> disposed = await administration.DisposeActivationAsync(
                new BaseActivationAdministrationDisposeRequest
                {
                    StoreId = storeId, Principal = adminPrincipal,
                    DefinitionId = semanticChild.Definition.Id,
                    DefinitionVersion = semanticChild.Definition.Version,
                    ActivationId = semanticChild.ActivationId,
                    ExpectedGeneration = semanticChild.Generation,
                    Identity = Identity("semantic-dispose", provider),
                });
            if (disposed is not BaseSuccess<BaseActivationTransitionResult>)
                throw new InvalidOperationException("The L53 semantic child disposal failed: "
                    + ((BaseFailure<BaseActivationTransitionResult>)disposed).Error.Code);
        }
        object[] observations = [.. initialObservations, .. await RetireAndResolveAsync()];
        ValidateSemanticResults(observations, provider);
        SemanticEnsureProofResult[] ensured = observations.OfType<SemanticEnsureProofResult>().ToArray();

        BaseResult<BaseSubjectTombstoneResult<ConsumerSubject>> tombstoned = await session
            .GetExportedSubjectContract<ConsumerSubject>(ConsumerSubject.HPDBaseSubjectRegistration)
            .TombstoneAsync(new BaseSubjectTombstoneRequest<ConsumerSubject>
            {
                Subject = subject,
                ExpectedPrivateRevision = created.Revision!.Value,
                Identity = Identity("semantic-tombstone", provider),
            });
        if (tombstoned is not BaseSuccess<BaseSubjectTombstoneResult<ConsumerSubject>> tombstone)
            throw new InvalidOperationException("The L53 semantic subject tombstone failed: "
                + ((BaseFailure<BaseSubjectTombstoneResult<ConsumerSubject>>)tombstoned).Error.Code);
        _ = LifecycleProof.Drain();
        string lifecycleId = await Enqueue("lifecycle", "semantic-lifecycle");
        BaseActivationDispatchResult lifecycle = await DispatchAsync(worker, lifecycleId);
        LifecycleProof.Observation[] lifecycleObservations = LifecycleProof.Drain();
        LifecycleProof.Observation acknowledgement = lifecycleObservations.FirstOrDefault(value =>
            value.Acknowledgement.Outcome == BaseSubjectRetirementMutationOutcome.Applied)
            ?? throw new InvalidOperationException("The L53 retirement acknowledgement was not observed.");
        if (lifecycle.State != BaseActivationState.Succeeded
            || acknowledgement.Acknowledgement.BarrierState != BaseSubjectRetirementBarrierState.Satisfied
            || acknowledgement.Acknowledgement.BarrierGeneration is not long barrierGeneration
            || acknowledgement.Acknowledgement.BarrierChecksum is not string barrierChecksum)
            throw new InvalidOperationException("The L53 retirement barrier was not satisfied.");

        BaseRecord<ConsumerPrivateSubject> privateTombstone = (await subjects.GetAsync(
            RecordId.Create(subjectId))).RequireValue();
        BaseResult<BaseSubjectFinalPurgeResult> purged = await adminSession.SubjectRetirements.PurgeAsync(
            new BaseSubjectFinalPurgeRequest
            {
                ContractId = "consumer.subject", ContractVersion = 1,
                SubjectId = subject.SubjectId, AuthorityEpoch = subject.AuthorityEpoch,
                Incarnation = subject.Incarnation,
                ExpectedTombstoneSequence = tombstone.Value.Fact.Fact.SubjectSequence,
                ExpectedPrivateRevision = privateTombstone.Revision!.Value,
                ExpectedBarrierGeneration = barrierGeneration,
                ExpectedBarrierChecksum = barrierChecksum,
                Identity = Identity("semantic-purge", provider),
            });
        if (purged is not BaseSuccess<BaseSubjectFinalPurgeResult>)
            throw new InvalidOperationException("The L53 final subject purge failed: "
                + ((BaseFailure<BaseSubjectFinalPurgeResult>)purged).Error.Code + ":"
                + ((BaseFailure<BaseSubjectFinalPurgeResult>)purged).Error.Message);
        _ = LifecycleProof.Drain();
        BaseActivationDispatchResult retiredLifecycle = await DispatchAsync(worker,
            await Enqueue("lifecycle-checkpoint", "semantic-retired-lifecycle"));
        string[] retiredErrors = LifecycleProof.DrainErrors();
        if (retiredLifecycle.State != BaseActivationState.Succeeded || retiredErrors.Length != 0)
            throw new InvalidOperationException("The L53 retired lifecycle projection did not advance: "
                + retiredLifecycle.State + ":" + string.Join(',', retiredErrors));
        if (provider != "sqlite") return;

        clock.Advance(TimeSpan.FromHours(25));
        BaseActivationReceiptCompactionCursor? receiptCursor = null;
        int receiptPage = 0;
        do
        {
            BaseResult<BaseActivationReceiptCompactionResult> receiptCompactionResult =
                await administration.CompactActivationReceiptsAsync(
                    new BaseActivationAdministrationReceiptCompactionRequest
                    {
                        StoreId = storeId, Principal = adminPrincipal, Scope = scope,
                        DefinitionId = ProofActivation.Registration.Definition.Id,
                        DefinitionVersion = ProofActivation.Registration.Definition.Version,
                        AfterActivationId = receiptCursor?.ActivationId,
                        AfterReceiptSequence = receiptCursor?.ReceiptSequence,
                        Take = ProofActivation.Registration.Definition.Limits.Provider.MaximumCandidates,
                        Identity = Identity($"semantic-receipt-compact-{receiptPage++}", provider),
                    });
            if (receiptCompactionResult is not BaseSuccess<BaseActivationReceiptCompactionResult> page)
                throw new InvalidOperationException("The L76 semantic receipt compaction failed: "
                    + ((BaseFailure<BaseActivationReceiptCompactionResult>)receiptCompactionResult).Error.Code);
            receiptCursor = page.Value.Next;
        }
        while (receiptCursor is not null);

        BaseResult<BaseActivationPrunePage> pruned = await administration.PruneActivationsAsync(
            new BaseActivationAdministrationPruneRequest
            {
                StoreId = storeId, Principal = adminPrincipal, Scope = scope,
                DefinitionId = ProofActivation.Registration.Definition.Id,
                DefinitionVersion = ProofActivation.Registration.Definition.Version,
                Take = 8, Identity = Identity("semantic-prune", provider),
            });
        if (pruned is not BaseSuccess<BaseActivationPrunePage> prune)
            throw new InvalidOperationException("The L53 semantic child prune failed: "
                + ((BaseFailure<BaseActivationPrunePage>)pruned).Error.Code);
        if (!prune.Value.Items.Any(value => value.ActivationId == semanticChild!.ActivationId))
            throw new InvalidOperationException("The L53 semantic child prune evidence was not created: "
                + string.Join(',', prune.Value.Items.Select(value => value.ActivationId)));

        BaseResult<BaseSemanticActivationControlDescriptor> descriptorResult =
            await administration.ReadSemanticActivationControlAsync(storeId, adminPrincipal,
                new BaseSemanticActivationDefinitionKey
                {
                    Id = SemanticProof.Definition.Id, Version = SemanticProof.Definition.Version,
                    Checksum = SemanticProof.Definition.Checksum,
                });
        if (descriptorResult is not BaseSuccess<BaseSemanticActivationControlDescriptor> descriptor)
            throw new InvalidOperationException("The L53 semantic control read failed: "
                + ((BaseFailure<BaseSemanticActivationControlDescriptor>)descriptorResult).Error.Code);
        BaseSemanticActivationControlToken token = descriptor.Value.Compact
            ?? throw new InvalidOperationException("The L53 compact control was unavailable.");
        var compactCommand = new BaseSemanticActivationControlCommand
        {
            Token = token, IdempotencyKey = "semantic-compact-" + provider,
            Confirmation = "compact-retired-semantic-authority",
        };
        BaseResult<BaseSemanticActivationControlResult> compactResult =
            await administration.ExecuteSemanticActivationControlAsync(
                storeId, adminPrincipal, compactCommand);
        if (compactResult is not BaseSuccess<BaseSemanticActivationControlResult> compactSuccess)
            throw new InvalidOperationException("The L53 semantic compact command failed: "
                + ((BaseFailure<BaseSemanticActivationControlResult>)compactResult).Error.Code);
        BaseSemanticActivationControlResult compacted = compactSuccess.Value;
        while (compacted.Resume is not null)
        {
            compacted = (await administration.ExecuteSemanticActivationControlAsync(storeId, adminPrincipal,
                new BaseSemanticActivationControlCommand
                {
                    Token = compacted.Resume, IdempotencyKey = "semantic-compact-" + provider,
                    Confirmation = "resume-semantic-maintenance",
                })).RequireValue();
        }
        if (compacted.Disposition != BaseSemanticActivationMaintenanceDisposition.Completed
            || compacted.AuthorityGeneration != 2
            || compacted.ExaminedRows != 1
            || compacted.ChangedRows != 1
            || compacted.CanonicalBytes is < 1 or > 1_048_576
            || compacted.ReceiptDisposition != BaseMutationRequestDisposition.Committed
            || compacted.SanitizedChecksum.Length != 32)
            throw new InvalidOperationException("The L53 semantic compaction did not complete: "
                + compacted.Disposition + "/generation=" + compacted.AuthorityGeneration
                + "/examined=" + compacted.ExaminedRows + "/changed=" + compacted.ChangedRows
                + "/bytes=" + compacted.CanonicalBytes + "/receipt=" + compacted.ReceiptDisposition
                + "/checksum=" + compacted.SanitizedChecksum.Length + ".");
        BaseSemanticActivationControlResult replayedCompaction =
            (await administration.ExecuteSemanticActivationControlAsync(
                storeId, adminPrincipal, compactCommand)).RequireValue();
        if (replayedCompaction.Disposition != BaseSemanticActivationMaintenanceDisposition.Duplicate
            || replayedCompaction.AuthorityGeneration != compacted.AuthorityGeneration
            || replayedCompaction.ExaminedRows != compacted.ExaminedRows
            || replayedCompaction.ChangedRows != compacted.ChangedRows
            || replayedCompaction.CanonicalBytes != compacted.CanonicalBytes
            || replayedCompaction.ReceiptDisposition != BaseMutationRequestDisposition.Duplicate)
            throw new InvalidOperationException("The L53 semantic compaction response-loss replay failed: "
                + $"disposition={replayedCompaction.Disposition}; authority={replayedCompaction.AuthorityGeneration}; "
                + $"rows={replayedCompaction.ExaminedRows}/{replayedCompaction.ChangedRows}; bytes={replayedCompaction.CanonicalBytes}; "
                + $"receipt={replayedCompaction.ReceiptDisposition}.");

        _ = SemanticProofObservations.Drain();
        BaseActivationDispatchResult absentEnsure = await DispatchAsync(worker,
            await Enqueue("ensure:" + subjectId, "ensure-compacted"));
        BaseActivationDispatchResult alreadyCompacted = await DispatchAsync(worker,
            await Enqueue("retire:" + subjectId, "retire-compacted"));
        object[] compactedObservations = SemanticProofObservations.Drain();
        if (absentEnsure.State != BaseActivationState.Succeeded
            || alreadyCompacted.State != BaseActivationState.Succeeded
            || compactedObservations.OfType<SemanticEnsureProofResult>().Single().Disposition
                != BaseSemanticActivationEnsureDisposition.Retired
            || compactedObservations.OfType<SemanticEnsureProofResult>().Single().WasMaterialized
            || compactedObservations.OfType<SemanticRetireProofResult>().Single().Disposition
                != BaseSemanticActivationRetirementDisposition.AlreadyCompacted)
            throw new InvalidOperationException("The L53 compacted absence/rematerialization proof failed: "
                + $"ensureState={absentEnsure.State}; retireState={alreadyCompacted.State}; "
                + string.Join(';', compactedObservations.Select(static value => value.ToString())));
    }

    private static BaseMutationRequestIdentity Identity(string operation, string provider) =>
        BaseMutationRequestIdentity.Create("proof-l53", operation, provider,
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("proof-l53:" + operation + ":" + provider))));

    private static void ValidateSemanticResults(object[] observations, string provider)
    {
        SemanticEnsureProofResult[] ensured = observations.OfType<SemanticEnsureProofResult>().ToArray();
        SemanticRetireProofResult[] retirements = observations.OfType<SemanticRetireProofResult>().ToArray();
        if (ensured.Length != 3 || retirements.Length != 1
            || ensured[0] is not { Disposition: BaseSemanticActivationEnsureDisposition.Created,
                WasMaterialized: true, ActivationId: not null }
            || ensured[1] is not { Disposition: BaseSemanticActivationEnsureDisposition.Existing,
                WasMaterialized: false, ActivationId: not null }
            || ensured[2] is not { Disposition: BaseSemanticActivationEnsureDisposition.Retired,
                WasMaterialized: false, ActivationId: null }
            || retirements[0].Disposition != BaseSemanticActivationRetirementDisposition.RetiredNow
            || ensured.Any(value => value.IncarnationBytes.ToArray().Length != 24))
            throw new InvalidOperationException("The L3B typed semantic result proof failed for " + provider + ".");
    }

    private static AccessGrant Grant(string id)
    {
        bool controlPlane = id is "proof.activation.inspect" or "proof.activation.dispose" or "proof.activation.remove"
                or "base.subjectRetirement.purge" or "consumer.subject.retirement.purge.source";
        bool adminSubject = controlPlane || id == SemanticProof.MaintainGrant;
        return new()
    {
        Id = id, ApplicationId = id == SemanticProof.MaintainGrant ? null : "hpd.auth.base.consumer-proof",
        ModuleId = id is "consumer.subject.acquire" or "consumer.subject.validate" or "consumer.subject.admin"
            or "consumer.other-subject.acquire" or "consumer.other-subject.validate" or "consumer.other-subject.admin"
            or "proof.lifecycle-subject.create.execute" or "consumer.subject.source"
            or "base.subjectLifecycle.tombstone" or "base.subjectRetirement.purge"
            or "consumer.subject.retirement.purge.source" ? "consumer.module" : "proof.module",
        Audience = controlPlane ? HPDBaseEndpointAudience.ControlPlane : HPDBaseEndpointAudience.Application,
        Subject = new AccessSubject
        {
            Kind = adminSubject ? AccessSubjectKind.Admin : AccessSubjectKind.ServicePrincipal,
            Id = adminSubject ? "proof-admin" : "proof-service", TenantId = "tenant-a",
        },
        Action = id switch
        {
            "proof.identity-and-generation.execute" => "proof.identity-and-generation.v1",
            "proof.request-control.execute" => "proof.request-control.v1",
            "proof.static-set.execute" => "proof.static-set.v1",
            "proof.presence-and-removal.execute" => "proof.presence-and-removal.v1",
            "proof.lifecycle-subject.create.execute" => "proof.lifecycle-subject.create.v1",
            "proof.semantic.ensure.execute" => "proof.semantic.ensure.v1",
            "proof.semantic.retire.execute" => "proof.semantic.retire.v1",
            SemanticProof.EnsureGrant or SemanticProof.RetireGrant or SemanticProof.MaintainGrant => SemanticProof.DefinitionId,
            SemanticProof.LifecycleRetirementGrant => "consumer.subject",
            var activationGrant when ProofActivation.GrantIds.Contains(activationGrant, StringComparer.Ordinal) =>
                ProofActivation.Registration.Definition.Id,
            var yieldGrant when ProofYieldActivation.GrantIds.Contains(yieldGrant, StringComparer.Ordinal) =>
                ProofYieldActivation.Registration.Definition.Id,
            ProofActivation.ScheduleManageGrant or ProofActivation.ScheduleMaterializeGrant =>
                ProofActivation.Schedule.Definition.Id,
            "proof.owner.source" => ProofOwner.Collection.Id,
            "proof.work.source" => ProofWorkItem.Collection.Id,
            "consumer.subject.source" => ConsumerPrivateSubject.Collection.Id,
            "proof.selection.source" => ProofSelectionItem.Collection.Id,
            ReadProofGrants.Json => ProofJsonRead.Definition.Id,
            ReadProofGrants.Count => ProofCountSummary.Definition.Id,
            "consumer.subject.acquire" or "consumer.subject.validate" or "consumer.subject.admin" =>
                "consumer.subject",
            "consumer.other-subject.acquire" or "consumer.other-subject.validate" or "consumer.other-subject.admin" =>
                "consumer.other-subject",
            "consumer.subject.read" => ConsumerStoredSubjectRead.Definition.Id,
            LifecycleProof.DeliveryGrant => LifecycleProof.ConsumerId,
            LifecycleProof.AcknowledgementGrant => LifecycleProof.ConsumerId,
            "base.subjectLifecycle.tombstone" or "base.subjectLifecycle.feed.read"
                or "base.subjectLifecycle.feed.checkpoint" or "base.subjectRetirement.acknowledge" => id,
            "base.subjectRetirement.purge" => id,
            "consumer.subject.retirement.purge.source" => ConsumerPrivateSubject.Collection.Id,
            _ => id,
        },
        Scope = id switch
        {
            "proof.owner.source" => new ResourceScope
            {
                Kind = ResourceScopeKind.Collection, CollectionId = ProofOwner.Collection.Id, TenantId = "tenant-a",
            },
            "proof.work.source" => new ResourceScope
            {
                Kind = ResourceScopeKind.Collection, CollectionId = ProofWorkItem.Collection.Id, TenantId = "tenant-a",
            },
            "consumer.subject.source" => new ResourceScope
            {
                Kind = ResourceScopeKind.Collection,
                CollectionId = ConsumerPrivateSubject.Collection.Id,
                TenantId = "tenant-a",
            },
            "proof.selection.source" => new ResourceScope
            {
                Kind = ResourceScopeKind.Collection, CollectionId = ProofSelectionItem.Collection.Id, TenantId = "tenant-a",
            },
            "consumer.subject.acquire" or "consumer.subject.validate" or "consumer.subject.admin"
                or "consumer.other-subject.acquire" or "consumer.other-subject.validate" or "consumer.other-subject.admin" =>
                new ResourceScope { Kind = ResourceScopeKind.Runtime, TenantId = "tenant-a" },
            LifecycleProof.DeliveryGrant or LifecycleProof.AcknowledgementGrant
                or SemanticProof.LifecycleRetirementGrant
                or "base.subjectLifecycle.tombstone" or "base.subjectLifecycle.feed.read"
                or "base.subjectLifecycle.feed.checkpoint" or "base.subjectRetirement.acknowledge"
                or "base.subjectRetirement.purge" =>
                new ResourceScope
                {
                    Kind = ResourceScopeKind.SubjectContract,
                    SubjectContractId = "consumer.subject",
                    SubjectContractVersion = 1,
                    TenantId = "tenant-a",
                },
            "consumer.subject.retirement.purge.source" => new ResourceScope
            {
                Kind = ResourceScopeKind.Collection,
                CollectionId = ConsumerPrivateSubject.Collection.Id,
                TenantId = "tenant-a",
            },
            _ => new ResourceScope
            {
                Kind = ResourceScopeKind.Runtime,
                TenantId = id == SemanticProof.MaintainGrant ? null : "tenant-a",
            },
        },
    };
    }

    private static BaseCollection<JsonElement>[] AuthorityCollections() =>
    [
        AuthorityCollection("authority.revisions", BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge),
        AuthorityCollection("authority.validations", BaseCollectionMutationMode.AppendOnly),
        AuthorityCollection("authority.audit", BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge),
        AuthorityCollection("authority.intents", BaseCollectionMutationMode.AppendOnly),
        AuthorityCollection("authority.desired", BaseCollectionMutationMode.Mutable),
        AuthorityCollection("authority.outbox", BaseCollectionMutationMode.AppendOnly),
    ];

    private static BaseCollection<JsonElement> AuthorityCollection(
        string id, BaseCollectionMutationMode mode) => BaseCollection<JsonElement>.Create(
        new CollectionDefinition
        {
            Id = id, Name = id, Kind = BaseCollectionKinds.Document,
            SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve,
            MutationMode = mode,
        }, HPDBaseJsonSerializerContext.Default.JsonElement, static _ => { });

    private sealed class ProofAllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = request;
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed,
                Audit = new PolicyAuditInfo
                {
                    MatchedGrantIds =
                    [
                        "proof.identity-and-generation.execute",
                        "proof.owner.source",
                        "proof.work.source",
                        "proof.selection.source",
                        "consumer.subject.acquire",
                        "consumer.subject.validate",
                    ],
                },
            });
        }
    }

    private sealed class ProofTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
