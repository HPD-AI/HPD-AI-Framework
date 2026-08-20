using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed record BaseInstalledSubjectLifecycleConsumer(BaseSubjectLifecycleConsumerDefinition Definition, string Checksum);

internal sealed class BaseSubjectLifecycleRegistry
{
    private readonly Dictionary<(string Id, int Version), BaseInstalledSubjectLifecycleConsumer> _consumers;

    internal BaseSubjectLifecycleRegistry(IEnumerable<BaseSubjectLifecycleConsumerDefinition> consumers, BaseSubjectContractRegistry subjects)
    {
        _consumers = [];
        foreach (BaseSubjectLifecycleConsumerDefinition candidate in consumers)
        {
            BaseSubjectLifecycleConsumerDefinition definition = Normalize(candidate);
            BaseGeneratedSubjectRegistration? subject = subjects.Find(definition.ContractId, definition.ContractVersion);
            if (subject is null ||
                !_consumers.TryAdd((definition.Id, definition.Version), new(definition, Checksum(definition, subject.Checksum))))
                throw new InvalidOperationException(BaseSubjectErrorCodes.LifecycleRegistrationConflict);
        }
    }

    internal IReadOnlyCollection<BaseInstalledSubjectLifecycleConsumer> All => _consumers.Values;
    internal IEnumerable<BaseInstalledSubjectLifecycleConsumer> ForContract(string id, int version) =>
        _consumers.Values.Where(value => value.Definition.ContractId == id && value.Definition.ContractVersion == version);

    internal static BaseSubjectLifecycleConsumerDefinition Normalize(BaseSubjectLifecycleConsumerDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            BaseApplicationId.Validate(value.Id, nameof(value));
            BaseApplicationId.Validate(value.OwningModuleId, nameof(value));
            BaseApplicationId.Validate(value.ContractId, nameof(value));
            BaseApplicationId.Validate(value.DeliveryGrantId, nameof(value));
            if (value.ReconciliationGrantId is not null) BaseApplicationId.Validate(value.ReconciliationGrantId, nameof(value));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException(BaseSubjectErrorCodes.LifecycleContractInvalid, exception);
        }
        BaseSubjectLifecycleConsumerLimits limits = value.Limits ?? throw new InvalidOperationException(BaseSubjectErrorCodes.LifecycleContractInvalid);
        ImmutableArray<BaseSubjectLifecycleState> states = [.. value.ObservedStates.Distinct().Order()];
        if (value.Version < 1 || value.ContractVersion < 1 || !Enum.IsDefined(value.Audience) || states.IsDefaultOrEmpty || states.Any(static state => !Enum.IsDefined(state)) ||
            limits.MaximumFactsPerPage is < 1 or > 256 || limits.MaximumResultBytes is < 1 or > 1_048_576 ||
            limits.MaximumCheckpointLag < TimeSpan.FromHours(1) || limits.MaximumCheckpointLag > TimeSpan.FromDays(30) ||
            limits.ReadTimeout < TimeSpan.FromMilliseconds(100) || limits.ReadTimeout > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException(BaseSubjectErrorCodes.LifecycleContractInvalid);
        return value with
        {
            Id = Copy(value.Id), OwningModuleId = Copy(value.OwningModuleId), ContractId = Copy(value.ContractId),
            DeliveryGrantId = Copy(value.DeliveryGrantId), ReconciliationGrantId = value.ReconciliationGrantId is null ? null : Copy(value.ReconciliationGrantId),
            ObservedStates = states, Limits = limits with { },
        };
    }

    internal static string Checksum(BaseSubjectLifecycleConsumerDefinition value, string contractChecksum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractChecksum);
        var writer = new ArrayBufferWriter<byte>();
        Write(writer, "hpd.base.subject-lifecycle-consumer.v1"); Write(writer, value.Id); Write(writer, value.Version);
        Write(writer, value.OwningModuleId); Write(writer, (int)value.Audience); Write(writer, value.ContractId); Write(writer, value.ContractVersion);
        Write(writer, contractChecksum);
        foreach (BaseSubjectLifecycleState state in value.ObservedStates) Write(writer, (int)state);
        Write(writer, value.DeliveryGrantId); Write(writer, value.ReconciliationGrantId);
        Write(writer, value.Limits.MaximumFactsPerPage); Write(writer, value.Limits.MaximumResultBytes);
        Write(writer, value.Limits.MaximumCheckpointLag.Ticks); Write(writer, value.Limits.ReadTimeout.Ticks);
        return Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan));
    }

    private static string Copy(string value) => new(value.AsSpan());
    private static void Write(ArrayBufferWriter<byte> writer, string? value)
    {
        Span<byte> tag = writer.GetSpan(1); tag[0] = value is null ? (byte)0 : (byte)1; writer.Advance(1);
        if (value is null) return;
        int count = Encoding.UTF8.GetByteCount(value);
        Span<byte> length = writer.GetSpan(4); BinaryPrimitives.WriteInt32BigEndian(length, count); writer.Advance(4);
        Encoding.UTF8.GetBytes(value, writer.GetSpan(count)); writer.Advance(count);
    }
    private static void Write(ArrayBufferWriter<byte> writer, int value) { BinaryPrimitives.WriteInt32BigEndian(writer.GetSpan(4), value); writer.Advance(4); }
    private static void Write(ArrayBufferWriter<byte> writer, long value) { BinaryPrimitives.WriteInt64BigEndian(writer.GetSpan(8), value); writer.Advance(8); }
}

internal sealed class BaseSubjectLifecycleInspectionAuthorityRegistry
{
    private readonly Dictionary<(string ContractId, int ContractVersion), BaseSubjectLifecycleInspectionAuthority> _authorities;

    internal BaseSubjectLifecycleInspectionAuthorityRegistry(
        string applicationId,
        IEnumerable<BaseGeneratedSubjectRegistration> subjects,
        BasePolicyAuthorityOwner policyOwner)
    {
        _authorities = [];
        foreach (BaseGeneratedSubjectRegistration subject in subjects)
        {
            BaseGrantRegistration? grant = policyOwner.Grants.SingleOrDefault(value =>
                value.Definition.Id == subject.Definition.AdministrationGrantId);
            if (grant is null) continue;
            string digest = Convert.ToHexStringLower(SHA256.HashData(Encode(applicationId, subject, grant, policyOwner.Checksum)));
            _authorities.Add((subject.Definition.Id, subject.Definition.Version), new BaseSubjectLifecycleInspectionAuthority
            {
                ContractId = subject.Definition.Id,
                ContractVersion = subject.Definition.Version,
                OwningModuleId = subject.Definition.OwningModuleId,
                GrantId = subject.Definition.AdministrationGrantId,
                Digest = digest,
            });
        }
    }

    internal IReadOnlyCollection<BaseSubjectLifecycleInspectionAuthority> All => _authorities.Values;
    internal BaseSubjectLifecycleInspectionAuthority? Find(string contractId, int contractVersion) =>
        _authorities.GetValueOrDefault((contractId, contractVersion));

    private static byte[] Encode(string applicationId, BaseGeneratedSubjectRegistration subject, BaseGrantRegistration grant, byte[] ownerChecksum)
    {
        var writer = new ArrayBufferWriter<byte>();
        Write(writer, "base.subjectLifecycle.allScopeInspectionAuthority.v1");
        Write(writer, applicationId); Write(writer, subject.Definition.OwningModuleId);
        Write(writer, subject.Definition.Id); Write(writer, subject.Definition.Version);
        Write(writer, subject.Definition.AdministrationGrantId); Write(writer, grant.Definition.Version);
        Write(writer, Convert.ToHexStringLower(grant.Registration.Checksum));
        Write(writer, Convert.ToHexStringLower(ownerChecksum));
        Write(writer, (int)HPDBaseEndpointAudience.ControlPlane);
        Write(writer, (int)BaseOperationKind.SubjectLifecycleMaintenance);
        return writer.WrittenSpan.ToArray();
    }

    private static void Write(ArrayBufferWriter<byte> writer, string value)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        Span<byte> length = writer.GetSpan(4); BinaryPrimitives.WriteInt32BigEndian(length, count); writer.Advance(4);
        Encoding.UTF8.GetBytes(value, writer.GetSpan(count)); writer.Advance(count);
    }

    private static void Write(ArrayBufferWriter<byte> writer, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(writer.GetSpan(4), value); writer.Advance(4);
    }
}
