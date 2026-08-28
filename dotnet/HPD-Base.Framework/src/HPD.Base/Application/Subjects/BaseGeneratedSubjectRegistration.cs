using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Contains one generator-owned immutable exported-subject installation receipt.</summary>
public sealed class BaseGeneratedSubjectRegistration
{
    internal BaseGeneratedSubjectRegistration(Type markerType, BaseExportedSubjectDefinition definition, string checksum, string planChecksum)
    {
        MarkerType = markerType;
        Definition = definition;
        Checksum = checksum;
        PlanChecksum = planChecksum;
    }

    internal Type MarkerType { get; }
    internal BaseExportedSubjectDefinition Definition { get; }
    internal string Checksum { get; }
    internal string PlanChecksum { get; }
}

/// <summary>Provides generated-only construction of exported-subject installation receipts.</summary>
public static class BaseGeneratedSubjects
{
    /// <summary>Creates one immutable generated exported-subject installation receipt.</summary>
    /// <typeparam name="TSubject">The generated exported-subject marker type.</typeparam>
    /// <param name="definition">The complete generated subject definition.</param>
    /// <returns>A deeply owned installation receipt.</returns>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseGeneratedSubjectRegistration Register<TSubject>(BaseExportedSubjectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        BaseExportedSubjectDefinition normalized = BaseSubjectContractGraph.Normalize(definition);
        return new BaseGeneratedSubjectRegistration(typeof(TSubject), normalized,
            BaseSubjectContractGraph.Checksum(normalized),
            BaseSubjectContractNormalizer.NormalizePlan(normalized.ValidationPlan).Checksum);
    }
}

/// <summary>Contains exact generated subject authority admitted by a module DTO property.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class BaseGeneratedModuleSubjectQualifier
{
    internal BaseGeneratedModuleSubjectQualifier(string contractId, int contractVersion, string contractChecksum,
        BaseSubjectIdKind subjectIdKind, int maximumSubjectIdUtf8Bytes,
        BaseSubjectReferenceRequirement requirement, BaseSubjectValidationGuarantee guarantee)
    {
        BaseApplicationId.Validate(contractId, nameof(contractId));
        if (contractVersion < 1 || contractChecksum.Length != 64
            || !contractChecksum.All(static value => value is >= '0' and <= '9' or >= 'a' and <= 'f')
            || !Enum.IsDefined(subjectIdKind) || maximumSubjectIdUtf8Bytes is < 1 or > 256
            || !Enum.IsDefined(requirement) || guarantee != BaseSubjectValidationGuarantee.TransactionSnapshot)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
        ContractId = new string(contractId.AsSpan()); ContractVersion = contractVersion;
        ContractChecksum = new string(contractChecksum.AsSpan()); SubjectIdKind = subjectIdKind;
        MaximumSubjectIdUtf8Bytes = maximumSubjectIdUtf8Bytes; Requirement = requirement; Guarantee = guarantee;
        CodecId = "hpd.base.subject-reference.v1";
        CodecChecksum = SHA256.HashData(Encoding.ASCII.GetBytes(CodecId));
        var writer = new ArrayBufferWriter<byte>();
        writer.Write(new byte[] { 1 });
        Write(writer, ContractId); WriteInt64(writer, ContractVersion); Write(writer, ContractChecksum);
        WriteInt64(writer, (long)SubjectIdKind); WriteInt64(writer, MaximumSubjectIdUtf8Bytes);
        WriteInt64(writer, (long)Requirement); WriteInt64(writer, (long)Guarantee);
        Write(writer, CodecId); Write(writer, CodecChecksum);
        QualifierChecksum = SHA256.HashData(writer.WrittenSpan);
    }
    internal string ContractId { get; }
    internal int ContractVersion { get; }
    internal string ContractChecksum { get; }
    internal BaseSubjectIdKind SubjectIdKind { get; }
    internal int MaximumSubjectIdUtf8Bytes { get; }
    internal BaseSubjectReferenceRequirement Requirement { get; }
    internal BaseSubjectValidationGuarantee Guarantee { get; }
    internal string CodecId { get; }
    internal byte[] CodecChecksum { get; }
    internal byte[] QualifierChecksum { get; }

    internal BaseGeneratedModuleSubjectQualifier Copy() => new(
        ContractId, ContractVersion, ContractChecksum, SubjectIdKind,
        MaximumSubjectIdUtf8Bytes, Requirement, Guarantee);

    private static void Write(ArrayBufferWriter<byte> writer, string value) => Write(writer, Encoding.UTF8.GetBytes(value));
    private static void Write(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        Span<byte> length = writer.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)value.Length)); writer.Advance(sizeof(uint));
        writer.Write(value);
    }
    private static void WriteInt64(ArrayBufferWriter<byte> writer, long value)
    {
        Span<byte> destination = writer.GetSpan(sizeof(long));
        BinaryPrimitives.WriteInt64BigEndian(destination, value); writer.Advance(sizeof(long));
    }
}

/// <summary>Publishes and resolves reflection-free generated subject authority.</summary>
public static class BaseGeneratedSubjectAuthority
{
    /// <summary>Publishes the one generated subject receipt for a closed marker.</summary>
    /// <typeparam name="TSubject">The generated exported-subject marker type.</typeparam>
    /// <param name="registration">The generator-owned subject registration.</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void Publish<TSubject>(BaseGeneratedSubjectRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (registration.MarkerType != typeof(TSubject) || Holder<TSubject>.Registration is not null)
            throw new InvalidOperationException(BaseSubjectErrorCodes.RegistrationConflict);
        Holder<TSubject>.Registration = registration;
    }

    /// <summary>Resolves one exact qualifier from generated marker authority.</summary>
    /// <typeparam name="TSubject">The generated exported-subject marker type.</typeparam>
    /// <param name="requirement">The required subject lifecycle state.</param>
    /// <param name="guarantee">The required validation guarantee.</param>
    /// <returns>A deeply owned module-property qualifier.</returns>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseGeneratedModuleSubjectQualifier Resolve<TSubject>(
        BaseSubjectReferenceRequirement requirement, BaseSubjectValidationGuarantee guarantee)
    {
        BaseGeneratedSubjectRegistration registration = Holder<TSubject>.Registration
            ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
        if (!Enum.IsDefined(requirement) || !Enum.IsDefined(guarantee))
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
        BaseExportedSubjectDefinition definition = registration.Definition;
        return new BaseGeneratedModuleSubjectQualifier(definition.Id, definition.Version, registration.Checksum,
            definition.SubjectIdKind, definition.MaximumSubjectIdUtf8Bytes, requirement, guarantee);
    }

    private static class Holder<TSubject>
    {
        internal static BaseGeneratedSubjectRegistration? Registration;
    }
}

internal sealed class BaseSubjectContractRegistry
{
    private readonly Dictionary<(string Id, int Version), BaseGeneratedSubjectRegistration> _byIdentity;
    private readonly Dictionary<Type, BaseGeneratedSubjectRegistration> _byMarker;
    private readonly Dictionary<string, BaseSubjectAcquisitionDefinition> _acquisitions;

    internal BaseSubjectContractRegistry(
        IEnumerable<BaseGeneratedSubjectRegistration> registrations,
        IEnumerable<BaseSubjectAcquisitionDefinition>? acquisitions = null)
    {
        BaseGeneratedSubjectRegistration[] values = registrations.ToArray();
        BaseSubjectAcquisitionDefinition[] acquisitionValues = (acquisitions ?? []).ToArray();
        if (values.Select(static value => (value.Definition.Id, value.Definition.Version)).Distinct().Count() != values.Length ||
            values.Select(static value => value.MarkerType).Distinct().Count() != values.Length ||
            values.Select(static value => (value.Definition.ValidationPlan.Id, value.Definition.ValidationPlan.Version)).Distinct().Count() != values.Length ||
            acquisitionValues.Select(static value => value.Id).Distinct(StringComparer.Ordinal).Count() != acquisitionValues.Length ||
            acquisitionValues.Select(static value => (value.ContractId, value.ContractVersion, value.RegisteredReadId, value.Audience)).Distinct().Count() != acquisitionValues.Length)
        {
            throw new InvalidOperationException(BaseSubjectErrorCodes.RegistrationConflict);
        }

        _byIdentity = values.ToDictionary(static value => (value.Definition.Id, value.Definition.Version));
        _byMarker = values.ToDictionary(static value => value.MarkerType);
        _acquisitions = acquisitionValues.ToDictionary(static value => value.Id, StringComparer.Ordinal);
    }

    internal IReadOnlyCollection<BaseGeneratedSubjectRegistration> All => _byIdentity.Values;
    internal BaseGeneratedSubjectRegistration? Find(string id, int version) => _byIdentity.GetValueOrDefault((id, version));
    internal BaseGeneratedSubjectRegistration? Find(Type marker) => _byMarker.GetValueOrDefault(marker);
    internal IReadOnlyCollection<BaseSubjectAcquisitionDefinition> Acquisitions => _acquisitions.Values;
    internal BaseSubjectAcquisitionDefinition? FindAcquisition(string id) => _acquisitions.GetValueOrDefault(id);
}

internal static class BaseSubjectContractGraph
{
    internal static BaseExportedSubjectDefinition Normalize(BaseExportedSubjectDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.ValidationPlan is null || value.TombstoneMetadata?.Instant is null || value.TombstoneMetadata.Sequence is null)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
        try
        {
            BaseApplicationId.Validate(value.Id, nameof(value));
            BaseApplicationId.Validate(value.OwningModuleId, nameof(value));
            BaseApplicationId.Validate(value.AcquisitionGrantId, nameof(value));
            BaseApplicationId.Validate(value.ValidationGrantId, nameof(value));
            BaseApplicationId.Validate(value.AdministrationGrantId, nameof(value));
            BaseApplicationId.Validate(value.TombstoneFieldId, nameof(value));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid, exception);
        }
        if (value.Version < 1 || !Enum.IsDefined(value.SubjectIdKind) || !Enum.IsDefined(value.Scope) ||
            !Enum.IsDefined(value.FinalRetirementExecutionMode) ||
            value.MaximumSubjectIdUtf8Bytes is < 1 or > 256 || value.Audiences is null || value.Audiences.Length == 0 ||
            value.Audiences.Any(static audience => !Enum.IsDefined(audience)) ||
            value.Audiences.Distinct().Count() != value.Audiences.Length)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);

        BaseSubjectTombstoneInstantBinding instant = NormalizeInstant(value.TombstoneMetadata.Instant);
        BaseSubjectTombstoneSequenceBinding sequence = NormalizeSequence(value.TombstoneMetadata.Sequence);
        string? activeFieldId = value.ValidationPlan.Active?.FieldId;
        string? scopeFieldId = value.ValidationPlan.Scope?.FieldId;
        string?[] reserved = [value.TombstoneFieldId, activeFieldId, scopeFieldId];
        if (instant.FieldId is not null && reserved.Contains(instant.FieldId, StringComparer.Ordinal)
            || sequence.FieldId is not null && (reserved.Contains(sequence.FieldId, StringComparer.Ordinal)
                || string.Equals(sequence.FieldId, instant.FieldId, StringComparison.Ordinal)))
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);

        BaseSubjectValidationPlanDefinition initialPlan = value.ValidationPlan with
        {
            ContractId = value.Id,
            ContractVersion = value.Version,
            ContractChecksum = new string('0', 64),
        };
        BaseSubjectValidationPlanDefinition normalizedPlan = BaseSubjectContractNormalizer.NormalizePlan(initialPlan).Plan;
        var initial = value with
        {
            Id = Copy(value.Id), OwningModuleId = Copy(value.OwningModuleId),
            AcquisitionGrantId = Copy(value.AcquisitionGrantId), ValidationGrantId = Copy(value.ValidationGrantId),
            AdministrationGrantId = Copy(value.AdministrationGrantId),
            TombstoneFieldId = Copy(value.TombstoneFieldId),
            TombstoneMetadata = new BaseSubjectTombstoneMetadataDefinition { Instant = instant, Sequence = sequence },
            Audiences = [.. value.Audiences.Order()], ValidationPlan = normalizedPlan,
        };
        string checksum = Checksum(initial);
        BaseSubjectValidationPlanDefinition finalPlan = BaseSubjectContractNormalizer.NormalizePlan(
            normalizedPlan with { ContractChecksum = checksum }).Plan;
        return initial with { ValidationPlan = finalPlan };
    }

    internal static string Checksum(BaseExportedSubjectDefinition value)
    {
        var writer = new ArrayBufferWriter<byte>();
        Write(writer, "hpd.base.exported-subject.v1"); Write(writer, value.Id); Write(writer, value.Version);
        Write(writer, value.OwningModuleId); Write(writer, (int)value.SubjectIdKind); Write(writer, value.MaximumSubjectIdUtf8Bytes);
        Write(writer, (int)value.Scope); Write(writer, value.AcquisitionGrantId); Write(writer, value.ValidationGrantId); Write(writer, value.AdministrationGrantId);
        Write(writer, value.TombstoneFieldId); Write(writer, value.SupportsCoordinatedRetirement ? 1 : 0);
        Write(writer, (int)value.TombstoneMetadata.Instant.Kind); Write(writer, value.TombstoneMetadata.Instant.FieldId);
        Write(writer, (int)value.TombstoneMetadata.Sequence.Kind); Write(writer, value.TombstoneMetadata.Sequence.FieldId);
        Write(writer, (int)value.FinalRetirementExecutionMode);
        foreach (HPDBaseEndpointAudience audience in value.Audiences) Write(writer, (int)audience);
        BaseSubjectValidationPlanDefinition plan = value.ValidationPlan;
        Write(writer, plan.Id); Write(writer, plan.Version); Write(writer, plan.PrivateCollectionId); Write(writer, (int)plan.SubjectId);
        Write(writer, (int)plan.Active.Kind); Write(writer, plan.Active.FieldId); Write(writer, plan.Active.ActiveValue ? 1 : 0);
        Write(writer, (int)plan.Scope.Kind); Write(writer, plan.Scope.FieldId); Write(writer, (int)plan.Access);
        BaseSubjectValidationLimits limits = plan.Limits;
        Write(writer, limits.MaximumReferencesPerRecord); Write(writer, limits.MaximumReferencesPerMutation);
        Write(writer, limits.MaximumValidationPlansPerMutation); Write(writer, limits.MaximumAuthorityReads); Write(writer, limits.MaximumReadIntervals);
        Write(writer, limits.MaximumSelectedBytes); Write(writer, limits.MaximumEvidenceBytes); Write(writer, limits.MaximumTransientBytes);
        Write(writer, limits.AcquisitionTimeout.Ticks); Write(writer, limits.ExecutionTimeout.Ticks);
        return Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan));
    }

    private static string Copy(string value) => new(value.AsSpan());
    private static BaseSubjectTombstoneInstantBinding NormalizeInstant(BaseSubjectTombstoneInstantBinding value)
    {
        if (!Enum.IsDefined(value.Kind) || value.Kind == BaseSubjectTombstoneMetadataBindingKind.NotStored && value.FieldId is not null
            || value.Kind == BaseSubjectTombstoneMetadataBindingKind.RequiredField && value.FieldId is null)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
        if (value.FieldId is not null)
        {
            try { BaseApplicationId.Validate(value.FieldId, nameof(value)); }
            catch (ArgumentException exception) { throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid, exception); }
        }
        return value with { FieldId = value.FieldId is null ? null : Copy(value.FieldId) };
    }
    private static BaseSubjectTombstoneSequenceBinding NormalizeSequence(BaseSubjectTombstoneSequenceBinding value)
    {
        if (!Enum.IsDefined(value.Kind) || value.Kind == BaseSubjectTombstoneMetadataBindingKind.NotStored && value.FieldId is not null
            || value.Kind == BaseSubjectTombstoneMetadataBindingKind.RequiredField && value.FieldId is null)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
        if (value.FieldId is not null)
        {
            try { BaseApplicationId.Validate(value.FieldId, nameof(value)); }
            catch (ArgumentException exception) { throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid, exception); }
        }
        return value with { FieldId = value.FieldId is null ? null : Copy(value.FieldId) };
    }
    private static void Write(ArrayBufferWriter<byte> writer, string? value)
    {
        Span<byte> tag = writer.GetSpan(1); tag[0] = value is null ? (byte)0 : (byte)1; writer.Advance(1);
        if (value is null) return;
        int count = Encoding.UTF8.GetByteCount(value);
        Span<byte> length = writer.GetSpan(4); BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)count)); writer.Advance(4);
        Encoding.UTF8.GetBytes(value, writer.GetSpan(count)); writer.Advance(count);
    }
    private static void Write(ArrayBufferWriter<byte> writer, int value)
    { Span<byte> span = writer.GetSpan(4); BinaryPrimitives.WriteInt32BigEndian(span, value); writer.Advance(4); }
    private static void Write(ArrayBufferWriter<byte> writer, long value)
    { Span<byte> span = writer.GetSpan(8); BinaryPrimitives.WriteInt64BigEndian(span, value); writer.Advance(8); }
}

/// <summary>Binds one installed exported-subject contract to its public marker type.</summary>
public sealed class BaseExportedSubjectContract<TSubject>
{
    private readonly BaseSession _session;
    private readonly BaseGeneratedSubjectRegistration _registration;
    internal BaseExportedSubjectContract(BaseSession session, BaseGeneratedSubjectRegistration registration)
    {
        _session = session;
        _registration = registration;
        Id = registration.Definition.Id;
        Version = registration.Definition.Version;
        Checksum = registration.Checksum;
    }
    /// <summary>Gets the stable installed contract identifier.</summary>
    public string Id { get; }
    /// <summary>Gets the installed contract version.</summary>
    public int Version { get; }
    /// <summary>Gets the installed normalized contract checksum.</summary>
    public string Checksum { get; }

    /// <summary>Atomically tombstones one exact exported-subject lifetime.</summary>
    public ValueTask<BaseResult<BaseSubjectLifecycleFact<TSubject>>> TombstoneAsync(
        BaseSubjectTombstoneRequest<TSubject> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        IBaseSubjectLifecycleExporterRuntime runtime = _session.Services.GetService(typeof(IBaseSubjectLifecycleExporterRuntime)) as IBaseSubjectLifecycleExporterRuntime
            ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
        return runtime.TombstoneAsync(_session, _registration, request, cancellationToken);
    }

    /// <summary>Atomically performs uncoordinated final retirement of one tombstoned lifetime.</summary>
    public ValueTask<BaseResult<BaseSubjectFinalRetirementResult<TSubject>>> FinalizeRetirementAsync(
        BaseSubjectFinalRetirementRequest<TSubject> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        IBaseSubjectLifecycleExporterRuntime runtime = _session.Services.GetService(typeof(IBaseSubjectLifecycleExporterRuntime)) as IBaseSubjectLifecycleExporterRuntime
            ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
        return runtime.FinalizeRetirementAsync(_session, _registration, request, cancellationToken);
    }
}
