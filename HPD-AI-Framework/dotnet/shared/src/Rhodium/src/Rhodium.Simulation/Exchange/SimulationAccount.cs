using Rhodium.Events;
using Rhodium.Options;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Exchange;

/// <summary>
/// Venue-owned account state for simulation cash and reservation effects.
/// </summary>
public sealed class SimulationAccount
{
    private readonly IInstrumentValuationModel _valuation;
    private readonly Dictionary<Instrument, InstrumentContract> _contracts = [];
    private readonly Dictionary<OrderId, AccountReservation> _reservations = [];
    private readonly Dictionary<(StrategyId StrategyId, int VariantId), Money> _cashSlices = [];
    private readonly Dictionary<(StrategyId StrategyId, int VariantId, Instrument Instrument), AccountPosition> _positions = [];
    private readonly Dictionary<(StrategyId StrategyId, int VariantId, Instrument Instrument), Qty> _settledPositions = [];
    private readonly Dictionary<(StrategyId StrategyId, int VariantId, Currency Currency), Money> _realizedPnL = [];
    private readonly List<PendingCashSettlement> _pendingSettlements = [];
    private readonly List<PendingAssetDelivery> _pendingAssetDeliveries = [];
    private readonly List<FinanceEvent> _events = [];
    private readonly List<AccountTradeNotional> _feeNotionalHistory = [];
    private readonly List<(StrategyId StrategyId, int VariantId, Instrument Instrument)> _positionKeyScratch = [];
    private readonly List<(StrategyId StrategyId, int VariantId, Instrument Instrument)> _settledPositionKeyScratch = [];
    private readonly Money _initialCash;
    private readonly SettlementParams? _settlementOverride;

    public SimulationAccount(
        Money initialCash,
        AccountType accountType = AccountType.Cash,
        SettlementParams? settlementOverride = null,
        IInstrumentValuationModel? valuation = null)
    {
        _initialCash = initialCash;
        _settlementOverride = settlementOverride;
        _valuation = valuation ?? DefaultInstrumentValuationModel.Instance;
        Cash = initialCash;
        AccountType = accountType;
    }

    public AccountType AccountType { get; }
    public Money Cash { get; private set; }
    public Money ReservedCash => new(GetReservedCashAmount(), Cash.Currency);
    public Money PendingSettlement => new(GetPendingSettlementAmount(Cash.Currency), Cash.Currency);
    public Qty PendingAssetDeliveryQuantity => new(GetPendingAssetDeliveryQuantity());
    public int PendingSettlementCount => _pendingSettlements.Count;
    public int PendingAssetDeliveryCount => _pendingAssetDeliveries.Count;
    public Money AvailableCash => Cash - ReservedCash;

    public void RegisterContract(InstrumentContract contract)
    {
        InstrumentContractValidator.Validate(contract).ThrowIfInvalid();
        _contracts[contract.Instrument] = contract;
    }

    public bool TryGetContract(Instrument instrument, out InstrumentContract contract) =>
        _contracts.TryGetValue(instrument, out contract!);

    internal InstrumentContract ResolveContract(Instrument instrument) => GetContract(instrument);

    public Money GetThirtyDayFeeVolume(StrategyId strategyId, int variantId, Currency currency, Instant now)
    {
        var cutoff = now - Duration.FromDays(30);
        var amount = 0m;
        for (var i = _feeNotionalHistory.Count - 1; i >= 0; i--)
        {
            var item = _feeNotionalHistory[i];
            if (item.TradedAt < cutoff)
            {
                _feeNotionalHistory.RemoveAt(i);
                continue;
            }

            if (item.StrategyId == strategyId
                && item.VariantId == variantId
                && item.Notional.Currency == currency)
            {
                amount += item.Notional.Amount;
            }
        }

        return new Money(amount, currency);
    }

    public bool TryReserve(
        SimulationOrderCommand command,
        Price price,
        MarginParams margin,
        SettlementParams settlement,
        bool allowCashBorrowing,
        out string reason)
    {
        reason = string.Empty;
        if (AccountType == AccountType.Cash)
            return TryReserveCashAccount(command, price, settlement, allowCashBorrowing, out reason);

        return TryReserveMarginAccount(command, price, margin, out reason);
    }

    public bool TryReservePackage(
        SimulationOrderCommand command,
        IReadOnlyList<PackageLegFill> legFills,
        Price packagePrice,
        MarginParams margin,
        SettlementParams settlement,
        bool allowCashBorrowing,
        out string reason)
    {
        reason = string.Empty;
        var packageContract = GetContract(command.Instrument);
        if (packageContract.Package is not { IsAtomicExecution: true })
        {
            reason = $"Instrument {command.Instrument} is not an atomic package.";
            return false;
        }

        if (legFills.Count == 0)
        {
            reason = $"Package order {command.Instrument} has no executable legs.";
            return false;
        }

        var netDebit = ToAccountCurrency(GetPackageNetDebit(legFills), Cash.Currency);
        if (AccountType == AccountType.Cash)
        {
            if (!allowCashBorrowing && netDebit.Amount > AvailableCash.Amount)
            {
                reason = $"Insufficient cash buying power: required {netDebit}, available {AvailableCash}.";
                return false;
            }

            if (netDebit.Amount > 0m)
                Reserve(command.ClientOrderId, netDebit, command.Instrument, command.Quantity, Qty.Zero);

            return true;
        }

        var requirement = ToAccountCurrency(
            GetPackageInitialMarginRequirement(legFills, packageContract, packagePrice, margin),
            Cash.Currency);
        var totalRequirement = new Money(Math.Max(0m, requirement.Amount + netDebit.Amount), Cash.Currency);
        if (totalRequirement.Amount > AvailableCash.Amount)
        {
            reason = $"Insufficient margin buying power: required {totalRequirement}, available {AvailableCash}.";
            return false;
        }

        Reserve(command.ClientOrderId, totalRequirement, command.Instrument, command.Quantity, Qty.Zero);
        return true;
    }

    private bool TryReserveCashAccount(
        SimulationOrderCommand command,
        Price price,
        SettlementParams settlement,
        bool allowCashBorrowing,
        out string reason)
    {
        reason = string.Empty;
        var contract = GetContract(command.Instrument);
        var contractSettlement = GetFillSettlement(contract, settlement.UnsettledSalePolicy);
        if (command.Side != Side.Buy)
        {
            if (ShouldDeliverAssetOnFill(contract)
                && contractSettlement.UnsettledSalePolicy == UnsettledSalePolicy.Reject)
            {
                var settled = GetSettledPosition(command.StrategyId, command.VariantId, command.Instrument);
                if (command.Quantity.Value > settled.Value)
                {
                    reason = $"Cash account cannot sell {command.Quantity.Value} with only {settled.Value} available.";
                    return false;
                }
            }

            return true;
        }

        var notional = ToAccountCurrency(GetUpfrontCashFlow(contract, command.Quantity, price), Cash.Currency);
        if (!allowCashBorrowing && notional.Amount > AvailableCash.Amount)
        {
            reason = $"Insufficient cash buying power: required {notional}, available {AvailableCash}.";
            return false;
        }

        Reserve(command.ClientOrderId, notional, command.Instrument, command.Quantity, Qty.Zero);
        return true;
    }

    private bool TryReserveMarginAccount(
        SimulationOrderCommand command,
        Price price,
        MarginParams margin,
        out string reason)
    {
        reason = string.Empty;
        var shortIncrease = GetShortIncrease(command);
        if (shortIncrease.Value > 0m
            && margin.ShortSalePolicy == ShortSalePolicy.RequireBorrow
            && GetAvailableBorrow(command.Instrument, margin).Value < shortIncrease.Value)
        {
            var availableLongOrLocatedInventory = Math.Max(
                0m,
                command.Quantity.Value - shortIncrease.Value + GetAvailableBorrow(command.Instrument, margin).Value);
            reason = $"Margin account short sale requires borrow/locate: requested {command.Quantity.Value}, available long or located inventory {availableLongOrLocatedInventory}.";
            return false;
        }

        var quantityRequiringMargin = command.Side == Side.Buy
            ? command.Quantity
            : shortIncrease;
        if (quantityRequiringMargin.Value <= 0m)
        {
            Reserve(command.ClientOrderId, Money.Zero(Cash.Currency), command.Instrument, Qty.Zero, shortIncrease);
            return true;
        }

        var contract = GetContract(command.Instrument);
        var marginQuantity = command.Side == Side.Sell
            ? new Qty(-quantityRequiringMargin.Value)
            : quantityRequiringMargin;
        var requirement = ToAccountCurrency(
            GetInitialMarginRequirement(contract, marginQuantity, price, margin, null),
            Cash.Currency);
        if (requirement.Amount > AvailableCash.Amount)
        {
            reason = $"Insufficient margin buying power: required {requirement}, available {AvailableCash}.";
            return false;
        }

        Reserve(command.ClientOrderId, requirement, command.Instrument, quantityRequiringMargin, shortIncrease);
        return true;
    }

    public void ApplyFill(
        SimulationOrderCommand command,
        Qty quantity,
        Price price,
        Money commission,
        Instant now)
    {
        var orderId = command.ClientOrderId;
        var contract = GetContract(command.Instrument);
        var settlement = GetFillSettlement(contract);
        var notional = ToAccountCurrency(GetUpfrontCashFlow(contract, quantity, price), Cash.Currency);
        var feeNotional = ToAccountCurrency(_valuation.Notional(contract, quantity, price), Cash.Currency);
        ReleaseFilledReservation(command, quantity, price);
        TrackFeeNotional(command.StrategyId, command.VariantId, feeNotional, now);

        if (command.Side == Side.Buy)
        {
            AdjustSliceCash(command.StrategyId, command.VariantId, -notional - commission);
            Cash = Cash - notional - commission;
            ApplyPositionFill(command, quantity, price);
            if (ShouldDeliverAssetOnFill(contract))
                ApplyAssetDelivery(command, quantity, settlement, now);
            return;
        }

        AdjustSliceCash(command.StrategyId, command.VariantId, -commission);
        Cash -= commission;
        ApplyPositionFill(command, quantity, price);
        if (AccountType == AccountType.Cash && ShouldDeliverAssetOnFill(contract))
            ApplyAssetSale(command, quantity, now);

        if (AccountType == AccountType.Cash && settlement.CashProceedsDelay > Duration.Zero)
        {
            var settlementId = SettlementId.New();
            var settlesAt = settlement.GetSettlementTime(now);
            _pendingSettlements.Add(new PendingCashSettlement(
                settlementId,
                command.StrategyId,
                command.VariantId,
                notional,
                settlesAt));
            _events.Add(new SettlementScheduled(
                settlementId,
                command.StrategyId,
                command.VariantId,
                notional,
                settlesAt)
            {
                Time = now
            });
            _events.Add(new SettlementStatusSnapshot(
                settlementId,
                command.StrategyId,
                command.VariantId,
                SettlementStatus.Scheduled,
                notional,
                settlesAt,
                now)
            {
                Time = now
            });
            return;
        }

        AdjustSliceCash(command.StrategyId, command.VariantId, notional);
        Cash += notional;
    }

    public void ApplyPackageFill(
        SimulationOrderCommand command,
        IReadOnlyList<PackageLegFill> legFills,
        Price packagePrice,
        Money commission,
        Instant now)
    {
        var packageContract = GetContract(command.Instrument);
        if (packageContract.Package is null)
            throw new InvalidOperationException($"Instrument {command.Instrument} is not a package.");

        var feeNotional = ToAccountCurrency(_valuation.Notional(packageContract, command.Quantity, packagePrice), Cash.Currency);
        ReleaseFilledReservation(command, command.Quantity, packagePrice);
        TrackFeeNotional(command.StrategyId, command.VariantId, feeNotional, now);

        var cashDelta = Money.Zero(Cash.Currency);
        for (var i = 0; i < legFills.Count; i++)
        {
            var leg = legFills[i];
            var legContract = GetContract(leg.Instrument);
            var upfront = ToAccountCurrency(GetUpfrontCashFlow(legContract, leg.Quantity, leg.Price), Cash.Currency);
            cashDelta += leg.Side == Side.Buy ? -upfront : upfront;
            ApplyPositionDelta(
                (command.StrategyId, command.VariantId, leg.Instrument),
                SignedQuantity(leg.Side, leg.Quantity),
                leg.Price);
            TrackFeeNotional(
                command.StrategyId,
                command.VariantId,
                ToAccountCurrency(_valuation.Notional(legContract, leg.Quantity, leg.Price), Cash.Currency),
                now);
        }

        cashDelta -= commission;
        AdjustSliceCash(command.StrategyId, command.VariantId, cashDelta);
        Cash += cashDelta;
    }

    private void TrackFeeNotional(StrategyId strategyId, int variantId, Money notional, Instant now)
    {
        if (notional.Amount <= 0m)
            return;

        _feeNotionalHistory.Add(new AccountTradeNotional(strategyId, variantId, notional, now));
    }

    private SettlementParams GetFillSettlement(
        InstrumentContract contract,
        UnsettledSalePolicy unsettledSalePolicy = UnsettledSalePolicy.Reject) =>
        _settlementOverride.HasValue
            ? _settlementOverride.Value.WithUnsettledSalePolicy(unsettledSalePolicy)
            : SettlementParams.FromContract(contract, unsettledSalePolicy);

    public void Release(OrderId orderId)
        => _reservations.Remove(orderId);

    public void ApplyFinancing(Money amount)
    {
        if (amount.Currency != Cash.Currency)
            throw new InvalidOperationException($"Financing currency {amount.Currency} does not match account currency {Cash.Currency}.");

        Cash += amount;
    }

    public void ApplyFinancing(FinancingChargeApplied financing)
    {
        if (financing.Instrument is { } instrument)
            ValidateContractFinancing(GetContract(instrument), financing);

        AdjustSliceCash(financing.StrategyId, financing.VariantId, financing.Amount);
        ApplyFinancing(financing.Amount);
    }

    private static void ValidateContractFinancing(InstrumentContract contract, FinancingChargeApplied financing)
    {
        var expected = contract.Financing switch
        {
            FinancingTerms.NoFinancing => (FinancingChargeType?)null,
            FinancingTerms.PerpetualFunding => FinancingChargeType.PerpetualFunding,
            FinancingTerms.ForexRollover => FinancingChargeType.ForexRollover,
            FinancingTerms.Borrow => FinancingChargeType.BorrowFee,
            _ => throw new InvalidOperationException(
                $"Instrument {contract.Instrument} has unsupported financing terms {contract.Financing.GetType().Name}.")
        };

        if (expected is null)
        {
            throw new InvalidOperationException(
                $"Instrument {contract.Instrument} does not permit instrument-level financing charges.");
        }

        if (financing.ChargeType != expected.Value)
        {
            throw new InvalidOperationException(
                $"Instrument {contract.Instrument} financing terms {contract.Financing.GetType().Name} require {expected.Value}, not {financing.ChargeType}.");
        }
    }

    internal bool TryApplyAccountTransfer(AccountTransferCompleted transfer, out string reason)
    {
        reason = string.Empty;
        return transfer.TransferType switch
        {
            AccountTransferType.CashDeposit => TryApplyCashDeposit(transfer, out reason),
            AccountTransferType.CashWithdrawal => TryApplyCashWithdrawal(transfer, out reason),
            AccountTransferType.AssetDeposit => TryApplyAssetDeposit(transfer, out reason),
            AccountTransferType.AssetWithdrawal => TryApplyAssetWithdrawal(transfer, out reason),
            AccountTransferType.InternalTransfer => TryApplyInternalTransfer(transfer, out reason),
            _ => Fail($"Transfer type {transfer.TransferType} is not supported.", out reason)
        };
    }

    public int ReleaseSettlements(Instant now)
    {
        var released = 0;
        for (var i = _pendingSettlements.Count - 1; i >= 0; i--)
        {
            var settlement = _pendingSettlements[i];
            if (settlement.SettlesAt > now)
                continue;

            AdjustSliceCash(settlement.StrategyId, settlement.VariantId, settlement.Amount);
            Cash += settlement.Amount;
            _pendingSettlements.RemoveAt(i);
            _events.Add(new SettlementReleased(
                settlement.SettlementId,
                settlement.StrategyId,
                settlement.VariantId,
                settlement.Amount,
                now)
            {
                Time = now
            });
            _events.Add(new SettlementStatusSnapshot(
                settlement.SettlementId,
                settlement.StrategyId,
                settlement.VariantId,
                SettlementStatus.Released,
                settlement.Amount,
                settlement.SettlesAt,
                now)
            {
                Time = now
            });
            released++;
        }

        for (var i = _pendingAssetDeliveries.Count - 1; i >= 0; i--)
        {
            var delivery = _pendingAssetDeliveries[i];
            if (delivery.DeliversAt > now)
                continue;

            AddSettledPosition(delivery.StrategyId, delivery.VariantId, delivery.Instrument, delivery.Quantity);
            _pendingAssetDeliveries.RemoveAt(i);
            _events.Add(new AssetDelivered(
                delivery.DeliveryId,
                delivery.StrategyId,
                delivery.VariantId,
                delivery.Instrument,
                delivery.Quantity,
                now)
            {
                Time = now
            });
            _events.Add(new AssetDeliveryStatusSnapshot(
                delivery.DeliveryId,
                delivery.StrategyId,
                delivery.VariantId,
                delivery.Instrument,
                delivery.Quantity,
                AssetDeliveryStatus.Delivered,
                delivery.DeliversAt,
                now)
            {
                Time = now
            });
            released++;
        }

        return released;
    }

    public int DrainEvents(Span<FinanceEvent> destination)
    {
        var count = Math.Min(destination.Length, _events.Count);
        for (var i = 0; i < count; i++)
            destination[i] = _events[i];

        _events.RemoveRange(0, count);
        return count;
    }

    public void EmitPendingLifecycleStatuses(Instant now)
    {
        foreach (var settlement in _pendingSettlements)
        {
            _events.Add(new SettlementStatusSnapshot(
                settlement.SettlementId,
                settlement.StrategyId,
                settlement.VariantId,
                SettlementStatus.Pending,
                settlement.Amount,
                settlement.SettlesAt,
                now)
            {
                Time = now
            });
        }

        foreach (var delivery in _pendingAssetDeliveries)
        {
            _events.Add(new AssetDeliveryStatusSnapshot(
                delivery.DeliveryId,
                delivery.StrategyId,
                delivery.VariantId,
                delivery.Instrument,
                delivery.Quantity,
                AssetDeliveryStatus.Pending,
                delivery.DeliversAt,
                now)
            {
                Time = now
            });
        }
    }

    public OptionLifecycleApplicationStatus ApplyOptionLifecycleResult(
        StrategyId strategyId,
        int variantId,
        Instrument instrument,
        OptionLifecycleResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var key = (strategyId, variantId, instrument);
        if (!_positions.TryGetValue(key, out var position) || position.Quantity.IsZero)
            return OptionLifecycleApplicationStatus.NoOpenPosition;

        var contract = GetContract(instrument);
        if (contract.Payoff is not PayoffTerms.Option)
            throw new InvalidOperationException($"Instrument {instrument} is not an option contract.");

        ValidateOptionLifecycleResultMatchesPosition(instrument, position.Quantity, result);
        ApplyOptionLifecycleResultToAccount(key, contract, result);
        return result.IsComplete
            ? OptionLifecycleApplicationStatus.Completed
            : OptionLifecycleApplicationStatus.Blocked;
    }

    public bool ApplyCashOutcomeContractLifecycle(
        StrategyId strategyId,
        int variantId,
        Instrument instrument,
        Price referencePrice,
        Instant now,
        bool outcomeOccurred)
    {
        var key = (strategyId, variantId, instrument);
        if (!_positions.TryGetValue(key, out var position) || position.Quantity.IsZero)
            return false;

        var contract = GetContract(instrument);
        switch (contract.Payoff)
        {
            case PayoffTerms.Binary:
                ApplyCashOutcomeContractSettlement(key, contract, position.Quantity, position.AveragePrice, referencePrice, now, outcomeOccurred);
                return true;

            case PayoffTerms.Betting:
                ApplyCashOutcomeContractSettlement(key, contract, position.Quantity, position.AveragePrice, position.AveragePrice, now, outcomeOccurred);
                return true;

            default:
                throw new InvalidOperationException($"Instrument {instrument} is not a binary or betting contract.");
        }
    }

    public AccountStatementSnapshot CreateStatement(
        StrategyId strategyId,
        int variantId,
        Currency currency,
        Instant now,
        int openOrders = 0,
        Money? marketValue = null,
        Money? unrealizedPnL = null,
        Money? realizedPnL = null,
        Money? equityValue = null,
        int openPositions = 0)
    {
        if (currency != Cash.Currency)
            throw new InvalidOperationException($"Statement currency {currency} does not match account currency {Cash.Currency}.");

        var cash = GetSliceCash(strategyId, variantId);
        var zero = Money.Zero(currency);
        var value = marketValue ?? zero;
        var equityContribution = equityValue ?? value;
        return new AccountStatementSnapshot(
            strategyId,
            variantId,
            currency,
            cash,
            cash - ReservedCash,
            PendingSettlement,
            ReservedCash,
            MarketValue: value,
            Equity: cash + PendingSettlement + equityContribution,
            UnrealizedPnL: unrealizedPnL ?? zero,
            RealizedPnL: realizedPnL ?? zero,
            openPositions,
            openOrders)
        {
            Time = now
        };
    }

    public AccountStatementSnapshot CreateStatement(
        StrategyId strategyId,
        int variantId,
        Currency currency,
        Instant now,
        IReadOnlyDictionary<Instrument, Price> marks,
        int openOrders = 0)
    {
        var marketValueAmount = 0m;
        var unrealizedAmount = 0m;
        var equityAmount = 0m;
        var openPositions = 0;
        foreach (var entry in _positions)
        {
            if (entry.Key.StrategyId != strategyId
                || entry.Key.VariantId != variantId
                || entry.Value.Quantity.Value == 0m)
            {
                continue;
            }

            openPositions++;
            var mark = marks.TryGetValue(entry.Key.Instrument, out var marked)
                ? marked
                : entry.Value.AveragePrice;
            if (mark.Currency != currency)
                continue;

            var contract = GetContract(entry.Key.Instrument);
            var value = _valuation.ValuePosition(
                contract,
                new PositionValuationInput(
                    entry.Key.Instrument,
                    entry.Value.Quantity,
                    entry.Value.AveragePrice,
                    GetRealizedPnL(strategyId, variantId, contract.Exposure.SettlementCurrency())),
                mark);
            if (value.MarketValue.Currency != currency || value.UnrealizedPnL.Currency != currency)
                continue;

            marketValueAmount += value.MarketValue.Amount;
            unrealizedAmount += value.UnrealizedPnL.Amount;
            equityAmount += GetEquityContribution(contract, value).Amount;
        }

        var marketValue = new Money(marketValueAmount, currency);
        var unrealizedPnL = new Money(unrealizedAmount, currency);
        var equityValue = new Money(equityAmount, currency);
        var realizedPnL = GetRealizedPnL(strategyId, variantId, currency);
        return CreateStatement(
            strategyId,
            variantId,
            currency,
            now,
            openOrders,
            marketValue,
            unrealizedPnL,
            realizedPnL,
            equityValue,
            openPositions);
    }

    internal CustodyPositionSnapshot CreateCustodySnapshot(
        StrategyId strategyId,
        int variantId,
        Instrument instrument,
        Price markPrice,
        AccountType accountType,
        MarginParams margin,
        Instant now)
    {
        var quantity = GetPositionQuantity(strategyId, variantId, instrument);
        _positions.TryGetValue((strategyId, variantId, instrument), out var position);
        var averagePrice = quantity.IsZero ? markPrice : position.AveragePrice;
        var settled = GetSettledPosition(strategyId, variantId, instrument);
        var pendingDelivery = GetPendingAssetDeliveryQuantity(strategyId, variantId, instrument);
        var rehypothecatable = GetRehypothecatableQuantity(accountType, margin, instrument, settled);
        var contract = GetContract(instrument);
        var value = _valuation.ValuePosition(
            contract,
            new PositionValuationInput(instrument, quantity, averagePrice, GetRealizedPnL(strategyId, variantId, contract.Exposure.SettlementCurrency())),
            markPrice);

        return new CustodyPositionSnapshot(
            strategyId,
            variantId,
            instrument,
            quantity,
            settled,
            pendingDelivery,
            rehypothecatable,
            averagePrice,
            markPrice,
            value.MarketValue,
            value.UnrealizedPnL,
            Money.Zero(markPrice.Currency),
            IsOpen: !quantity.IsZero)
        {
            Time = now
        };
    }

    private static Qty GetRehypothecatableQuantity(
        AccountType accountType,
        MarginParams margin,
        Instrument instrument,
        Qty settled)
    {
        if (accountType != AccountType.Margin
            || margin.RehypothecationPolicy != RehypothecationPolicy.Allowed
            || settled.Value <= 0m)
        {
            return Qty.Zero;
        }

        return margin.RehypothecationAvailability.TryGetValue(instrument, out var available)
            ? new Qty(Math.Min(settled.Value, available.Value))
            : settled;
    }

    public int OpenPositionCount => GetOpenPositionCount();

    internal void CopyPositions(List<AccountPositionSnapshot> destination)
    {
        destination.Clear();
        foreach (var entry in _positions)
        {
            if (entry.Value.Quantity.Value == 0m)
                continue;

            destination.Add(new AccountPositionSnapshot(
                entry.Key.StrategyId,
                entry.Key.VariantId,
                entry.Key.Instrument,
                entry.Value.Quantity,
                entry.Value.AveragePrice));
        }
    }

    internal void CopyPositions(StrategyId strategyId, int variantId, List<AccountPositionSnapshot> destination)
    {
        destination.Clear();
        foreach (var entry in _positions)
        {
            if (entry.Key.StrategyId != strategyId
                || entry.Key.VariantId != variantId
                || entry.Value.Quantity.Value == 0m)
                continue;

            destination.Add(new AccountPositionSnapshot(
                entry.Key.StrategyId,
                entry.Key.VariantId,
                entry.Key.Instrument,
                entry.Value.Quantity,
                entry.Value.AveragePrice));
        }
    }

    internal void CopyPositions(Instrument instrument, List<AccountPositionSnapshot> destination)
    {
        destination.Clear();
        foreach (var entry in _positions)
        {
            if (entry.Key.Instrument != instrument || entry.Value.Quantity.Value == 0m)
                continue;

            destination.Add(new AccountPositionSnapshot(
                entry.Key.StrategyId,
                entry.Key.VariantId,
                entry.Key.Instrument,
                entry.Value.Quantity,
                entry.Value.AveragePrice));
        }
    }

    public Qty GetPositionQuantity(StrategyId strategyId, int variantId, Instrument instrument)
        => _positions.TryGetValue((strategyId, variantId, instrument), out var position)
            ? position.Quantity
            : Qty.Zero;

    internal void ApplyCorporateAction(CorporateActionApplied action, List<FinanceEvent> destination)
    {
        switch (action.ActionType)
        {
            case CorporateActionType.StockSplit:
                ApplyStockSplit(action, destination);
                break;
            case CorporateActionType.CashDividend:
                ApplyCashDividend(action, destination);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), $"Corporate action type {action.ActionType} is not supported.");
        }
    }

    public void CalculateMarginStatuses(
        IReadOnlyDictionary<(StrategyId StrategyId, int VariantId, Instrument Instrument), Price> marks,
        IReadOnlyDictionary<Instrument, Price> marketMarks,
        MarginParams margin,
        Currency currency,
        List<MarginAccountStatus> destination,
        Dictionary<(StrategyId StrategyId, int VariantId), MarginStatusAccumulator> accumulators)
    {
        destination.Clear();
        accumulators.Clear();
        if (AccountType != AccountType.Margin)
            return;

        var optionMarginEntries = new List<OptionMarginEntry>();
        foreach (var entry in _positions)
        {
            var position = entry.Value;
            if (position.Quantity.Value == 0m)
                continue;

            var key = (entry.Key.StrategyId, entry.Key.VariantId);
            var mark = marks.TryGetValue((entry.Key.StrategyId, entry.Key.VariantId, entry.Key.Instrument), out var marked)
                ? marked
                : position.AveragePrice;
            accumulators.TryGetValue(key, out var accumulator);
            var contract = GetContract(entry.Key.Instrument);
            var marketValue = _valuation.MarketValue(contract, position.Quantity, mark);
            var unrealized = _valuation.UnrealizedPnL(contract, position.Quantity, position.AveragePrice, mark);
            var underlyingMark = TryGetUnderlyingMark(contract, marketMarks, out var markedUnderlying)
                ? markedUnderlying
                : (Price?)null;
            var maintenance = GetMaintenanceMarginRequirement(contract, position.Quantity, mark, margin, underlyingMark);
            if (marketValue.Currency == currency)
                accumulator.MarketValue += marketValue.Amount;
            if (unrealized.Currency == currency)
                accumulator.EquityContribution += GetEquityContribution(
                    contract,
                    new PositionValuation(
                        _valuation.Notional(contract, position.Quantity, mark),
                        marketValue,
                        unrealized,
                        GetRealizedPnL(entry.Key.StrategyId, entry.Key.VariantId, unrealized.Currency))).Amount;
            if (maintenance.Currency == currency)
                accumulator.Maintenance += maintenance.Amount;
            accumulators[key] = accumulator;

            if (contract.Payoff is PayoffTerms.Option option && maintenance.Currency == currency)
            {
                optionMarginEntries.Add(new OptionMarginEntry(
                    entry.Key.StrategyId,
                    entry.Key.VariantId,
                    contract,
                    position.Quantity,
                    mark,
                    maintenance));
            }
        }

        ApplyOptionStrategyMarginOffsets(optionMarginEntries, accumulators, currency);

        var pendingSettlement = PendingSettlement;
        foreach (var (key, accumulator) in accumulators)
        {
            if (accumulator.Maintenance == 0m)
                continue;

            var marketValue = new Money(accumulator.MarketValue, currency);
            var maintenance = new Money(accumulator.Maintenance, currency);
            var equity = Cash + pendingSettlement + new Money(accumulator.EquityContribution, currency);
            destination.Add(new MarginAccountStatus(
                key.StrategyId,
                key.VariantId,
                equity,
                maintenance,
                equity.Amount < maintenance.Amount));
        }
    }

    public struct MarginStatusAccumulator
    {
        public decimal MarketValue;
        public decimal EquityContribution;
        public decimal Maintenance;
    }

    internal Money CalculateMaintenanceRequirement(
        AccountPositionSnapshot position,
        Price mark,
        MarginParams margin,
        IReadOnlyDictionary<Instrument, Price> marketMarks)
    {
        var contract = GetContract(position.Instrument);
        var underlyingMark = TryGetUnderlyingMark(contract, marketMarks, out var markedUnderlying)
            ? markedUnderlying
            : (Price?)null;
        return ToAccountCurrency(
            GetMaintenanceMarginRequirement(contract, position.Quantity, mark, margin, underlyingMark),
            Cash.Currency);
    }

    private readonly record struct PendingCashSettlement(
        SettlementId SettlementId,
        StrategyId StrategyId,
        int VariantId,
        Money Amount,
        Instant SettlesAt);

    private readonly record struct PendingAssetDelivery(
        AssetDeliveryId DeliveryId,
        StrategyId StrategyId,
        int VariantId,
        Instrument Instrument,
        Qty Quantity,
        Instant DeliversAt);

    private readonly record struct AccountTradeNotional(
        StrategyId StrategyId,
        int VariantId,
        Money Notional,
        Instant TradedAt);

    private decimal GetReservedCashAmount()
    {
        var amount = 0m;
        foreach (var reservation in _reservations.Values)
        {
            amount += reservation.Cash.Amount;
        }

        return amount;
    }

    private decimal GetPendingSettlementAmount(Currency currency)
    {
        var amount = 0m;
        for (var i = 0; i < _pendingSettlements.Count; i++)
        {
            var settlement = _pendingSettlements[i];
            if (settlement.Amount.Currency == currency)
                amount += settlement.Amount.Amount;
        }

        return amount;
    }

    private decimal GetPendingAssetDeliveryQuantity()
    {
        var quantity = 0m;
        for (var i = 0; i < _pendingAssetDeliveries.Count; i++)
        {
            quantity += _pendingAssetDeliveries[i].Quantity.Value;
        }

        return quantity;
    }

    private Qty GetPendingAssetDeliveryQuantity(StrategyId strategyId, int variantId, Instrument instrument)
    {
        var quantity = 0m;
        for (var i = 0; i < _pendingAssetDeliveries.Count; i++)
        {
            var delivery = _pendingAssetDeliveries[i];
            if (delivery.StrategyId == strategyId
                && delivery.VariantId == variantId
                && delivery.Instrument == instrument)
            {
                quantity += delivery.Quantity.Value;
            }
        }

        return new Qty(quantity);
    }

    private int GetOpenPositionCount()
    {
        var count = 0;
        foreach (var entry in _positions)
        {
            if (entry.Value.Quantity.Value != 0m)
                count++;
        }

        return count;
    }

    private void ApplyStockSplit(CorporateActionApplied action, List<FinanceEvent> outcomes)
    {
        if (action.SplitRatio <= 0m)
            throw new ArgumentOutOfRangeException(nameof(action), "Stock split requires a positive split ratio.");

        _positionKeyScratch.Clear();
        foreach (var entry in _positions)
        {
            if (entry.Key.Instrument == action.Instrument && !entry.Value.Quantity.IsZero)
                _positionKeyScratch.Add(entry.Key);
        }

        for (var i = 0; i < _positionKeyScratch.Count; i++)
        {
            var key = _positionKeyScratch[i];
            var position = _positions[key];
            var quantityAfter = new Qty(position.Quantity.Value * action.SplitRatio);
            var averageAfter = new Price(position.AveragePrice.Value / action.SplitRatio, position.AveragePrice.Currency);
            _positions[key] = position with
            {
                Quantity = quantityAfter,
                AveragePrice = averageAfter
            };

            outcomes.Add(new CorporateActionEffectSnapshot(
                action.CorporateActionId,
                action.ActionType,
                key.StrategyId,
                key.VariantId,
                key.Instrument,
                position.Quantity,
                quantityAfter,
                position.AveragePrice,
                averageAfter,
                CashAmount: null,
                action.EffectiveAt)
            {
                Time = action.EffectiveAt
            });
        }

        _settledPositionKeyScratch.Clear();
        foreach (var entry in _settledPositions)
        {
            if (entry.Key.Instrument == action.Instrument && !entry.Value.IsZero)
                _settledPositionKeyScratch.Add(entry.Key);
        }

        for (var i = 0; i < _settledPositionKeyScratch.Count; i++)
        {
            var key = _settledPositionKeyScratch[i];
            var quantity = _settledPositions[key];
            _settledPositions[key] = new Qty(quantity.Value * action.SplitRatio);
        }

        for (var i = 0; i < _pendingAssetDeliveries.Count; i++)
        {
            var delivery = _pendingAssetDeliveries[i];
            if (delivery.Instrument != action.Instrument)
                continue;

            _pendingAssetDeliveries[i] = delivery with
            {
                Quantity = new Qty(delivery.Quantity.Value * action.SplitRatio)
            };
        }
    }

    private void ApplyCashDividend(CorporateActionApplied action, List<FinanceEvent> outcomes)
    {
        var dividend = action.DividendPerShare
            ?? throw new ArgumentOutOfRangeException(nameof(action), "Cash dividend requires a dividend per share.");
        if (dividend.Amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(action), "Cash dividend requires a positive dividend per share.");
        if (dividend.Currency != Cash.Currency)
            throw new InvalidOperationException($"Dividend currency {dividend.Currency} does not match account currency {Cash.Currency}.");

        _settledPositionKeyScratch.Clear();
        foreach (var entry in _settledPositions)
        {
            if (entry.Key.Instrument == action.Instrument && entry.Value.Value > 0m)
                _settledPositionKeyScratch.Add(entry.Key);
        }

        for (var i = 0; i < _settledPositionKeyScratch.Count; i++)
        {
            var key = _settledPositionKeyScratch[i];
            var settledQuantity = _settledPositions[key];
            var position = _positions.TryGetValue(key, out var existingPosition)
                ? existingPosition
                : new AccountPosition(settledQuantity, Price.Zero);
            var cashAmount = new Money(settledQuantity.Value * dividend.Amount, dividend.Currency);
            Cash += cashAmount;
            outcomes.Add(new CorporateActionEffectSnapshot(
                action.CorporateActionId,
                action.ActionType,
                key.StrategyId,
                key.VariantId,
                key.Instrument,
                settledQuantity,
                settledQuantity,
                position.AveragePrice,
                position.AveragePrice,
                cashAmount,
                action.EffectiveAt)
            {
                Time = action.EffectiveAt
            });
        }

    }

    private void Reserve(OrderId orderId, Money cash, Instrument instrument, Qty marginQuantity, Qty shortQuantity)
    {
        if (cash.Amount <= 0m && shortQuantity.Value <= 0m)
            return;

        _reservations[orderId] = new AccountReservation(cash, instrument, marginQuantity, shortQuantity);
    }

    private void ReleaseFilledReservation(SimulationOrderCommand command, Qty filledQuantity, Price fillPrice)
    {
        if (!_reservations.TryGetValue(command.ClientOrderId, out var reservation))
            return;

        var release = reservation.MarginQuantity.Value <= 0m || reservation.Cash.Amount <= 0m
            ? Money.Zero(Cash.Currency)
            : new Money(
                reservation.Cash.Amount * Math.Min(1m, filledQuantity.Value / reservation.MarginQuantity.Value),
                reservation.Cash.Currency);
        var remainingCash = reservation.Cash.Amount - release.Amount;
        var remainingMarginQuantity = Math.Max(0m, reservation.MarginQuantity.Value - filledQuantity.Value);
        var remainingShort = Math.Max(0m, reservation.ShortQuantity.Value - filledQuantity.Value);

        if (remainingCash <= 0m && remainingMarginQuantity <= 0m && remainingShort <= 0m)
            _reservations.Remove(command.ClientOrderId);
        else
            _reservations[command.ClientOrderId] = reservation with
            {
                Cash = new Money(Math.Max(0m, remainingCash), reservation.Cash.Currency),
                MarginQuantity = new Qty(remainingMarginQuantity),
                ShortQuantity = new Qty(remainingShort)
            };
    }

    private Qty GetShortIncrease(SimulationOrderCommand command)
    {
        if (command.Side != Side.Sell)
            return Qty.Zero;

        var position = GetAggregatePosition(command.Instrument);
        var longAvailable = Math.Max(0m, position.Value);
        var shortIncrease = Math.Max(0m, command.Quantity.Value - longAvailable);
        return new Qty(shortIncrease);
    }

    private Qty GetAvailableBorrow(Instrument instrument, MarginParams margin)
    {
        margin.BorrowAvailability.TryGetValue(instrument, out var explicitBorrow);
        var aggregatePosition = GetAggregatePosition(instrument);
        var rehypothecated = margin.RehypothecationPolicy == RehypothecationPolicy.Allowed
            ? new Qty(Math.Max(0m, aggregatePosition.Value))
            : Qty.Zero;
        var existingShort = Math.Max(0m, -aggregatePosition.Value);
        var reserved = 0m;
        foreach (var reservation in _reservations.Values)
        {
            if (reservation.Instrument == instrument)
                reserved += reservation.ShortQuantity.Value;
        }

        return new Qty(Math.Max(0m, explicitBorrow.Value + rehypothecated.Value - existingShort - reserved));
    }

    private Qty GetAggregatePosition(Instrument instrument)
    {
        var quantity = 0m;
        foreach (var entry in _positions)
        {
            if (entry.Key.Instrument == instrument)
                quantity += entry.Value.Quantity.Value;
        }

        return new Qty(quantity);
    }

    private Qty GetSettledPosition(StrategyId strategyId, int variantId, Instrument instrument)
        => _settledPositions.TryGetValue((strategyId, variantId, instrument), out var quantity)
            ? quantity
            : Qty.Zero;

    private void AddSettledPosition(
        StrategyId strategyId,
        int variantId,
        Instrument instrument,
        Qty delta)
    {
        if (delta.IsZero)
            return;

        var key = (strategyId, variantId, instrument);
        var next = GetSettledPosition(strategyId, variantId, instrument) + delta;
        if (next.IsZero)
            _settledPositions.Remove(key);
        else
            _settledPositions[key] = next;
    }

    private void ApplyAssetDelivery(
        SimulationOrderCommand command,
        Qty quantity,
        SettlementParams settlement,
        Instant now)
    {
        if (AccountType != AccountType.Cash)
            return;

        if (settlement.CashProceedsDelay <= Duration.Zero)
        {
            AddSettledPosition(command.StrategyId, command.VariantId, command.Instrument, quantity);
            return;
        }

        var deliveryId = AssetDeliveryId.New();
        var deliversAt = settlement.GetSettlementTime(now);
        _pendingAssetDeliveries.Add(new PendingAssetDelivery(
            deliveryId,
            command.StrategyId,
            command.VariantId,
            command.Instrument,
            quantity,
            deliversAt));
        _events.Add(new AssetDeliveryScheduled(
            deliveryId,
            command.StrategyId,
            command.VariantId,
            command.Instrument,
            quantity,
            deliversAt)
        {
            Time = now
        });
        _events.Add(new AssetDeliveryStatusSnapshot(
            deliveryId,
            command.StrategyId,
            command.VariantId,
            command.Instrument,
            quantity,
            AssetDeliveryStatus.Scheduled,
            deliversAt,
            now)
        {
            Time = now
        });
    }

    private void ApplyAssetSale(
        SimulationOrderCommand command,
        Qty quantity,
        Instant now)
    {
        var remainingSettledQuantity = ConsumePendingAssetDeliveries(command, quantity, now);
        if (remainingSettledQuantity.Value > 0m)
            AddSettledPosition(
                command.StrategyId,
                command.VariantId,
                command.Instrument,
                new Qty(-remainingSettledQuantity.Value));
    }

    private Qty ConsumePendingAssetDeliveries(
        SimulationOrderCommand command,
        Qty quantity,
        Instant now)
    {
        var remaining = quantity.Value;
        for (var i = 0; i < _pendingAssetDeliveries.Count && remaining > 0m; i++)
        {
            var delivery = _pendingAssetDeliveries[i];
            if (delivery.StrategyId != command.StrategyId
                || delivery.VariantId != command.VariantId
                || delivery.Instrument != command.Instrument)
            {
                continue;
            }

            var canceled = Math.Min(remaining, delivery.Quantity.Value);
            remaining -= canceled;
            var canceledQuantity = new Qty(canceled);
            _events.Add(new AssetDeliveryCanceled(
                delivery.DeliveryId,
                command.StrategyId,
                command.VariantId,
                command.Instrument,
                canceledQuantity,
                now)
            {
                Time = now
            });
            _events.Add(new AssetDeliveryStatusSnapshot(
                delivery.DeliveryId,
                command.StrategyId,
                command.VariantId,
                command.Instrument,
                canceledQuantity,
                AssetDeliveryStatus.Canceled,
                delivery.DeliversAt,
                now)
            {
                Time = now
            });

            var remainingDelivery = delivery.Quantity.Value - canceled;
            if (remainingDelivery <= 0m)
            {
                _pendingAssetDeliveries.RemoveAt(i);
                i--;
            }
            else
            {
                _pendingAssetDeliveries[i] = delivery with { Quantity = new Qty(remainingDelivery) };
            }
        }

        return new Qty(remaining);
    }

    private void ApplyPositionFill(SimulationOrderCommand command, Qty quantity, Price price)
    {
        var key = (command.StrategyId, command.VariantId, command.Instrument);
        var sideSign = command.Side == Side.Buy ? 1m : -1m;
        var delta = new Qty(quantity.Value * sideSign);
        ApplyPositionDelta(key, delta, price);
    }

    private void ApplyOptionLifecycleResultToAccount(
        (StrategyId StrategyId, int VariantId, Instrument Instrument) key,
        InstrumentContract contract,
        OptionLifecycleResult result)
    {
        for (var i = 0; i < result.Outcomes.Count; i++)
            ApplyOptionLifecycleOutcome(key, contract, result.Outcomes[i]);

        if (result.IsComplete)
            RemovePosition(key);
    }

    private static void ValidateOptionLifecycleResultMatchesPosition(
        Instrument instrument,
        Qty positionQuantity,
        OptionLifecycleResult result)
    {
        var total = 0m;
        var positionSign = Math.Sign(positionQuantity.Value);
        for (var i = 0; i < result.Outcomes.Count; i++)
        {
            var outcomeQuantity = result.Outcomes[i].Quantity;
            if (Math.Sign(outcomeQuantity.Value) != positionSign)
            {
                throw new InvalidOperationException(
                    $"Option lifecycle outcome quantity {outcomeQuantity.Value} for {instrument} has the opposite sign of open position quantity {positionQuantity.Value}.");
            }

            total += outcomeQuantity.Value;
        }

        if (total != positionQuantity.Value)
        {
            throw new InvalidOperationException(
                $"Option lifecycle outcomes for {instrument} cover quantity {total}, but open position quantity is {positionQuantity.Value}.");
        }
    }

    private void ApplyOptionLifecycleOutcome(
        (StrategyId StrategyId, int VariantId, Instrument Instrument) key,
        InstrumentContract contract,
        OptionLifecycleOutcome outcome)
    {
        switch (outcome)
        {
            case OptionLifecycleOutcome.Block blocked:
                _events.Add(new OptionLifecycleApplied(
                    key.StrategyId,
                    key.VariantId,
                    contract.Instrument,
                    OptionLifecycleKind.Blocked,
                    blocked.Quantity,
                    Money.Zero(Cash.Currency),
                    blocked.AppliedAt,
                    ReferenceSource: OptionLifecycleReferenceSource.None,
                    Reason: blocked.Reason)
                {
                    Time = blocked.AppliedAt
                });
                break;

            case OptionLifecycleOutcome.ExpireWorthless expired:
                var expiredAveragePrice = GetLifecycleAveragePrice(key);
                ExpireOptionQuantity(
                    key,
                    contract,
                    OptionLifecycleKind.ExpireWorthless,
                    expired.Quantity,
                    expiredAveragePrice,
                    expired.ReferencePrice,
                    expired.AppliedAt,
                    expired.ReferenceSource,
                    expired.Reason);
                break;

            case OptionLifecycleOutcome.ExpireUnexercised unexercised:
                var unexercisedAveragePrice = GetLifecycleAveragePrice(key);
                ExpireOptionQuantity(
                    key,
                    contract,
                    OptionLifecycleKind.ExpireUnexercised,
                    unexercised.Quantity,
                    unexercisedAveragePrice,
                    unexercised.ReferencePrice,
                    unexercised.AppliedAt,
                    unexercised.ReferenceSource,
                    unexercised.Reason);
                break;

            case OptionLifecycleOutcome.ExpireUnassigned unassigned:
                var unassignedAveragePrice = GetLifecycleAveragePrice(key);
                ExpireOptionQuantity(
                    key,
                    contract,
                    OptionLifecycleKind.ExpireUnassigned,
                    unassigned.Quantity,
                    unassignedAveragePrice,
                    unassigned.ReferencePrice,
                    unassigned.AppliedAt,
                    unassigned.ReferenceSource,
                    unassigned.Reason);
                break;

            case OptionLifecycleOutcome.CashSettle cash:
                var cashAveragePrice = GetLifecycleAveragePrice(key);
                ApplyOptionCashSettlement(
                    key,
                    contract,
                    cash.Quantity,
                    cashAveragePrice,
                    cash.ReferencePrice,
                    cash.AppliedAt,
                    cash.ReferenceSource,
                    cash.LifecycleKind,
                    cash.Reason);
                break;

            case OptionLifecycleOutcome.PhysicalDeliver physical:
                var physicalAveragePrice = GetLifecycleAveragePrice(key);
                var terms = ((PayoffTerms.Option)contract.Payoff).Terms;
                EmitOptionLifecycle(
                    key,
                    contract,
                    physical.LifecycleKind,
                    physical.Quantity,
                    Money.Zero(Cash.Currency),
                    physical.AppliedAt,
                    physical.ReferenceSource,
                    physical.ReferencePrice,
                    Reason: physical.Reason);
                var cashFlow = ToAccountCurrency(-GetNoUpfrontOptionPremiumBasis(contract, physical.Quantity, physicalAveragePrice), Cash.Currency);
                var realized = ToAccountCurrency(-GetLifecyclePremiumBasis(contract, physical.Quantity, physicalAveragePrice), Cash.Currency);
                ApplyLifecycleAccounting(
                    key,
                    contract,
                    physical.Quantity,
                    cashFlow,
                    realized,
                    physical.AppliedAt,
                    physical.ReferencePrice,
                    physical.ReferenceSource,
                    physical.PremiumReason);
                ApplyPhysicalOptionDelivery(
                    key,
                    contract,
                    terms,
                    physical.Quantity,
                    physical.ReferencePrice,
                    physical.AppliedAt,
                    physical.ReferenceSource);
                break;
        }
    }

    private void ExpireOptionQuantity(
        (StrategyId StrategyId, int VariantId, Instrument Instrument) key,
        InstrumentContract contract,
        OptionLifecycleKind lifecycleKind,
        Qty quantity,
        Price averagePrice,
        Price underlyingMark,
        Instant now,
        OptionLifecycleReferenceSource referenceSource,
        string reason)
    {
        var cashFlow = ToAccountCurrency(-GetNoUpfrontOptionPremiumBasis(contract, quantity, averagePrice), Cash.Currency);
        var realized = ToAccountCurrency(-GetLifecyclePremiumBasis(contract, quantity, averagePrice), Cash.Currency);
        ApplyLifecycleAccounting(key, contract, quantity, cashFlow, realized, now, underlyingMark, referenceSource, "Premium settlement at expiry.");
        EmitOptionLifecycle(
            key,
            contract,
            lifecycleKind,
            quantity,
            cashFlow,
            now,
            referenceSource,
            underlyingMark,
            settlementPrice: underlyingMark,
            Reason: reason);
    }

    private Price GetLifecycleAveragePrice((StrategyId StrategyId, int VariantId, Instrument Instrument) key)
    {
        if (_positions.TryGetValue(key, out var position))
            return position.AveragePrice;

        throw new InvalidOperationException($"Cannot apply lifecycle outcome for {key.Instrument}: no open account position.");
    }

    private void EmitOptionLifecycle(
        (StrategyId StrategyId, int VariantId, Instrument Instrument) key,
        InstrumentContract contract,
        OptionLifecycleKind kind,
        Qty quantity,
        Money cashFlow,
        Instant appliedAt,
        OptionLifecycleReferenceSource referenceSource,
        Price? underlyingMark = null,
        Instrument? deliverable = null,
        Qty? deliverableQuantity = null,
        Price? settlementPrice = null,
        string? Reason = null)
    {
        if (referenceSource == OptionLifecycleReferenceSource.None)
            throw new ArgumentException("Resolved option lifecycle event requires a non-None reference source.", nameof(referenceSource));

        _events.Add(new OptionLifecycleApplied(
            key.StrategyId,
            key.VariantId,
            contract.Instrument,
            kind,
            quantity,
            cashFlow,
            appliedAt,
            underlyingMark,
            deliverable,
            deliverableQuantity,
            settlementPrice,
            referenceSource,
            Reason)
        {
            Time = appliedAt
        });
    }

    private void ApplyOptionCashSettlement(
        (StrategyId StrategyId, int VariantId, Instrument Instrument) key,
        InstrumentContract contract,
        Qty quantity,
        Price averagePrice,
        Price referencePrice,
        Instant now,
        OptionLifecycleReferenceSource referenceSource,
        OptionLifecycleKind lifecycleKind,
        string lifecycleReason)
    {
        if (contract.Payoff is not PayoffTerms.Option)
            throw new InvalidOperationException($"Instrument {contract.Instrument} is not an option contract.");

        var grossPayoff = _valuation.ExpiryPayoff(contract, quantity, referencePrice);
        var cashFlow = ToAccountCurrency(grossPayoff - GetNoUpfrontOptionPremiumBasis(contract, quantity, averagePrice), Cash.Currency);
        var realized = ToAccountCurrency(grossPayoff - GetLifecyclePremiumBasis(contract, quantity, averagePrice), Cash.Currency);
        EmitOptionLifecycle(key, contract, lifecycleKind, quantity, Money.Zero(Cash.Currency), now, referenceSource, referencePrice, settlementPrice: referencePrice, Reason: lifecycleReason);
        ApplyLifecycleAccounting(key, contract, quantity, cashFlow, realized, now, referencePrice, referenceSource, "Cash settlement payoff.", emitZeroCashFlow: true);
    }

    private void ApplyCashOutcomeContractSettlement(
        (StrategyId StrategyId, int VariantId, Instrument Instrument) key,
        InstrumentContract contract,
        Qty quantity,
        Price averagePrice,
        Price payoffReferencePrice,
        Instant now,
        bool outcomeOccurred)
    {
        if (contract.Payoff is not (PayoffTerms.Binary or PayoffTerms.Betting))
            throw new InvalidOperationException($"Instrument {contract.Instrument} is not a binary or betting contract.");

        var grossPayoff = _valuation.ExpiryPayoff(contract, quantity, payoffReferencePrice, outcomeOccurred);
        var cashFlow = ToAccountCurrency(grossPayoff, Cash.Currency);
        var realized = ToAccountCurrency(grossPayoff - GetLifecyclePremiumBasis(contract, quantity, averagePrice), Cash.Currency);

        if (!cashFlow.IsZero)
        {
            AdjustSliceCash(key.StrategyId, key.VariantId, cashFlow);
            Cash += cashFlow;
        }

        AddRealizedPnL(key.StrategyId, key.VariantId, realized);
        RemovePosition(key);
    }

    private void ApplyLifecycleAccounting(
        (StrategyId StrategyId, int VariantId, Instrument Instrument) key,
        InstrumentContract contract,
        Qty quantity,
        Money cashFlow,
        Money realizedPnL,
        Instant now,
        Price underlyingMark,
        OptionLifecycleReferenceSource referenceSource,
        string reason,
        bool emitZeroCashFlow = false)
    {
        if (!cashFlow.IsZero || emitZeroCashFlow)
        {
            if (!cashFlow.IsZero)
            {
                AdjustSliceCash(key.StrategyId, key.VariantId, cashFlow);
                Cash += cashFlow;
            }

            EmitOptionLifecycle(key, contract, OptionLifecycleKind.CashSettlement, quantity, cashFlow, now, referenceSource, underlyingMark, settlementPrice: underlyingMark, Reason: reason);
        }

        AddRealizedPnL(key.StrategyId, key.VariantId, realizedPnL);
    }

    private void ApplyPhysicalOptionDelivery(
        (StrategyId StrategyId, int VariantId, Instrument Instrument) key,
        InstrumentContract contract,
        OptionTerms terms,
        Qty quantity,
        Price underlyingMark,
        Instant now,
        OptionLifecycleReferenceSource referenceSource)
    {
        var deliverable = contract.Settlement is SettlementTerms.Physical physical
            ? physical.Deliverable
            : terms.Underlying;
        var direction = terms.Right == OptionRight.Call ? 1m : -1m;
        var deliverableQuantity = new Qty(quantity.Value * terms.ContractUnitOfTrade * direction);
        var strike = terms.Strike.ScaledStrike;
        var cashFlow = ToAccountCurrency(new Money(-deliverableQuantity.Value * strike.Value, strike.Currency), Cash.Currency);

        if (!cashFlow.IsZero)
        {
            AdjustSliceCash(key.StrategyId, key.VariantId, cashFlow);
            Cash += cashFlow;
        }

        if (!deliverableQuantity.IsZero)
        {
            ApplyPositionDelta((key.StrategyId, key.VariantId, deliverable), deliverableQuantity, strike);
            AddSettledPosition(key.StrategyId, key.VariantId, deliverable, deliverableQuantity);
        }

        EmitOptionLifecycle(
            key,
            contract,
            OptionLifecycleKind.PhysicalDelivery,
            quantity,
            cashFlow,
            now,
            referenceSource,
            underlyingMark,
            deliverable,
            deliverableQuantity,
            strike,
            "Physical delivery at expiry.");
    }

    private bool TryApplyCashDeposit(AccountTransferCompleted transfer, out string reason)
    {
        if (!TryGetPositiveCashAmount(transfer, out var amount, out reason))
            return false;

        AdjustSliceCash(transfer.StrategyId, transfer.VariantId, amount);
        Cash += amount;
        return true;
    }

    private bool TryApplyCashWithdrawal(AccountTransferCompleted transfer, out string reason)
    {
        if (!TryGetPositiveCashAmount(transfer, out var amount, out reason))
            return false;
        if (amount.Amount > AvailableCash.Amount)
            return Fail($"Cash withdrawal requires {amount.Amount:N2} {amount.Currency}, available {AvailableCash.Amount:N2} {AvailableCash.Currency}.", out reason);
        var sliceCash = GetSliceCash(transfer.StrategyId, transfer.VariantId);
        if (amount.Amount > sliceCash.Amount)
            return Fail($"Cash withdrawal requires {amount.Amount:N2} {amount.Currency}, available {sliceCash.Amount:N2} {sliceCash.Currency}.", out reason);

        AdjustSliceCash(transfer.StrategyId, transfer.VariantId, -amount);
        Cash -= amount;
        return true;
    }

    private bool TryApplyAssetDeposit(AccountTransferCompleted transfer, out string reason)
    {
        if (!TryGetPositiveAssetTransfer(transfer, out var instrument, out var quantity, out var carryingPrice, out reason))
            return false;

        var key = (transfer.StrategyId, transfer.VariantId, instrument);
        ApplyPositionDelta(key, quantity, carryingPrice);
        AddSettledPosition(transfer.StrategyId, transfer.VariantId, instrument, quantity);
        return true;
    }

    private bool TryApplyAssetWithdrawal(AccountTransferCompleted transfer, out string reason)
    {
        if (!TryGetPositiveAssetTransfer(transfer, out var instrument, out var quantity, out var carryingPrice, out reason))
            return false;

        var settled = GetSettledPosition(transfer.StrategyId, transfer.VariantId, instrument);
        if (quantity.Value > settled.Value)
            return Fail($"Asset withdrawal requires {quantity.Value} settled units, available {settled.Value}.", out reason);

        var key = (transfer.StrategyId, transfer.VariantId, instrument);
        ApplyPositionDelta(key, new Qty(-quantity.Value), carryingPrice);
        AddSettledPosition(transfer.StrategyId, transfer.VariantId, instrument, new Qty(-quantity.Value));
        return true;
    }

    private bool TryApplyInternalTransfer(AccountTransferCompleted transfer, out string reason)
    {
        if (!transfer.DestinationStrategyId.HasValue)
            return Fail("InternalTransfer requires a destination strategy id.", out reason);
        if (transfer.DestinationStrategyId.Value == transfer.StrategyId
            && transfer.DestinationVariantId == transfer.VariantId)
        {
            return Fail("InternalTransfer source and destination must be different account slices.", out reason);
        }

        if (transfer.CashAmount.HasValue)
            return TryApplyInternalCashTransfer(transfer, out reason);

        return TryApplyInternalAssetTransfer(transfer, out reason);
    }

    private bool TryApplyInternalCashTransfer(AccountTransferCompleted transfer, out string reason)
    {
        if (!TryGetPositiveCashAmount(transfer, out var amount, out reason))
            return false;
        EnsureSliceCash(transfer.StrategyId, transfer.VariantId);
        EnsureSliceCash(transfer.DestinationStrategyId!.Value, transfer.DestinationVariantId);

        var sourceCash = GetSliceCash(transfer.StrategyId, transfer.VariantId);
        if (amount.Amount > sourceCash.Amount)
            return Fail($"Internal cash transfer requires {amount.Amount:N2} {amount.Currency}, available {sourceCash.Amount:N2} {sourceCash.Currency}.", out reason);

        AdjustSliceCash(transfer.StrategyId, transfer.VariantId, -amount);
        AdjustSliceCash(transfer.DestinationStrategyId!.Value, transfer.DestinationVariantId, amount);
        return true;
    }

    private bool TryApplyInternalAssetTransfer(AccountTransferCompleted transfer, out string reason)
    {
        if (!TryGetPositiveAssetTransfer(transfer, out var instrument, out var quantity, out var carryingPrice, out reason))
            return false;

        var sourceSettled = GetSettledPosition(transfer.StrategyId, transfer.VariantId, instrument);
        if (quantity.Value > sourceSettled.Value)
            return Fail($"Internal asset transfer requires {quantity.Value} settled units, available {sourceSettled.Value}.", out reason);

        var destinationStrategyId = transfer.DestinationStrategyId!.Value;
        var sourceKey = (transfer.StrategyId, transfer.VariantId, instrument);
        var destinationKey = (destinationStrategyId, transfer.DestinationVariantId, instrument);
        ApplyPositionDelta(sourceKey, new Qty(-quantity.Value), carryingPrice);
        ApplyPositionDelta(destinationKey, quantity, carryingPrice);
        AddSettledPosition(transfer.StrategyId, transfer.VariantId, instrument, new Qty(-quantity.Value));
        AddSettledPosition(destinationStrategyId, transfer.DestinationVariantId, instrument, quantity);
        return true;
    }

    private bool TryGetPositiveCashAmount(AccountTransferCompleted transfer, out Money amount, out string reason)
    {
        if (!transfer.CashAmount.HasValue || transfer.CashAmount.Value.Amount <= 0m)
        {
            amount = default;
            reason = $"{transfer.TransferType} requires a positive cash amount.";
            return false;
        }

        amount = transfer.CashAmount.Value;
        if (amount.Currency != Cash.Currency)
        {
            reason = $"Transfer currency {amount.Currency} does not match account currency {Cash.Currency}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool TryGetPositiveAssetTransfer(
        AccountTransferCompleted transfer,
        out Instrument instrument,
        out Qty quantity,
        out Price carryingPrice,
        out string reason)
    {
        if (!transfer.Instrument.HasValue || transfer.Quantity.Value <= 0m)
        {
            instrument = default;
            quantity = default;
            carryingPrice = default;
            reason = $"{transfer.TransferType} requires an instrument and positive quantity.";
            return false;
        }

        instrument = transfer.Instrument.Value;
        quantity = transfer.Quantity;
        carryingPrice = transfer.CarryingPrice ?? new Price(0m, Cash.Currency);
        if (carryingPrice.Currency != default && carryingPrice.Currency != Cash.Currency)
        {
            reason = $"Transfer carrying price currency {carryingPrice.Currency} does not match account currency {Cash.Currency}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool Fail(string message, out string reason)
    {
        reason = message;
        return false;
    }

    private Money GetSliceCash(StrategyId strategyId, int variantId)
        => _cashSlices.TryGetValue((strategyId, variantId), out var cash)
            ? cash
            : Cash;

    private void EnsureSliceCash(StrategyId strategyId, int variantId)
    {
        _cashSlices.TryAdd((strategyId, variantId), _initialCash);
    }

    private void AdjustSliceCash(StrategyId strategyId, int variantId, Money delta)
    {
        var current = GetSliceCash(strategyId, variantId);
        if (current.Currency != delta.Currency)
            throw new InvalidOperationException($"Cash adjustment currency {delta.Currency} does not match strategy cash currency {current.Currency}.");

        _cashSlices[(strategyId, variantId)] = current + delta;
    }

    private void ApplyPositionDelta(
        (StrategyId StrategyId, int VariantId, Instrument Instrument) key,
        Qty delta,
        Price price)
    {
        if (!_positions.TryGetValue(key, out var position))
        {
            if (delta.Value != 0m)
                _positions[key] = new AccountPosition(delta, price);
            return;
        }

        var current = position.Quantity.Value;
        var next = current + delta.Value;
        TrackRealizedPnL(key.StrategyId, key.VariantId, key.Instrument, current, delta.Value, position.AveragePrice, price);
        if (next == 0m)
        {
            _positions.Remove(key);
            return;
        }

        var addsSameSide = current == 0m || Math.Sign(current) == Math.Sign(delta.Value);
        var average = addsSameSide
            ? new Price(
                ((Math.Abs(current) * position.AveragePrice.Value) + (Math.Abs(delta.Value) * price.Value)) / Math.Abs(next),
                price.Currency)
            : position.AveragePrice;

        _positions[key] = new AccountPosition(new Qty(next), average);
    }

    private void RemovePosition((StrategyId StrategyId, int VariantId, Instrument Instrument) key)
    {
        _positions.Remove(key);
        _settledPositions.Remove(key);
    }

    private void TrackRealizedPnL(
        StrategyId strategyId,
        int variantId,
        Instrument instrument,
        decimal currentQuantity,
        decimal deltaQuantity,
        Price averagePrice,
        Price fillPrice)
    {
        if (currentQuantity == 0m
            || deltaQuantity == 0m
            || Math.Sign(currentQuantity) == Math.Sign(deltaQuantity))
        {
            return;
        }

        var closingQuantity = Math.Min(Math.Abs(currentQuantity), Math.Abs(deltaQuantity));
        var contract = GetContract(instrument);
        var realized = _valuation.RealizedPnL(
            contract,
            new Qty(closingQuantity * Math.Sign(currentQuantity)),
            averagePrice,
            fillPrice);
        if (realized.Amount == 0m)
            return;

        AddRealizedPnL(strategyId, variantId, realized);

        if (GetUpfrontCashFlow(contract, new Qty(closingQuantity), fillPrice).IsZero)
        {
            AdjustSliceCash(strategyId, variantId, realized);
            if (realized.Currency == Cash.Currency)
                Cash += realized;
        }
    }

    private Money GetRealizedPnL(StrategyId strategyId, int variantId, Currency currency)
        => _realizedPnL.TryGetValue((strategyId, variantId, currency), out var realized)
            ? realized
            : Money.Zero(currency);

    private void AddRealizedPnL(StrategyId strategyId, int variantId, Money realized)
    {
        if (realized.IsZero)
            return;

        var key = (strategyId, variantId, realized.Currency);
        _realizedPnL[key] = _realizedPnL.TryGetValue(key, out var existing)
            ? existing + realized
            : realized;
    }

    private static void ApplyOptionStrategyMarginOffsets(
        List<OptionMarginEntry> entries,
        Dictionary<(StrategyId StrategyId, int VariantId), MarginStatusAccumulator> accumulators,
        Currency currency)
    {
        var strategyModel = DefaultOptionStrategyMarginModel.Instance;
        foreach (var group in entries
            .Where(entry => entry.Maintenance.Currency == currency)
            .GroupBy(static entry => (entry.StrategyId, entry.VariantId)))
        {
            var groupEntries = group.ToArray();
            var contracts = groupEntries.ToDictionary(static entry => entry.Contract.Instrument, static entry => entry.Contract);
            var positions = groupEntries
                .Select(static entry => new PositionValuationInput(
                    entry.Contract.Instrument,
                    entry.Quantity,
                    entry.Mark,
                    Money.Zero(entry.Maintenance.Currency)))
                .ToArray();
            var packages = DefaultOptionStrategyRecognizer.Instance.Recognize(positions, contracts);
            if (packages.Count == 0)
                continue;

            var marketState = groupEntries.ToDictionary(
                static entry => entry.Contract.Instrument,
                static entry => new OptionMarketState(
                    entry.Contract.Instrument,
                    Timestamp: Instant.MinValue,
                    Last: entry.Mark));
            var context = new OptionMarginContext(
                contracts,
                marketState,
                new OptionPricingScenario(RiskFreeRate: 0m));

            foreach (var package in packages)
            {
                var shortLeg = package.OptionLegs.FirstOrDefault(static leg => leg.Side == Side.Sell);
                if (shortLeg == default)
                    continue;

                var shortEntry = groupEntries.FirstOrDefault(entry => entry.Contract.Instrument == shortLeg.Instrument);
                if (shortEntry == default || shortEntry.Quantity.Value >= 0m)
                    continue;

                var individualShortForCoveredQuantity =
                    shortEntry.Maintenance.Amount * shortLeg.Ratio / shortEntry.Quantity.Abs.Value;
                var spreadRequirement = strategyModel.MarginForPackage(package, context).Requirement.Amount;
                var reduction = Math.Max(0m, individualShortForCoveredQuantity - spreadRequirement);
                if (reduction <= 0m)
                    continue;

                if (accumulators.TryGetValue(group.Key, out var accumulator))
                {
                    accumulator.Maintenance = Math.Max(0m, accumulator.Maintenance - reduction);
                    accumulators[group.Key] = accumulator;
                }
            }
        }
    }

    private Money GetInitialMarginRequirement(
        InstrumentContract contract,
        Qty signedQuantity,
        Price mark,
        MarginParams margin,
        Price? underlyingMark)
    {
        if (contract.Payoff is PayoffTerms.Option option)
            return DefaultOptionMarginModel.Instance.InitialMargin(
                BuildOptionMarginRequest(contract, option.Terms, signedQuantity, mark, underlyingMark)).Requirement;

        var (initial, _) = GetMarginFractions(contract, margin);
        return _valuation.Notional(contract, signedQuantity, mark) * initial;
    }

    private Money GetPackageNetDebit(IReadOnlyList<PackageLegFill> legFills)
    {
        var net = Money.Zero(Cash.Currency);
        for (var i = 0; i < legFills.Count; i++)
        {
            var leg = legFills[i];
            var contract = GetContract(leg.Instrument);
            var upfront = ToAccountCurrency(GetUpfrontCashFlow(contract, leg.Quantity, leg.Price), Cash.Currency);
            net += leg.Side == Side.Buy ? upfront : -upfront;
        }

        return net.Amount > 0m ? net : Money.Zero(Cash.Currency);
    }

    private Money GetPackageInitialMarginRequirement(
        IReadOnlyList<PackageLegFill> legFills,
        InstrumentContract packageContract,
        Price packagePrice,
        MarginParams margin)
    {
        if (packageContract.Package?.Kind == PackageKind.OptionSpread &&
            TryRecognizeOptionStrategyPackage(legFills, out var package, out var context))
        {
            return DefaultOptionStrategyMarginModel.Instance.MarginForPackage(package, context).Requirement;
        }

        var total = Money.Zero(Cash.Currency);
        for (var i = 0; i < legFills.Count; i++)
        {
            var leg = legFills[i];
            var contract = GetContract(leg.Instrument);
            var requirement = ToAccountCurrency(
                GetInitialMarginRequirement(contract, SignedQuantity(leg.Side, leg.Quantity), leg.Price, margin, null),
                Cash.Currency);
            total += requirement;
        }

        return total;
    }

    private bool TryRecognizeOptionStrategyPackage(
        IReadOnlyList<PackageLegFill> legFills,
        out OptionStrategyPackage package,
        out OptionMarginContext context)
    {
        var contracts = new Dictionary<Instrument, InstrumentContract>();
        var positions = new PositionValuationInput[legFills.Count];
        var marketState = new Dictionary<Instrument, OptionMarketState>();
        for (var i = 0; i < legFills.Count; i++)
        {
            var leg = legFills[i];
            var contract = GetContract(leg.Instrument);
            if (contract.Payoff is not PayoffTerms.Option)
            {
                package = default!;
                context = default!;
                return false;
            }

            contracts[leg.Instrument] = contract;
            positions[i] = new PositionValuationInput(
                leg.Instrument,
                SignedQuantity(leg.Side, leg.Quantity),
                leg.Price,
                Money.Zero(Cash.Currency));
            marketState[leg.Instrument] = new OptionMarketState(
                leg.Instrument,
                Timestamp: Instant.MinValue,
                Last: leg.Price);
        }

        var recognized = DefaultOptionStrategyRecognizer.Instance.Recognize(positions, contracts);
        if (recognized.Count != 1)
        {
            package = default!;
            context = default!;
            return false;
        }

        package = recognized[0];
        context = new OptionMarginContext(contracts, marketState, new OptionPricingScenario(RiskFreeRate: 0m));
        return true;
    }

    private Money GetMaintenanceMarginRequirement(
        InstrumentContract contract,
        Qty signedQuantity,
        Price mark,
        MarginParams margin,
        Price? underlyingMark)
    {
        if (contract.Payoff is PayoffTerms.Option option)
            return DefaultOptionMarginModel.Instance.MaintenanceMargin(
                BuildOptionMarginRequest(contract, option.Terms, signedQuantity, mark, underlyingMark)).Requirement;

        var (_, maintenance) = GetMarginFractions(contract, margin);
        return _valuation.Notional(contract, signedQuantity, mark) * maintenance;
    }

    private static OptionMarginRequest BuildOptionMarginRequest(
        InstrumentContract contract,
        OptionTerms terms,
        Qty signedQuantity,
        Price optionMark,
        Price? underlyingMark) =>
        new(
            contract,
            signedQuantity,
            new OptionMarketState(
                contract.Instrument,
                Timestamp: Instant.MinValue,
                Last: optionMark,
                UnderlyingMark: underlyingMark ?? terms.Strike.ScaledStrike),
            new OptionPricingScenario(RiskFreeRate: 0m));

    private static (decimal Initial, decimal Maintenance) GetMarginFractions(
        InstrumentContract contract,
        MarginParams defaults)
        => contract.Margin switch
        {
            MarginTerms.CashMargin => (1m, 1m),
            MarginTerms.RegT => (defaults.InitialMarginFraction, defaults.MaintenanceMarginFraction),
            MarginTerms.FixedFraction fixedFraction => (fixedFraction.Initial, fixedFraction.Maintenance),
            MarginTerms.Portfolio => (defaults.InitialMarginFraction, defaults.MaintenanceMarginFraction),
            _ => (defaults.InitialMarginFraction, defaults.MaintenanceMarginFraction)
        };

    private static bool TryGetUnderlyingMark(
        InstrumentContract contract,
        IReadOnlyDictionary<Instrument, Price> marks,
        out Price mark)
    {
        if (contract.Payoff is PayoffTerms.Option option)
            return marks.TryGetValue(option.Terms.Underlying, out mark);

        mark = default;
        return false;
    }

    private InstrumentContract GetContract(Instrument instrument)
    {
        if (_contracts.TryGetValue(instrument, out var contract))
            return contract;

        throw new InvalidOperationException(
            $"Instrument {instrument} has no registered InstrumentContract. Register the contract before reservation, fill, valuation, margin, or accounting.");
    }

    private Money GetUpfrontCashFlow(InstrumentContract contract, Qty quantity, Price price)
    {
        if (contract.Payoff is PayoffTerms.Option option)
            return option.Terms.PremiumStyle switch
            {
                OptionPremiumStyle.Upfront => _valuation.MarketValue(contract, quantity, price),
                OptionPremiumStyle.FuturesStyle => Money.Zero(contract.Exposure.SettlementCurrency()),
                OptionPremiumStyle.Deferred => Money.Zero(contract.Exposure.SettlementCurrency()),
                _ => throw new InvalidOperationException($"Unsupported option premium style {option.Terms.PremiumStyle}.")
            };

        if (contract.Payoff is PayoffTerms.Betting)
            return new Money(quantity.Abs.Value, contract.Exposure.SettlementCurrency());

        var shouldExchangeCash = contract.Exposure is EconomicExposure.Spot
            || contract.Payoff is PayoffTerms.Binary;
        return shouldExchangeCash
            ? _valuation.MarketValue(contract, quantity, price)
            : Money.Zero(contract.Exposure.SettlementCurrency());
    }

    private static Money GetEquityContribution(InstrumentContract contract, PositionValuation value)
    {
        if (contract.Payoff is PayoffTerms.Option option)
        {
            return option.Terms.PremiumStyle == OptionPremiumStyle.Upfront
                ? value.MarketValue
                : value.UnrealizedPnL;
        }

        var contributesMarketValue = contract.Exposure is EconomicExposure.Spot
            || contract.Payoff is PayoffTerms.Binary;
        return contributesMarketValue ? value.MarketValue : value.UnrealizedPnL;
    }

    private Money GetNoUpfrontOptionPremiumBasis(
        InstrumentContract contract,
        Qty quantity,
        Price averagePrice)
    {
        if (contract.Payoff is not PayoffTerms.Option option)
            return Money.Zero(contract.Exposure.SettlementCurrency());

        return option.Terms.PremiumStyle switch
        {
            OptionPremiumStyle.Upfront => Money.Zero(contract.Exposure.SettlementCurrency()),
            OptionPremiumStyle.FuturesStyle or OptionPremiumStyle.Deferred =>
                _valuation.MarketValue(contract, quantity, averagePrice),
            _ => throw new InvalidOperationException($"Unsupported option premium style {option.Terms.PremiumStyle}.")
        };
    }

    private Money GetLifecyclePremiumBasis(
        InstrumentContract contract,
        Qty quantity,
        Price averagePrice) =>
        contract.Payoff switch
        {
            PayoffTerms.Option or PayoffTerms.Binary => _valuation.MarketValue(contract, quantity, averagePrice),
            PayoffTerms.Betting => new Money(quantity.Value, contract.Exposure.SettlementCurrency()),
            _ => Money.Zero(contract.Exposure.SettlementCurrency())
        };

    private static bool ShouldDeliverAssetOnFill(InstrumentContract contract) =>
        contract.Exposure is EconomicExposure.Spot
        && contract.Payoff is PayoffTerms.LinearPayoff;

    private static Qty SignedQuantity(Side side, Qty quantity) =>
        side == Side.Buy ? quantity : new Qty(-quantity.Value);

    private static Money ToAccountCurrency(Money money, Currency accountCurrency) =>
        money.Currency == default || money.Currency == accountCurrency
            ? new Money(money.Amount, accountCurrency)
            : throw new InvalidOperationException(
                $"Cash movement currency {money.Currency} does not match account currency {accountCurrency}. Add an explicit FX conversion path before applying this account effect.");

    private readonly record struct AccountReservation(
        Money Cash,
        Instrument Instrument,
        Qty MarginQuantity,
        Qty ShortQuantity);

    private readonly record struct AccountPosition(
        Qty Quantity,
        Price AveragePrice);

    private readonly record struct OptionMarginEntry(
        StrategyId StrategyId,
        int VariantId,
        InstrumentContract Contract,
        Qty Quantity,
        Price Mark,
        Money Maintenance);
}

public sealed record AccountPositionSnapshot(
    StrategyId StrategyId,
    int VariantId,
    Instrument Instrument,
    Qty Quantity,
    Price AveragePrice);

public sealed record MarginAccountStatus(
    StrategyId StrategyId,
    int VariantId,
    Money Equity,
    Money MaintenanceRequirement,
    bool IsMaintenanceBreached);
