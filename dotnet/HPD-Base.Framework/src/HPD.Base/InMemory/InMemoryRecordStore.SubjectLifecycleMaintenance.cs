using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore
{
    private volatile InMemoryLifecycleMaintenanceProgress? _lifecycleMaintenance;

    private async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteBoundedLifecycleMaintenanceAsync(
        BaseSubjectLifecycleMaintenanceExecutionRequest request,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string receiptKey = ReceiptKey(request.Identity);
            if (_publishedState.Receipts.TryGetValue(receiptKey, out InMemoryMutationReceipt? receipt)
                && receipt.ExpiresAt > _timeProvider.GetUtcNow())
            {
                if (!CryptographicOperations.FixedTimeEquals(receipt.Fingerprint, request.Identity.Fingerprint.ToArray())
                    || !CryptographicOperations.FixedTimeEquals(receipt.StructuralDigest, request.PlanChecksum)
                    || receipt.Result.SubjectLifecycleMaintenance is not { } stored)
                    return LifecycleMaintenanceFailure(BaseMutationRequestErrorCodes.FingerprintConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
                return OperationResults.Ok(stored with { RollingChecksum = new string(stored.RollingChecksum.AsSpan()), Duplicate = true });
            }

            InMemoryLifecycleMaintenanceProgress progress = _lifecycleMaintenance ??= CreateProgress(request);
            if (!progress.Matches(request))
                return LifecycleMaintenanceFailure(BaseSubjectErrorCodes.MaintenanceRequired, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);

            while (!progress.Complete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.ValidateEvidence();
                ExecuteLifecyclePage(progress, request.PageSize);
                checked { progress.CompletedPages++; }
                if (_options.SubjectLifecycleMaintenancePageCompleted is { } completed)
                    await completed(progress.CompletedPages, cancellationToken).ConfigureAwait(false);
                await Task.Yield();
            }

            progress.ValidateEvidence();
            FinalizeLifecycleStage(progress);
            var result = new BaseSubjectLifecycleMaintenanceResult
            {
                Kind = request.Kind,
                ExaminedCount = progress.Examined,
                ChangedCount = progress.Changed,
                CanonicalBytes = progress.CanonicalBytes,
                RollingChecksum = Convert.ToHexStringLower(progress.RollingChecksum),
                DeliveryEpoch = progress.Working.SubjectLifecycleDeliveryEpoch,
                ProjectionGeneration = progress.ProjectionGeneration,
                Duplicate = false,
            };
            progress.Working.Receipts[receiptKey] = new InMemoryMutationReceipt(
                request.Identity.Fingerprint.ToArray(), request.PlanChecksum.ToArray(),
                new BaseAtomicReceiptResult { Kind = BaseAtomicReceiptResultKind.SubjectLifecycleMaintenance, Mutations = [], SubjectLifecycleMaintenance = result with { RollingChecksum = new string(result.RollingChecksum.AsSpan()) } },
                _timeProvider.GetUtcNow().AddDays(30));
            _publishedState = progress.Working;
            checked { _generation++; }
            if (progress.ReplacementScopeKey is { } replacement)
            {
                _subjectScopeProtectionKey = replacement;
                _subjectScopeProtectionKeyId = replacement.ToString(CultureInfo.InvariantCulture);
                _subjectScopeProtectionGeneration = progress.ReplacementScopeGeneration;
            }
            _lifecycleMaintenance = null;
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    private InMemoryLifecycleMaintenanceProgress CreateProgress(BaseSubjectLifecycleMaintenanceExecutionRequest request)
    {
        InMemoryStoreState current = _publishedState;
        InMemorySubjectContractState? contract = request.ContractId is null ? null : current.SubjectContracts.GetValueOrDefault(SubjectContractKey(request.ContractId, request.ContractVersion!.Value));
        long restoreEpoch = contract?.RestoreEpoch ?? current.SubjectContracts.Values.Select(static value => value.RestoreEpoch).DefaultIfEmpty(0).Max();
        if (request.ExpectedStoreGeneration != 1 || request.ExpectedSchemaGeneration != 1 || restoreEpoch != request.ExpectedRestoreEpoch
            || current.SubjectLifecycleDeliveryEpoch != request.ExpectedDeliveryEpoch || request.ExpectedScopeProtectionGeneration != _subjectScopeProtectionGeneration
            || !string.Equals(request.ExpectedScopeProtectionKeyId, _subjectScopeProtectionKeyId, StringComparison.Ordinal))
            throw new InvalidDataException(BaseSubjectErrorCodes.ScopeProtectionRotationConflict);
        if (request.ExpectedProjectionGeneration is long expected
            && (!current.SubjectLifecycleConsumers.TryGetValue($"{request.ConsumerId}\n{request.ConsumerVersion}", out InMemorySubjectLifecycleConsumerProjection? projection)
                || projection.ProjectionGeneration != expected))
            throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleRegistrationConflict);
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
                default: progress.Complete = true; break;
            }
        }
    }

    private static void PagePrune(InMemoryLifecycleMaintenanceProgress p, ref int consumed, int take)
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
                InMemorySubjectLifecycleCheckpointState? checkpoint = p.Original.SubjectLifecycleCheckpoints.Values.SingleOrDefault(value => value.ConsumerId == membership.ConsumerId && value.ConsumerVersion == membership.ConsumerVersion && ScopeEquals(value.Scope, row.Scope));
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
                bool terminal = p.Original.SubjectTerminals.Values.Any(value => value.ContractId == row.Fact.ContractId && value.ContractVersion == row.Fact.ContractVersion && ScopeEquals(value.Scope, row.Scope) && value.RetiredPosition == row.Boundary.CommitPosition.Value && value.SubjectId.Equals(row.Boundary.SubjectId) && value.AuthorityEpoch.Equals(row.Boundary.AuthorityEpoch) && value.Incarnation.Equals(row.Boundary.Incarnation) && value.SubjectSequence == row.Boundary.SubjectSequence);
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
            p.MembershipAdds.Add(new(definition.Id, definition.Version, projection.ConsumerChecksum, p.ProjectionGeneration.Value, state, index)); p.Evidence($"membership\0{definition.Id}\0{row.Boundary.CommitPosition.Value}\0{row.Boundary.SubjectId.Value}");
        }
        if (p.Index >= p.Original.SubjectLifecycleFacts.Count) { checked { p.Examined++; } p.Evidence($"consumer\0{key}\0{p.ProjectionGeneration}"); p.Complete = true; }
    }

    private void PageRotate(InMemoryLifecycleMaintenanceProgress p, ref int consumed, int take)
    {
        if (p.ReplacementScopeKey is null)
        {
            if (!byte.TryParse(p.Request.ReplacementScopeProtectionKeyId, NumberStyles.None, CultureInfo.InvariantCulture, out byte replacement) || replacement == _subjectScopeProtectionKey || !_subjectScopeTokens.CanIssueWithKey(replacement)) throw new InvalidDataException(BaseSubjectErrorCodes.ScopeProtectionRotationConflict);
            p.ReplacementScopeKey = replacement; p.ReplacementScopeGeneration = checked(_subjectScopeProtectionGeneration + 1);
        }
        byte keyId = p.ReplacementScopeKey.Value;
        if (p.Domain == 0) { RotateDictionaryPage(p, p.Original.SubjectLifetimes.Keys.Order(StringComparer.Ordinal).ToArray(), ref consumed, take, key => { InMemorySubjectLifetimeState value=p.Original.SubjectLifetimes[key];p.Working.SubjectLifetimes.Remove(key);string replacement=LifecycleScopeKey(value.Scope,value.ContractId,value.ContractVersion,value.SubjectId,keyId);if(!p.Working.SubjectLifetimes.TryAdd(replacement,value))throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);return $"lifetime\0{replacement}";}); return; }
        if (p.Domain == 1) { RotateDictionaryPage(p, p.Original.SubjectTerminals.Keys.Order(StringComparer.Ordinal).ToArray(), ref consumed, take, key => { InMemorySubjectTerminalState value=p.Original.SubjectTerminals[key];p.Working.SubjectTerminals.Remove(key);string replacement=LifecycleScopeKey(value.Scope,value.ContractId,value.ContractVersion,value.SubjectId,keyId);if(!p.Working.SubjectTerminals.TryAdd(replacement,value))throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);return $"terminal\0{replacement}";}); return; }
        if (p.Domain == 2) { string[] keys=p.Original.SubjectLifecycleConsumers.Keys.Order(StringComparer.Ordinal).ToArray();RotateDictionaryPage(p,keys,ref consumed,take,key=>{InMemorySubjectLifecycleConsumerProjection value=p.Original.SubjectLifecycleConsumers[key];p.Working.SubjectLifecycleConsumers[key]=value with { ProjectionGeneration=checked(value.ProjectionGeneration+1),PublishedGraphGeneration=checked(value.PublishedGraphGeneration+1)};p.ProjectionGeneration=Math.Max(p.ProjectionGeneration??1,value.ProjectionGeneration+1);return $"consumer\0{key}";});return; }
        if (p.Domain == 3) { while(p.Index<p.Original.SubjectLifecycleMemberships.Count&&consumed<take){int index=p.Index++;InMemorySubjectLifecycleMembershipRow value=p.Original.SubjectLifecycleMemberships[index];p.Working.SubjectLifecycleMemberships[index]=value with { ProjectionGeneration=checked(value.ProjectionGeneration+1)};checked{p.Examined++;consumed++;}p.Evidence($"membership\0{value.ConsumerId}\0{index}");}if(p.Index>=p.Original.SubjectLifecycleMemberships.Count){p.Domain++;p.Index=0;}return; }
        if (p.Domain == 4) { RotateDictionaryPage(p,p.Original.SubjectLifecycleCheckpoints.Keys.Order(StringComparer.Ordinal).ToArray(),ref consumed,take,key=>{InMemorySubjectLifecycleCheckpointState value=p.Original.SubjectLifecycleCheckpoints[key];p.Working.SubjectLifecycleCheckpoints[key]=value with { ProjectionGeneration=checked(value.ProjectionGeneration+1),Generation=checked(value.Generation+1)};return $"checkpoint\0{key}";});return; }
        checked { p.Working.SubjectLifecycleDeliveryEpoch++; p.Examined++; consumed++; }
        p.Evidence($"authority\0{p.ReplacementScopeGeneration}\0{p.Working.SubjectLifecycleDeliveryEpoch}"); p.Complete=true;
    }

    private static void RotateDictionaryPage(InMemoryLifecycleMaintenanceProgress p, string[] keys, ref int consumed, int take, Func<string,string> apply)
    {
        while(p.Index<keys.Length&&consumed<take){string evidence=apply(keys[p.Index++]);checked{p.Examined++;consumed++;}p.Evidence(evidence);}if(p.Index>=keys.Length){p.Domain++;p.Index=0;}
    }

    private string LifecycleScopeKey(BaseOwnedSubjectScopeEvidence scope,string contractId,int version,BaseSubjectId subjectId,byte keyId)=>$"{(int)scope.Kind}\n{Convert.ToHexString(_subjectScopes.Protect(scope,keyId).IndexDigest)}\n{contractId}\n{version}\n{subjectId.Value}";

    private static void FinalizeLifecycleStage(InMemoryLifecycleMaintenanceProgress p)
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
            foreach(string key in p.CheckpointRemovals)p.Working.SubjectLifecycleCheckpoints.Remove(key);p.Working.SubjectLifecycleConsumers.Remove($"{p.Request.ConsumerId}\n{p.Request.ConsumerVersion}");
        }
        if (p.Request.Kind==BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection)
        {
            p.Working.SubjectLifecycleMemberships.AddRange(p.MembershipAdds);string key=$"{p.Request.ConsumerId}\n{p.Request.ConsumerVersion}";InMemorySubjectLifecycleConsumerProjection projection=p.Working.SubjectLifecycleConsumers[key];p.Working.SubjectLifecycleConsumers[key]=projection with { ProjectionGeneration=p.ProjectionGeneration!.Value };
            foreach((string checkpointKey,InMemorySubjectLifecycleCheckpointState checkpoint) in p.Working.SubjectLifecycleCheckpoints.Where(pair=>pair.Value.ConsumerId==p.Request.ConsumerId&&pair.Value.ConsumerVersion==p.Request.ConsumerVersion).ToArray())p.Working.SubjectLifecycleCheckpoints[checkpointKey]=checkpoint with { ProjectionGeneration=p.ProjectionGeneration.Value,Generation=checked(checkpoint.Generation+1),Overtaken=false};
        }
    }

    private static OperationResult<BaseSubjectLifecycleMaintenanceResult> LifecycleMaintenanceFailure(string code,OperationStatus status,ErrorCategory category)=>new(){Status=status,Error=new BaseError{Code=code,Category=category,Message="The subject lifecycle maintenance operation failed."}};
    private static OperationResult<T> LifecycleMaintenanceRequired<T>() => OperationResults.CapabilityUnavailable<T>(BaseSubjectFailureContract.Error(BaseSubjectErrorCodes.MaintenanceRequired));

    private sealed class InMemoryLifecycleMaintenanceProgress
    {
        internal InMemoryLifecycleMaintenanceProgress(BaseSubjectLifecycleMaintenanceExecutionRequest request,InMemoryStoreState original,InMemoryStoreState working){Request=request;Original=original;Working=working;RollingChecksum=SHA256.HashData("base.subjectLifecycle.maintenance.empty.v1"u8);}
        internal BaseSubjectLifecycleMaintenanceExecutionRequest Request { get; }
        internal InMemoryStoreState Original { get; }
        internal InMemoryStoreState Working { get; }
        internal int Domain;internal int Index;internal int CompletedPages;internal bool Complete;internal long Examined;internal long Changed;internal long CanonicalBytes;internal byte[] RollingChecksum;internal long? ProjectionGeneration;internal byte? ReplacementScopeKey;internal long ReplacementScopeGeneration;
        internal HashSet<int> MembershipRemovals { get; }=[];internal HashSet<int> FactRemovals { get; }=[];internal HashSet<int> RetainedFactIndexes { get; }=[];internal HashSet<string> CheckpointRemovals { get; }=new(StringComparer.Ordinal);internal List<InMemorySubjectLifecycleMembershipRow> MembershipAdds { get; }=[];
        private List<byte[]> EvidenceBytes { get; }=[];
        internal bool Matches(BaseSubjectLifecycleMaintenanceExecutionRequest value)=>Request.Kind==value.Kind&&Request.Identity.Scope==value.Identity.Scope&&Request.Identity.Operation==value.Identity.Operation&&Request.Identity.IdempotencyKey==value.Identity.IdempotencyKey&&CryptographicOperations.FixedTimeEquals(Request.Identity.Fingerprint.ToArray(),value.Identity.Fingerprint.ToArray())&&CryptographicOperations.FixedTimeEquals(Request.PlanChecksum,value.PlanChecksum);
        internal void Evidence(string value){byte[] bytes=Encoding.UTF8.GetBytes(value);EvidenceBytes.Add(bytes);RollingChecksum=Roll(RollingChecksum,bytes);checked{Changed++;CanonicalBytes+=4+bytes.Length;}}
        internal void ValidateEvidence(){byte[] rolling=SHA256.HashData("base.subjectLifecycle.maintenance.empty.v1"u8);long bytes=0;foreach(byte[] evidence in EvidenceBytes){rolling=Roll(rolling,evidence);checked{bytes+=4+evidence.Length;}}if(EvidenceBytes.Count!=Changed||bytes!=CanonicalBytes||!CryptographicOperations.FixedTimeEquals(rolling,RollingChecksum))throw new InvalidDataException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);}
        private static byte[] Roll(byte[] previous,byte[] bytes){byte[] input=new byte[previous.Length+4+bytes.Length];previous.CopyTo(input,0);System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(previous.Length,4),bytes.Length);bytes.CopyTo(input,previous.Length+4);return SHA256.HashData(input);}
    }
}
