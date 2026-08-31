using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore
{
    private volatile InMemoryLifecycleMaintenanceProgress? _lifecycleMaintenance;

    private async ValueTask<OperationResult<BaseSubjectAuthorityMaintenancePageResult>> ExecuteSubjectAuthorityMaintenancePageAsync(
        BaseSubjectAuthorityMaintenanceExecutionRequest execution,
        BaseSubjectAuthorityMaintenancePageRequest page,
        CancellationToken cancellationToken)
    {
        if (page.FormatVersion != 1 || page.PageOrdinal < 1
            || page.LifecycleKind != execution.Lifecycle.Kind
            || page.RetirementKind != execution.Retirement?.Kind
            || page.PageSize != execution.PageSize
            || !CryptographicOperations.FixedTimeEquals(page.CombinedPlanChecksum, execution.CombinedPlanChecksum))
            return OperationResults.CapabilityUnavailable<BaseSubjectAuthorityMaintenancePageResult>(BaseSubjectFailureContract.Error(BaseSubjectErrorCodes.LifecycleProviderContractInvalid));

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string receiptKey = ReceiptKey(execution.Identity);
            if (_publishedState.Receipts.TryGetValue(receiptKey, out InMemoryMutationReceipt? receipt)
                && receipt.ExpiresAt > _timeProvider.GetUtcNow())
            {
                if (!CryptographicOperations.FixedTimeEquals(receipt.Fingerprint, execution.Identity.Fingerprint.ToArray())
                    || !CryptographicOperations.FixedTimeEquals(receipt.StructuralDigest, execution.CombinedPlanChecksum)
                    || receipt.Result.SubjectLifecycleMaintenance is not { } stored)
                    return new() { Status=OperationStatus.Conflict, Error=BaseSubjectFailureContract.Error(BaseMutationRequestErrorCodes.FingerprintConflict) };
                BaseSubjectRetirementMaintenanceResult? storedRetirement=receipt.Result.SubjectRetirement?.Maintenance;
                if(execution.Retirement is not null&&storedRetirement is null)return OperationResults.CapabilityUnavailable<BaseSubjectAuthorityMaintenancePageResult>(BaseSubjectFailureContract.Error(BaseSubjectRetirementErrorCodes.ProviderContractInvalid));
                return OperationResults.Ok(new BaseSubjectAuthorityMaintenancePageResult
                {
                    PageOrdinal=page.PageOrdinal,HasMore=false,NextCanonicalKey=null,
                    LifecycleExaminedCount=stored.ExaminedCount,LifecycleChangedCount=stored.ChangedCount,
                    RetirementExaminedCount=storedRetirement?.ExaminedCount??0,RetirementChangedCount=storedRetirement?.ChangedCount??0,CanonicalBytes=stored.CanonicalBytes,
                    RollingChecksum=new string(stored.RollingChecksum.AsSpan()),LifecycleResult=stored with{Duplicate=true},RetirementResult=storedRetirement is null?null:storedRetirement with{Outcome=BaseSubjectRetirementMutationOutcome.Duplicate},
                });
            }

            if (execution.Lifecycle.ContractId is { } contractId
                && execution.Lifecycle.ContractVersion is { } contractVersion
                && SemanticMaintenanceFencesSubjectContract(
                    _publishedState, contractId, contractVersion))
                return new OperationResult<BaseSubjectAuthorityMaintenancePageResult>
                {
                    Status = OperationStatus.CapabilityUnavailable,
                    Error = BaseSubjectFailureContract.Error(
                        BaseSubjectErrorCodes.MaintenanceRequired),
                };

            InMemoryLifecycleMaintenanceProgress progress = _lifecycleMaintenance ??= CreateProgress(execution);
            if (!progress.Matches(execution))
                return LifecycleMaintenanceRequired<BaseSubjectAuthorityMaintenancePageResult>();
            if(!ContinuationMatches(page.LastCanonicalKey,progress))
                return OperationResults.CapabilityUnavailable<BaseSubjectAuthorityMaintenancePageResult>(BaseSubjectFailureContract.Error(BaseSubjectErrorCodes.LifecycleProviderContractInvalid));

            progress.ValidateEvidence();
            ExecuteLifecyclePage(progress, execution.PageSize);
            checked { progress.CompletedPages++; }
            if (_options.SubjectLifecycleMaintenancePageCompleted is { } completed)
                await completed(progress.CompletedPages, cancellationToken).ConfigureAwait(false);
            progress.ValidateEvidence();
            if(!progress.Complete)
            {
                byte[] next=Continuation(progress);
                return OperationResults.Ok(new BaseSubjectAuthorityMaintenancePageResult
                {
                    PageOrdinal=page.PageOrdinal,HasMore=true,NextCanonicalKey=next,
                    LifecycleExaminedCount=progress.Examined,LifecycleChangedCount=progress.Changed,
                    RetirementExaminedCount=progress.RetirementExamined,RetirementChangedCount=progress.RetirementChanged,
                    CanonicalBytes=progress.CanonicalBytes,RollingChecksum=Convert.ToHexStringLower(progress.RollingChecksum),
                });
            }
            FinalizeLifecycleStage(progress);
            var result = new BaseSubjectLifecycleMaintenanceResult
            {
                Kind = execution.Lifecycle.Kind,
                ExaminedCount = progress.Examined,
                ChangedCount = progress.Changed,
                CanonicalBytes = progress.CanonicalBytes,
                RollingChecksum = Convert.ToHexStringLower(progress.RollingChecksum),
                DeliveryEpoch = progress.Working.SubjectLifecycleDeliveryEpoch,
                ProjectionGeneration = progress.ProjectionGeneration,
                Duplicate = false,
            };
            BaseSubjectRetirementMaintenanceResult? retirementResult=execution.Retirement is null?null:new BaseSubjectRetirementMaintenanceResult{Kind=execution.Retirement.Kind,Outcome=BaseSubjectRetirementMutationOutcome.Applied,ExaminedCount=progress.RetirementExamined,ChangedCount=progress.RetirementChanged,CanonicalBytes=result.CanonicalBytes,RollingChecksum=result.RollingChecksum,PublishedBarrierControlGeneration=progress.Working.SubjectRetirementPosition};
            var maintenanceReceipt = new BaseAtomicReceiptResult { Kind = BaseAtomicReceiptResultKind.SubjectLifecycleMaintenance, Mutations = [], SubjectLifecycleMaintenance = result with { RollingChecksum = new string(result.RollingChecksum.AsSpan()) },SubjectRetirement=retirementResult is null?null:new(){Operation=BaseSubjectRetirementReceiptOperation.Maintenance,Maintenance=retirementResult} };
            byte[] maintenanceReceiptBytes = JsonSerializer.SerializeToUtf8Bytes(BaseAtomicReceiptWire.From(maintenanceReceipt), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
            DateTimeOffset committedAt = _timeProvider.GetUtcNow();
            progress.Working.Receipts[receiptKey] = new InMemoryMutationReceipt(
                execution.Identity.Fingerprint.ToArray(), execution.CombinedPlanChecksum.ToArray(),
                maintenanceReceipt, maintenanceReceiptBytes, committedAt, committedAt.AddDays(30));
            _publishedState = progress.Working;
            checked { _generation++; }
            if (progress.ReplacementScopeKey is { } replacement)
            {
                _subjectScopeProtectionKey = replacement;
                _subjectScopeProtectionKeyId = replacement.ToString(CultureInfo.InvariantCulture);
                _subjectScopeProtectionGeneration = progress.ReplacementScopeGeneration;
            }
            _lifecycleMaintenance = null;
            return OperationResults.Ok(new BaseSubjectAuthorityMaintenancePageResult
            {
                PageOrdinal=page.PageOrdinal,HasMore=false,NextCanonicalKey=null,
                LifecycleExaminedCount=result.ExaminedCount,LifecycleChangedCount=result.ChangedCount,
                RetirementExaminedCount=progress.RetirementExamined,RetirementChangedCount=progress.RetirementChanged,
                CanonicalBytes=result.CanonicalBytes,RollingChecksum=result.RollingChecksum,LifecycleResult=result,
                RetirementResult=retirementResult,
            });
        }
        finally { _stateGate.Release(); }
    }

    private static byte[] Continuation(InMemoryLifecycleMaintenanceProgress progress)=>
        Encoding.UTF8.GetBytes($"{progress.Domain}\0{progress.Index}\0{progress.CompletedPages}\0{Convert.ToHexStringLower(progress.RollingChecksum)}");

    private static bool ContinuationMatches(byte[]? supplied,InMemoryLifecycleMaintenanceProgress progress)
    {
        if(supplied is null)return true;
        return supplied is not null&&CryptographicOperations.FixedTimeEquals(supplied,Continuation(progress));
    }

    private InMemoryLifecycleMaintenanceProgress CreateProgress(BaseSubjectAuthorityMaintenanceExecutionRequest request)
    {
        InMemoryStoreState current = _publishedState;
        InMemorySubjectContractState? contract = request.Lifecycle.ContractId is null ? null : current.SubjectContracts.GetValueOrDefault(SubjectContractKey(request.Lifecycle.ContractId, request.Lifecycle.ContractVersion!.Value));
        long restoreEpoch = contract?.RestoreEpoch ?? current.SubjectContracts.Values.Select(static value => value.RestoreEpoch).DefaultIfEmpty(0).Max();
        if (request.ExpectedStoreGeneration != 1 || request.ExpectedSchemaGeneration != 1 || restoreEpoch != request.ExpectedRestoreEpoch
            || current.SubjectLifecycleDeliveryEpoch != request.Lifecycle.ExpectedDeliveryEpoch || request.ExpectedScopeProtectionGeneration != _subjectScopeProtectionGeneration
            || !string.Equals(request.ExpectedScopeProtectionKeyId, _subjectScopeProtectionKeyId, StringComparison.Ordinal))
            throw new InvalidDataException(BaseSubjectErrorCodes.ScopeProtectionRotationConflict);
        if (request.Lifecycle.ExpectedProjectionGeneration is long expected
            && (!current.SubjectLifecycleConsumers.TryGetValue($"{request.Lifecycle.ConsumerId}\n{request.Lifecycle.ConsumerVersion}", out InMemorySubjectLifecycleConsumerProjection? projection)
                || projection.ProjectionGeneration != expected))
            throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleRegistrationConflict);
        if(request.Lifecycle.Kind==BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection
            &&_options.SubjectRetirementPolicies.Length!=0&&request.Retirement is null)
            throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        if (request.Retirement is { } retirement)
        {
            bool matching = request.Lifecycle.Kind switch
            {
                BaseSubjectLifecycleMaintenanceKind.RemoveConsumer => retirement.Kind == BaseSubjectRetirementMaintenanceKind.RemoveConsumer,
                BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection => retirement.Kind == BaseSubjectRetirementMaintenanceKind.RotateScopeProtection,
                BaseSubjectLifecycleMaintenanceKind.RestoreTransform => retirement.Kind == BaseSubjectRetirementMaintenanceKind.RestoreTransform,
                BaseSubjectLifecycleMaintenanceKind.RecoverPublication => retirement.Kind == BaseSubjectRetirementMaintenanceKind.RecoverPublication,
                _ => false,
            };
            if (!matching || retirement.PlanChecksum is not { Length: 32 }) throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        }
        return new(request, current, current.Clone());
    }

    private void ExecuteLifecyclePage(InMemoryLifecycleMaintenanceProgress progress, int take)
    {
        int consumed = 0;
        while (consumed < take && !progress.Complete)
        {
            switch (progress.Request.Kind)
            {
                case BaseSubjectLifecycleMaintenanceKind.Prune: PagePrune(progress, ref consumed, take); break;
                case BaseSubjectLifecycleMaintenanceKind.RemoveConsumer: PageRemoveConsumer(progress, ref consumed, take); break;
                case BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection: PageRebuild(progress, ref consumed, take); break;
                case BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection: PageRotate(progress, ref consumed, take); break;
                case BaseSubjectLifecycleMaintenanceKind.MarkCheckpointOvertaken: PageOvertake(progress,ref consumed); break;
                default: progress.Complete = true; break;
            }
        }
    }

    private void PageOvertake(InMemoryLifecycleMaintenanceProgress p,ref int consumed)
    {
        if(p.Request.Scope is null||p.Request.RetainedFrom is null)throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleContractInvalid);
        string consumerKey=$"{p.Request.ConsumerId}\n{p.Request.ConsumerVersion}";
        if(!p.Original.SubjectLifecycleConsumers.TryGetValue(consumerKey,out InMemorySubjectLifecycleConsumerProjection? projection))throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleRegistrationConflict);
        BaseProtectedSubjectScope scope=_subjectScopes.Protect(p.Request.Scope,_subjectScopeProtectionKey);string checkpointKey=ProtectedScopeKey(p.Request.ConsumerId!,p.Request.ConsumerVersion!.Value,scope);
        p.Original.SubjectLifecycleCheckpoints.TryGetValue(checkpointKey,out InMemorySubjectLifecycleCheckpointState? checkpoint);
        DateTimeOffset last=checkpoint?.AdvancedAtUtc??projection.InstalledAtUtc;BaseSubjectLifecycleOrderingBoundary? through=checkpoint?.Through??projection.Cutoff;
        if(_timeProvider.GetUtcNow()-last<projection.MaximumCheckpointLag||through is not null&&CompareBoundary(through,p.Request.RetainedFrom)>=0)throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleRegistrationConflict);
        p.Working.SubjectLifecycleCheckpoints[checkpointKey]=checkpoint is null
            ?new(projection.ConsumerId,projection.ConsumerVersion,projection.ConsumerChecksum,projection.ContractId,projection.ContractVersion,projection.ProjectionGeneration,scope,projection.Cutoff,1,_timeProvider.GetUtcNow(),true)
            :checkpoint with{Generation=checked(checkpoint.Generation+1),Overtaken=true};
        checked{consumed++;p.Examined++;}p.Evidence($"checkpoint-overtaken\0{checkpointKey}");p.Complete=true;
    }

    private void PagePrune(InMemoryLifecycleMaintenanceProgress p, ref int consumed, int take)
    {
        BaseSubjectLifecycleOrderingBoundary retained = p.Request.RetainedFrom!;
        if (p.Domain == 0)
        {
            while (p.Index < p.Original.SubjectLifecycleMemberships.Count && consumed < take)
            {
                int index = p.Index++; InMemorySubjectLifecycleMembershipRow membership = p.Original.SubjectLifecycleMemberships[index]; InMemorySubjectLifecycleFactRow row = p.Original.SubjectLifecycleFacts[membership.FactIndex];
                checked { consumed++; }
                if (row.Fact.ContractId != p.Request.ContractId || row.Fact.ContractVersion != p.Request.ContractVersion || CompareBoundary(row.Boundary, retained) >= 0) continue;
                checked { p.Examined++; }
                InMemorySubjectLifecycleCheckpointState? checkpoint = p.Original.SubjectLifecycleCheckpoints.GetValueOrDefault(ProtectedScopeKey(membership.ConsumerId, membership.ConsumerVersion, membership.Scope));
                if (checkpoint is null || !checkpoint.Overtaken && (checkpoint.Through is null || CompareBoundary(checkpoint.Through, row.Boundary) < 0)) continue;
                p.MembershipRemovals.Add(index); p.Evidence($"membership\0{membership.ConsumerId}\0{row.Boundary.CommitPosition.Value}\0{row.Boundary.SubjectId.Value}");
            }
            if (p.Index >= p.Original.SubjectLifecycleMemberships.Count) { p.Domain = 1; p.Index = 0; }
            return;
        }
        if (p.Domain == 1)
        {
            while (p.Index < p.Original.SubjectLifecycleMemberships.Count && consumed < take)
            {
                int index = p.Index++; checked { consumed++; p.Examined++; }
                if (!p.MembershipRemovals.Contains(index)) p.RetainedFactIndexes.Add(p.Original.SubjectLifecycleMemberships[index].FactIndex);
            }
            if (p.Index >= p.Original.SubjectLifecycleMemberships.Count) { p.Domain = 2; p.Index = 0; }
            return;
        }
        if (p.Domain == 2)
        {
            while (p.Index < p.Original.SubjectLifecycleFacts.Count && consumed < take)
            {
                int index = p.Index++; InMemorySubjectLifecycleFactRow row = p.Original.SubjectLifecycleFacts[index];
                checked { consumed++; }
                if (row.Fact.ContractId != p.Request.ContractId || row.Fact.ContractVersion != p.Request.ContractVersion || CompareBoundary(row.Boundary, retained) >= 0) continue;
                checked { p.Examined++; }
                bool terminal = p.Original.SubjectTerminals.Values.Any(value => value.ContractId == row.Fact.ContractId && value.ContractVersion == row.Fact.ContractVersion && _subjectScopes.Matches(row.Scope, value.Scope) && value.RetiredPosition == row.Boundary.CommitPosition.Value && value.SubjectId.Equals(row.Boundary.SubjectId) && value.AuthorityEpoch.Equals(row.Boundary.AuthorityEpoch) && value.Incarnation.Equals(row.Boundary.Incarnation) && value.SubjectSequence == row.Boundary.SubjectSequence);
                if (p.RetainedFactIndexes.Contains(index) || terminal) continue;
                p.FactRemovals.Add(index); p.Evidence($"fact\0{row.Boundary.CommitPosition.Value}\0{row.Boundary.SubjectId.Value}");
            }
            if (p.Index >= p.Original.SubjectLifecycleFacts.Count) p.Complete = true;
        }
    }

    private static void PageRemoveConsumer(InMemoryLifecycleMaintenanceProgress p, ref int consumed, int take)
    {
        if (p.Domain == 0)
        {
            string[] barriers=p.Original.SubjectRetirementBarriers.Keys.Order(StringComparer.Ordinal).ToArray();
            while(p.Index<barriers.Length&&consumed<take)
            {
                InMemorySubjectRetirementBarrierState barrier=p.Original.SubjectRetirementBarriers[barriers[p.Index++]];checked{consumed++;}
                if(barrier.Barrier.ContractId!=p.Request.ContractId||barrier.Barrier.ContractVersion!=p.Request.ContractVersion)continue;
                checked{p.Examined++;p.RetirementExamined++;}
                string consumer=$"{p.Request.ConsumerId}\n{p.Request.ConsumerVersion}";
                if(barrier.Barrier.State is BaseSubjectRetirementBarrierState.Pending or BaseSubjectRetirementBarrierState.TimedOut or BaseSubjectRetirementBarrierState.Quarantined
                    && !barrier.Acknowledgements.ContainsKey(consumer))
                    throw new InvalidDataException(BaseSubjectRetirementErrorCodes.BarrierPending);
                p.Evidence($"retirement-barrier\0{barrier.Barrier.SubjectId.Value}\0{barrier.Barrier.Generation}");
            }
            if(p.Index>=barriers.Length){p.Domain=1;p.Index=0;}
            return;
        }
        if (p.Domain == 1)
        {
            while (p.Index < p.Original.SubjectLifecycleMemberships.Count && consumed < take)
            {
                int index = p.Index++; InMemorySubjectLifecycleMembershipRow row = p.Original.SubjectLifecycleMemberships[index];
                checked { consumed++; }
                if (row.ConsumerId != p.Request.ConsumerId || row.ConsumerVersion != p.Request.ConsumerVersion) continue;
                checked { p.Examined++; }
                p.MembershipRemovals.Add(index); p.Evidence($"membership\0{row.ConsumerId}\0{row.FactIndex}");
            }
            if (p.Index >= p.Original.SubjectLifecycleMemberships.Count) { p.Domain = 2; p.Index = 0; }
            return;
        }
        string[] checkpoints = p.Original.SubjectLifecycleCheckpoints.Keys.Order(StringComparer.Ordinal).ToArray();
        while (p.Index < checkpoints.Length && consumed < take)
        {
            string key = checkpoints[p.Index++]; InMemorySubjectLifecycleCheckpointState row = p.Original.SubjectLifecycleCheckpoints[key];
            checked { consumed++; }
            if (row.ConsumerId != p.Request.ConsumerId || row.ConsumerVersion != p.Request.ConsumerVersion) continue;
            checked { p.Examined++; }
            p.CheckpointRemovals.Add(key); p.Evidence($"checkpoint\0{key}");
        }
        if (p.Index >= checkpoints.Length) { checked { p.Examined++; } p.Evidence($"consumer\0{p.Request.ConsumerId}\n{p.Request.ConsumerVersion}"); p.Complete = true; }
    }

    private void PageRebuild(InMemoryLifecycleMaintenanceProgress p, ref int consumed, int take)
    {
        string key = $"{p.Request.ConsumerId}\n{p.Request.ConsumerVersion}";
        InMemorySubjectLifecycleConsumerProjection projection = p.Original.SubjectLifecycleConsumers[key];
        BaseSubjectLifecycleConsumerDefinition definition = _options.SubjectLifecycleConsumers.Single(value => value.Id == p.Request.ConsumerId && value.Version == p.Request.ConsumerVersion);
        p.ProjectionGeneration ??= checked(projection.ProjectionGeneration + 1);
        if (p.Domain == 0)
        {
            while (p.Index < p.Original.SubjectLifecycleMemberships.Count && consumed < take)
            {
                int index = p.Index++; InMemorySubjectLifecycleMembershipRow row = p.Original.SubjectLifecycleMemberships[index];
                checked { consumed++; }
                if (row.ConsumerId != p.Request.ConsumerId || row.ConsumerVersion != p.Request.ConsumerVersion) continue;
                checked { p.Examined++; }
                p.MembershipRemovals.Add(index); p.Evidence($"membership\0{row.ConsumerId}\0{row.FactIndex}");
            }
            if (p.Index >= p.Original.SubjectLifecycleMemberships.Count) { p.Domain = 1; p.Index = 0; }
            return;
        }
        while (p.Index < p.Original.SubjectLifecycleFacts.Count && consumed < take)
        {
            int index = p.Index++; InMemorySubjectLifecycleFactRow row = p.Original.SubjectLifecycleFacts[index];
            checked { consumed++; }
            if (row.Fact.ContractId != p.Request.ContractId || row.Fact.ContractVersion != p.Request.ContractVersion || projection.Cutoff is not null && CompareBoundary(row.Boundary, projection.Cutoff) <= 0) continue;
            checked { p.Examined++; }
            BaseSubjectLifecycleState state = row.Fact.Kind switch { BaseSubjectLifecycleFactKind.Created => row.Fact.Created!.CurrentState, BaseSubjectLifecycleFactKind.Transitioned => row.Fact.Transitioned!.CurrentState, _ => BaseSubjectLifecycleState.Retired };
            if (!definition.ObservedStates.Contains(state)) continue;
            p.MembershipAdds.Add(new(definition.Id, definition.Version, projection.ConsumerChecksum, p.ProjectionGeneration.Value, state, row.Scope, index)); p.Evidence($"membership\0{definition.Id}\0{row.Boundary.CommitPosition.Value}\0{row.Boundary.SubjectId.Value}");
        }
        if (p.Index >= p.Original.SubjectLifecycleFacts.Count) { checked { p.Examined++; } p.Evidence($"consumer\0{key}\0{p.ProjectionGeneration}"); p.Complete = true; }
    }

    private void PageRotate(InMemoryLifecycleMaintenanceProgress p, ref int consumed, int take)
    {
        if (p.ReplacementScopeKey is null)
        {
            if (!byte.TryParse(p.Execution.ReplacementScopeProtectionKeyId, NumberStyles.None, CultureInfo.InvariantCulture, out byte replacement) || replacement == _subjectScopeProtectionKey || !_subjectScopeTokens.CanIssueWithKey(replacement)) throw new InvalidDataException(BaseSubjectErrorCodes.ScopeProtectionRotationConflict);
            p.ReplacementScopeKey = replacement; p.ReplacementScopeGeneration = checked(_subjectScopeProtectionGeneration + 1);
        }
        byte keyId = p.ReplacementScopeKey.Value;
        if (p.Domain == 0) { RotateDictionaryPage(p, p.Original.SubjectLifetimes.Keys.Order(StringComparer.Ordinal).ToArray(), ref consumed, take, key => { InMemorySubjectLifetimeState value=p.Original.SubjectLifetimes[key];p.Working.SubjectLifetimes.Remove(key);string replacement=LifecycleScopeKey(value.Scope,value.ContractId,value.ContractVersion,value.SubjectId,keyId);if(!p.Working.SubjectLifetimes.TryAdd(replacement,value))throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);return $"lifetime\0{replacement}";}); return; }
        if (p.Domain == 1) { RotateDictionaryPage(p, p.Original.SubjectTerminals.Keys.Order(StringComparer.Ordinal).ToArray(), ref consumed, take, key => { InMemorySubjectTerminalState value=p.Original.SubjectTerminals[key];p.Working.SubjectTerminals.Remove(key);string replacement=LifecycleScopeKey(value.Scope,value.ContractId,value.ContractVersion,value.SubjectId,keyId);if(!p.Working.SubjectTerminals.TryAdd(replacement,value))throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);return $"terminal\0{replacement}";}); return; }
        if (p.Domain == 2) { string[] keys=p.Original.SubjectLifecycleConsumers.Keys.Order(StringComparer.Ordinal).ToArray();RotateDictionaryPage(p,keys,ref consumed,take,key=>{InMemorySubjectLifecycleConsumerProjection value=p.Original.SubjectLifecycleConsumers[key];p.Working.SubjectLifecycleConsumers[key]=value with { ProjectionGeneration=checked(value.ProjectionGeneration+1),PublishedGraphGeneration=checked(value.PublishedGraphGeneration+1)};p.ProjectionGeneration=Math.Max(p.ProjectionGeneration??1,value.ProjectionGeneration+1);return $"consumer\0{key}";});return; }
        if (p.Domain == 3) { while(p.Index<p.Original.SubjectLifecycleFacts.Count&&consumed<take){int index=p.Index++;InMemorySubjectLifecycleFactRow value=p.Original.SubjectLifecycleFacts[index];BaseOwnedSubjectScopeEvidence scope=_subjectScopes.Unprotect(value.Scope)??throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);p.Working.SubjectLifecycleFacts[index]=value with { Scope=_subjectScopes.Protect(scope,keyId)};checked{p.Examined++;consumed++;}p.Evidence($"fact\0{index}");}if(p.Index>=p.Original.SubjectLifecycleFacts.Count){p.Domain++;p.Index=0;}return; }
        if (p.Domain == 4) { while(p.Index<p.Original.SubjectLifecycleMemberships.Count&&consumed<take){int index=p.Index++;InMemorySubjectLifecycleMembershipRow value=p.Original.SubjectLifecycleMemberships[index];BaseOwnedSubjectScopeEvidence scope=_subjectScopes.Unprotect(value.Scope)??throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);p.Working.SubjectLifecycleMemberships[index]=value with { ProjectionGeneration=checked(value.ProjectionGeneration+1),Scope=_subjectScopes.Protect(scope,keyId)};checked{p.Examined++;consumed++;}p.Evidence($"membership\0{value.ConsumerId}\0{index}");}if(p.Index>=p.Original.SubjectLifecycleMemberships.Count){p.Domain++;p.Index=0;}return; }
        if (p.Domain == 5) { string[] keys=p.Original.SubjectLifecycleCheckpoints.Keys.Order(StringComparer.Ordinal).ToArray();while(p.Index<keys.Length&&consumed<take){string oldKey=keys[p.Index++];InMemorySubjectLifecycleCheckpointState value=p.Original.SubjectLifecycleCheckpoints[oldKey];BaseOwnedSubjectScopeEvidence scope=_subjectScopes.Unprotect(value.Scope)??throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);BaseProtectedSubjectScope replacement=_subjectScopes.Protect(scope,keyId);string newKey=ProtectedScopeKey(value.ConsumerId,value.ConsumerVersion,replacement);p.Working.SubjectLifecycleCheckpoints.Remove(oldKey);if(!p.Working.SubjectLifecycleCheckpoints.TryAdd(newKey,value with { Scope=replacement,ProjectionGeneration=checked(value.ProjectionGeneration+1),Generation=checked(value.Generation+1)}))throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);checked{p.Examined++;consumed++;}p.Evidence($"checkpoint\0{newKey}");}if(p.Index>=keys.Length){p.Domain++;p.Index=0;}return; }
        if(p.Domain==6){string[] keys=p.Original.SubjectRetirementBarriers.Keys.Order(StringComparer.Ordinal).ToArray();while(p.Index<keys.Length&&consumed<take){string oldKey=keys[p.Index++];InMemorySubjectRetirementBarrierState value=p.Original.SubjectRetirementBarriers[oldKey];BaseOwnedSubjectScopeEvidence scope=_subjectScopes.Unprotect(value.Scope)??throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);BaseProtectedSubjectScope replacement=_subjectScopes.Protect(scope,keyId);string newKey=RetirementKey(replacement,value.Barrier.ContractId,value.Barrier.ContractVersion,value.Barrier.SubjectId,value.Barrier.AuthorityEpoch,value.Barrier.Incarnation);p.Working.SubjectRetirementBarriers.Remove(oldKey);if(!p.Working.SubjectRetirementBarriers.TryAdd(newKey,value with{Scope=replacement}))throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);checked{p.Examined++;p.RetirementExamined++;p.RetirementChanged++;consumed++;}p.Evidence($"retirement-barrier\0{newKey}");}if(p.Index>=keys.Length){p.Domain++;p.Index=0;}return;}
        if(p.Domain==7){string[] keys=p.Original.SubjectRetirementTerminals.Keys.Order(StringComparer.Ordinal).ToArray();while(p.Index<keys.Length&&consumed<take){string oldKey=keys[p.Index++];BaseSubjectRetirementTerminalReceipt receipt=p.Original.SubjectRetirementTerminals[oldKey].Receipt;BaseOwnedSubjectScopeEvidence scope=_subjectScopes.Unprotect(receipt.Scope)??throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);BaseProtectedSubjectScope replacement=_subjectScopes.Protect(scope,keyId);BaseSubjectRetirementTerminalReceipt rotated=receipt with{Scope=replacement,ReceiptChecksum=string.Empty};rotated=rotated with{ReceiptChecksum=BaseSubjectRetirementRegistry.TerminalChecksum(rotated)};string newKey=RetirementKey(replacement,rotated.ContractId,rotated.ContractVersion,rotated.SubjectId,rotated.AuthorityEpoch,rotated.Incarnation);p.Working.SubjectRetirementTerminals.Remove(oldKey);if(!p.Working.SubjectRetirementTerminals.TryAdd(newKey,new(rotated)))throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);checked{p.Examined++;p.RetirementExamined++;p.RetirementChanged++;consumed++;}p.Evidence($"retirement-terminal\0{newKey}\0{rotated.ReceiptChecksum}");}if(p.Index>=keys.Length){p.Domain++;p.Index=0;}return;}
        if(p.Domain==8){while(p.Index<p.Original.SubjectRetirementPublications.Count&&consumed<take){int index=p.Index++;BaseSubjectRetirementPublicationRow row=p.Original.SubjectRetirementPublications[index];if(row.Scope is not null){BaseOwnedSubjectScopeEvidence scope=_subjectScopes.Unprotect(row.Scope)??throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);p.Working.SubjectRetirementPublications[index]=row with{Scope=_subjectScopes.Protect(scope,keyId)};checked{p.RetirementChanged++;}}checked{p.Examined++;p.RetirementExamined++;consumed++;}p.Evidence($"retirement-publication\0{index}");}if(p.Index>=p.Original.SubjectRetirementPublications.Count){p.Domain++;p.Index=0;}return;}
        checked { p.Working.SubjectLifecycleDeliveryEpoch++;p.Examined++;consumed++;if(p.Execution.Retirement is not null){p.Working.SubjectRetirementPosition++;p.RetirementExamined++;p.RetirementChanged++;} }
        p.Evidence($"authority\0{p.ReplacementScopeGeneration}\0{p.Working.SubjectLifecycleDeliveryEpoch}"); p.Complete=true;
    }

    private static void RotateDictionaryPage(InMemoryLifecycleMaintenanceProgress p, string[] keys, ref int consumed, int take, Func<string,string> apply)
    {
        while(p.Index<keys.Length&&consumed<take){string evidence=apply(keys[p.Index++]);checked{p.Examined++;consumed++;}p.Evidence(evidence);}if(p.Index>=keys.Length){p.Domain++;p.Index=0;}
    }

    private string LifecycleScopeKey(BaseOwnedSubjectScopeEvidence scope,string contractId,int version,BaseSubjectId subjectId,byte keyId)=>$"{(int)scope.Kind}\n{Convert.ToHexString(_subjectScopes.Protect(scope,keyId).IndexDigest)}\n{contractId}\n{version}\n{subjectId.Value}";

    private void FinalizeLifecycleStage(InMemoryLifecycleMaintenanceProgress p)
    {
        if (p.Request.Kind is BaseSubjectLifecycleMaintenanceKind.Prune or BaseSubjectLifecycleMaintenanceKind.RemoveConsumer or BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection)
        {
            var retainedMemberships=p.Working.SubjectLifecycleMemberships.Select((value,index)=>(value,index)).Where(pair=>!p.MembershipRemovals.Contains(pair.index)).Select(pair=>pair.value).ToList();
            p.Working.SubjectLifecycleMemberships.Clear();p.Working.SubjectLifecycleMemberships.AddRange(retainedMemberships);
        }
        if (p.Request.Kind==BaseSubjectLifecycleMaintenanceKind.Prune&&p.FactRemovals.Count!=0)
        {
            var mapping=new Dictionary<int,int>();var facts=new List<InMemorySubjectLifecycleFactRow>();for(int index=0;index<p.Working.SubjectLifecycleFacts.Count;index++){if(p.FactRemovals.Contains(index))continue;mapping[index]=facts.Count;facts.Add(p.Working.SubjectLifecycleFacts[index]);}
            p.Working.SubjectLifecycleFacts.Clear();p.Working.SubjectLifecycleFacts.AddRange(facts);for(int index=0;index<p.Working.SubjectLifecycleMemberships.Count;index++)p.Working.SubjectLifecycleMemberships[index]=p.Working.SubjectLifecycleMemberships[index] with { FactIndex=mapping[p.Working.SubjectLifecycleMemberships[index].FactIndex]};
        }
        if (p.Request.Kind==BaseSubjectLifecycleMaintenanceKind.RemoveConsumer)
        {
            if(p.Execution.Retirement is not null)
            {
                BaseSubjectRetirementPolicy policy=_options.SubjectRetirementPolicies.SingleOrDefault(value=>value.ContractId==p.Request.ContractId&&value.ContractVersion==p.Request.ContractVersion)??throw new InvalidDataException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
                BaseAcceptedRetirementConsumer accepted=policy.AcceptedConsumers.SingleOrDefault(value=>value.ConsumerId==p.Request.ConsumerId&&value.ConsumerVersion==p.Request.ConsumerVersion)??throw new InvalidDataException(BaseSubjectRetirementErrorCodes.RegistrationConflict);
                string previousSet=BaseSubjectRetirementRegistry.AcceptedSetChecksum(policy.AcceptedConsumers);string publishedSet=BaseSubjectRetirementRegistry.AcceptedSetChecksum(policy.AcceptedConsumers.Where(value=>value.ConsumerId!=accepted.ConsumerId||value.ConsumerVersion!=accepted.ConsumerVersion));long position=checked(++p.Working.SubjectRetirementPosition);var publication=new BaseSubjectRetirementPublicationRow{Scope=null,Fact=BaseSubjectRetirementRegistry.SealPublication(new(){Position=new(position),Kind=BaseSubjectRetirementPublicationKind.ConsumerSetChanged,ConsumerSet=new(){ContractId=policy.ContractId,ContractVersion=policy.ContractVersion,PreviousConsumerSetChecksum=previousSet,PublishedConsumerSetChecksum=publishedSet,PreviousGraphGeneration=p.Execution.Retirement.ExpectedGraphGeneration,PublishedGraphGeneration=checked(p.Execution.Retirement.ExpectedGraphGeneration+1),RemovedConsumerId=accepted.ConsumerId}})};BaseSubjectRetirementRegistry.ValidatePublication(publication);p.Working.SubjectRetirementPublications.Add(publication);p.Evidence($"retirement-consumer\0{accepted.ConsumerId}\0{accepted.ConsumerVersion}\0{position}");
                checked{p.RetirementChanged++;}
            }
            foreach(string key in p.CheckpointRemovals)p.Working.SubjectLifecycleCheckpoints.Remove(key);p.Working.SubjectLifecycleConsumers.Remove($"{p.Request.ConsumerId}\n{p.Request.ConsumerVersion}");
        }
        if (p.Request.Kind==BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection)
        {
            p.Working.SubjectLifecycleMemberships.AddRange(p.MembershipAdds);string key=$"{p.Request.ConsumerId}\n{p.Request.ConsumerVersion}";InMemorySubjectLifecycleConsumerProjection projection=p.Working.SubjectLifecycleConsumers[key];p.Working.SubjectLifecycleConsumers[key]=projection with { ProjectionGeneration=p.ProjectionGeneration!.Value };
            foreach((string checkpointKey,InMemorySubjectLifecycleCheckpointState checkpoint) in p.Working.SubjectLifecycleCheckpoints.Where(pair=>pair.Value.ConsumerId==p.Request.ConsumerId&&pair.Value.ConsumerVersion==p.Request.ConsumerVersion).ToArray())p.Working.SubjectLifecycleCheckpoints[checkpointKey]=checkpoint with { ProjectionGeneration=p.ProjectionGeneration.Value,Generation=checked(checkpoint.Generation+1),Overtaken=false};
        }
        p.Working.RebuildSubjectLifecycleMembershipIndex();
    }

    private static OperationResult<BaseSubjectLifecycleMaintenanceResult> LifecycleMaintenanceFailure(string code,OperationStatus status,ErrorCategory category)=>new(){Status=status,Error=new BaseError{Code=code,Category=category,Message="The subject lifecycle maintenance operation failed."}};
    private static OperationResult<T> LifecycleMaintenanceRequired<T>() => OperationResults.CapabilityUnavailable<T>(BaseSubjectFailureContract.Error(BaseSubjectErrorCodes.MaintenanceRequired));

    private sealed class InMemoryLifecycleMaintenanceProgress
    {
        internal InMemoryLifecycleMaintenanceProgress(BaseSubjectAuthorityMaintenanceExecutionRequest request,InMemoryStoreState original,InMemoryStoreState working){Execution=request;Request=request.Lifecycle;Original=original;Working=working;RollingChecksum=SHA256.HashData("base.subjectLifecycle.maintenance.empty.v1"u8);}
        internal BaseSubjectAuthorityMaintenanceExecutionRequest Execution { get; }
        internal BaseSubjectLifecycleMaintenancePlan Request { get; }
        internal InMemoryStoreState Original { get; }
        internal InMemoryStoreState Working { get; }
        internal int Domain;internal int Index;internal int CompletedPages;internal bool Complete;internal long Examined;internal long Changed;internal long RetirementExamined;internal long RetirementChanged;internal long CanonicalBytes;internal byte[] RollingChecksum;internal long? ProjectionGeneration;internal byte? ReplacementScopeKey;internal long ReplacementScopeGeneration;
        internal HashSet<int> MembershipRemovals { get; }=[];internal HashSet<int> FactRemovals { get; }=[];internal HashSet<int> RetainedFactIndexes { get; }=[];internal HashSet<string> CheckpointRemovals { get; }=new(StringComparer.Ordinal);internal List<InMemorySubjectLifecycleMembershipRow> MembershipAdds { get; }=[];
        private List<byte[]> EvidenceBytes { get; }=[];
        internal bool Matches(BaseSubjectAuthorityMaintenanceExecutionRequest value)=>Request.Kind==value.Lifecycle.Kind&&Execution.Identity.Scope==value.Identity.Scope&&Execution.Identity.Operation==value.Identity.Operation&&Execution.Identity.IdempotencyKey==value.Identity.IdempotencyKey&&CryptographicOperations.FixedTimeEquals(Execution.Identity.Fingerprint.ToArray(),value.Identity.Fingerprint.ToArray())&&CryptographicOperations.FixedTimeEquals(Execution.CombinedPlanChecksum,value.CombinedPlanChecksum);
        internal void Evidence(string value){byte[] bytes=Encoding.UTF8.GetBytes(value);EvidenceBytes.Add(bytes);RollingChecksum=Roll(RollingChecksum,bytes);checked{Changed++;CanonicalBytes+=4+bytes.Length;}}
        internal void ValidateEvidence(){byte[] rolling=SHA256.HashData("base.subjectLifecycle.maintenance.empty.v1"u8);long bytes=0;foreach(byte[] evidence in EvidenceBytes){rolling=Roll(rolling,evidence);checked{bytes+=4+evidence.Length;}}if(EvidenceBytes.Count!=Changed||bytes!=CanonicalBytes||!CryptographicOperations.FixedTimeEquals(rolling,RollingChecksum))throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);}
        private static byte[] Roll(byte[] previous,byte[] bytes){byte[] input=new byte[previous.Length+4+bytes.Length];previous.CopyTo(input,0);System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(previous.Length,4),bytes.Length);bytes.CopyTo(input,previous.Length+4);return SHA256.HashData(input);}
    }
}
