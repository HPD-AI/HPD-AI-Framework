namespace HPD.Base;

/// <summary>Contains one inert generator-owned retirement consumer identity.</summary>
public sealed class BaseGeneratedSubjectRetirementConsumerIdentity<TSubject>
{
    internal BaseGeneratedSubjectRetirementConsumerIdentity(BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> lifecycle, BaseSubjectRetirementConsumerDefinition definition, string checksum)
    { Lifecycle = lifecycle; Definition = definition; Checksum = checksum; }
    internal BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> Lifecycle { get; }
    internal BaseSubjectRetirementConsumerDefinition Definition { get; }
    internal string Checksum { get; }
}

/// <summary>Provides generated-only retirement identity construction.</summary>
public static class BaseGeneratedSubjectRetirementConsumers
{
    /// <summary>Creates one immutable generated retirement identity.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseGeneratedSubjectRetirementConsumerIdentity<TSubject> Register<TSubject>(BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> lifecycle, BaseSubjectRetirementConsumerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(lifecycle); BaseSubjectRetirementConsumerDefinition normalized=BaseSubjectRetirementRegistry.Normalize(definition);
        if(normalized.ConsumerId!=lifecycle.Definition.Id||normalized.ConsumerVersion!=lifecycle.Definition.Version||normalized.LifecycleConsumerChecksum!=lifecycle.Checksum)throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.RegistrationConflict);
        return new(lifecycle,normalized,BaseSubjectRetirementRegistry.ConsumerChecksum(normalized));
    }
}

/// <summary>Resolves mutually installed retirement consumers for one principal-bound session.</summary>
public sealed class BaseSubjectRetirementSession
{
    private readonly BaseSession _session; internal BaseSubjectRetirementSession(BaseSession session)=>_session=session;
    /// <summary>Resolves an inert generated identity to its exact executable retirement handle.</summary>
    public BaseInstalledSubjectRetirementConsumer<TSubject> Get<TSubject>(BaseGeneratedSubjectRetirementConsumerIdentity<TSubject> identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        BaseSubjectRetirementRegistry registry=_session.Services.GetService(typeof(BaseSubjectRetirementRegistry)) as BaseSubjectRetirementRegistry??throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid);
        BaseInstalledSubjectRetirementConsumer installed=registry.FindConsumer(identity.Definition.ConsumerId,identity.Definition.ConsumerVersion)??throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid);
        if(installed.Checksum!=identity.Checksum||installed.Definition.Participation!=identity.Definition.Participation)throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.RegistrationConflict);
        IBaseSubjectRetirementRuntime runtime=_session.Services.GetService(typeof(IBaseSubjectRetirementRuntime)) as IBaseSubjectRetirementRuntime??throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        return new(_session,runtime,installed,_session.SubjectLifecycle.Get(identity.Lifecycle));
    }

    /// <summary>Reads one bounded, grant-authorized retirement-barrier page.</summary>
    public ValueTask<BaseResult<BaseSubjectRetirementPage>> ReadBarriersAsync(
        string contractId, int contractVersion, BaseSubjectRetirementBarrierState? state,
        BaseSubjectRetirementCursor? after, int take, CancellationToken cancellationToken = default)
    {
        IBaseSubjectRetirementRuntime runtime = _session.Services.GetService(typeof(IBaseSubjectRetirementRuntime)) as IBaseSubjectRetirementRuntime
            ?? throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        return runtime.ReadBarriersAsync(_session, contractId, contractVersion, state, after, take, cancellationToken);
    }

    /// <summary>Inspects one exact grant-authorized retirement lifetime.</summary>
    public ValueTask<BaseResult<BaseSubjectRetirementInspection>> InspectAsync(BaseSubjectRetirementInspectionRequest request, CancellationToken cancellationToken = default)
    {
        IBaseSubjectRetirementRuntime runtime = _session.Services.GetService(typeof(IBaseSubjectRetirementRuntime)) as IBaseSubjectRetirementRuntime
            ?? throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        return runtime.InspectAsync(_session, request, cancellationToken);
    }

    /// <summary>Processes one identified retirement timeout.</summary>
    public ValueTask<BaseResult<BaseSubjectRetirementTimeoutResult>> ProcessTimeoutAsync(BaseSubjectRetirementTimeoutRequest request, CancellationToken cancellationToken = default)
    {
        IBaseSubjectRetirementRuntime runtime = _session.Services.GetService(typeof(IBaseSubjectRetirementRuntime)) as IBaseSubjectRetirementRuntime
            ?? throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        return runtime.TimeoutAsync(_session, request, cancellationToken);
    }

    /// <summary>Applies one identified audited retirement-barrier override.</summary>
    public ValueTask<BaseResult<BaseSubjectRetirementOverrideResult>> OverrideAsync(BaseSubjectRetirementOverrideRequest request, CancellationToken cancellationToken = default)
    {
        IBaseSubjectRetirementRuntime runtime = _session.Services.GetService(typeof(IBaseSubjectRetirementRuntime)) as IBaseSubjectRetirementRuntime
            ?? throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        return runtime.OverrideAsync(_session, request, cancellationToken);
    }

    /// <summary>Performs one identified final physical purge.</summary>
    public ValueTask<BaseResult<BaseSubjectFinalPurgeResult>> PurgeAsync(BaseSubjectFinalPurgeRequest request, CancellationToken cancellationToken = default)
    {
        IBaseSubjectRetirementRuntime runtime = _session.Services.GetService(typeof(IBaseSubjectRetirementRuntime)) as IBaseSubjectRetirementRuntime
            ?? throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        return runtime.PurgeAsync(_session, request, cancellationToken);
    }

    /// <summary>Removes one mutually accepted consumer after bounded barrier reconciliation.</summary>
    public ValueTask<BaseResult<BaseSubjectRetirementConsumerRemovalResult>> RemoveConsumerAsync(BaseSubjectRetirementConsumerRemovalRequest request,CancellationToken cancellationToken=default)
    {
        IBaseSubjectRetirementRuntime runtime=_session.Services.GetService(typeof(IBaseSubjectRetirementRuntime)) as IBaseSubjectRetirementRuntime??throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        return runtime.RemoveConsumerAsync(_session,request,cancellationToken);
    }
}

/// <summary>Executes one exact installed retirement consumer without exposing provider authority.</summary>
public sealed class BaseInstalledSubjectRetirementConsumer<TSubject>
{
    private readonly BaseSession _session;private readonly IBaseSubjectRetirementRuntime _runtime;private readonly BaseInstalledSubjectRetirementConsumer _installed;private readonly BaseInstalledSubjectLifecycleConsumer<TSubject> _lifecycle;
    internal BaseInstalledSubjectRetirementConsumer(BaseSession session,IBaseSubjectRetirementRuntime runtime,BaseInstalledSubjectRetirementConsumer installed,BaseInstalledSubjectLifecycleConsumer<TSubject> lifecycle){_session=session;_runtime=runtime;_installed=installed;_lifecycle=lifecycle;}
    /// <summary>Enumerates advisory deliveries. Required consumers cannot invoke this surface.</summary>
    public async IAsyncEnumerable<BaseSubjectAdvisoryLifecycleDelivery<TSubject>> ReadAdvisoryAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken=default)
    {
        if(_installed.Definition.Participation!=BaseSubjectRetirementParticipation.AdvisoryAcknowledgement)throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid);
        await foreach(BaseSubjectLifecycleDelivery<TSubject> delivery in _lifecycle.ReadAsync(cancellationToken).ConfigureAwait(false))yield return (await _runtime.IssueAdvisoryAsync(_session,_installed,delivery,cancellationToken).ConfigureAwait(false)).RequireValue();
    }
    /// <summary>Enumerates required deliveries. Advisory consumers cannot invoke this surface.</summary>
    public async IAsyncEnumerable<BaseSubjectRequiredLifecycleDelivery<TSubject>> ReadRequiredAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken=default)
    {
        if(_installed.Definition.Participation!=BaseSubjectRetirementParticipation.RequiredBeforePurge)throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid);
        await foreach(BaseSubjectLifecycleDelivery<TSubject> delivery in _lifecycle.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if(delivery.Fact.Fact.Transitioned?.CurrentState!=BaseSubjectLifecycleState.Tombstoned)continue;
            yield return (await _runtime.IssueRequiredAsync(_session,_installed,delivery,cancellationToken).ConfigureAwait(false)).RequireValue();
        }
    }
    /// <summary>Submits one advisory acknowledgement through its identified receipt authority.</summary>
    public ValueTask<BaseResult<BaseSubjectAcknowledgementResult>> AcknowledgeAsync(BaseSubjectAdvisoryAcknowledgementEvidence<TSubject> evidence,BaseSubjectAcknowledgementDisposition disposition,BaseMutationRequestIdentity identity,BaseActivationGuard? activationGuard=null,CancellationToken cancellationToken=default)=>_runtime.AcknowledgeAsync(_session,_installed,evidence.EncodedToken,BaseSubjectRetirementParticipation.AdvisoryAcknowledgement,disposition,identity,activationGuard,cancellationToken);
    /// <summary>Submits one required acknowledgement through its barrier-bound receipt authority.</summary>
    public ValueTask<BaseResult<BaseSubjectAcknowledgementResult>> AcknowledgeAsync(BaseSubjectRequiredAcknowledgementEvidence<TSubject> evidence,BaseSubjectAcknowledgementDisposition disposition,BaseMutationRequestIdentity identity,BaseActivationGuard? activationGuard=null,CancellationToken cancellationToken=default)=>_runtime.AcknowledgeAsync(_session,_installed,evidence.EncodedToken,BaseSubjectRetirementParticipation.RequiredBeforePurge,disposition,identity,activationGuard,cancellationToken);
}

internal interface IBaseSubjectRetirementRuntime
{
    ValueTask<bool> AuthorizeGenerationAsync(BaseSession session,BaseInstalledSubjectRetirementConsumer installed,CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectAdvisoryLifecycleDelivery<TSubject>>> IssueAdvisoryAsync<TSubject>(BaseSession session,BaseInstalledSubjectRetirementConsumer installed,BaseSubjectLifecycleDelivery<TSubject> delivery,CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectRequiredLifecycleDelivery<TSubject>>> IssueRequiredAsync<TSubject>(BaseSession session,BaseInstalledSubjectRetirementConsumer installed,BaseSubjectLifecycleDelivery<TSubject> delivery,CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectAcknowledgementResult>> AcknowledgeAsync(BaseSession session,BaseInstalledSubjectRetirementConsumer installed,ReadOnlyMemory<byte> evidence,BaseSubjectRetirementParticipation participation,BaseSubjectAcknowledgementDisposition disposition,BaseMutationRequestIdentity identity,BaseActivationGuard? activationGuard,CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectRetirementPage>> ReadBarriersAsync(BaseSession session,string contractId,int contractVersion,BaseSubjectRetirementBarrierState? state,BaseSubjectRetirementCursor? after,int take,CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectRetirementInspection>> InspectAsync(BaseSession session,BaseSubjectRetirementInspectionRequest request,CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectRetirementTimeoutResult>> TimeoutAsync(BaseSession session,BaseSubjectRetirementTimeoutRequest request,CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectRetirementOverrideResult>> OverrideAsync(BaseSession session,BaseSubjectRetirementOverrideRequest request,CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectFinalPurgeResult>> PurgeAsync(BaseSession session,BaseSubjectFinalPurgeRequest request,CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectRetirementConsumerRemovalResult>> RemoveConsumerAsync(BaseSession session,BaseSubjectRetirementConsumerRemovalRequest request,CancellationToken cancellationToken);
}
