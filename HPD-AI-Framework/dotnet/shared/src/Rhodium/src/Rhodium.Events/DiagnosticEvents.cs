using Rhodium.Primitives;

namespace Rhodium.Events;

// ==================== DIAGNOSTIC EVENTS ====================

/// <summary>
/// Performance snapshot for monitoring.
/// Background priority - processed when idle.
/// </summary>
public sealed record PerformanceSnapshot(
    Money Equity,
    Money Cash,
    Money UnrealizedPnL,
    Money RealizedPnL,
    int OpenPositions,
    int OpenOrders
) : DiagnosticEvent;

/// <summary>
/// Same-asset quotes across venues are crossed before routing costs.
/// This is a market-quality signal for smart-routing and cross-venue diagnostics,
/// not an instruction to trade.
/// </summary>
public sealed record CrossVenueArbitrageOpportunity(
    Asset Asset,
    Venue BuyVenue,
    Venue SellVenue,
    Price BuyAsk,
    Price SellBid,
    Qty ExecutableQuantity,
    Money GrossSpreadPerUnit,
    decimal GrossSpreadBps,
    Instant DetectedAt
) : DiagnosticEvent;

/// <summary>
/// Replay account statement for a strategy/variant/currency account slice.
/// Emitted at replay end so backtests can inspect cash, reservations, settlement, and marks together.
/// </summary>
public sealed record AccountStatementSnapshot(
    StrategyId StrategyId,
    int VariantId,
    Currency Currency,
    Money Cash,
    Money AvailableCash,
    Money PendingSettlement,
    Money ReservedCash,
    Money MarketValue,
    Money Equity,
    Money UnrealizedPnL,
    Money RealizedPnL,
    int OpenPositions,
    int OpenOrders
) : DiagnosticEvent;

/// <summary>
/// Replay custody position statement for a strategy/variant/instrument slice.
/// Emitted after fills and at replay end so account totals can be reconciled to holdings.
/// </summary>
public sealed record CustodyPositionSnapshot(
    StrategyId StrategyId,
    int VariantId,
    Instrument Instrument,
    Qty Quantity,
    Qty SettledQuantity,
    Qty PendingDeliveryQuantity,
    Qty RehypothecatableQuantity,
    Price AvgEntryPrice,
    Price MarkPrice,
    Money MarketValue,
    Money UnrealizedPnL,
    Money RealizedPnL,
    bool IsOpen
) : DiagnosticEvent;

/// <summary>
/// Replay asset quantity scheduled for future custody delivery.
/// </summary>
public sealed record AssetDeliveryScheduled(
    AssetDeliveryId DeliveryId,
    StrategyId StrategyId,
    int VariantId,
    Instrument Instrument,
    Qty Quantity,
    Instant DeliversAt
) : DiagnosticEvent;

/// <summary>
/// Replay asset quantity delivered into settled custody.
/// </summary>
public sealed record AssetDelivered(
    AssetDeliveryId DeliveryId,
    StrategyId StrategyId,
    int VariantId,
    Instrument Instrument,
    Qty Quantity,
    Instant DeliveredAt
) : DiagnosticEvent;

/// <summary>
/// Replay asset quantity removed from pending delivery before custody settlement.
/// </summary>
public sealed record AssetDeliveryCanceled(
    AssetDeliveryId DeliveryId,
    StrategyId StrategyId,
    int VariantId,
    Instrument Instrument,
    Qty Quantity,
    Instant CanceledAt
) : DiagnosticEvent;

/// <summary>
/// Replay-visible asset-delivery lifecycle state.
/// </summary>
public sealed record AssetDeliveryStatusSnapshot(
    AssetDeliveryId DeliveryId,
    StrategyId StrategyId,
    int VariantId,
    Instrument Instrument,
    Qty Quantity,
    AssetDeliveryStatus Status,
    Instant DeliversAt,
    Instant StatusAt
) : DiagnosticEvent;

/// <summary>
/// Replay cash proceeds scheduled for future settlement.
/// </summary>
public sealed record SettlementScheduled(
    SettlementId SettlementId,
    StrategyId StrategyId,
    int VariantId,
    Money Amount,
    Instant SettlesAt
) : DiagnosticEvent;

/// <summary>
/// Replay cash proceeds released into settled cash.
/// </summary>
public sealed record SettlementReleased(
    SettlementId SettlementId,
    StrategyId StrategyId,
    int VariantId,
    Money Amount,
    Instant SettledAt
) : DiagnosticEvent;

/// <summary>
/// Replay-visible settlement lifecycle state.
/// Emitted alongside schedule/release events so consumers can track status by settlement id.
/// </summary>
public sealed record SettlementStatusSnapshot(
    SettlementId SettlementId,
    StrategyId StrategyId,
    int VariantId,
    SettlementStatus Status,
    Money Amount,
    Instant SettlesAt,
    Instant StatusAt
) : DiagnosticEvent;

/// <summary>
/// Replay account or custody transfer requested by an external account workflow.
/// Cash transfers set <see cref="CashAmount"/>; asset transfers set <see cref="Instrument"/> and <see cref="Quantity"/>.
/// </summary>
public sealed record AccountTransferRequested(
    AccountTransferId TransferId,
    StrategyId StrategyId,
    int VariantId,
    AccountTransferType TransferType,
    Money? CashAmount,
    Instrument? Instrument,
    Qty Quantity,
    Instant RequestedAt,
    string? ExternalReference = null,
    StrategyId? DestinationStrategyId = null,
    int DestinationVariantId = 0,
    Venue? Venue = null,
    Price? CarryingPrice = null
) : DiagnosticEvent;

/// <summary>
/// Replay account or custody transfer completed.
/// </summary>
public sealed record AccountTransferCompleted(
    AccountTransferId TransferId,
    StrategyId StrategyId,
    int VariantId,
    AccountTransferType TransferType,
    Money? CashAmount,
    Instrument? Instrument,
    Qty Quantity,
    Instant CompletedAt,
    string? ExternalReference = null,
    StrategyId? DestinationStrategyId = null,
    int DestinationVariantId = 0,
    Venue? Venue = null,
    Price? CarryingPrice = null
) : DiagnosticEvent;

/// <summary>
/// Replay account or custody transfer canceled before completion.
/// </summary>
public sealed record AccountTransferCanceled(
    AccountTransferId TransferId,
    StrategyId StrategyId,
    int VariantId,
    AccountTransferType TransferType,
    Money? CashAmount,
    Instrument? Instrument,
    Qty Quantity,
    Instant CanceledAt,
    string? Reason = null,
    string? ExternalReference = null,
    StrategyId? DestinationStrategyId = null,
    int DestinationVariantId = 0,
    Venue? Venue = null,
    Price? CarryingPrice = null
) : DiagnosticEvent;

/// <summary>
/// Replay account or custody transfer failed before completion.
/// </summary>
public sealed record AccountTransferFailed(
    AccountTransferId TransferId,
    StrategyId StrategyId,
    int VariantId,
    AccountTransferType TransferType,
    Money? CashAmount,
    Instrument? Instrument,
    Qty Quantity,
    Instant FailedAt,
    string Reason,
    string? ExternalReference = null,
    StrategyId? DestinationStrategyId = null,
    int DestinationVariantId = 0,
    Venue? Venue = null,
    Price? CarryingPrice = null
) : DiagnosticEvent;

/// <summary>
/// Replay-visible account-transfer lifecycle state.
/// </summary>
public sealed record AccountTransferStatusSnapshot(
    AccountTransferId TransferId,
    StrategyId StrategyId,
    int VariantId,
    AccountTransferType TransferType,
    AccountTransferStatus Status,
    Money? CashAmount,
    Instrument? Instrument,
    Qty Quantity,
    Instant StatusAt,
    string? Reason = null,
    string? ExternalReference = null,
    StrategyId? DestinationStrategyId = null,
    int DestinationVariantId = 0,
    Venue? Venue = null,
    Price? CarryingPrice = null
) : DiagnosticEvent;

/// <summary>
/// Replay corporate action applied to account and custody state.
/// </summary>
public sealed record CorporateActionApplied(
    CorporateActionId CorporateActionId,
    CorporateActionType ActionType,
    Instrument Instrument,
    Instant EffectiveAt,
    decimal SplitRatio = 0m,
    Money? DividendPerShare = null,
    string? ExternalReference = null
) : DiagnosticEvent;

/// <summary>
/// Replay-visible effect of a corporate action on a strategy/variant account slice.
/// </summary>
public sealed record CorporateActionEffectSnapshot(
    CorporateActionId CorporateActionId,
    CorporateActionType ActionType,
    StrategyId StrategyId,
    int VariantId,
    Instrument Instrument,
    Qty QuantityBefore,
    Qty QuantityAfter,
    Price AvgEntryPriceBefore,
    Price AvgEntryPriceAfter,
    Money? CashAmount,
    Instant EffectiveAt
) : DiagnosticEvent;

/// <summary>
/// Replay financing cash flow applied to an account slice.
/// Positive amounts credit cash; negative amounts debit cash.
/// </summary>
public sealed record FinancingChargeApplied(
    FinancingChargeId FinancingChargeId,
    FinancingChargeType ChargeType,
    StrategyId StrategyId,
    int VariantId,
    Money Amount,
    Instant EffectiveAt,
    Instrument? Instrument = null,
    Qty Quantity = default,
    decimal Rate = 0m,
    string? ExternalReference = null
) : DiagnosticEvent;

/// <summary>
/// Replay-visible option lifecycle effect produced by expiry, exercise, assignment, or settlement.
/// </summary>
public sealed record OptionLifecycleApplied : DiagnosticEvent
{
    public OptionLifecycleApplied(
        StrategyId StrategyId,
        int VariantId,
        Instrument Instrument,
        OptionLifecycleKind LifecycleKind,
        Qty Quantity,
        Money CashFlow,
        Instant AppliedAt,
        Price? UnderlyingMark = null,
        Instrument? Deliverable = null,
        Qty? DeliverableQuantity = null,
        Price? SettlementPrice = null,
        OptionLifecycleReferenceSource ReferenceSource = OptionLifecycleReferenceSource.None,
        string? Reason = null)
    {
        if (!Enum.IsDefined(LifecycleKind))
            throw new ArgumentOutOfRangeException(nameof(LifecycleKind), LifecycleKind, "Unknown option lifecycle kind.");

        if (!Enum.IsDefined(ReferenceSource))
            throw new ArgumentOutOfRangeException(nameof(ReferenceSource), ReferenceSource, "Unknown option lifecycle reference source.");

        if (Quantity.IsZero)
            throw new ArgumentException("Option lifecycle event requires a nonzero quantity.", nameof(Quantity));

        if (LifecycleKind == OptionLifecycleKind.Blocked && ReferenceSource != OptionLifecycleReferenceSource.None)
            throw new ArgumentException("Blocked option lifecycle event must use reference source None.", nameof(ReferenceSource));

        if (LifecycleKind != OptionLifecycleKind.Blocked && ReferenceSource == OptionLifecycleReferenceSource.None)
            throw new ArgumentException("Resolved option lifecycle event requires a non-None reference source.", nameof(ReferenceSource));

        if (LifecycleKind != OptionLifecycleKind.Blocked && UnderlyingMark is null)
            throw new ArgumentException("Resolved option lifecycle event requires an underlying/reference mark.", nameof(UnderlyingMark));

        if (LifecycleKind == OptionLifecycleKind.PhysicalDelivery)
        {
            if (Deliverable is null)
                throw new ArgumentException("Physical delivery lifecycle event requires a deliverable instrument.", nameof(Deliverable));
            if (DeliverableQuantity is null || DeliverableQuantity.Value.IsZero)
                throw new ArgumentException("Physical delivery lifecycle event requires a nonzero deliverable quantity.", nameof(DeliverableQuantity));
            if (SettlementPrice is null)
                throw new ArgumentException("Physical delivery lifecycle event requires a settlement price.", nameof(SettlementPrice));
        }
        else
        {
            if (Deliverable is not null)
                throw new ArgumentException("Only physical delivery lifecycle events can carry a deliverable instrument.", nameof(Deliverable));
            if (DeliverableQuantity is not null)
                throw new ArgumentException("Only physical delivery lifecycle events can carry a deliverable quantity.", nameof(DeliverableQuantity));
        }

        if (LifecycleKind == OptionLifecycleKind.Blocked)
        {
            if (!CashFlow.IsZero)
                throw new ArgumentException("Blocked option lifecycle event cannot carry cash flow.", nameof(CashFlow));
            if (UnderlyingMark is not null)
                throw new ArgumentException("Blocked option lifecycle event cannot carry an underlying mark.", nameof(UnderlyingMark));
            if (SettlementPrice is not null)
                throw new ArgumentException("Blocked option lifecycle event cannot carry a settlement price.", nameof(SettlementPrice));
        }

        if (LifecycleKind is OptionLifecycleKind.Exercise or OptionLifecycleKind.Assignment && !CashFlow.IsZero)
        {
            throw new ArgumentException("Exercise and assignment lifecycle events cannot carry cash flow.", nameof(CashFlow));
        }

        if (Reason is not null && string.IsNullOrWhiteSpace(Reason))
            throw new ArgumentException("Option lifecycle event reason cannot be empty.", nameof(Reason));

        this.StrategyId = StrategyId;
        this.VariantId = VariantId;
        this.Instrument = Instrument;
        this.LifecycleKind = LifecycleKind;
        this.Quantity = Quantity;
        this.CashFlow = CashFlow;
        this.AppliedAt = AppliedAt;
        this.UnderlyingMark = UnderlyingMark;
        this.Deliverable = Deliverable;
        this.DeliverableQuantity = DeliverableQuantity;
        this.SettlementPrice = SettlementPrice;
        this.ReferenceSource = ReferenceSource;
        this.Reason = Reason;
    }

    public StrategyId StrategyId { get; }
    public int VariantId { get; }
    public Instrument Instrument { get; }
    public OptionLifecycleKind LifecycleKind { get; }
    public Qty Quantity { get; }
    public Money CashFlow { get; }
    public Instant AppliedAt { get; }
    public Price? UnderlyingMark { get; }
    public Instrument? Deliverable { get; }
    public Qty? DeliverableQuantity { get; }
    public Price? SettlementPrice { get; }
    public OptionLifecycleReferenceSource ReferenceSource { get; }
    public string? Reason { get; }
}

public enum OptionLifecycleKind : byte
{
    Exercise,
    Assignment,
    ExpireWorthless,
    ExpireUnexercised,
    ExpireUnassigned,
    CashSettlement,
    PhysicalDelivery,
    Blocked
}

public enum OptionLifecycleReferenceSource : byte
{
    None,
    MarketMark,
    InstrumentSettlementData,
    UnderlyingSettlementData,
    InstrumentSettlementOverride,
    UnderlyingSettlementOverride
}

/// <summary>
/// Replay-visible order lifecycle state.
/// Emitted alongside execution events so consumers do not need to infer order state.
/// </summary>
public sealed record OrderStateSnapshot(
    OrderId OrderId,
    StrategyId StrategyId,
    int VariantId,
    OrderStatus Status,
    Qty? FilledQty = null,
    Qty? RemainingQty = null,
    string? Reason = null
) : DiagnosticEvent;

/// <summary>
/// Margin status for a strategy/variant account slice.
/// Emitted by replay when margin positions are marked against current market data.
/// </summary>
public sealed record MarginStatusSnapshot(
    StrategyId StrategyId,
    int VariantId,
    Money Equity,
    Money MaintenanceRequirement,
    bool IsMaintenanceBreached
) : DiagnosticEvent;

/// <summary>
/// Margin call lifecycle event emitted when replay equity falls below maintenance requirement.
/// </summary>
public sealed record MarginCallIssued(
    StrategyId StrategyId,
    int VariantId,
    Money Equity,
    Money MaintenanceRequirement,
    Instant DueAt
) : DiagnosticEvent;

/// <summary>
/// Margin call lifecycle event emitted when replay equity recovers before liquidation.
/// </summary>
public sealed record MarginCallResolved(
    StrategyId StrategyId,
    int VariantId,
    Money Equity,
    Money MaintenanceRequirement
) : DiagnosticEvent;

/// <summary>
/// Latency measurement for monitoring.
/// </summary>
public sealed record LatencyMeasured(
    string Operation,
    Duration Latency
) : DiagnosticEvent;
