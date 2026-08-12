using System.Collections.Immutable;

namespace HPD.Base;

internal static class BaseConfidentialityPolicy
{
    internal static BaseFieldDisclosurePolicy Normalize(BaseFieldConfidentiality confidentiality, BaseFieldDisclosurePolicy? declared)
    {
        if (!Enum.IsDefined(confidentiality)) throw new InvalidOperationException(BaseConfidentialityErrorCodes.ContractInvalid);
        BaseFieldDisclosurePolicy maximum = Default(confidentiality);
        BaseFieldDisclosurePolicy value = declared is null ? maximum : Clone(declared);
        ValidateEnums(value);
        if (!Narrows(value.RecordRead, maximum.RecordRead)
            || value.AuthoritativeHistory != BaseHistoryProtection.AuthoritativeRequired
            || !Narrows(value.Event, maximum.Event)
            || !Narrows(value.Realtime, maximum.Realtime)
            || !Narrows(value.Diagnostic, maximum.Diagnostic)
            || value.AuthoritativeBackup != BaseAuthoritativeBackupProtection.PreserveAuthoritativeValue
            || !Narrows(value.AdministrativeDataExport, maximum.AdministrativeDataExport)
            || !Narrows(value.OrdinaryDataExport, maximum.OrdinaryDataExport)
            || !Narrows(value.Indexing, maximum.Indexing))
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.ContractInvalid);
        return value;
    }

    internal static BaseFieldDisclosurePolicy Clone(BaseFieldDisclosurePolicy value) => value with { };

    internal static BaseFieldDisclosurePolicy Default(BaseFieldConfidentiality value) => value switch
    {
        BaseFieldConfidentiality.Public => Policy(BaseRecordDisclosure.Include, BaseProjectionDisclosure.Include, BaseProjectionDisclosure.Include, BaseProjectionDisclosure.Omit, BaseProjectionDisclosure.Include, BaseProjectionDisclosure.Include, BaseIndexDisclosure.DeclaredOperators),
        BaseFieldConfidentiality.Internal => Policy(BaseRecordDisclosure.Include, BaseProjectionDisclosure.Omit, BaseProjectionDisclosure.Omit, BaseProjectionDisclosure.Omit, BaseProjectionDisclosure.Include, BaseProjectionDisclosure.Omit, BaseIndexDisclosure.DeclaredOperators),
        BaseFieldConfidentiality.Confidential => Policy(BaseRecordDisclosure.Omit, BaseProjectionDisclosure.Omit, BaseProjectionDisclosure.Omit, BaseProjectionDisclosure.Omit, BaseProjectionDisclosure.Omit, BaseProjectionDisclosure.Omit, BaseIndexDisclosure.EqualityOnly),
        BaseFieldConfidentiality.Secret => Policy(BaseRecordDisclosure.Omit, BaseProjectionDisclosure.Omit, BaseProjectionDisclosure.Omit, BaseProjectionDisclosure.Omit, BaseProjectionDisclosure.Omit, BaseProjectionDisclosure.Omit, BaseIndexDisclosure.Forbidden),
        _ => throw new InvalidOperationException(BaseConfidentialityErrorCodes.ContractInvalid),
    };

    private static BaseFieldDisclosurePolicy Policy(BaseRecordDisclosure record, BaseProjectionDisclosure @event, BaseProjectionDisclosure realtime, BaseProjectionDisclosure diagnostic, BaseProjectionDisclosure admin, BaseProjectionDisclosure ordinary, BaseIndexDisclosure index) => new()
    {
        RecordRead = record, AuthoritativeHistory = BaseHistoryProtection.AuthoritativeRequired,
        Event = @event, Realtime = realtime, Diagnostic = diagnostic,
        AuthoritativeBackup = BaseAuthoritativeBackupProtection.PreserveAuthoritativeValue,
        AdministrativeDataExport = admin, OrdinaryDataExport = ordinary, Indexing = index,
    };

    private static bool Narrows(BaseRecordDisclosure value, BaseRecordDisclosure maximum) => maximum == BaseRecordDisclosure.Include || value == maximum || value == BaseRecordDisclosure.Omit;
    private static bool Narrows(BaseProjectionDisclosure value, BaseProjectionDisclosure maximum) => maximum == BaseProjectionDisclosure.Include || value == maximum || value == BaseProjectionDisclosure.Omit;
    private static bool Narrows(BaseIndexDisclosure value, BaseIndexDisclosure maximum) => (int)value <= (int)maximum;
    private static void ValidateEnums(BaseFieldDisclosurePolicy value)
    {
        if (!Enum.IsDefined(value.RecordRead) || !Enum.IsDefined(value.AuthoritativeHistory) || !Enum.IsDefined(value.Event)
            || !Enum.IsDefined(value.Realtime) || !Enum.IsDefined(value.Diagnostic) || !Enum.IsDefined(value.AuthoritativeBackup)
            || !Enum.IsDefined(value.AdministrativeDataExport) || !Enum.IsDefined(value.OrdinaryDataExport) || !Enum.IsDefined(value.Indexing))
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.ContractInvalid);
    }
}

internal static class BaseStorageProtectionContract
{
    internal static BaseStorageProtectionCapability Clone(BaseStorageProtectionCapability value) => value with
    {
        OwningModuleId = new string(value.OwningModuleId.AsSpan()),
        Coverage = value.Coverage with { },
    };

    internal static void ValidateCapability(BaseStorageProtectionCapability? value)
    {
        if (value is null || !HPDBaseStoreProviderFactory.ValidIdentifier(value.OwningModuleId) || value.Coverage is null
            || !Enum.IsDefined(value.Guarantee) || !Enum.IsDefined(value.KeyOwner) || !Enum.IsDefined(value.Rotation) || !Enum.IsDefined(value.Verification)
            || !CoverageValues(value.Coverage).All(Enum.IsDefined)
            || value.Guarantee == BaseStorageEncryptionGuarantee.ProviderVerified && value.Verification != BaseStorageVerificationStatus.OperationallyVerified
            || value.Guarantee is BaseStorageEncryptionGuarantee.HostDeclared or BaseStorageEncryptionGuarantee.ProviderDeclared && value.Verification == BaseStorageVerificationStatus.Unverified
            || value.Guarantee == BaseStorageEncryptionGuarantee.None && (value.KeyOwner != BaseStorageKeyOwner.None
                || value.Rotation != BaseStorageRotationSupport.None
                || CoverageValues(value.Coverage).Any(static state => state == BaseStorageProtectionState.Protected)))
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageDescriptorInvalid);

        BaseStorageProtectionState[] durable =
        [
            value.Coverage.AuthoritativeRecords,
            value.Coverage.Journal,
            value.Coverage.Receipts,
            value.Coverage.ProviderState,
            value.Coverage.Indexes,
            value.Coverage.TemporaryFiles,
            value.Coverage.AuthoritativeBackups,
        ];
        if (durable.Any(static state => state == BaseStorageProtectionState.NotRetained))
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageDescriptorInvalid);
    }

    internal static BaseStorageProtectionRequirement Clone(BaseStorageProtectionRequirement value) => value with
    {
        OwningModuleId = new string(value.OwningModuleId.AsSpan()),
        PermittedGuarantees = ImmutableArray.CreateRange(value.PermittedGuarantees.OrderBy(static item => item.ToString(), StringComparer.Ordinal)),
        PermittedKeyOwners = ImmutableArray.CreateRange(value.PermittedKeyOwners.OrderBy(static item => item.ToString(), StringComparer.Ordinal)),
        Coverage = value.Coverage with
        {
            AuthoritativeRecords = Copy(value.Coverage.AuthoritativeRecords), Journal = Copy(value.Coverage.Journal), Receipts = Copy(value.Coverage.Receipts),
            ProviderState = Copy(value.Coverage.ProviderState), Indexes = Copy(value.Coverage.Indexes), TemporaryFiles = Copy(value.Coverage.TemporaryFiles),
            AuthoritativeBackups = Copy(value.Coverage.AuthoritativeBackups), AdministrativeExports = Copy(value.Coverage.AdministrativeExports),
            OrdinaryExports = Copy(value.Coverage.OrdinaryExports), ExternalFilesAndBlobs = Copy(value.Coverage.ExternalFilesAndBlobs),
        },
    };

    internal static void NormalizeRequirement(BaseStorageProtectionRequirement? value)
    {
        if (value is null || !HPDBaseStoreProviderFactory.ValidIdentifier(value.OwningModuleId) || value.Coverage is null
            || !Valid(value.PermittedGuarantees) || !Valid(value.PermittedKeyOwners)
            || !Enum.IsDefined(value.RequiredRotation) || !Enum.IsDefined(value.MinimumVerification)
            || !Valid(value.Coverage.AuthoritativeRecords) || !Valid(value.Coverage.Journal) || !Valid(value.Coverage.Receipts)
            || !Valid(value.Coverage.ProviderState) || !Valid(value.Coverage.Indexes) || !Valid(value.Coverage.TemporaryFiles)
            || !Valid(value.Coverage.AuthoritativeBackups) || !Valid(value.Coverage.AdministrativeExports)
            || !Valid(value.Coverage.OrdinaryExports) || !Valid(value.Coverage.ExternalFilesAndBlobs))
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageRequirementInvalid);
    }

    internal static BaseStorageProtectionGraph FinalizeGraph(
        IEnumerable<BaseStorageProtectionRequirement> application,
        IEnumerable<CollectionDefinition> collections,
        IReadOnlyDictionary<(string Feature, string Module), BaseStorageProtectionRequirement> features,
        IEnumerable<BaseStorageProtectionCapability> capabilities)
    {
        var owners = new Dictionary<string, BaseStorageProtectionCapability>(StringComparer.Ordinal);
        foreach (BaseStorageProtectionCapability capability in capabilities)
        {
            ValidateCapability(capability);
            if (!owners.TryAdd(capability.OwningModuleId, Clone(capability)))
                throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageDescriptorInvalid);
        }
        var effective = new List<BaseStorageProtectionRequirement>();
        foreach (IGrouping<string, BaseStorageProtectionRequirement> group in application
            .Concat(collections.SelectMany(static collection => collection.StorageProtectionRequirements ?? []))
            .Concat(features.Values).GroupBy(static item => item.OwningModuleId, StringComparer.Ordinal))
        {
            foreach (BaseStorageProtectionRequirement declared in group) NormalizeRequirement(declared);
            if (!owners.TryGetValue(group.Key, out BaseStorageProtectionCapability? capability))
                throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageRequirementOwnerMissing);
            BaseStorageProtectionRequirement requirement = group.Select(Clone).Aggregate(Intersect);
            if (!Satisfies(capability, requirement))
                throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageProtectionInsufficient);
            effective.Add(requirement);
        }
        return new BaseStorageProtectionGraph(effective.OrderBy(static item => item.OwningModuleId, StringComparer.Ordinal).ToArray(), owners.Values.OrderBy(static item => item.OwningModuleId, StringComparer.Ordinal).ToArray());
    }

    private static BaseStorageProtectionRequirement Intersect(BaseStorageProtectionRequirement left, BaseStorageProtectionRequirement right)
    {
        if (!string.Equals(left.OwningModuleId, right.OwningModuleId, StringComparison.Ordinal)) throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageRequirementConflict);
        ImmutableArray<T> Intersection<T>(ImmutableArray<T> a, ImmutableArray<T> b) where T : struct, Enum
        {
            T[] values = a.Intersect(b).OrderBy(static item => item.ToString(), StringComparer.Ordinal).ToArray();
            if (values.Length == 0) throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageRequirementConflict);
            return ImmutableArray.Create(values);
        }
        BaseStorageRotationSupport rotation = RotationIntersection(left.RequiredRotation, right.RequiredRotation);
        return left with
        {
            PermittedGuarantees = Intersection(left.PermittedGuarantees, right.PermittedGuarantees),
            PermittedKeyOwners = Intersection(left.PermittedKeyOwners, right.PermittedKeyOwners),
            RequiredRotation = rotation,
            MinimumVerification = (BaseStorageVerificationStatus)Math.Max((int)left.MinimumVerification, (int)right.MinimumVerification),
            Coverage = left.Coverage with
            {
                AuthoritativeRecords = Intersection(left.Coverage.AuthoritativeRecords, right.Coverage.AuthoritativeRecords), Journal = Intersection(left.Coverage.Journal, right.Coverage.Journal),
                Receipts = Intersection(left.Coverage.Receipts, right.Coverage.Receipts), ProviderState = Intersection(left.Coverage.ProviderState, right.Coverage.ProviderState),
                Indexes = Intersection(left.Coverage.Indexes, right.Coverage.Indexes), TemporaryFiles = Intersection(left.Coverage.TemporaryFiles, right.Coverage.TemporaryFiles),
                AuthoritativeBackups = Intersection(left.Coverage.AuthoritativeBackups, right.Coverage.AuthoritativeBackups), AdministrativeExports = Intersection(left.Coverage.AdministrativeExports, right.Coverage.AdministrativeExports),
                OrdinaryExports = Intersection(left.Coverage.OrdinaryExports, right.Coverage.OrdinaryExports), ExternalFilesAndBlobs = Intersection(left.Coverage.ExternalFilesAndBlobs, right.Coverage.ExternalFilesAndBlobs),
            },
        };
    }

    private static BaseStorageRotationSupport RotationIntersection(BaseStorageRotationSupport left, BaseStorageRotationSupport right)
    {
        if (left == right) return left;
        if (left == BaseStorageRotationSupport.None) return right;
        if (right == BaseStorageRotationSupport.None) return left;
        if (left is BaseStorageRotationSupport.Offline or BaseStorageRotationSupport.Online && right is BaseStorageRotationSupport.Offline or BaseStorageRotationSupport.Online)
            return BaseStorageRotationSupport.Online;
        throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageRequirementConflict);
    }

    private static bool Satisfies(BaseStorageProtectionCapability capability, BaseStorageProtectionRequirement requirement) =>
        GuaranteeSatisfies(capability.Guarantee, requirement.PermittedGuarantees)
        && requirement.PermittedKeyOwners.Contains(capability.KeyOwner)
        && (int)capability.Verification >= (int)requirement.MinimumVerification
        && RotationSatisfies(capability.Rotation, requirement.RequiredRotation)
        && CoveragePairs(capability.Coverage, requirement.Coverage).All(static pair =>
            pair.Actual != BaseStorageProtectionState.Unprotected
            && pair.Required.Contains(pair.Actual));

    private static bool GuaranteeSatisfies(
        BaseStorageEncryptionGuarantee actual,
        ImmutableArray<BaseStorageEncryptionGuarantee> permitted) =>
        permitted.Contains(actual)
        || actual == BaseStorageEncryptionGuarantee.ProviderVerified
            && permitted.Contains(BaseStorageEncryptionGuarantee.ProviderDeclared);

    private static bool RotationSatisfies(BaseStorageRotationSupport actual, BaseStorageRotationSupport required) =>
        actual == required || actual == BaseStorageRotationSupport.Online && required == BaseStorageRotationSupport.Offline;

    private static IEnumerable<BaseStorageProtectionState> CoverageValues(BaseStorageProtectionCoverage value)
    {
        yield return value.AuthoritativeRecords; yield return value.Journal; yield return value.Receipts; yield return value.ProviderState; yield return value.Indexes;
        yield return value.TemporaryFiles; yield return value.AuthoritativeBackups; yield return value.AdministrativeExports; yield return value.OrdinaryExports; yield return value.ExternalFilesAndBlobs;
    }
    private static IEnumerable<(BaseStorageProtectionState Actual, ImmutableArray<BaseStorageProtectionState> Required)> CoveragePairs(BaseStorageProtectionCoverage actual, BaseStorageProtectionCoverageRequirement required)
    {
        yield return (actual.AuthoritativeRecords, required.AuthoritativeRecords); yield return (actual.Journal, required.Journal); yield return (actual.Receipts, required.Receipts);
        yield return (actual.ProviderState, required.ProviderState); yield return (actual.Indexes, required.Indexes); yield return (actual.TemporaryFiles, required.TemporaryFiles);
        yield return (actual.AuthoritativeBackups, required.AuthoritativeBackups); yield return (actual.AdministrativeExports, required.AdministrativeExports);
        yield return (actual.OrdinaryExports, required.OrdinaryExports); yield return (actual.ExternalFilesAndBlobs, required.ExternalFilesAndBlobs);
    }

    private static ImmutableArray<T> Copy<T>(ImmutableArray<T> value) where T : struct, Enum =>
        ImmutableArray.CreateRange(value.OrderBy(static item => item.ToString(), StringComparer.Ordinal));
    private static bool Valid<T>(ImmutableArray<T> value) where T : struct, Enum =>
        !value.IsDefaultOrEmpty && value.All(Enum.IsDefined) && value.Distinct().Count() == value.Length;
}

internal sealed record BaseStorageProtectionGraph(BaseStorageProtectionRequirement[] Requirements, BaseStorageProtectionCapability[] Capabilities);
