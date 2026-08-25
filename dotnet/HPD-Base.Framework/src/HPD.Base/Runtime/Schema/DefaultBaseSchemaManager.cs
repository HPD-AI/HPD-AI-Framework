using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class DefaultBaseSchemaManager(
    BaseLogicalSchema logicalSchema,
    IRecordStoreRegistry stores,
    IBaseSchemaPlanProtector protector,
    IBaseProviderBootstrap bootstrap,
    IOptions<HPDBaseSchemaOptions> options,
    TimeProvider timeProvider) : IBaseSchemaManager
{
    private readonly HPDBaseSchemaOptions _options = options.Value;

    /// <summary>Executes the plan async operation.</summary>
    public async ValueTask<OperationResult<BaseSchemaPlan>> PlanAsync(BaseSchemaPlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using HPDBaseRelationalTelemetry.Scope telemetry = HPDBaseRelationalTelemetry.StartSchema(HPDBaseTelemetrySpans.SchemaPlan, "plan");
        await bootstrap.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (Resolve(request.StoreId) is not { } store) return Capability<BaseSchemaPlan>();
        if (!store.SchemaExecution.Inspect || !store.SchemaExecution.Prepare) return Capability<BaseSchemaPlan>();
        OperationResult<BaseSchemaObservedState> inspection = await InspectAsync(store, request.StoreId, VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false);
        if (!inspection.IsSuccess() || inspection.Value is null) return Copy<BaseSchemaPlan, BaseSchemaObservedState>(inspection);

        BaseSchemaLogicalOperation[] delta = Delta(inspection.Value);
        if (delta.Length > _options.MaxPlanOperations) return Failure<BaseSchemaPlan>(OperationStatus.ValidationFailed, BaseSchemaErrorCodes.PlanLimitExceeded, "The schema plan operation limit was exceeded.", ErrorCategory.Validation);
        BaseSchemaPlanClassification classification = Classify(delta, inspection.Value);
        telemetry.SetClassification(classification);
        BaseExternalMigrationAttestation? attestation = request.ExternalMigrationAttestation;
        bool attestationApplied = false;
        if (attestation is not null && classification == BaseSchemaPlanClassification.DataMigrationRequired)
        {
            if (!ValidAttestation(attestation, request.StoreId, inspection.Value))
                return Failure<BaseSchemaPlan>(OperationStatus.ValidationFailed, BaseSchemaErrorCodes.PlanInvalid, "The external migration attestation is invalid.", ErrorCategory.Validation);
            delta = [new BaseSchemaLogicalOperation { Kind = BaseSchemaOperationKind.AdoptExternalBaseline, LogicalId = "baseline:" + logicalSchema.ApplicationId }];
            classification = BaseSchemaPlanClassification.SafeStructural;
            attestationApplied = true;
        }
        if (!store.SchemaExecution.Classifications.Contains(classification)) return Failure<BaseSchemaPlan>(OperationStatus.Unsupported, BaseSchemaErrorCodes.MigrationUnsupported, "The provider cannot prepare this schema plan classification.", ErrorCategory.Unsupported);
        var preparation = new BaseSchemaPreparationRequest
        {
            ApplicationId = logicalSchema.ApplicationId, LogicalDelta = delta, ObservedState = inspection.Value,
            Classification = classification,
            ExpectedGeneration = inspection.Value.Generation, BaselineChecksum = inspection.Value.AcceptedChecksum,
            TargetChecksum = logicalSchema.CanonicalChecksum, PreparationTimeout = _options.MigrationLeaseTimeout
        };
        OperationResult<BaseSchemaPreparedPlan> prepared;
        try
        {
            prepared = await store.PrepareSchemaPlanAsync(preparation, cancellationToken).AsTask()
                .WaitAsync(_options.MigrationLeaseTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) { return Failure<BaseSchemaPlan>(OperationStatus.CapabilityUnavailable, BaseSchemaErrorCodes.MigrationBusy, "Schema plan preparation exceeded its bounded lifetime.", ErrorCategory.Capability); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Failure<BaseSchemaPlan>(OperationStatus.CapabilityUnavailable, BaseSchemaErrorCodes.MigrationBusy, "Schema plan preparation exceeded its bounded lifetime.", ErrorCategory.Capability); }
        catch when (!cancellationToken.IsCancellationRequested) { return Failure<BaseSchemaPlan>(OperationStatus.StoreError, BaseSchemaErrorCodes.PlanInvalid, "Schema plan preparation failed.", ErrorCategory.Store); }
        if (!prepared.IsSuccess() || prepared.Value is null) return Copy<BaseSchemaPlan, BaseSchemaPreparedPlan>(prepared);
        if (prepared.Value.RefinedClassification is { } refined)
        {
            if (classification != BaseSchemaPlanClassification.Destructive || refined != BaseSchemaPlanClassification.DataMigrationRequired)
            {
                CryptographicOperations.ZeroMemory(prepared.Value.ProviderApplyArtifact);
                return Failure<BaseSchemaPlan>(OperationStatus.StoreError, BaseSchemaErrorCodes.PlanInvalid, "The provider returned an invalid schema classification.", ErrorCategory.Store);
            }
            classification = refined;
            telemetry.SetClassification(classification);
            if (!store.SchemaExecution.Classifications.Contains(classification))
            {
                CryptographicOperations.ZeroMemory(prepared.Value.ProviderApplyArtifact);
                return Failure<BaseSchemaPlan>(OperationStatus.Unsupported, BaseSchemaErrorCodes.MigrationUnsupported, "The provider cannot prepare the refined schema plan classification.", ErrorCategory.Unsupported);
            }
            if (attestation is not null)
            {
                if (!ValidAttestation(attestation, request.StoreId, inspection.Value))
                {
                    CryptographicOperations.ZeroMemory(prepared.Value.ProviderApplyArtifact);
                    return Failure<BaseSchemaPlan>(OperationStatus.ValidationFailed, BaseSchemaErrorCodes.PlanInvalid, "The external migration attestation is invalid.", ErrorCategory.Validation);
                }
                CryptographicOperations.ZeroMemory(prepared.Value.ProviderApplyArtifact);
                delta = [new BaseSchemaLogicalOperation { Kind = BaseSchemaOperationKind.AdoptExternalBaseline, LogicalId = "baseline:" + logicalSchema.ApplicationId }];
                classification = BaseSchemaPlanClassification.SafeStructural;
                attestationApplied = true;
                preparation = preparation with { LogicalDelta = delta, Classification = classification };
                try
                {
                    prepared = await store.PrepareSchemaPlanAsync(preparation, cancellationToken).AsTask()
                        .WaitAsync(_options.MigrationLeaseTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException) { return Failure<BaseSchemaPlan>(OperationStatus.CapabilityUnavailable, BaseSchemaErrorCodes.MigrationBusy, "Schema plan preparation exceeded its bounded lifetime.", ErrorCategory.Capability); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Failure<BaseSchemaPlan>(OperationStatus.CapabilityUnavailable, BaseSchemaErrorCodes.MigrationBusy, "Schema plan preparation exceeded its bounded lifetime.", ErrorCategory.Capability); }
                catch when (!cancellationToken.IsCancellationRequested) { return Failure<BaseSchemaPlan>(OperationStatus.StoreError, BaseSchemaErrorCodes.PlanInvalid, "Schema plan preparation failed.", ErrorCategory.Store); }
                if (!prepared.IsSuccess() || prepared.Value is null) return Copy<BaseSchemaPlan, BaseSchemaPreparedPlan>(prepared);
                if (prepared.Value.RefinedClassification is not null)
                {
                    CryptographicOperations.ZeroMemory(prepared.Value.ProviderApplyArtifact);
                    return Failure<BaseSchemaPlan>(OperationStatus.StoreError, BaseSchemaErrorCodes.PlanInvalid, "The provider returned an invalid adoption classification.", ErrorCategory.Store);
                }
            }
        }
        else if (attestation is not null && !attestationApplied)
        {
            CryptographicOperations.ZeroMemory(prepared.Value.ProviderApplyArtifact);
            return Failure<BaseSchemaPlan>(OperationStatus.ValidationFailed, BaseSchemaErrorCodes.PlanInvalid, "The external migration attestation is not applicable.", ErrorCategory.Validation);
        }
        if (!ValidPrepared(prepared.Value, request.StoreId, inspection.Value) || prepared.Value.ProviderApplyArtifact.Length > _options.MaxPlanArtifactBytes ||
            !DefaultBaseSchemaPlanProtector.Digest(prepared.Value.ProviderApplyArtifact).Equals(prepared.Value.ProviderApplyArtifactDigest, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(prepared.Value.ProviderApplyArtifact);
            return Failure<BaseSchemaPlan>(OperationStatus.StoreError, BaseSchemaErrorCodes.PlanInvalid, "The provider returned an invalid prepared schema plan.", ErrorCategory.Store);
        }

        DateTimeOffset created = timeProvider.GetUtcNow();
        var plan = new BaseSchemaPlan
        {
            PlanId = OpaqueId(), ApplicationId = logicalSchema.ApplicationId, StoreId = request.StoreId,
            PersistedStoreInstanceId = prepared.Value.PersistedStoreInstanceId, ProviderId = prepared.Value.ProviderId,
            ProviderVersion = prepared.Value.ProviderVersion, PlannerVersion = prepared.Value.PlannerVersion,
            ExpectedGeneration = inspection.Value.Generation, BaselineId = inspection.Value.AcceptedBaselineId,
            BaselineChecksum = inspection.Value.AcceptedChecksum, TargetBaselineId = OpaqueId(), TargetChecksum = logicalSchema.CanonicalChecksum,
            Classification = classification, Operations = delta, RequiresExternalDataMigration = classification == BaseSchemaPlanClassification.DataMigrationRequired,
            ExternalMigrationAttestation = attestation,
            CreatedAt = created, ExpiresAt = created + _options.PlanLifetime, LogicalPlanDigest = LogicalDigest(delta),
            ProviderApplyArtifactDigest = prepared.Value.ProviderApplyArtifactDigest, ProtectedArtifact = []
        };
        try
        {
            byte[] artifact = protector.Protect(plan, prepared.Value.ProviderApplyArtifact);
            if (artifact.Length > _options.MaxPlanArtifactBytes) return Failure<BaseSchemaPlan>(OperationStatus.ValidationFailed, BaseSchemaErrorCodes.PlanLimitExceeded, "The protected schema plan limit was exceeded.", ErrorCategory.Validation);
            return OperationResults.Ok(plan with { ProtectedArtifact = artifact });
        }
        catch
        {
            return Failure<BaseSchemaPlan>(OperationStatus.ValidationFailed, BaseSchemaErrorCodes.PlanInvalid, "Schema plan protection is unavailable.", ErrorCategory.Validation);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prepared.Value.ProviderApplyArtifact);
        }
    }

    /// <summary>Executes the verify async operation.</summary>
    public async ValueTask<OperationResult<BaseSchemaObservedState>> VerifyAsync(BaseSchemaVerifyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using HPDBaseRelationalTelemetry.Scope telemetry = HPDBaseRelationalTelemetry.StartSchema(HPDBaseTelemetrySpans.SchemaVerify, "verify");
        await bootstrap.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return Resolve(request.StoreId) is { } store
            ? await InspectAsync(store, request.StoreId, request.Visibility, cancellationToken).ConfigureAwait(false)
            : Capability<BaseSchemaObservedState>();
    }

    /// <summary>Executes the apply async operation.</summary>
    public async ValueTask<OperationResult<BaseSchemaApplyResult>> ApplyAsync(BaseSchemaApplyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using HPDBaseRelationalTelemetry.Scope telemetry = HPDBaseRelationalTelemetry.StartSchema(HPDBaseTelemetrySpans.SchemaApply, "apply");
        await bootstrap.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        OperationResult<BaseSchemaVerifiedPlan> unprotected = protector.Unprotect(request.ProtectedArtifact);
        if (!unprotected.IsSuccess() || unprotected.Value is null) return Copy<BaseSchemaApplyResult, BaseSchemaVerifiedPlan>(unprotected);
        BaseSchemaVerifiedPlan verified = unprotected.Value;
        bool providerOwnsArtifactLifetime = false;
        try
        {
        BaseSchemaPlan plan = verified.Plan;
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (plan.ExpiresAt <= now) return Failure<BaseSchemaApplyResult>(OperationStatus.ValidationFailed, BaseSchemaErrorCodes.PlanExpired, "The schema plan has expired.", ErrorCategory.Validation);
        if (!plan.ApplicationId.Equals(logicalSchema.ApplicationId, StringComparison.Ordinal) || !plan.TargetChecksum.Equals(logicalSchema.CanonicalChecksum, StringComparison.Ordinal) ||
            !LogicalDigest(plan.Operations).Equals(plan.LogicalPlanDigest, StringComparison.Ordinal)) return Failure<BaseSchemaApplyResult>(OperationStatus.Conflict, BaseSchemaErrorCodes.PlanStale, "The schema plan does not match the installed contract.", ErrorCategory.Conflict);
        if (plan.Classification == BaseSchemaPlanClassification.Destructive && !request.AllowDestructive)
            return Failure<BaseSchemaApplyResult>(OperationStatus.ValidationFailed, BaseSchemaErrorCodes.MigrationRequired, "Destructive schema work requires fresh operator authorization.", ErrorCategory.Validation);
        if (plan.Classification is BaseSchemaPlanClassification.DataMigrationRequired or BaseSchemaPlanClassification.Unsupported or BaseSchemaPlanClassification.DriftBlocked)
            return Failure<BaseSchemaApplyResult>(OperationStatus.Unsupported, BaseSchemaErrorCodes.MigrationRequired, "The schema plan cannot be applied automatically.", ErrorCategory.Unsupported);
        if (Resolve(plan.StoreId) is not { } store)
            return Failure<BaseSchemaApplyResult>(OperationStatus.Conflict, BaseSchemaErrorCodes.PlanStale, "The schema plan target is no longer installed.", ErrorCategory.Conflict);
        if (!store.SchemaExecution.Inspect || !store.SchemaExecution.Apply) return Capability<BaseSchemaApplyResult>();
        OperationResult<BaseSchemaObservedState> inspection = await InspectAsync(store, plan.StoreId, VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false);
        if (!inspection.IsSuccess() || inspection.Value is null) return Copy<BaseSchemaApplyResult, BaseSchemaObservedState>(inspection);
        if (inspection.Value.Generation != plan.ExpectedGeneration ||
            inspection.Value.AcceptedChecksum != plan.BaselineChecksum) return Failure<BaseSchemaApplyResult>(OperationStatus.Conflict, BaseSchemaErrorCodes.PlanStale, "The schema plan is stale.", ErrorCategory.Conflict);

        var envelope = new BaseSchemaProviderVerifiedEnvelope
        {
            PlanId = plan.PlanId, TargetBaselineId = plan.TargetBaselineId, ApplicationId = plan.ApplicationId, StoreId = plan.StoreId,
            PersistedStoreInstanceId = plan.PersistedStoreInstanceId, ProviderId = plan.ProviderId, ProviderVersion = plan.ProviderVersion,
            PlannerVersion = plan.PlannerVersion, Classification = plan.Classification, LogicalPlanDigest = plan.LogicalPlanDigest,
            ProviderApplyArtifactDigest = plan.ProviderApplyArtifactDigest, CreatedAt = plan.CreatedAt, ExpiresAt = plan.ExpiresAt,
            StructuralVerification = plan.ExternalMigrationAttestation is null ? BaseSchemaStructuralVerification.NotApplicable : BaseSchemaStructuralVerification.Verified,
            ExternalDataMigration = plan.ExternalMigrationAttestation is null ? BaseExternalDataMigrationVerification.NotApplicable : BaseExternalDataMigrationVerification.HostAttested,
            SemanticConversion = plan.ExternalMigrationAttestation is null ? BaseSemanticConversionVerification.NotApplicable : BaseSemanticConversionVerification.NotVerifiedByBase,
            ExternalAttestationId = plan.ExternalMigrationAttestation?.AttestationId,
            ExternalSignerId = plan.ExternalMigrationAttestation?.SignerId
        };
        byte[] verifiedEnvelope = JsonSerializer.SerializeToUtf8Bytes(envelope, HPDBaseJsonSerializerContext.Default.BaseSchemaProviderVerifiedEnvelope);
        Task<OperationResult<BaseSchemaApplyResult>>? applyTask = null;
        providerOwnsArtifactLifetime = true;
        try
        {
            applyTask = store.ApplySchemaAsync(new BaseSchemaProviderApplyRequest
            {
                VerifiedPlanEnvelope = verifiedEnvelope, ProviderApplyArtifact = verified.ProviderApplyArtifact,
                ExpectedGeneration = plan.ExpectedGeneration, ExpectedBaselineChecksum = plan.BaselineChecksum,
                ExpectedTargetChecksum = plan.TargetChecksum, AllowDestructive = request.AllowDestructive,
                LeaseTimeout = _options.MigrationLeaseTimeout, ApplyTimeout = _options.MaxApplyDuration,
                CommitCompletionTimeout = _options.CommitCompletionTimeout
            }, cancellationToken).AsTask();
            OperationResult<BaseSchemaApplyResult> applied = await applyTask.WaitAsync(
                _options.MaxApplyDuration + _options.CommitCompletionTimeout, cancellationToken).ConfigureAwait(false);
            if (!applied.IsSuccess() || applied.Value is null)
            {
                string code = applied.Error?.Code switch
                {
                    BaseSchemaErrorCodes.PlanInvalid => BaseSchemaErrorCodes.PlanInvalid,
                    BaseSchemaErrorCodes.PlanStale => BaseSchemaErrorCodes.PlanStale,
                    BaseSchemaErrorCodes.MigrationBusy => BaseSchemaErrorCodes.MigrationBusy,
                    BaseSchemaErrorCodes.MigrationRolledBack => BaseSchemaErrorCodes.MigrationRolledBack,
                    BaseSchemaErrorCodes.MigrationIndeterminate => BaseSchemaErrorCodes.MigrationIndeterminate,
                    _ => BaseSchemaErrorCodes.MigrationFailed,
                };
                ErrorCategory category = code switch
                {
                    BaseSchemaErrorCodes.PlanInvalid => ErrorCategory.Validation,
                    BaseSchemaErrorCodes.PlanStale => ErrorCategory.Conflict,
                    BaseSchemaErrorCodes.MigrationBusy => ErrorCategory.Capability,
                    _ => ErrorCategory.Store,
                };
                return Failure<BaseSchemaApplyResult>(applied.Status, code, "Schema application failed.", category);
            }
            if (!ValidApplyResult(plan, applied.Value))
                return Failure<BaseSchemaApplyResult>(OperationStatus.StoreError, BaseSchemaErrorCodes.MigrationFailed,
                    "The provider returned an invalid schema application result.", ErrorCategory.Store);
            return applied;
        }
        catch (TimeoutException) { return Failure<BaseSchemaApplyResult>(OperationStatus.StoreError, BaseSchemaErrorCodes.MigrationIndeterminate, "Schema application completion is indeterminate.", ErrorCategory.Store); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Failure<BaseSchemaApplyResult>(OperationStatus.StoreError, BaseSchemaErrorCodes.MigrationIndeterminate, "Schema application completion is indeterminate.", ErrorCategory.Store); }
        catch when (!cancellationToken.IsCancellationRequested) { return Failure<BaseSchemaApplyResult>(OperationStatus.StoreError, BaseSchemaErrorCodes.MigrationFailed, "Schema application failed.", ErrorCategory.Store); }
        finally
        {
            if (applyTask is null || applyTask.IsCompleted)
            {
                CryptographicOperations.ZeroMemory(verified.ProviderApplyArtifact);
                CryptographicOperations.ZeroMemory(verifiedEnvelope);
            }
            else
            {
                _ = applyTask.ContinueWith(static (completed, state) =>
                {
                    _ = completed.Exception;
                    (byte[] artifact, byte[] envelope) = ((byte[], byte[]))state!;
                    CryptographicOperations.ZeroMemory(artifact);
                    CryptographicOperations.ZeroMemory(envelope);
                }, (verified.ProviderApplyArtifact, verifiedEnvelope), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        }
        finally
        {
            if (!providerOwnsArtifactLifetime)
                CryptographicOperations.ZeroMemory(verified.ProviderApplyArtifact);
        }
    }

    /// <summary>Executes the read history async operation.</summary>
    public async ValueTask<OperationResult<BaseSchemaHistoryPage>> ReadHistoryAsync(string storeId, BaseSchemaHistoryRequest request, CancellationToken cancellationToken = default)
    {
        await bootstrap.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return Resolve(storeId) is { } store && store.SchemaExecution.History
            ? await store.ReadSchemaHistoryAsync(request, cancellationToken).ConfigureAwait(false)
            : Capability<BaseSchemaHistoryPage>();
    }

    private async ValueTask<OperationResult<BaseSchemaObservedState>> InspectAsync(IBaseSchemaStore store, string storeId, VisibilityLevel visibility, CancellationToken cancellationToken)
    {
        using HPDBaseRelationalTelemetry.Scope telemetry = HPDBaseRelationalTelemetry.StartSchema(HPDBaseTelemetrySpans.SchemaInspect, "inspect");
        if (!store.SchemaExecution.Inspect) return Capability<BaseSchemaObservedState>();
        try
        {
            OperationResult<BaseSchemaObservedState> result = await store.InspectSchemaAsync(new BaseSchemaInspectionRequest
            { ApplicationId = logicalSchema.ApplicationId, ExpectedLogicalChecksum = logicalSchema.CanonicalChecksum, Visibility = visibility, InspectionTimeout = _options.MigrationLeaseTimeout }, cancellationToken)
                .AsTask().WaitAsync(_options.MigrationLeaseTimeout, cancellationToken).ConfigureAwait(false);
            if (result.Value is { } state && (!state.StoreId.Equals(storeId, StringComparison.Ordinal) || state.Generation < 0 ||
                state.Assets is null || state.Assets.Any(static asset => string.IsNullOrWhiteSpace(asset.LogicalId)) ||
                state.Assets.Select(static asset => asset.LogicalId).Distinct(StringComparer.Ordinal).Count() != state.Assets.Length))
                return Failure<BaseSchemaObservedState>(OperationStatus.StoreError, BaseSchemaErrorCodes.VerifyFailed, "The provider returned invalid schema state.", ErrorCategory.Store);
            return result;
        }
        catch (TimeoutException) { return Failure<BaseSchemaObservedState>(OperationStatus.CapabilityUnavailable, BaseSchemaErrorCodes.VerifyFailed, "Schema verification exceeded its bounded lifetime.", ErrorCategory.Capability); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Failure<BaseSchemaObservedState>(OperationStatus.CapabilityUnavailable, BaseSchemaErrorCodes.VerifyFailed, "Schema verification exceeded its bounded lifetime.", ErrorCategory.Capability); }
        catch when (!cancellationToken.IsCancellationRequested) { return Failure<BaseSchemaObservedState>(OperationStatus.StoreError, BaseSchemaErrorCodes.VerifyFailed, "Schema verification failed.", ErrorCategory.Store); }
    }

    private IBaseSchemaStore? Resolve(string storeId) => stores.GetStore(storeId) as IBaseSchemaStore;
    private bool ValidPrepared(BaseSchemaPreparedPlan plan, string storeId, BaseSchemaObservedState observed) =>
        !string.IsNullOrWhiteSpace(plan.ProviderId) && !string.IsNullOrWhiteSpace(plan.ProviderVersion) &&
        !string.IsNullOrWhiteSpace(plan.PlannerVersion) && !string.IsNullOrWhiteSpace(plan.PersistedStoreInstanceId) &&
        (observed.PersistedStoreInstanceId is null || string.Equals(observed.PersistedStoreInstanceId, plan.PersistedStoreInstanceId, StringComparison.Ordinal)) &&
        stores.GetRegistration(storeId) is not null;

    private BaseSchemaLogicalOperation[] Delta(BaseSchemaObservedState observed)
    {
        if (observed.Compatibility == BaseSchemaCompatibility.Compatible &&
            string.Equals(observed.AcceptedChecksum, logicalSchema.CanonicalChecksum, StringComparison.Ordinal))
            return [];
        Dictionary<string, string> current = CurrentAssets();
        Dictionary<string, BaseSchemaObservedAsset> accepted = observed.Assets.ToDictionary(static asset => asset.LogicalId, StringComparer.Ordinal);
        var operations = new List<BaseSchemaLogicalOperation>();
        foreach ((string id, string summary) in current.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!accepted.TryGetValue(id, out BaseSchemaObservedAsset? prior)) operations.Add(Operation(AddKind(id), id));
            else if (!string.Equals(prior.SafeSummary, summary, StringComparison.Ordinal))
            {
                string[] before = (prior.SafeSummary ?? "").Split('\u001f');
                string[] after = summary.Split('\u001f');
                bool rename = id.StartsWith("c:", StringComparison.Ordinal) ||
                    id.StartsWith("f:", StringComparison.Ordinal) && before.Length >= 4 && before.Length == after.Length && before[1..].SequenceEqual(after[1..], StringComparer.Ordinal);
                operations.Add(rename
                    ? new BaseSchemaLogicalOperation { Kind = id.StartsWith("c:", StringComparison.Ordinal) ? BaseSchemaOperationKind.RenameCollection : BaseSchemaOperationKind.RenameField, LogicalId = id, PreviousName = before[0], TargetName = after[0] }
                    : Operation(ChangeKind(id), id, destructive: id.StartsWith("f:", StringComparison.Ordinal) || id.StartsWith("r:", StringComparison.Ordinal)));
            }
        }
        foreach (string id in accepted.Keys.Except(current.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)) operations.Add(Operation(RemoveKind(id), id, destructive: true));
        return operations.ToArray();
    }

    private Dictionary<string, string> CurrentAssets()
    {
        var assets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (BaseLogicalCollection value in logicalSchema.Collections) assets["c:" + value.Id] = value.Name;
        foreach (BaseLogicalField value in logicalSchema.Fields) assets[$"f:{value.CollectionId}:{value.Id}"] = string.Join('\u001f', value.StoredName, value.Type, (int)value.Presence, (int)value.Nullability, value.ScalarKind is null ? "" : ((int)value.ScalarKind.Value).ToString(System.Globalization.CultureInfo.InvariantCulture), value.ScalarCodecChecksum?.ToString() ?? "", value.ScalarConstraintChecksum?.ToString() ?? "");
        foreach (RelationDefinition value in logicalSchema.Relations) assets["r:" + value.Id] = string.Join('\u001f', value.SourceCollectionId, value.SourceFieldId, value.TargetCollectionId, value.TargetFieldId, value.OwningSide, value.LocalMultiplicity, value.InverseMultiplicity, value.Required, value.Ordered, value.DeleteBehavior);
        foreach (BaseLogicalIndex value in logicalSchema.Indexes) assets[$"i:{value.CollectionId}:{value.Id}"] = string.Join('\u001f', value.Unique ? "1" : "0", string.Join('\u001e', value.FieldIds), value.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), value.StoreRequired ? "1" : "0", value.PredicateChecksum?.ToString() ?? "", value.Checksum?.ToString() ?? "", string.Join('\u001e', (value.Parts ?? []).Select(static part => $"{part.FieldOrdinal}:{(int)part.Direction}:{(int)part.Collation}:{(int)part.NullOrder}")));
        foreach (BaseLogicalRead value in logicalSchema.ReadDefinitions) assets["q:" + value.Id] = string.Join('\u001f', string.Join('\u001e', value.SourceIds), string.Join('\u001e', value.ProjectionFieldIds));
        return assets;
    }

    private BaseSchemaPlanClassification Classify(BaseSchemaLogicalOperation[] delta, BaseSchemaObservedState observed)
    {
        if (observed.Compatibility == BaseSchemaCompatibility.Drifted) return BaseSchemaPlanClassification.DriftBlocked;
        if (delta.Length == 0) return BaseSchemaPlanClassification.NoChanges;
        if (delta.Any(operation => operation.Kind is BaseSchemaOperationKind.AlterField or BaseSchemaOperationKind.AlterRelation) ||
            observed.Generation > 0 && delta.Where(operation => operation.Kind == BaseSchemaOperationKind.AddField).Any(operation =>
            {
                string[] parts = operation.LogicalId.Split(':');
                BaseLogicalField field = logicalSchema.Fields.Single(value => value.CollectionId == parts[1] && value.Id == parts[2]);
                return field.Presence == BaseFieldPresence.Required && field.Nullability == BaseFieldNullability.NonNullable;
            })) return BaseSchemaPlanClassification.DataMigrationRequired;
        return delta.Any(static operation => operation.Destructive) ? BaseSchemaPlanClassification.Destructive : BaseSchemaPlanClassification.SafeStructural;
    }

    private static BaseSchemaLogicalOperation Operation(BaseSchemaOperationKind kind, string id, bool destructive = false) => new() { Kind = kind, LogicalId = id, Destructive = destructive };
    private static BaseSchemaOperationKind AddKind(string id) => id[0] switch { 'c' => BaseSchemaOperationKind.CreateCollection, 'f' => BaseSchemaOperationKind.AddField, 'r' => BaseSchemaOperationKind.AddRelation, 'i' => BaseSchemaOperationKind.AddIndex, 'q' => BaseSchemaOperationKind.AddRead, _ => throw new InvalidOperationException() };
    private static BaseSchemaOperationKind ChangeKind(string id) => id[0] switch { 'c' => BaseSchemaOperationKind.RenameCollection, 'f' => BaseSchemaOperationKind.AlterField, 'r' => BaseSchemaOperationKind.AlterRelation, 'i' => BaseSchemaOperationKind.AlterIndex, 'q' => BaseSchemaOperationKind.AlterRead, _ => throw new InvalidOperationException() };
    private static BaseSchemaOperationKind RemoveKind(string id) => id[0] switch { 'c' => BaseSchemaOperationKind.RemoveCollection, 'f' => BaseSchemaOperationKind.RemoveField, 'r' => BaseSchemaOperationKind.RemoveRelation, 'i' => BaseSchemaOperationKind.RemoveIndex, 'q' => BaseSchemaOperationKind.RemoveRead, _ => throw new InvalidOperationException() };
    private static string LogicalDigest(BaseSchemaLogicalOperation[] operations)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        foreach (BaseSchemaLogicalOperation operation in operations.OrderBy(static item => item.LogicalId, StringComparer.Ordinal).ThenBy(static item => item.Kind))
        { writer.Write((int)operation.Kind); Write(writer, operation.LogicalId); Write(writer, operation.PreviousName ?? ""); Write(writer, operation.TargetName ?? ""); writer.Write(operation.Destructive); }
        writer.Flush(); return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
    private static void Write(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
    private static string OpaqueId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private static bool ValidApplyResult(BaseSchemaPlan plan, BaseSchemaApplyResult result) => result.Outcome switch
    {
        BaseSchemaApplyOutcome.Applied => result.Generation == plan.ExpectedGeneration + 1 &&
            result.BaselineId == plan.TargetBaselineId && result.Checksum == plan.TargetChecksum &&
            result.State == BaseSchemaMigrationState.Ready,
        BaseSchemaApplyOutcome.NoChanges => plan.Classification == BaseSchemaPlanClassification.NoChanges &&
            result.Generation == plan.ExpectedGeneration && result.BaselineId == plan.BaselineId &&
            result.Checksum == plan.TargetChecksum && result.State == BaseSchemaMigrationState.Ready,
        _ => false,
    };
    private bool ValidAttestation(BaseExternalMigrationAttestation value, string storeId, BaseSchemaObservedState observed)
    {
        if (_options.ExternalMigrationAttestationKey.Length != 32 || value.AuthenticationTag.Length != 32 || value.ApplicationId != logicalSchema.ApplicationId ||
            value.StoreId != storeId || value.SourceChecksum != observed.AcceptedChecksum || value.TargetChecksum != logicalSchema.CanonicalChecksum ||
            value.CompletedAt > timeProvider.GetUtcNow() || value.CompletedAt < timeProvider.GetUtcNow() - TimeSpan.FromDays(365) ||
            string.IsNullOrWhiteSpace(value.AttestationId) || value.AttestationId.Length > 256 || string.IsNullOrWhiteSpace(value.SignerId) || value.SignerId.Length > 256 ||
            string.IsNullOrWhiteSpace(value.Tool) || value.Tool.Length > 256 || string.IsNullOrWhiteSpace(value.ToolVersion) || value.ToolVersion.Length > 128) return false;
        try { return CryptographicOperations.FixedTimeEquals(value.AuthenticationTag, BaseExternalMigrationAttestationAuthenticator.ComputeAuthenticationTag(value, _options.ExternalMigrationAttestationKey)); }
        catch { return false; }
    }
    private static OperationResult<T> Capability<T>() => Failure<T>(OperationStatus.CapabilityUnavailable, BaseSchemaErrorCodes.MigrationUnsupported, "The selected store does not support schema execution.", ErrorCategory.Capability);
    private static OperationResult<T> Failure<T>(OperationStatus status, string code, string message, ErrorCategory category) => new() { Status = status, Error = new BaseError { Code = code, Message = message, Category = category } };
    private static OperationResult<TTarget> Copy<TTarget, TSource>(OperationResult<TSource> source) => new() { Status = source.Status, Error = source.Error, Warnings = source.Warnings, Diagnostics = source.Diagnostics };
}
