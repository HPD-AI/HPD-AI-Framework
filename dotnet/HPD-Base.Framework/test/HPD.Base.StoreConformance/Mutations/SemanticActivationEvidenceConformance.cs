using System.Collections.Immutable;

namespace HPD.Base.StoreConformance;

/// <summary>
/// Compiles the complete semantic-evidence checksum surface from an assembly that has no
/// framework-internal visibility. External provider conformance fixtures use this adapter
/// to author evidence and Runtime independently validates the resulting bytes.
/// </summary>
public static class SemanticActivationEvidenceConformance
{
    /// <summary>
    /// Builds a complete missing-slot capture exclusively through the public provider contract.
    /// This method deliberately lives outside every framework friend assembly.
    /// </summary>
    public static BaseCapturedSemanticActivationEvidence CreateMissingCapture(
        BaseAtomicSemanticActivationExtension extension,
        BaseSemanticActivationKeyDigest key,
        BaseSemanticActivationStoreAuthorityRequirement store,
        BaseSubjectScopeKind scopeKind,
        ReadOnlySpan<byte> bindingId,
        ReadOnlySpan<byte> protectedScope,
        ReadOnlySpan<byte> seekDigest,
        string protectionKeyId,
        int protectionKeyVersion,
        BaseAtomicReadIntervalEvidence scopeInterval,
        BaseAtomicReadIntervalEvidence slotInterval,
        BaseSemanticActivationAccounting accounting,
        BaseAcceptedTimeReceipt acceptedTime)
    {
        BaseSemanticActivationScopeBinding binding = BaseSemanticActivationEvidenceContract.CreateScopeBinding(
            scopeKind, bindingId, protectedScope, seekDigest, protectionKeyId, protectionKeyVersion);
        var directory = new BaseSemanticActivationScopeDirectoryCapture
        {
            State = BaseSemanticActivationScopeDirectoryState.Missing,
            ResultingBinding = binding,
            ReadIntervals = [scopeInterval],
            CanonicalBytes = checked(binding.BindingId.Length + binding.ProtectedCanonicalScope.Length + binding.SeekDigest.Length),
            Checksum = BaseSemanticActivationEvidenceContract.ScopeDirectoryChecksum(binding),
        };
        var missing = new BaseSemanticActivationMissingAuthority
        {
            Key = key,
            StoreAuthority = BaseSemanticActivationEvidenceContract.CreateStoreAuthority(store),
            AccessPathChecksum = BaseSemanticActivationEvidenceContract.MissingAccessPathChecksum(slotInterval.CanonicalLowerBound.AsSpan()),
        };
        var result = new BaseCapturedSemanticActivationEvidence
        {
            State = BaseSemanticActivationCapturedState.Missing,
            ScopeDirectory = directory,
            Missing = missing,
            ReadIntervals = [scopeInterval, slotInterval],
            Accounting = accounting,
            AcceptedTime = acceptedTime,
            Checksum = [],
        };
        return result with { Checksum = BaseSemanticActivationEvidenceContract.CapturedChecksum(extension, result) };
    }

    /// <summary>Computes live authority evidence through the public provider contract.</summary>
    public static ImmutableArray<byte> Live(BaseSemanticActivationLiveAuthority value) =>
        BaseSemanticActivationEvidenceContract.LiveChecksum(value);

    /// <summary>Computes retirement authority evidence through the public provider contract.</summary>
    public static ImmutableArray<byte> Retired(BaseSemanticActivationRetirementAuthority value) =>
        BaseSemanticActivationEvidenceContract.RetirementChecksum(value);

    /// <summary>Computes compacted-absence evidence through the public provider contract.</summary>
    public static ImmutableArray<byte> Absent(BaseSemanticActivationAbsenceAuthority value) =>
        BaseSemanticActivationEvidenceContract.AbsenceChecksum(value);

    /// <summary>Computes captured evidence through the public provider contract.</summary>
    public static ImmutableArray<byte> Captured(
        BaseAtomicSemanticActivationExtension extension,
        BaseCapturedSemanticActivationEvidence value) =>
        BaseSemanticActivationEvidenceContract.CapturedChecksum(extension, value);

    /// <summary>Computes write-interval evidence through the public provider contract.</summary>
    public static ImmutableArray<byte> WriteInterval(BaseSemanticActivationWriteIntervalEvidence value) =>
        BaseSemanticActivationEvidenceContract.WriteIntervalChecksum(value);

    /// <summary>Computes prepared evidence through the public provider contract.</summary>
    public static ImmutableArray<byte> Prepared(
        BaseAtomicSemanticActivationExtension extension,
        BasePreparedSemanticActivation value) =>
        BaseSemanticActivationEvidenceContract.PreparedChecksum(extension, value);

    /// <summary>Computes provisional evidence through the public provider contract.</summary>
    public static ImmutableArray<byte> Provisional(
        BasePreparedSemanticActivation prepared,
        BaseProvisionalSemanticActivation value) =>
        BaseSemanticActivationEvidenceContract.ProvisionalChecksum(prepared, value);
}
