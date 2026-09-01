using System.Globalization;
using System.Text.Json.Serialization;
using HPD.Payments.Contracts.HeldPosition.QuotaWallet;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.QuotaWallet;

namespace HPD.Payments.Runtime.Base;

/// <summary>Provides the closed AOT-safe codec for conserved RES-009 wallet lot projections.</summary>
public static class PaymentsWalletLotFactCodecs
{
    /// <summary>Gets the exact wallet-lot state codec.</summary>
    public static PaymentsFactJsonCodec<WalletLotState> State { get; } = PaymentsFactJsonCodec.Create(
        "hpd.payments.wallet-lot-state.v1", PaymentsWalletLotJsonContext.Default.WalletLotStatePayload,
        WalletLotStatePayload.From, static payload => payload.ToValue());
}

internal sealed record WalletLotPayload(PaymentsIdentityPayload LotId, long Remaining, string Unit, int OriginKind,
    string OriginEffectiveAt, string? ExpiresAt, ulong Generation)
{
    internal static WalletLotPayload From(WalletLot value) => new(PaymentsIdentityPayload.From(value.LotId), value.Remaining,
        value.Unit, (int)value.OriginKind, value.OriginEffectiveAt.ToString("O", CultureInfo.InvariantCulture),
        value.ExpiresAt?.ToString("O", CultureInfo.InvariantCulture), value.Generation.Value);
    internal WalletLot ToValue() => new(LotId.ToValue(), Remaining, Unit, (WalletLotOriginKind)OriginKind,
        DateTimeOffset.Parse(OriginEffectiveAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        ExpiresAt is null ? null : DateTimeOffset.Parse(ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        OwnerGeneration.Create(Generation));
}

internal sealed record WalletLotStatePayload(WalletLotPayload Lot, long TotalCredited, long Available, long Reserved,
    long Consumed, long Expired, long TransferredOut, long Residual, ulong Generation, PaymentsIdentityPayload LastOperationId)
{
    internal static WalletLotStatePayload From(WalletLotState value) => new(WalletLotPayload.From(value.Lot), value.TotalCredited,
        value.Available, value.Reserved, value.Consumed, value.Expired, value.TransferredOut, value.Residual,
        value.Generation.Value, PaymentsIdentityPayload.From(value.LastOperationId));
    internal WalletLotState ToValue() => WalletLotState.Restore(Lot.ToValue(), TotalCredited, Available, Reserved, Consumed,
        Expired, TransferredOut, Residual, OwnerGeneration.Create(Generation), LastOperationId.ToValue());
}

[JsonSerializable(typeof(WalletLotStatePayload))]
internal sealed partial class PaymentsWalletLotJsonContext : JsonSerializerContext;
