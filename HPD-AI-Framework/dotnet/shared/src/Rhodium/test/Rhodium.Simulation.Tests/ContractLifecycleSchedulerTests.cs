using Rhodium.Primitives;
using Rhodium.Simulation.Exchange;

namespace Rhodium.Simulation.Tests;

public sealed class ContractLifecycleSchedulerTests
{
    [Fact]
    public void MarkCompleted_RemovesDueLifecycleWork()
    {
        var scheduler = new ContractLifecycleScheduler();
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var contract = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE")),
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        var due = new List<ScheduledContractLifecycle>();

        scheduler.Register(contract);
        scheduler.CopyDue(expiry, due);

        var scheduled = Assert.Single(due);
        Assert.Equal(contract.Instrument, scheduled.Instrument);

        scheduler.MarkCompleted(contract.Instrument);
        scheduler.CopyDue(expiry, due);

        Assert.Empty(due);
    }
}
