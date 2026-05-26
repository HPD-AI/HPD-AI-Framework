using Rhodium.Events;
using Rhodium.Options;
using Rhodium.Primitives;
using Rhodium.Simulation.Exchange;

namespace Rhodium.Simulation.Tests;

public sealed class OptionLifecycleProcessorTests
{
    [Fact]
    public void Process_LongCashSettledCall_ReturnsCashSettlementOutcome()
    {
        var processor = new OptionLifecycleProcessor();
        var contract = CreateCashSettledCall(OptionExercisePolicy.CashSettledAtExpiry);

        var result = processor.Process(CreateRequest(
            contract,
            quantity: new Qty(1m),
            referencePrice: new Price(105m, Currency.USD)));

        var outcome = Assert.Single(result.Outcomes);
        var cash = Assert.IsType<OptionLifecycleOutcome.CashSettle>(outcome);
        Assert.Equal(new Qty(1m), cash.Quantity);
        Assert.Equal("Cash settled at expiry.", cash.Reason);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Process_LongCashSettledCallOutOfTheMoney_ReturnsExpireWorthlessOutcome()
    {
        var processor = new OptionLifecycleProcessor();
        var contract = CreateCashSettledCall(OptionExercisePolicy.CashSettledAtExpiry);

        var result = processor.Process(CreateRequest(
            contract,
            quantity: new Qty(1m),
            referencePrice: new Price(95m, Currency.USD)));

        var outcome = Assert.Single(result.Outcomes);
        var expired = Assert.IsType<OptionLifecycleOutcome.ExpireWorthless>(outcome);
        Assert.Equal(new Qty(1m), expired.Quantity);
        Assert.Equal("Out of the money at expiry.", expired.Reason);
    }

    [Fact]
    public void Process_MissingReference_ReturnsBlockOutcome()
    {
        var processor = new OptionLifecycleProcessor();
        var contract = CreateCashSettledCall(OptionExercisePolicy.CashSettledAtExpiry);
        var reason = "No settlement reference.";

        var result = processor.Process(new OptionLifecycleRequest(
            contract,
            new Qty(1m),
            new OptionLifecycleReference(null, OptionLifecycleReferenceSource.None, reason),
            Instant.FromUnixSeconds(1_796_016_000)));

        var outcome = Assert.Single(result.Outcomes);
        var blocked = Assert.IsType<OptionLifecycleOutcome.Block>(outcome);
        Assert.False(result.IsComplete);
        Assert.Equal(new Qty(1m), blocked.Quantity);
        Assert.Equal(reason, blocked.Reason);
    }

    [Fact]
    public void OptionLifecycleRequest_NonOptionContract_Throws()
    {
        var contract = Contracts.Equity("SPY", Venue.NASDAQ, Currency.USD);

        var exception = Assert.Throws<ArgumentException>(() => CreateRequest(
            contract,
            quantity: new Qty(1m),
            referencePrice: new Price(105m, Currency.USD)));

        Assert.Contains("requires an option contract", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleRequest_ZeroQuantity_Throws()
    {
        var contract = CreateCashSettledCall(OptionExercisePolicy.CashSettledAtExpiry);

        var exception = Assert.Throws<ArgumentException>(() => CreateRequest(
            contract,
            quantity: Qty.Zero,
            referencePrice: new Price(105m, Currency.USD)));

        Assert.Contains("nonzero position quantity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleRequest_LongQuantityWithAssignmentInput_Throws()
    {
        var contract = CreateCashSettledCall(
            OptionExercisePolicy.CashSettledAtExpiry,
            OptionAssignmentPolicy.Random);

        var exception = Assert.Throws<ArgumentException>(() => CreateRequest(
            contract,
            quantity: new Qty(1m),
            referencePrice: new Price(105m, Currency.USD),
            assignmentInput: new SimulationOptionAssignmentInput(isSelectedForRandomAssignment: true)));

        Assert.Contains("only valid for short option lifecycle requests", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleReference_WithMissingPriceAndResolvedSource_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleReference(
            null,
            OptionLifecycleReferenceSource.MarketMark,
            "No reference price."));

        Assert.Contains("must use reference source None", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleReference_WithResolvedPriceAndNoSource_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleReference(
            new Price(105m, Currency.USD),
            OptionLifecycleReferenceSource.None));

        Assert.Contains("requires a non-None reference source", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleReference_WithMissingPriceAndNoBlockReason_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleReference(
            null,
            OptionLifecycleReferenceSource.None));

        Assert.Contains("requires a block reason", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleReference_WithResolvedPriceAndBlockReason_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleReference(
            new Price(105m, Currency.USD),
            OptionLifecycleReferenceSource.MarketMark,
            "Should not be here."));

        Assert.Contains("cannot carry a block reason", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleReference_WithUnknownSource_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new OptionLifecycleReference(
            new Price(105m, Currency.USD),
            (OptionLifecycleReferenceSource)99));

        Assert.Contains("Unknown option lifecycle reference source", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleRequest_WithNullReference_Throws()
    {
        var contract = CreateCashSettledCall(OptionExercisePolicy.CashSettledAtExpiry);

        Assert.Throws<ArgumentNullException>(() => new OptionLifecycleRequest(
            contract,
            new Qty(1m),
            null!,
            Instant.FromUnixSeconds(1_796_016_000)));
    }

    [Fact]
    public void OptionLifecycleResult_WithNonBlockedOutcome_IsComplete()
    {
        var result = new OptionLifecycleResult([
            new OptionLifecycleOutcome.ExpireWorthless(
                new Qty(1m),
                new Price(95m, Currency.USD),
                Instant.FromUnixSeconds(1_796_016_000),
                OptionLifecycleReferenceSource.MarketMark,
                "Out of the money at expiry.")
        ]);

        Assert.True(result.IsComplete);
    }

    [Fact]
    public void OptionLifecycleResult_WithBlockedOutcome_IsIncomplete()
    {
        var result = new OptionLifecycleResult([
            new OptionLifecycleOutcome.Block(
                new Qty(1m),
                Instant.FromUnixSeconds(1_796_016_000),
                "No reference price.")
        ]);

        Assert.False(result.IsComplete);
    }

    [Fact]
    public void OptionLifecycleResult_WithBlockedAndSettlementOutcomes_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleResult([
            new OptionLifecycleOutcome.Block(
                new Qty(1m),
                Instant.FromUnixSeconds(1_796_016_000),
                "No reference price."),
            new OptionLifecycleOutcome.ExpireWorthless(
                new Qty(1m),
                new Price(95m, Currency.USD),
                Instant.FromUnixSeconds(1_796_016_000),
                OptionLifecycleReferenceSource.MarketMark,
                "Out of the money at expiry.")
        ]));

        Assert.Contains("cannot contain settlement outcomes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleResult_WithMixedQuantitySigns_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleResult([
            new OptionLifecycleOutcome.CashSettle(
                OptionLifecycleKind.Assignment,
                new Qty(-0.5m),
                new Price(105m, Currency.USD),
                Instant.FromUnixSeconds(1_796_016_000),
                OptionLifecycleReferenceSource.MarketMark,
                "Short option assigned."),
            new OptionLifecycleOutcome.ExpireWorthless(
                new Qty(0.5m),
                new Price(95m, Currency.USD),
                Instant.FromUnixSeconds(1_796_016_000),
                OptionLifecycleReferenceSource.MarketMark,
                "Out of the money at expiry.")
        ]));

        Assert.Contains("same quantity sign", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleResult_SnapshotsCallerOutcomes()
    {
        var outcomes = new List<OptionLifecycleOutcome>
        {
            new OptionLifecycleOutcome.ExpireWorthless(
                new Qty(1m),
                new Price(95m, Currency.USD),
                Instant.FromUnixSeconds(1_796_016_000),
                OptionLifecycleReferenceSource.MarketMark,
                "Out of the money at expiry.")
        };
        var result = new OptionLifecycleResult(outcomes);

        outcomes.Clear();
        outcomes.Add(new OptionLifecycleOutcome.Block(
            new Qty(1m),
            Instant.FromUnixSeconds(1_796_016_000),
            "No reference price."));

        var outcome = Assert.Single(result.Outcomes);
        Assert.IsType<OptionLifecycleOutcome.ExpireWorthless>(outcome);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void OptionLifecycleResult_WithNullOutcomes_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OptionLifecycleResult(null!));
    }

    [Fact]
    public void OptionLifecycleResult_WithNullOutcome_Throws()
    {
        var outcomes = new List<OptionLifecycleOutcome> { null! };

        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleResult(outcomes));

        Assert.Contains("cannot contain null outcomes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleOutcome_WithZeroQuantity_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleOutcome.ExpireWorthless(
            Qty.Zero,
            new Price(95m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionLifecycleReferenceSource.MarketMark,
            "Out of the money at expiry."));

        Assert.Contains("nonzero quantity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleOutcome_WithEmptyReason_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleOutcome.Block(
            new Qty(1m),
            Instant.FromUnixSeconds(1_796_016_000),
            ""));

        Assert.Contains("requires a reason", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleOutcome_NonBlockedWithNoReferenceSource_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleOutcome.CashSettle(
            OptionLifecycleKind.Exercise,
            new Qty(1m),
            new Price(105m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionLifecycleReferenceSource.None,
            "Cash settled at expiry."));

        Assert.Contains("requires a non-None reference source", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleOutcome_WithUnknownReferenceSource_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new OptionLifecycleOutcome.CashSettle(
            OptionLifecycleKind.Exercise,
            new Qty(1m),
            new Price(105m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            (OptionLifecycleReferenceSource)99,
            "Cash settled at expiry."));

        Assert.Contains("Unknown option lifecycle reference source", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleOutcome_SettlementWithNonExerciseOrAssignmentKind_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleOutcome.CashSettle(
            OptionLifecycleKind.CashSettlement,
            new Qty(1m),
            new Price(105m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionLifecycleReferenceSource.MarketMark,
            "Cash settled at expiry."));

        Assert.Contains("requires Exercise or Assignment", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleOutcome_AssignmentWithPositiveQuantity_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleOutcome.CashSettle(
            OptionLifecycleKind.Assignment,
            new Qty(1m),
            new Price(105m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionLifecycleReferenceSource.MarketMark,
            "Cash settled at expiry."));

        Assert.Contains("requires a short option quantity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleOutcome_ExerciseWithNegativeQuantity_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleOutcome.CashSettle(
            OptionLifecycleKind.Exercise,
            new Qty(-1m),
            new Price(105m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionLifecycleReferenceSource.MarketMark,
            "Cash settled at expiry."));

        Assert.Contains("requires a long option quantity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleOutcome_UnassignedWithPositiveQuantity_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleOutcome.ExpireUnassigned(
            new Qty(1m),
            new Price(105m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionLifecycleReferenceSource.MarketMark,
            "Unassigned short option quantity expired."));

        Assert.Contains("requires a short option quantity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleOutcome_UnexercisedWithNegativeQuantity_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleOutcome.ExpireUnexercised(
            new Qty(-1m),
            new Price(105m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionLifecycleReferenceSource.MarketMark,
            "In the money but not exercised by policy."));

        Assert.Contains("requires a long option quantity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionLifecycleOutcome_PhysicalWithEmptyPremiumReason_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleOutcome.PhysicalDeliver(
            OptionLifecycleKind.Exercise,
            new Qty(1m),
            new Price(105m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionLifecycleReferenceSource.MarketMark,
            "In the money at expiry.",
            ""));

        Assert.Contains("requires a premium reason", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_ManualLongCallInTheMoney_ReturnsExpireUnexercisedOutcome()
    {
        var processor = new OptionLifecycleProcessor();
        var contract = CreateCashSettledCall(OptionExercisePolicy.Manual);

        var result = processor.Process(CreateRequest(
            contract,
            quantity: new Qty(1m),
            referencePrice: new Price(105m, Currency.USD)));

        var outcome = Assert.Single(result.Outcomes);
        var expired = Assert.IsType<OptionLifecycleOutcome.ExpireUnexercised>(outcome);
        Assert.Equal(new Qty(1m), expired.Quantity);
        Assert.Contains("not exercised by contract policy", expired.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_LongPhysicalPutInTheMoney_ReturnsPhysicalDeliveryOutcome()
    {
        var processor = new OptionLifecycleProcessor();
        var contract = CreatePhysicalPut();

        var result = processor.Process(CreateRequest(
            contract,
            quantity: new Qty(1m),
            referencePrice: new Price(95m, Currency.USD)));

        var outcome = Assert.Single(result.Outcomes);
        var physical = Assert.IsType<OptionLifecycleOutcome.PhysicalDeliver>(outcome);
        Assert.Equal(new Qty(1m), physical.Quantity);
        Assert.Equal("In the money at expiry.", physical.Reason);
        Assert.Equal("Premium settlement at physical expiry.", physical.PremiumReason);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Process_ShortRandomAssignmentSelected_ReturnsCashSettlementOutcome()
    {
        var processor = new OptionLifecycleProcessor();
        var contract = CreateCashSettledCall(
            OptionExercisePolicy.CashSettledAtExpiry,
            OptionAssignmentPolicy.Random);

        var result = processor.Process(CreateRequest(
            contract,
            quantity: new Qty(-1m),
            referencePrice: new Price(105m, Currency.USD),
            assignmentInput: new SimulationOptionAssignmentInput(isSelectedForRandomAssignment: true)));

        var outcome = Assert.Single(result.Outcomes);
        var cash = Assert.IsType<OptionLifecycleOutcome.CashSettle>(outcome);
        Assert.Equal(new Qty(-1m), cash.Quantity);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Process_ShortRandomAssignmentWithoutSelection_ReturnsExpireUnassignedOutcome()
    {
        var processor = new OptionLifecycleProcessor();
        var contract = CreateCashSettledCall(
            OptionExercisePolicy.CashSettledAtExpiry,
            OptionAssignmentPolicy.Random);

        var result = processor.Process(CreateRequest(
            contract,
            quantity: new Qty(-1m),
            referencePrice: new Price(105m, Currency.USD)));

        var outcome = Assert.Single(result.Outcomes);
        var expired = Assert.IsType<OptionLifecycleOutcome.ExpireUnassigned>(outcome);
        Assert.Equal(new Qty(-1m), expired.Quantity);
        Assert.Contains("Random assignment requires explicit selection input", expired.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_ShortProRataAssignment_ReturnsAssignedAndUnassignedOutcomes()
    {
        var processor = new OptionLifecycleProcessor();
        var contract = CreateCashSettledCall(
            OptionExercisePolicy.CashSettledAtExpiry,
            OptionAssignmentPolicy.ProRata);

        var result = processor.Process(CreateRequest(
            contract,
            quantity: new Qty(-2m),
            referencePrice: new Price(105m, Currency.USD),
            assignmentInput: new SimulationOptionAssignmentInput(proRataAssignmentRatio: 0.5m)));

        Assert.Equal(2, result.Outcomes.Count);
        var cash = Assert.IsType<OptionLifecycleOutcome.CashSettle>(result.Outcomes[0]);
        var expired = Assert.IsType<OptionLifecycleOutcome.ExpireUnassigned>(result.Outcomes[1]);
        Assert.Equal(new Qty(-1m), cash.Quantity);
        Assert.Equal(new Qty(-1m), expired.Quantity);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Process_ShortAssignmentClampsToOpenShortQuantity()
    {
        var processor = new OptionLifecycleProcessor(new FixedAssignmentModel(new Qty(5m)));
        var contract = CreateCashSettledCall(
            OptionExercisePolicy.CashSettledAtExpiry,
            OptionAssignmentPolicy.VenueDefined);

        var result = processor.Process(CreateRequest(
            contract,
            quantity: new Qty(-2m),
            referencePrice: new Price(105m, Currency.USD)));

        var cash = Assert.Single(result.Outcomes);
        var settlement = Assert.IsType<OptionLifecycleOutcome.CashSettle>(cash);
        Assert.Equal(new Qty(-2m), settlement.Quantity);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void OptionTerms_UnknownOptionRight_Throws()
    {
        var contract = CreateCashSettledCall(OptionExercisePolicy.CashSettledAtExpiry);
        var terms = ((PayoffTerms.Option)contract.Payoff).Terms;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => terms.With(right: (OptionRight)99));

        Assert.Contains("Unknown option right", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionTerms_UnknownOptionExercisePolicy_Throws()
    {
        var contract = CreateCashSettledCall(OptionExercisePolicy.CashSettledAtExpiry);
        var terms = ((PayoffTerms.Option)contract.Payoff).Terms;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => terms.With(exercisePolicy: (OptionExercisePolicy)99));

        Assert.Contains("Unknown option exercise policy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionTerms_UnknownOptionSettlementStyle_Throws()
    {
        var contract = CreateCashSettledCall(OptionExercisePolicy.CashSettledAtExpiry);
        var terms = ((PayoffTerms.Option)contract.Payoff).Terms;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => terms.With(settlementStyle: (OptionSettlementStyle)99));

        Assert.Contains("Unknown option settlement style", exception.Message, StringComparison.Ordinal);
    }

    private static OptionLifecycleRequest CreateRequest(
        InstrumentContract contract,
        Qty quantity,
        Price referencePrice,
        SimulationOptionAssignmentInput? assignmentInput = null)
        => new(
            contract,
            quantity,
            new OptionLifecycleReference(referencePrice, OptionLifecycleReferenceSource.MarketMark),
            Instant.FromUnixSeconds(1_796_016_000),
            assignmentInput);

    private static InstrumentContract CreateCashSettledCall(
        OptionExercisePolicy exercisePolicy,
        OptionAssignmentPolicy assignmentPolicy = OptionAssignmentPolicy.VenueDefined)
        => Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE")),
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: exercisePolicy,
            assignmentPolicy: assignmentPolicy);

    private static InstrumentContract CreatePhysicalPut()
    {
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var contract = Contracts.OptionContract(
            "SPY261218P00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            Instant.FromUnixSeconds(1_796_016_000),
            OptionRight.Put,
            ExerciseStyle.American);
        var terms = Assert.IsType<PayoffTerms.Option>(contract.Payoff).Terms;
        return contract with
        {
            Lifecycle = new ContractLifecycle.Expiring(terms.Expiration, ExpiryAction.PhysicalDelivery),
            Settlement = new SettlementTerms.Physical(Currency.USD, underlying, SettlementDelay.Immediate()),
            Payoff = new PayoffTerms.Option(terms.With(
                settlementStyle: OptionSettlementStyle.Physical,
                exercisePolicy: OptionExercisePolicy.AutoExerciseInTheMoney))
        };
    }

    private sealed class FixedAssignmentModel(Qty quantity, bool isAssigned = true) : IOptionAssignmentModel
    {
        public OptionAssignmentDecision GetAssignment(OptionAssignmentContext context) =>
            new(isAssigned, quantity, "Forced test assignment.");
    }
}
