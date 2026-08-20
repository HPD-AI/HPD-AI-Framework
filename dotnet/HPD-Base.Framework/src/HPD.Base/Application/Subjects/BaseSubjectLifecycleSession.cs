using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Contains one inert generator-owned lifecycle-consumer identity.</summary>
public sealed class BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject>
{
    internal BaseGeneratedSubjectLifecycleConsumerIdentity(BaseSubjectLifecycleConsumerDefinition definition, string checksum)
    { Definition = definition; Checksum = checksum; }
    internal BaseSubjectLifecycleConsumerDefinition Definition { get; }
    internal string Checksum { get; }
}

/// <summary>Provides generated-only lifecycle-consumer identity construction.</summary>
public static class BaseGeneratedSubjectLifecycleConsumers
{
    /// <summary>Creates one immutable generated consumer identity.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> Register<TSubject>(BaseSubjectLifecycleConsumerDefinition definition, BaseGeneratedSubjectRegistration subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        BaseSubjectLifecycleConsumerDefinition normalized = BaseSubjectLifecycleRegistry.Normalize(definition);
        if (normalized.ContractId != subject.Definition.Id || normalized.ContractVersion != subject.Definition.Version)
            throw new InvalidOperationException(BaseSubjectErrorCodes.LifecycleContractInvalid);
        return new(normalized, BaseSubjectLifecycleRegistry.Checksum(normalized, subject.Checksum));
    }
}

/// <summary>Resolves installed lifecycle consumers for one principal-bound session.</summary>
public sealed class BaseSubjectLifecycleSession
{
    private readonly BaseSession _session;
    internal BaseSubjectLifecycleSession(BaseSession session) => _session = session;

    /// <summary>Resolves an inert generated identity to an executable consumer handle.</summary>
    public BaseInstalledSubjectLifecycleConsumer<TSubject> Get<TSubject>(BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        BaseSubjectLifecycleRegistry registry = _session.Services.GetService(typeof(BaseSubjectLifecycleRegistry)) as BaseSubjectLifecycleRegistry
            ?? throw new InvalidOperationException(BaseSubjectErrorCodes.LifecycleContractInvalid);
        BaseInstalledSubjectLifecycleConsumer installed = registry.All.SingleOrDefault(value => value.Definition.Id == identity.Definition.Id && value.Definition.Version == identity.Definition.Version)
            ?? throw new InvalidOperationException(BaseSubjectErrorCodes.LifecycleContractInvalid);
        if (!string.Equals(installed.Checksum, identity.Checksum, StringComparison.Ordinal)) throw new InvalidOperationException(BaseSubjectErrorCodes.LifecycleRegistrationConflict);
        IBaseSubjectLifecycleRuntime runtime = _session.Services.GetService(typeof(IBaseSubjectLifecycleRuntime)) as IBaseSubjectLifecycleRuntime
            ?? throw new InvalidOperationException(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
        return new(runtime, _session, identity, installed);
    }
}

/// <summary>Reads one exact installed lifecycle consumer through its owning session.</summary>
public sealed class BaseInstalledSubjectLifecycleConsumer<TSubject>
{
    private readonly IBaseSubjectLifecycleRuntime _runtime; private readonly BaseSession _session;
    private readonly BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> _identity; private readonly BaseInstalledSubjectLifecycleConsumer _installed;
    internal BaseInstalledSubjectLifecycleConsumer(IBaseSubjectLifecycleRuntime runtime, BaseSession session, BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> identity, BaseInstalledSubjectLifecycleConsumer installed)
    { _runtime = runtime; _session = session; _identity = identity; _installed = installed; }

    /// <summary>Reads one bounded authorized page without advancing the durable checkpoint.</summary>
    public ValueTask<BaseResult<BaseSubjectLifecyclePage<TSubject>>> ReadAsync(BaseSubjectLifecycleCursor? after = null, int? take = null, CancellationToken cancellationToken = default) =>
        _runtime.ReadAsync(_session, _identity, _installed, after, take, cancellationToken);
    /// <summary>Enumerates immutable deliveries without implicitly advancing durable ownership.</summary>
    public async IAsyncEnumerable<BaseSubjectLifecycleDelivery<TSubject>> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        BaseSubjectLifecycleCursor? cursor = null;
        while (true)
        {
            BaseResult<BaseSubjectLifecyclePage<TSubject>> result = await _runtime.ReadAsync(_session, _identity, _installed, cursor, 1, cancellationToken).ConfigureAwait(false);
            BaseSubjectLifecyclePage<TSubject> page = result.RequireValue();
            if (page.Facts.IsDefaultOrEmpty) yield break;
            BaseSubjectLifecycleFact<TSubject> fact = page.Facts[0];
            yield return new BaseSubjectLifecycleDelivery<TSubject>
            {
                Fact = fact, Checkpoint = page.Through,
                ProcessingIdentity = Identity("process", fact.Fact), AdvanceIdentity = Identity("advance", fact.Fact),
            };
            cursor = page.Next;
            if (cursor is null) yield break;
        }
    }
    /// <summary>Advances the provider-owned checkpoint through exact issued evidence.</summary>
    public ValueTask<BaseResult<BaseSubjectLifecycleCheckpointResult>> AdvanceAsync(BaseSubjectLifecycleCheckpoint checkpoint, BaseMutationRequestIdentity identity, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(checkpoint); ArgumentNullException.ThrowIfNull(identity); return _runtime.AdvanceAsync(_session, _identity, _installed, checkpoint, identity, cancellationToken); }

    private BaseMutationRequestIdentity Identity(string operation, BaseSubjectLifecycleFact fact)
    {
        string semantic = $"base.subjectLifecycle.delivery.{operation}.v1\0{_installed.Checksum}\0{fact.CommitPosition.Value}\0{fact.SubjectId.Value}\0{fact.AuthorityEpoch.ToBase64Url()}\0{fact.Incarnation.ToBase64Url()}\0{fact.SubjectSequence}";
        byte[] digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(semantic));
        return BaseMutationRequestIdentity.Create($"subject-lifecycle:{_installed.Definition.Id}", $"subjectLifecycle.{operation}", Convert.ToHexStringLower(digest), BaseMutationRequestFingerprint.Create(digest));
    }
}

internal interface IBaseSubjectLifecycleRuntime
{
    ValueTask<BaseResult<BaseSubjectLifecycleCheckpoint>> CreateHintCheckpointAsync(BaseSession session, BaseInstalledSubjectLifecycleConsumer installed, BaseSubjectLifecycleCommitEvidence evidence, CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseUntypedSubjectLifecyclePage>> ReadUntypedAsync(BaseSession session, BaseInstalledSubjectLifecycleConsumer installed, BaseSubjectLifecycleCursor? after, int? take, CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectLifecycleCheckpointResult>> AdvanceUntypedAsync(BaseSession session, BaseInstalledSubjectLifecycleConsumer installed, BaseSubjectLifecycleCheckpoint checkpoint, BaseMutationRequestIdentity requestIdentity, CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectLifecyclePage<TSubject>>> ReadAsync<TSubject>(BaseSession session, BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> identity, BaseInstalledSubjectLifecycleConsumer installed, BaseSubjectLifecycleCursor? after, int? take, CancellationToken cancellationToken);
    ValueTask<BaseResult<BaseSubjectLifecycleCheckpointResult>> AdvanceAsync<TSubject>(BaseSession session, BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> identity, BaseInstalledSubjectLifecycleConsumer installed, BaseSubjectLifecycleCheckpoint checkpoint, BaseMutationRequestIdentity requestIdentity, CancellationToken cancellationToken);
}

internal sealed record BaseUntypedSubjectLifecyclePage
{
    internal required ImmutableArray<BaseSubjectLifecycleFact> Facts { get; init; }
    internal required BaseSubjectLifecycleCursor? Next { get; init; }
    internal required BaseSubjectLifecycleCheckpoint Through { get; init; }
}
