using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Declares one graph-owned policy evaluator.</summary>
public sealed record BasePolicyAuthorityDefinition
{
    /// <summary>Gets the stable policy identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive policy version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning module identity.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the stable evaluator contract identity.</summary>
    public required string EvaluatorContractId { get; init; }
    /// <summary>Gets the positive evaluator contract version.</summary>
    public required int EvaluatorContractVersion { get; init; }
    /// <summary>Gets the deterministic composition order.</summary>
    public required int CompositionOrder { get; init; }
}

/// <summary>Declares one graph-owned grant authority.</summary>
public sealed record BaseGrantAuthorityDefinition
{
    /// <summary>Gets the stable grant identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive definition version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning module identity.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the stable source contract identity.</summary>
    public required string SourceContractId { get; init; }
    /// <summary>Gets the positive source contract version.</summary>
    public required int SourceContractVersion { get; init; }
}

/// <summary>Emits request-specific grants through graph-issued registrations.</summary>
public interface IBaseGrantAuthoritySource
{
    /// <summary>Emits grants for one bounded policy evaluation.</summary>
    ValueTask EmitAsync(BaseGrantAuthorityEmissionContext context, CancellationToken cancellationToken = default);
}

/// <summary>Opaque graph-issued registration for one grant authority.</summary>
public sealed class BaseInstalledGrantRegistration
{
    private readonly byte[] _checksum;
    internal BaseInstalledGrantRegistration(string id, int version, object owner, byte[] checksum)
    {
        Id = id;
        Version = version;
        Owner = owner;
        _checksum = checksum.ToArray();
    }

    /// <summary>Gets the stable grant identity.</summary>
    public string Id { get; }
    /// <summary>Gets the positive grant version.</summary>
    public int Version { get; }
    internal object Owner { get; }
    /// <summary>Returns the frozen grant-registration checksum.</summary>
    public byte[] GetChecksum() => _checksum.ToArray();
    internal ReadOnlySpan<byte> Checksum => _checksum;
}

/// <summary>Provides one source with its exact graph-issued grant registrations.</summary>
public sealed class BaseGrantAuthorityEmissionContext
{
    private readonly object _sourceOwner;
    private readonly List<BaseEmittedGrant> _emitted = [];

    internal BaseGrantAuthorityEmissionContext(
        PrincipalContext principal,
        OperationContext operation,
        object sourceOwner)
    {
        Principal = principal;
        Operation = operation;
        _sourceOwner = sourceOwner;
    }

    /// <summary>Gets the principal being evaluated.</summary>
    public PrincipalContext Principal { get; }
    /// <summary>Gets the operation being evaluated.</summary>
    public OperationContext Operation { get; }

    /// <summary>Emits one exact grant through its graph-issued registration.</summary>
    public void Emit(BaseInstalledGrantRegistration registration, AccessGrant grant)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(grant);
        if (!ReferenceEquals(registration.Owner, _sourceOwner)
            || !string.Equals(registration.Id, grant.Id, StringComparison.Ordinal))
            throw new InvalidOperationException(BasePolicyAuthorityErrorCodes.Invalid);
        _emitted.Add(new BaseEmittedGrant(registration, BasePolicyAuthorityCanonicalizer.CloneGrant(grant)));
    }

    internal ImmutableArray<BaseEmittedGrant> Complete() => [.. _emitted];
}

internal sealed record BaseEmittedGrant(BaseInstalledGrantRegistration Registration, AccessGrant Grant);

/// <summary>Collects immutable policy and grant authority registrations.</summary>
public sealed class BasePolicyAuthorityBuilder
{
    private readonly List<BasePolicyRegistration> _policies = [];
    private readonly List<BaseGrantRegistration> _grants = [];
    private bool _frozen;

    /// <summary>Adds one policy definition and its exact evaluator.</summary>
    public BasePolicyAuthorityBuilder AddPolicy(BasePolicyAuthorityDefinition definition, IPolicyEvaluator evaluator)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(evaluator);
        BasePolicyAuthorityCanonicalizer.Validate(definition);
        if (_policies.Any(value => value.Definition.Id == definition.Id)
            || _policies.Any(value => ReferenceEquals(value.Evaluator, evaluator)))
            throw new InvalidOperationException(BasePolicyAuthorityErrorCodes.Duplicate);
        _policies.Add(new BasePolicyRegistration(BasePolicyAuthorityCanonicalizer.Clone(definition), evaluator, null, evaluator.GetType()));
        return this;
    }

    internal BasePolicyAuthorityBuilder AddPolicyFactory(
        BasePolicyAuthorityDefinition definition,
        Type evaluatorType,
        Func<IServiceProvider, IPolicyEvaluator> factory)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(evaluatorType);
        ArgumentNullException.ThrowIfNull(factory);
        BasePolicyAuthorityCanonicalizer.Validate(definition);
        if (_policies.Any(value => value.Definition.Id == definition.Id || value.EvaluatorType == evaluatorType))
            throw new InvalidOperationException(BasePolicyAuthorityErrorCodes.Duplicate);
        _policies.Add(new BasePolicyRegistration(BasePolicyAuthorityCanonicalizer.Clone(definition), null, factory, evaluatorType));
        return this;
    }

    /// <summary>Adds one dynamic grant definition and source.</summary>
    public BaseInstalledGrantRegistration AddGrant(BaseGrantAuthorityDefinition definition, IBaseGrantAuthoritySource source)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(source);
        return AddGrantCore(definition, source, null);
    }

    /// <summary>Adds one immutable static grant.</summary>
    public BaseInstalledGrantRegistration AddStaticGrant(BaseGrantAuthorityDefinition definition, AccessGrant grant)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(grant);
        return AddGrantCore(definition, null, BasePolicyAuthorityCanonicalizer.CloneGrant(grant));
    }

    internal BasePolicyAuthorityOwner Freeze(string applicationId)
    {
        EnsureMutable();
        _frozen = true;
        return BasePolicyAuthorityOwner.Create(applicationId, _policies, _grants);
    }

    private BaseInstalledGrantRegistration AddGrantCore(
        BaseGrantAuthorityDefinition definition,
        IBaseGrantAuthoritySource? source,
        AccessGrant? grant)
    {
        ArgumentNullException.ThrowIfNull(definition);
        BasePolicyAuthorityCanonicalizer.Validate(definition);
        if (grant is not null && !string.Equals(definition.Id, grant.Id, StringComparison.Ordinal))
            throw new InvalidOperationException(BasePolicyAuthorityErrorCodes.Invalid);
        if (_grants.Any(value => value.Definition.Id == definition.Id
            && value.Definition.Version == definition.Version))
            throw new InvalidOperationException(BasePolicyAuthorityErrorCodes.Duplicate);
        object owner = new();
        byte[] checksum = BasePolicyAuthorityCanonicalizer.HashGrantDefinition(definition, grant);
        var registration = new BaseInstalledGrantRegistration(
            new string(definition.Id.AsSpan()), definition.Version, owner, checksum);
        _grants.Add(new BaseGrantRegistration(
            BasePolicyAuthorityCanonicalizer.Clone(definition), source, grant, registration, owner));
        return registration;
    }

    private void EnsureMutable()
    {
        if (_frozen) throw new InvalidOperationException(BasePolicyAuthorityErrorCodes.Late);
    }
}

internal sealed record BasePolicyRegistration(
    BasePolicyAuthorityDefinition Definition,
    IPolicyEvaluator? Evaluator,
    Func<IServiceProvider, IPolicyEvaluator>? Factory,
    Type EvaluatorType)
{
    internal IPolicyEvaluator Resolve(IServiceProvider services) => Evaluator ?? Factory?.Invoke(services)
        ?? throw new InvalidOperationException(BasePolicyAuthorityErrorCodes.Invalid);
}
internal sealed record BaseGrantRegistration(
    BaseGrantAuthorityDefinition Definition,
    IBaseGrantAuthoritySource? Source,
    AccessGrant? StaticGrant,
    BaseInstalledGrantRegistration Registration,
    object SourceOwner);

internal static class BasePolicyAuthorityErrorCodes
{
    internal const string Invalid = "base.policy.authorityInvalid";
    internal const string Duplicate = "base.policy.authorityDuplicate";
    internal const string Late = "base.policy.authorityLate";
}

internal static class BasePolicyAuthorityCanonicalizer
{
    internal static void Validate(BasePolicyAuthorityDefinition value)
    {
        ValidateId(value.Id);
        ValidateId(value.OwningModuleId);
        ValidateId(value.EvaluatorContractId);
        if (value.Version < 1 || value.EvaluatorContractVersion < 1 || value.CompositionOrder is < 0 or > 1_000_000)
            throw new InvalidOperationException(BasePolicyAuthorityErrorCodes.Invalid);
    }

    internal static void Validate(BaseGrantAuthorityDefinition value)
    {
        ValidateId(value.Id);
        ValidateId(value.OwningModuleId);
        ValidateId(value.SourceContractId);
        if (value.Version < 1 || value.SourceContractVersion < 1)
            throw new InvalidOperationException(BasePolicyAuthorityErrorCodes.Invalid);
    }

    internal static BasePolicyAuthorityDefinition Clone(BasePolicyAuthorityDefinition value) => value with
    {
        Id = Copy(value.Id), OwningModuleId = Copy(value.OwningModuleId), EvaluatorContractId = Copy(value.EvaluatorContractId),
    };

    internal static BaseGrantAuthorityDefinition Clone(BaseGrantAuthorityDefinition value) => value with
    {
        Id = Copy(value.Id), OwningModuleId = Copy(value.OwningModuleId), SourceContractId = Copy(value.SourceContractId),
    };

    internal static AccessGrant CloneGrant(AccessGrant value) => value with
    {
        ApplicationId = CopyNullable(value.ApplicationId), ModuleId = CopyNullable(value.ModuleId), Id = Copy(value.Id),
        Action = Copy(value.Action), Source = CopyNullable(value.Source),
        Subject = value.Subject with
        {
            Id = CopyNullable(value.Subject.Id), Qualifier = CopyNullable(value.Subject.Qualifier),
            TenantId = CopyNullable(value.Subject.TenantId), Source = CopyNullable(value.Subject.Source),
        },
        Scope = value.Scope with
        {
            CollectionId = CopyNullable(value.Scope.CollectionId), RecordId = CopyNullable(value.Scope.RecordId),
            FieldPath = CopyNullable(value.Scope.FieldPath), VectorIndexId = CopyNullable(value.Scope.VectorIndexId), TextIndexId = CopyNullable(value.Scope.TextIndexId),
            SubjectContractId = CopyNullable(value.Scope.SubjectContractId), TenantId = CopyNullable(value.Scope.TenantId),
            ProjectId = CopyNullable(value.Scope.ProjectId),
        },
        Condition = CloneFilter(value.Condition),
        WriteCondition = CloneFilter(value.WriteCondition),
    };

    private static FilterExpression? CloneFilter(FilterExpression? value) => value is null ? null : value with
    {
        Field = CopyNullable(value.Field), ModuleId = CopyNullable(value.ModuleId), Name = CopyNullable(value.Name),
        Value = CloneValue(value.Value),
        Values = value.Values?.Select(static item => CloneValue(item)!).ToArray(),
        Arguments = value.Arguments?.Select(static item => CloneValue(item)!).ToArray(),
        Children = value.Children?.Select(static item => CloneFilter(item)!).ToArray(),
    };

    private static QueryValue? CloneValue(QueryValue? value) => value is null ? null : value with
    {
        String = CopyNullable(value.String), Decimal = CopyNullable(value.Decimal), Id = CopyNullable(value.Id),
        SubjectId = CopyNullable(value.SubjectId), SubjectAuthorityEpoch = CopyNullable(value.SubjectAuthorityEpoch),
        SubjectIncarnation = CopyNullable(value.SubjectIncarnation),
        Array = value.Array?.Select(static item => CloneValue(item)!).ToArray(),
    };

    internal static byte[] HashGrantDefinition(BaseGrantAuthorityDefinition definition, AccessGrant? grant) => Hash(writer =>
    {
        Write(writer, "base.grant.definition.v1");
        Write(writer, definition.Id); Write(writer, definition.Version); Write(writer, definition.OwningModuleId);
        Write(writer, definition.SourceContractId); Write(writer, definition.SourceContractVersion);
        writer.Write(grant is not null);
        if (grant is not null) WriteGrant(writer, grant);
    });

    internal static byte[] HashPolicyDefinition(BasePolicyAuthorityDefinition definition) => Hash(writer =>
    {
        Write(writer, "base.policy.definition.v1"); Write(writer, definition.Id); Write(writer, definition.Version);
        Write(writer, definition.OwningModuleId); Write(writer, definition.EvaluatorContractId);
        Write(writer, definition.EvaluatorContractVersion); Write(writer, definition.CompositionOrder);
    });

    internal static byte[] HashGrant(AccessGrant grant) => Hash(writer =>
    {
        Write(writer, "base.grant.semantics.v1");
        WriteGrant(writer, grant);
    });

    internal static byte[] Hash(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) write(writer);
        return SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }

    internal static void WriteGrant(BinaryWriter writer, AccessGrant grant)
    {
        Write(writer, grant.ApplicationId); Write(writer, grant.ModuleId); Write(writer, (long?)grant.Audience);
        Write(writer, grant.Id); Write(writer, grant.Subject.ToString()); Write(writer, grant.Action);
        Write(writer, (long)grant.Scope.Kind); Write(writer, grant.Scope.CollectionId); Write(writer, grant.Scope.RecordId);
        Write(writer, grant.Scope.FieldPath); Write(writer, grant.Scope.VectorIndexId); Write(writer, grant.Scope.TextIndexId); Write(writer, grant.Scope.SubjectContractId);
        Write(writer, grant.Scope.SubjectContractVersion); Write(writer, grant.Scope.TenantId); Write(writer, grant.Scope.ProjectId);
        Write(writer, (long)grant.Effect); Write(writer, grant.Condition?.ToString()); Write(writer, grant.WriteCondition?.ToString());
        Write(writer, grant.ExpiresAt?.ToUniversalTime().Ticks);
    }

    internal static void Write(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is null) return;
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length); writer.Write(bytes);
    }

    internal static void Write(BinaryWriter writer, int value) => writer.Write((long)value);
    internal static void Write(BinaryWriter writer, int? value) { writer.Write(value.HasValue); if (value.HasValue) writer.Write((long)value.Value); }
    internal static void Write(BinaryWriter writer, long value) => writer.Write(value);
    internal static void Write(BinaryWriter writer, long? value) { writer.Write(value.HasValue); if (value.HasValue) writer.Write(value.Value); }

    private static void ValidateId(string value)
    {
        BaseApplicationId.Validate(value, nameof(value));
        if (Encoding.UTF8.GetByteCount(value) > 128) throw new InvalidOperationException(BasePolicyAuthorityErrorCodes.Invalid);
    }

    private static string Copy(string value) => new(value.AsSpan());
    private static string? CopyNullable(string? value) => value is null ? null : Copy(value);
}

internal sealed class BasePolicyAuthorityOwner
{
    private BasePolicyAuthorityOwner(
        string applicationId,
        ImmutableArray<BasePolicyRegistration> policies,
        ImmutableArray<BaseGrantRegistration> grants,
        byte[] checksum)
    {
        ApplicationId = applicationId;
        Policies = policies;
        Grants = grants;
        Checksum = checksum;
    }

    internal string ApplicationId { get; }
    internal long Generation => 1;
    internal ImmutableArray<BasePolicyRegistration> Policies { get; }
    internal ImmutableArray<BaseGrantRegistration> Grants { get; }
    internal byte[] Checksum { get; }

    internal static BasePolicyAuthorityOwner Create(
        string applicationId,
        IEnumerable<BasePolicyRegistration> policies,
        IEnumerable<BaseGrantRegistration> grants)
    {
        BaseApplicationId.Validate(applicationId, nameof(applicationId));
        ImmutableArray<BasePolicyRegistration> orderedPolicies = [.. policies.OrderBy(x => x.Definition.CompositionOrder)
            .ThenBy(x => x.Definition.Id, StringComparer.Ordinal).ThenBy(x => x.Definition.Version)];
        ImmutableArray<BaseGrantRegistration> orderedGrants = [.. grants.OrderBy(x => x.Definition.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Definition.Version)];
        byte[] checksum = BasePolicyAuthorityCanonicalizer.Hash(writer =>
        {
            BasePolicyAuthorityCanonicalizer.Write(writer, "base.policy.owner.v1");
            BasePolicyAuthorityCanonicalizer.Write(writer, applicationId);
            BasePolicyAuthorityCanonicalizer.Write(writer, orderedPolicies.Length);
            foreach (BasePolicyRegistration policy in orderedPolicies)
            {
                byte[] bytes = BasePolicyAuthorityCanonicalizer.HashPolicyDefinition(policy.Definition);
                writer.Write(bytes.Length); writer.Write(bytes);
            }
            BasePolicyAuthorityCanonicalizer.Write(writer, orderedGrants.Length);
            foreach (BaseGrantRegistration grant in orderedGrants)
            {
                writer.Write(grant.Registration.Checksum.Length); writer.Write(grant.Registration.Checksum);
            }
        });
        return new BasePolicyAuthorityOwner(new string(applicationId.AsSpan()), orderedPolicies, orderedGrants, checksum);
    }
}
