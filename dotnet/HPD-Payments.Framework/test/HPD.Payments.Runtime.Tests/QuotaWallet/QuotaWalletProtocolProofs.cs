using HPD.Payments.Contracts.HeldPosition.QuotaWallet;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.QuotaWallet;

namespace HPD.Payments.Runtime.Tests.QuotaWallet;

internal static class QuotaWalletProtocolProofs
{
    internal static void Run(List<string> failures)
    {
        void Check(bool condition, string message) { if (!condition) failures.Add(message); }
        ScopeId scope = ScopeId.Create("tenant", "runtime", "quota");
        SemanticId operation = SemanticId.Create(scope, "quota", "operation", "one");
        var reserved = QuotaReservationProtocol.FromAdmission(operation, new(QuotaAdmissionKind.Accepted, 6, 4));
        Check(reserved.Consume().State == QuotaReservationState.Consumed, "accepted quota reservation did not consume");
        Check(reserved.Release(true, false).State == QuotaReservationState.Released, "proven pre-effect quota reservation did not release");
        Throws<InvalidOperationException>(() => reserved.Release(true, true), failures, "possible effect released quota capacity");
        var uncertain = QuotaReservationProtocol.FromAdmission(operation, new(QuotaAdmissionKind.Indeterminate, 6, 4));
        Check(uncertain.RetainResidue().State == QuotaReservationState.Residual, "uncertain quota reservation lost residue");
        Throws<InvalidOperationException>(() => uncertain.Consume(), failures, "indeterminate quota reservation consumed blindly");

        SemanticId lot = SemanticId.Create(scope, "wallet", "lot", "one");
        var slice = new WalletSourceSlice(lot, 4, OwnerGeneration.Create(2));
        var current = new Dictionary<SemanticId, OwnerGeneration> { [lot] = OwnerGeneration.Create(2) };
        Check(WalletPlanAdmission.Admit([slice], current, false, false) == QuotaAdmissionKind.Accepted, "current wallet plan rejected");
        current[lot] = OwnerGeneration.Create(3);
        Check(WalletPlanAdmission.Admit([slice], current, false, false) == QuotaAdmissionKind.Rejected, "stale wallet generation admitted");
        Check(WalletPlanAdmission.Admit([slice], current, false, true) == QuotaAdmissionKind.Indeterminate, "possible effect was flattened during wallet replan");

        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        SemanticId Op(string local) => SemanticId.Create(scope, "wallet", "operation", local);
        var source = WalletLotState.Create(new WalletLot(lot, 10, "usd", WalletLotOriginKind.Paid, now.AddDays(-5), now.AddDays(5), OwnerGeneration.Create(1)), Op("create-source"))
            .Apply(WalletLotChangeKind.Reserve, 6, Op("reserve"), now)
            .Apply(WalletLotChangeKind.Consume, 2, Op("consume"), now)
            .Apply(WalletLotChangeKind.RetainResidue, 1, Op("residue"), now)
            .Apply(WalletLotChangeKind.Release, 3, Op("release"), now, nonOccurrenceProven: true)
            .Apply(WalletLotChangeKind.CorrectCredit, 2, Op("correction-credit"), now);
        SemanticId destinationLot = SemanticId.Create(scope, "wallet", "lot", "two");
        var destination = WalletLotState.Create(new WalletLot(destinationLot, 1, "usd", WalletLotOriginKind.Paid, now, null, OwnerGeneration.Create(1)), Op("create-destination"));
        (source, destination) = WalletLotState.Transfer(source, destination, 4, Op("transfer-out"), Op("transfer-in"), now);
        source = source.Apply(WalletLotChangeKind.CorrectDebit, 1, Op("correction-debit"), now);
        Check(source.TotalCredited == 11 && source.Available == 4 && source.Consumed == 2 && source.TransferredOut == 4 && source.Residual == 1 &&
            destination.TotalCredited == 5 && destination.Available == 5, "wallet correction/recredit/transfer/residue did not conserve lots");
        Throws<InvalidOperationException>(() => source.Apply(WalletLotChangeKind.Expire, 1, Op("early-expiry"), now), failures, "wallet lot expired before expiry");
        var atExpiry = source.Apply(WalletLotChangeKind.Expire, 4, Op("expiry"), now.AddDays(5));
        Check(atExpiry.Available == 0 && atExpiry.Expired == 4, "wallet expiry did not move exact available quantity");
    }

    private static void Throws<T>(Action action, List<string> failures, string message) where T : Exception
    { try { action(); } catch (T) { return; } failures.Add(message); }
}
