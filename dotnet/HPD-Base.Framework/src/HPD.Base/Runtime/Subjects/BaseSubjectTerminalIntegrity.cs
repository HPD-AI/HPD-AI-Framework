using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal static class BaseSubjectTerminalIntegrity
{
    internal static string Compute(
        string contractId,
        int contractVersion,
        BaseSubjectId subjectId,
        BaseOwnedSubjectScopeEvidence scope,
        BaseSubjectAuthorityEpoch authorityEpoch,
        BaseSubjectIncarnation incarnation,
        long lifetimeGeneration,
        long subjectSequence,
        BaseMutationJournalPosition retiredPosition,
        long contractStateGeneration,
        long restoreEpoch) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.subjectLifecycle.terminal.v1\0{contractId}\0{contractVersion}\0{subjectId.Value}\0{(int)scope.Kind}\0{scope.Value}\0{authorityEpoch.ToBase64Url()}\0{incarnation.ToBase64Url()}\0{lifetimeGeneration}\0{subjectSequence}\0{retiredPosition.Value}\0{contractStateGeneration}\0{restoreEpoch}")));

    internal static bool Verify(BaseSubjectTerminalLifetimeReceipt receipt, BaseOwnedSubjectScopeEvidence scope) =>
        receipt.Scope.Kind == scope.Kind &&
        string.Equals(receipt.ReceiptChecksum, Compute(
            receipt.ContractId, receipt.ContractVersion, receipt.SubjectId, scope,
            receipt.RetiredAuthorityEpoch, receipt.RetiredIncarnation,
            receipt.RetiredLifetimeGeneration, receipt.RetiredSubjectSequence,
            receipt.RetiredPosition, receipt.ContractStateGeneration, receipt.RestoreEpoch),
            StringComparison.Ordinal);
}
