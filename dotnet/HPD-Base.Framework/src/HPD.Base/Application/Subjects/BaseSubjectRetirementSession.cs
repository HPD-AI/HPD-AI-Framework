using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Contains one inert exporter-owned coordinated-retirement policy identity.</summary>
/// <typeparam name="TSubject">The exact exported-subject marker type.</typeparam>
public sealed class BaseGeneratedSubjectRetirementPolicyIdentity<TSubject>
{
    internal BaseGeneratedSubjectRetirementPolicyIdentity(BaseSubjectRetirementPolicy policy) => Policy = policy;
    internal BaseSubjectRetirementPolicy Policy { get; }
}

/// <summary>Contains one inert generator-owned retirement consumer identity.</summary>
/// <typeparam name="TSubject">The exact exported-subject marker type.</typeparam>
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
    internal static BaseGeneratedSubjectRetirementConsumerIdentity<TSubject> Register<TSubject>(BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> lifecycle, BaseSubjectRetirementConsumerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(lifecycle); BaseSubjectRetirementConsumerDefinition normalized=BaseSubjectRetirementRegistry.Normalize(definition);
        if(normalized.ConsumerId!=lifecycle.Definition.Id||normalized.ConsumerVersion!=lifecycle.Definition.Version||normalized.LifecycleConsumerChecksum!=lifecycle.Checksum)throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.RegistrationConflict);
        return new(lifecycle,normalized,BaseSubjectRetirementRegistry.ConsumerChecksum(normalized));
    }

    /// <summary>Creates one immutable required retirement consumer from opaque lifecycle authority.</summary>
    /// <typeparam name="TSubject">The exact generated exported-subject marker type.</typeparam>
    /// <param name="lifecycle">The generated lifecycle-consumer identity.</param>
    /// <param name="owningModuleId">The installed consumer module.</param>
    /// <param name="audience">The exact lifecycle audience.</param>
    /// <param name="retirementProfileId">The stable retirement-profile identifier.</param>
    /// <param name="retirementProfileVersion">The positive retirement-profile version.</param>
    /// <param name="retirementProfileChecksum">The canonical retirement-profile checksum.</param>
    /// <param name="acknowledgementGrantId">The exact acknowledgement grant.</param>
    /// <param name="limits">The immutable acknowledgement limits.</param>
    /// <returns>The opaque typed retirement-consumer identity.</returns>
    public static BaseGeneratedSubjectRetirementConsumerIdentity<TSubject> RegisterRequired<TSubject>(
        BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> lifecycle,
        string owningModuleId,
        BaseSubjectLifecycleConsumerAudience audience,
        string retirementProfileId,
        int retirementProfileVersion,
        string retirementProfileChecksum,
        string acknowledgementGrantId,
        BaseSubjectRetirementConsumerLimits limits)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        return Register(lifecycle, new BaseSubjectRetirementConsumerDefinition
        {
            ConsumerId = lifecycle.Definition.Id,
            ConsumerVersion = lifecycle.Definition.Version,
            OwningModuleId = owningModuleId,
            Audience = audience,
            LifecycleConsumerChecksum = lifecycle.Checksum,
            RetirementProfileId = retirementProfileId,
            RetirementProfileVersion = retirementProfileVersion,
            RetirementProfileChecksum = retirementProfileChecksum,
            Participation = BaseSubjectRetirementParticipation.RequiredBeforePurge,
            AcknowledgementGrantId = acknowledgementGrantId,
            Limits = limits,
        });
    }
}

/// <summary>Seals exporter-owned retirement policies from opaque typed consumer authority.</summary>
public static class BaseGeneratedSubjectRetirementPolicies
{
    /// <summary>Creates one canonical required-consumer policy without exposing checksum inputs.</summary>
    /// <typeparam name="TSubject">The exact generated exported-subject marker type.</typeparam>
    /// <param name="subject">The generated exported-subject registration.</param>
    /// <param name="coordinationWindow">The bounded coordination window.</param>
    /// <param name="timeoutBehavior">The closed timeout behavior.</param>
    /// <param name="purgeRetention">The minimum authoritative tombstone retention.</param>
    /// <param name="finalPurgeExecutionMode">The authority required by the final purge path.</param>
    /// <param name="consumers">The exact required consumer identities.</param>
    /// <returns>The opaque typed policy identity accepted by the application builder.</returns>
    public static BaseGeneratedSubjectRetirementPolicyIdentity<TSubject> Register<TSubject>(
        BaseGeneratedSubjectRegistration subject,
        TimeSpan coordinationWindow,
        BaseSubjectRetirementTimeoutBehavior timeoutBehavior,
        BaseSubjectPurgeRetentionPolicy purgeRetention,
        BaseSubjectFinalExecutionMode finalPurgeExecutionMode,
        params BaseGeneratedSubjectRetirementConsumerIdentity<TSubject>[] consumers)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(purgeRetention);
        ArgumentNullException.ThrowIfNull(consumers);
        if (subject.MarkerType != typeof(TSubject)
            || !subject.Definition.SupportsCoordinatedRetirement || consumers.Any(static value => value is null))
            throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ContractInvalid);
        ImmutableArray<BaseAcceptedRetirementConsumer> accepted = [.. consumers.Select(identity =>
        {
            BaseSubjectRetirementConsumerDefinition definition = identity.Definition;
            if (!string.Equals(identity.Lifecycle.Definition.ContractId, subject.Definition.Id, StringComparison.Ordinal)
                || identity.Lifecycle.Definition.ContractVersion != subject.Definition.Version
                || definition.Participation != BaseSubjectRetirementParticipation.RequiredBeforePurge)
                throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.RegistrationConflict);
            return new BaseAcceptedRetirementConsumer
            {
                ConsumerId = definition.ConsumerId,
                ConsumerVersion = definition.ConsumerVersion,
                OwningModuleId = definition.OwningModuleId,
                Audience = definition.Audience,
                LifecycleConsumerChecksum = definition.LifecycleConsumerChecksum,
                RetirementProfileId = definition.RetirementProfileId,
                RetirementProfileVersion = definition.RetirementProfileVersion,
                RetirementProfileChecksum = definition.RetirementProfileChecksum,
                Participation = definition.Participation,
                AcknowledgementGrantId = definition.AcknowledgementGrantId,
                Limits = definition.Limits with { },
                RetirementConsumerChecksum = identity.Checksum,
            };
        }).OrderBy(static value => value.ConsumerId, StringComparer.Ordinal)
            .ThenBy(static value => value.ConsumerVersion)];
        var draft = new BaseSubjectRetirementPolicy
        {
            ContractId = subject.Definition.Id,
            ContractVersion = subject.Definition.Version,
            AcceptedConsumers = accepted,
            CoordinationWindow = coordinationWindow,
            TimeoutBehavior = timeoutBehavior,
            PurgeRetention = purgeRetention with { },
            FinalPurgeExecutionMode = finalPurgeExecutionMode,
            PolicyChecksum = string.Empty,
        };
        string checksum = BaseSubjectRetirementRegistry.PolicyChecksum(draft);
        return new(BaseSubjectRetirementRegistry.NormalizePolicy(draft with { PolicyChecksum = checksum }));
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
    public ValueTask<BaseResult<BaseSubjectFinalPurgeResult>> PurgeAsync(
        BaseSubjectFinalPurgeRequest request,
        BaseSubjectFinalPurgeExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        IBaseSubjectRetirementRuntime runtime = _session.Services.GetService(typeof(IBaseSubjectRetirementRuntime)) as IBaseSubjectRetirementRuntime
            ?? throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        return runtime.PurgeAsync(_session, request, options, cancellationToken);
    }

    /// <summary>Removes one mutually accepted consumer after bounded barrier reconciliation.</summary>
    public ValueTask<BaseResult<BaseSubjectRetirementConsumerRemovalResult>> RemoveConsumerAsync(BaseSubjectRetirementConsumerRemovalRequest request,CancellationToken cancellationToken=default)
    {
        IBaseSubjectRetirementRuntime runtime=_session.Services.GetService(typeof(IBaseSubjectRetirementRuntime)) as IBaseSubjectRetirementRuntime??throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        return runtime.RemoveConsumerAsync(_session,request,cancellationToken);
    }
}

/// <summary>Executes one exact installed retirement consumer without exposing provider authority.</summary>
/// <typeparam name="TSubject">The exact exported-subject marker type.</typeparam>
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
    ValueTask<BaseResult<BaseSubjectFinalPurgeResult>> PurgeAsync(BaseSession session,BaseSubjectFinalPurgeRequest request,BaseSubjectFinalPurgeExecutionOptions? options,CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectRetirementConsumerRemovalResult>> RemoveConsumerAsync(BaseSession session,BaseSubjectRetirementConsumerRemovalRequest request,CancellationToken cancellationToken);
}
