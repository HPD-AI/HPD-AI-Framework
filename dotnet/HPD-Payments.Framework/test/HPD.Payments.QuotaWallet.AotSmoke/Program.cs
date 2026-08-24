using HPD.Payments.Contracts.HeldPosition.QuotaWallet;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.QuotaWallet;

ScopeId scope = ScopeId.Create("tenant", "aot", "held-position");
SemanticId Id(string kind, string local) => SemanticId.Create(scope, "wallet", kind, local);
DateTimeOffset now = DateTimeOffset.UnixEpoch.AddDays(10);
var lot = new WalletLot(Id("lot", "one"), 10, "usd", WalletLotOriginKind.Paid, now.AddDays(-5), now.AddDays(5), OwnerGeneration.Create(1));
IReadOnlyList<WalletSourceSlice> plan = ExpiryFirstWalletPlanner.Plan([lot], "usd", 6, now);
if (WalletPlanAdmission.Admit(plan, new Dictionary<SemanticId, OwnerGeneration> { [lot.LotId] = lot.Generation }, false, false) != QuotaAdmissionKind.Accepted)
    return 1;
WalletLotState state = WalletLotState.Create(lot, Id("operation", "create"))
    .Apply(WalletLotChangeKind.Reserve, 6, Id("operation", "reserve"), now)
    .Apply(WalletLotChangeKind.Consume, 2, Id("operation", "consume"), now)
    .Apply(WalletLotChangeKind.RetainResidue, 1, Id("operation", "residue"), now)
    .Apply(WalletLotChangeKind.Release, 3, Id("operation", "release"), now, nonOccurrenceProven: true)
    .Apply(WalletLotChangeKind.CorrectCredit, 2, Id("operation", "correct"), now)
    .Apply(WalletLotChangeKind.Expire, 9, Id("operation", "expire"), now.AddDays(5));
QuotaReservationProtocol quota = QuotaReservationProtocol.FromAdmission(Id("operation", "quota"), StrictFixedWindowQuota.Admit(10, 2, 1, 4, true, true)).RetainResidue();
if (state.TotalCredited != 12 || state.Consumed != 2 || state.Expired != 9 || state.Residual != 1 || quota.State != QuotaReservationState.Residual)
    return 1;
Console.WriteLine("PASS quota and wallet Native AOT conservation graph");
return 0;
