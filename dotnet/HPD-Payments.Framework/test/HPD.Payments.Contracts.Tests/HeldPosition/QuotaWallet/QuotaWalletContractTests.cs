using HPD.Payments.Contracts.HeldPosition.QuotaWallet;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Contracts.Tests.HeldPosition.QuotaWallet;

/// <summary>Executes the accepted RES-009 quota and wallet decision vectors.</summary>
public static class QuotaWalletContractTests
{
    /// <summary>Runs deterministic positive and negative policy vectors.</summary>
    public static void Run()
    {
        Equal(QuotaAdmissionKind.Accepted, StrictFixedWindowQuota.Admit(10, 7, 0, 3, true).Kind);
        Equal(QuotaAdmissionKind.Rejected, StrictFixedWindowQuota.Admit(10, 7, 3, 3, true).Kind);
        QuotaAdmissionResult uncertain = StrictFixedWindowQuota.Admit(10, 0, 0, 4, true, true);
        Equal(QuotaAdmissionKind.Indeterminate, uncertain.Kind); Equal(4L, uncertain.RetainedReservation); Equal(6L, uncertain.Remaining);
        Equal(QuotaAdmissionKind.Rejected, StrictFixedWindowQuota.Admit(10, 0, 0, 1, false).Kind);

        DateTimeOffset now = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        ScopeId scope = ScopeId.Create("tenant", "wallet", "lots");
        WalletLot paid = Lot("a", 5, WalletLotOriginKind.Paid, now.AddDays(-5), now.AddDays(22));
        WalletLot promo = Lot("b", 8, WalletLotOriginKind.Promotional, now.AddDays(-2), now.AddDays(10));
        IReadOnlyList<WalletSourceSlice> slices = ExpiryFirstWalletPlanner.Plan([paid, promo], "usd", 10, now);
        Equal(2, slices.Count); Equal(promo.LotId, slices[0].LotId); Equal(8L, slices[0].Quantity); Equal(2L, slices[1].Quantity);
        Equal(0, ExpiryFirstWalletPlanner.Plan([paid], "usd", 6, now).Count);
        Equal(QuotaAdmissionKind.Rejected, WalletTransferGuard.Validate("usd", "eur", true, true));
        Equal(QuotaAdmissionKind.Indeterminate, WalletTransferGuard.Validate("usd", "usd", true, false));
        Equal(QuotaAdmissionKind.Accepted, WalletTransferGuard.Validate("usd", "usd", true, true));
        Throws<OverflowException>(() => StrictFixedWindowQuota.Admit(long.MaxValue, long.MaxValue, 1, 1, true));

        WalletLot Lot(string id, long amount, WalletLotOriginKind kind, DateTimeOffset origin, DateTimeOffset? expiry) =>
            new(SemanticId.Create(scope, "wallet", "lot", id), amount, "usd", kind, origin, expiry, OwnerGeneration.Create(1));
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; got {actual}."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
