using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal static class BaseSubjectContractNormalizer
{
    private const int MaximumPlanBytes = 8 * 1024;

    internal static (BaseSubjectValidationPlanDefinition Plan, string Checksum) NormalizePlan(
        BaseSubjectValidationPlanDefinition value)
    {
        if (value is null || value.Active is null || value.Scope is null || value.Limits is null)
            throw Invalid();
        try
        {
            BaseApplicationId.Validate(value.Id, nameof(value));
            BaseApplicationId.Validate(value.ContractId, nameof(value));
            BaseApplicationId.Validate(value.PrivateCollectionId, nameof(value));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid, exception);
        }
        if (value.Version < 1 || value.ContractVersion < 1 || !Enum.IsDefined(value.SubjectId) ||
            !Enum.IsDefined(value.Active.Kind) || !Enum.IsDefined(value.Scope.Kind) || !Enum.IsDefined(value.Access) ||
            value.SubjectId != BaseSubjectIdBinding.RecordId ||
            value.Access != BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys)
            throw Invalid();
        ValidateChecksum(value.ContractChecksum);
        ValidateBindings(value.Active, value.Scope);
        ValidateLimits(value.Limits);

        var plan = value with
        {
            Id = CopyRequired(value.Id),
            ContractId = CopyRequired(value.ContractId),
            ContractChecksum = CopyRequired(value.ContractChecksum),
            PrivateCollectionId = CopyRequired(value.PrivateCollectionId),
            Active = value.Active with { FieldId = Copy(value.Active.FieldId) },
            Scope = value.Scope with { FieldId = Copy(value.Scope.FieldId) },
            Limits = value.Limits with { },
        };
        byte[] encoded = EncodePlan(plan);
        if (encoded.Length > MaximumPlanBytes) throw Invalid();
        return (plan, Convert.ToHexStringLower(SHA256.HashData(encoded)));
    }

    internal static BaseSubjectValidationLimits CloneAndValidateLimits(BaseSubjectValidationLimits value)
    {
        ValidateLimits(value);
        return value with { };
    }

    private static void ValidateBindings(BaseSubjectActiveBinding active, BaseSubjectScopeBinding scope)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(scope);
        if (active.Kind == BaseSubjectActiveBindingKind.NotDeclared && active.FieldId is not null ||
            active.Kind == BaseSubjectActiveBindingKind.RequiredBooleanField && active.FieldId is null ||
            scope.Kind == BaseSubjectScopeBindingKind.Global && scope.FieldId is not null ||
            scope.Kind is BaseSubjectScopeBindingKind.RequiredTenantField or BaseSubjectScopeBindingKind.RequiredProjectField && scope.FieldId is null)
            throw Invalid();
        try
        {
            if (active.FieldId is not null) BaseApplicationId.Validate(active.FieldId, nameof(active));
            if (scope.FieldId is not null) BaseApplicationId.Validate(scope.FieldId, nameof(scope));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid, exception);
        }
    }

    private static void ValidateLimits(BaseSubjectValidationLimits value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.MaximumReferencesPerRecord is < 1 or > 32 ||
            value.MaximumReferencesPerMutation is < 1 or > 1_024 ||
            value.MaximumValidationPlansPerMutation is < 1 or > 64 ||
            value.MaximumAuthorityReads is < 1 or > 1_024 ||
            value.MaximumReadIntervals is < 1 or > 1_024 ||
            value.MaximumSelectedBytes is < 1_024 or > 8_388_608 ||
            value.MaximumEvidenceBytes is < 1_024 or > 8_388_608 ||
            value.MaximumTransientBytes is < 65_536 or > 67_108_864 ||
            value.AcquisitionTimeout < TimeSpan.FromMilliseconds(100) || value.AcquisitionTimeout > TimeSpan.FromSeconds(30) ||
            value.ExecutionTimeout < TimeSpan.FromMilliseconds(100) || value.ExecutionTimeout > TimeSpan.FromMinutes(2))
            throw Invalid();
    }

    private static byte[] EncodePlan(BaseSubjectValidationPlanDefinition plan)
    {
        var writer = new ArrayBufferWriter<byte>();
        Write(writer, "hpd.base.subject-plan");
        Write(writer, 1);
        Write(writer, plan.Id); Write(writer, plan.Version); Write(writer, plan.ContractId); Write(writer, plan.ContractVersion);
        Write(writer, plan.ContractChecksum); Write(writer, plan.PrivateCollectionId); Write(writer, (int)plan.SubjectId);
        Write(writer, (int)plan.Active.Kind); Write(writer, plan.Active.FieldId); Write(writer, plan.Active.ActiveValue ? 1 : 0);
        Write(writer, (int)plan.Scope.Kind); Write(writer, plan.Scope.FieldId); Write(writer, (int)plan.Access);
        BaseSubjectValidationLimits limits = plan.Limits;
        Write(writer, limits.MaximumReferencesPerRecord); Write(writer, limits.MaximumReferencesPerMutation);
        Write(writer, limits.MaximumValidationPlansPerMutation); Write(writer, limits.MaximumAuthorityReads);
        Write(writer, limits.MaximumReadIntervals); Write(writer, limits.MaximumSelectedBytes);
        Write(writer, limits.MaximumEvidenceBytes); Write(writer, limits.MaximumTransientBytes);
        Write(writer, limits.AcquisitionTimeout.Ticks); Write(writer, limits.ExecutionTimeout.Ticks);
        return writer.WrittenSpan.ToArray();
    }

    private static void ValidateChecksum(string value)
    {
        if (value is null || value.Length != 64 || value.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw Invalid();
    }

    private static void Write(ArrayBufferWriter<byte> writer, string? value)
    {
        Span<byte> tag = writer.GetSpan(1);
        tag[0] = value is null ? (byte)0 : (byte)1;
        writer.Advance(1);
        if (value is null) return;
        int count = Encoding.UTF8.GetByteCount(value);
        if (count > 256) throw Invalid();
        Span<byte> length = writer.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)count));
        writer.Advance(sizeof(uint));
        Encoding.UTF8.GetBytes(value, writer.GetSpan(count));
        writer.Advance(count);
    }

    private static void Write(ArrayBufferWriter<byte> writer, int value)
    {
        Span<byte> target = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(target, value);
        writer.Advance(sizeof(int));
    }

    private static void Write(ArrayBufferWriter<byte> writer, long value)
    {
        Span<byte> target = writer.GetSpan(sizeof(long));
        BinaryPrimitives.WriteInt64BigEndian(target, value);
        writer.Advance(sizeof(long));
    }

    private static string? Copy(string? value) => value is null ? null : new string(value.AsSpan());
    private static string CopyRequired(string value) => new(value.AsSpan());
    private static InvalidOperationException Invalid() => new(BaseSubjectErrorCodes.ContractInvalid);
}
