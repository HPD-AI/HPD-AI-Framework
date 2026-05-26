using Rhodium.Options;
using Rhodium.Primitives;

namespace Rhodium.Options.Tests;

public class OptionLifecycleModelTests
{
    [Fact]
    public void DefaultAssignmentModel_AssignsShortInTheMoneyWhenRuleAllows()
    {
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var underlying = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var contract = Contracts.OptionContract(
            "AAPL-20261225-250-C",
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(250m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American);
        var context = new OptionAssignmentContext(
            contract,
            ShortQuantity: new Qty(3m),
            Market: Market(contract, expiry, 255m),
            Timestamp: expiry,
            AssignmentRule: new OptionAssignmentRule(Money.Zero(Currency.USD)));

        var decision = DefaultOptionAssignmentModel.Instance.GetAssignment(context);

        Assert.True(decision.IsAssigned);
        Assert.Equal(new Qty(3m), decision.Quantity);
    }

    [Fact]
    public void OptionAssignmentDecision_AssignedRequiresPositiveQuantity()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionAssignmentDecision(
            isAssigned: true,
            quantity: Qty.Zero,
            reason: "Invalid test decision."));

        Assert.Contains("positive quantity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionAssignmentDecision_UnassignedRequiresZeroQuantity()
    {
        var exception = Assert.Throws<ArgumentException>(() => new OptionAssignmentDecision(
            isAssigned: false,
            quantity: new Qty(1m),
            reason: "Invalid test decision."));

        Assert.Contains("zero quantity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionAssignmentContext_RequiresPositiveShortQuantity()
    {
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = CreateContract(expiry);

        var exception = Assert.Throws<ArgumentException>(() => new OptionAssignmentContext(
            contract,
            ShortQuantity: Qty.Zero,
            Market: Market(contract, expiry, 255m),
            Timestamp: expiry));

        Assert.Contains("positive short quantity", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(0.0)]
    [InlineData(1.1)]
    public void OptionAssignmentContext_RejectsInvalidProRataRatio(double ratio)
    {
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = CreateContract(expiry, assignmentPolicy: OptionAssignmentPolicy.ProRata);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new OptionAssignmentContext(
            contract,
            ShortQuantity: new Qty(10m),
            Market: Market(contract, expiry, 255m),
            Timestamp: expiry,
            ProRataAssignmentRatio: (decimal)ratio));

        Assert.Equal("ProRataAssignmentRatio", exception.ParamName);
    }

    [Fact]
    public void OptionAssignmentRule_RejectsNegativeMinimumIntrinsicValue()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OptionAssignmentRule(new Money(-0.01m, Currency.USD)));

        Assert.Equal("MinimumIntrinsicValue", exception.ParamName);
    }

    [Fact]
    public void DefaultAssignmentModel_RandomPolicyRequiresExplicitSelection()
    {
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var underlying = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var contract = Contracts.OptionContract(
            "AAPL-20261225-250-C",
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(250m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American,
            assignmentPolicy: OptionAssignmentPolicy.Random);

        var noSelection = DefaultOptionAssignmentModel.Instance.GetAssignment(new OptionAssignmentContext(
            contract,
            ShortQuantity: new Qty(3m),
            Market: Market(contract, expiry, 255m),
            Timestamp: expiry));
        var selected = DefaultOptionAssignmentModel.Instance.GetAssignment(new OptionAssignmentContext(
            contract,
            ShortQuantity: new Qty(3m),
            Market: Market(contract, expiry, 255m),
            Timestamp: expiry,
            IsSelectedForRandomAssignment: true));

        Assert.False(noSelection.IsAssigned);
        Assert.Contains("requires explicit selection", noSelection.Reason, StringComparison.Ordinal);
        Assert.True(selected.IsAssigned);
        Assert.Equal(new Qty(3m), selected.Quantity);
    }

    [Fact]
    public void DefaultAssignmentModel_ProRataPolicyRequiresAssignmentRatio()
    {
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var underlying = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var contract = Contracts.OptionContract(
            "AAPL-20261225-250-C",
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(250m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American,
            assignmentPolicy: OptionAssignmentPolicy.ProRata);

        var missing = DefaultOptionAssignmentModel.Instance.GetAssignment(new OptionAssignmentContext(
            contract,
            ShortQuantity: new Qty(10m),
            Market: Market(contract, expiry, 255m),
            Timestamp: expiry));
        var assigned = DefaultOptionAssignmentModel.Instance.GetAssignment(new OptionAssignmentContext(
            contract,
            ShortQuantity: new Qty(10m),
            Market: Market(contract, expiry, 255m),
            Timestamp: expiry,
            ProRataAssignmentRatio: 0.35m));

        Assert.False(missing.IsAssigned);
        Assert.Contains("requires an assignment ratio", missing.Reason, StringComparison.Ordinal);
        Assert.True(assigned.IsAssigned);
        Assert.Equal(new Qty(3.5m), assigned.Quantity);
    }

    [Fact]
    public void DefaultAssignmentModel_RespectsAssignmentPolicyNone()
    {
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var underlying = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        var contract = Contracts.OptionContract(
            "AAPL-20261225-250-C",
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(250m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American);
        contract = contract with
        {
            Payoff = new PayoffTerms.Option(GetTerms(contract).With(assignmentPolicy: OptionAssignmentPolicy.None))
        };
        var context = new OptionAssignmentContext(
            contract,
            ShortQuantity: new Qty(3m),
            Market: Market(contract, expiry, 255m),
            Timestamp: expiry);

        var decision = DefaultOptionAssignmentModel.Instance.GetAssignment(context);

        Assert.False(decision.IsAssigned);
        Assert.Equal(Qty.Zero, decision.Quantity);
    }

    [Fact]
    public void DefaultAssignmentModel_UnknownAssignmentPolicyThrows()
    {
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = CreateContract(expiry);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            GetTerms(contract).With(assignmentPolicy: (OptionAssignmentPolicy)99));

        Assert.Contains("Unknown option assignment policy", exception.Message, StringComparison.Ordinal);
    }

    private static OptionMarketState Market(InstrumentContract contract, Instant timestamp, decimal underlyingMark) =>
        new(
            contract.Instrument,
            Timestamp: timestamp,
            UnderlyingMark: new Price(underlyingMark, Currency.USD));

    private static InstrumentContract CreateContract(
        Instant expiry,
        OptionAssignmentPolicy assignmentPolicy = OptionAssignmentPolicy.VenueDefined)
    {
        var underlying = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        return Contracts.OptionContract(
            "AAPL-20261225-250-C",
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(250m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American,
            assignmentPolicy: assignmentPolicy);
    }

    private static OptionTerms GetTerms(InstrumentContract contract) =>
        ((PayoffTerms.Option)contract.Payoff).Terms;
}
