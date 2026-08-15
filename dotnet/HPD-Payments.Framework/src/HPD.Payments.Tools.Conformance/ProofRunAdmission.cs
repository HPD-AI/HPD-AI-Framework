namespace HPD.Payments.Tools.Conformance;

/// <summary>Admits a candidate receipt only after exact command and source-envelope checks.</summary>
internal static class ProofRunAdmission
{
    internal static ProofReceipt Admit(ProofReceipt candidate, SourceTreeSnapshot before, SourceTreeSnapshot after,
        ProofCommandDefinition command, ExecutionCleanupSnapshot cleanup, ProofAssertionOutcome assertions)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(command);
        if (!command.Enabled) throw new InvalidOperationException("A disabled command cannot emit an admitted receipt.");
        if (!StringComparer.Ordinal.Equals(candidate.CommandBinding, command.Binding))
            throw new InvalidDataException("Receipt command binding does not match the admitted manifest entry.");
        SourceTreeSnapshotter.RequireStable(before, after);
        if (!StringComparer.Ordinal.Equals(candidate.WholeTreeDigest, before.InventoryDigest))
            throw new InvalidDataException("Receipt whole-tree digest does not match the stable execution inventory.");
        if (!command.AcceptedExitCodes.Contains(candidate.ExitStatus))
            throw new InvalidDataException("Receipt exit status is not admitted by the command.");
        if (!cleanup.IsClean || !StringComparer.Ordinal.Equals(candidate.CleanupAttestation, cleanup.Attestation))
            throw new InvalidDataException("Receipt cleanup attestation is missing, dirty, or mismatched.");
        assertions.Validate();
        if (!assertions.IsPassing || !StringComparer.Ordinal.Equals(candidate.AssertionsDigest, assertions.EvidenceDigest))
            throw new InvalidDataException("Receipt assertion evidence is incomplete, non-passing, or mismatched.");
        var errors = ProofReceiptContractValidator.Validate(candidate);
        if (errors.Count != 0) throw new InvalidDataException("Receipt contract is invalid: " + string.Join(',', errors));
        return candidate;
    }
}
