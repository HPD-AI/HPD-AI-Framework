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
    int DestinationVariantId = 0
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
    int DestinationVariantId = 0
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
    int DestinationVariantId = 0
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
    int DestinationVariantId = 0
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
    int DestinationVariantId = 0
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
