using Helium.Finance.Conventions;
using Helium.Finance.Curves;
using Helium.Finance.Solvers;

namespace Helium.Finance.CashFlows;

public static class CashFlowValuation
{
    public static double NetPresentValue(
        CashFlowLeg leg,
        DiscountCurve discountCurve,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly? settlementDate = null,
        DateOnly? npvDate = null,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(leg);
        ArgumentNullException.ThrowIfNull(discountCurve);

        var settlement = settlementDate ?? referenceDate;
        var npv = npvDate ?? settlement;
        var npvTime = DayCounts.YearFraction(referenceDate, npv, dayCountConvention).Value;
        var npvDiscount = discountCurve.DiscountFactor(npvTime);

        var total = 0.0;
        foreach (var cashFlow in leg.Flows)
        {
            if (CashFlowLeg.HasOccurred(cashFlow.PaymentDate, settlement, includeSettlementDateFlows))
                continue;

            var paymentTime = DayCounts.YearFraction(referenceDate, cashFlow.PaymentDate, dayCountConvention).Value;
            total = AddDiscountedAmount(total, cashFlow.Amount, discountCurve.DiscountFactor(paymentTime));
        }

        var result = total / npvDiscount;
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(nameof(leg), "Cash-flow NPV must be finite.");

        return result;
    }

    public static double NetPresentValue(
        CashFlowLeg leg,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly? settlementDate = null,
        DateOnly? npvDate = null,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(leg);

        var settlement = settlementDate ?? referenceDate;
        var npv = npvDate ?? settlement;

        var total = 0.0;
        foreach (var cashFlow in leg.Flows)
        {
            if (CashFlowLeg.HasOccurred(cashFlow.PaymentDate, settlement, includeSettlementDateFlows))
                continue;

            var timeFromNpvDate = DayCounts.YearFraction(npv, cashFlow.PaymentDate, dayCountConvention).Value;
            if (timeFromNpvDate < 0.0)
                throw new ArgumentOutOfRangeException(nameof(npvDate), "Payment time from NPV date must be nonnegative.");

            total = AddDiscountedAmount(total, cashFlow.Amount, yield.DiscountFactor(timeFromNpvDate));
        }

        return total;
    }

    public static CashFlowYieldResult Yield(
        CashFlowLeg leg,
        double targetNpv,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        CompoundingConvention compounding,
        int frequency = 1,
        DateOnly? settlementDate = null,
        DateOnly? npvDate = null,
        bool includeSettlementDateFlows = false,
        double lower = -0.95,
        double upper = 1.0,
        double absoluteTolerance = 1e-12,
        int maxIterations = 100)
    {
        ArgumentNullException.ThrowIfNull(leg);

        if (!double.IsFinite(targetNpv))
            throw new ArgumentOutOfRangeException(nameof(targetNpv), "Target NPV must be finite.");

        var activeFlows = ActiveYieldFlows(leg, referenceDate, dayCountConvention, settlementDate, npvDate, includeSettlementDateFlows);
        if (!HasYieldSignChange(targetNpv, activeFlows))
            return FailedYieldResult(RootStatus.NoBracket, lower, upper);

        var paymentTimes = activeFlows.Select(static flow => flow.Time).ToArray();
        var bracket = AdjustYieldBracket(compounding, frequency, lower, upper, paymentTimes);
        double Objective(double rate)
        {
            var interestRate = new InterestRate(rate, compounding, frequency);
            return NetPresentValue(
                leg,
                interestRate,
                referenceDate,
                dayCountConvention,
                settlementDate,
                npvDate,
                includeSettlementDateFlows) - targetNpv;
        }

        var root = RootFinders.Brent(Objective, bracket.Lower, bracket.Upper, absoluteTolerance, maxIterations);
        return new CashFlowYieldResult(
            root.Converged,
            root.Converged ? root.Root : double.NaN,
            root.Converged ? root.FunctionValue : double.NaN,
            root);
    }

    public static double Duration(
        CashFlowLeg leg,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DurationType type,
        DateOnly? settlementDate = null,
        DateOnly? npvDate = null,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(leg);

        return type switch
        {
            DurationType.Simple => SimpleDuration(leg, yield, referenceDate, dayCountConvention, settlementDate, npvDate, includeSettlementDateFlows),
            DurationType.Modified => ModifiedDuration(leg, yield, referenceDate, dayCountConvention, settlementDate, npvDate, includeSettlementDateFlows),
            DurationType.Macaulay => MacaulayDuration(leg, yield, referenceDate, dayCountConvention, settlementDate, npvDate, includeSettlementDateFlows),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported duration type.")
        };
    }

    public static double Convexity(
        CashFlowLeg leg,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly? settlementDate = null,
        DateOnly? npvDate = null,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(leg);

        var presentValue = 0.0;
        var secondDerivative = 0.0;

        foreach (var weighted in ActiveDiscountedFlows(leg, yield, referenceDate, dayCountConvention, settlementDate, npvDate, includeSettlementDateFlows))
        {
            presentValue = AddFinite(presentValue, weighted.PresentValue, nameof(leg), "Cash-flow present value total must be finite.");
            secondDerivative = AddFinite(
                secondDerivative,
                MultiplyFinite(weighted.Amount, SecondDiscountDerivative(yield, weighted.Time), nameof(leg), "Cash-flow convexity contribution must be finite."),
                nameof(leg),
                "Cash-flow convexity total must be finite.");
        }

        return FiniteRatio(secondDerivative, presentValue, nameof(leg), "Cash-flow convexity must be finite.");
    }

    public static double BasisPointValue(
        CashFlowLeg leg,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly? settlementDate = null,
        DateOnly? npvDate = null,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(leg);

        var npv = NetPresentValue(leg, yield, referenceDate, dayCountConvention, settlementDate, npvDate, includeSettlementDateFlows);
        var modifiedDuration = Duration(leg, yield, referenceDate, dayCountConvention, DurationType.Modified, settlementDate, npvDate, includeSettlementDateFlows);
        var convexity = Convexity(leg, yield, referenceDate, dayCountConvention, settlementDate, npvDate, includeSettlementDateFlows);
        var shift = 0.0001;
        var delta = MultiplyFinite(-modifiedDuration, npv, nameof(leg), "Cash-flow BPV delta contribution must be finite.") * shift;
        var gamma = 0.5
            * MultiplyFinite(convexity, npv, nameof(leg), "Cash-flow BPV gamma contribution must be finite.")
            * shift
            * shift;

        return AddFinite(delta, gamma, nameof(leg), "Cash-flow BPV must be finite.");
    }

    private static double SimpleDuration(
        CashFlowLeg leg,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly? settlementDate,
        DateOnly? npvDate,
        bool includeSettlementDateFlows)
    {
        var presentValue = 0.0;
        var weightedTime = 0.0;

        foreach (var weighted in ActiveDiscountedFlows(leg, yield, referenceDate, dayCountConvention, settlementDate, npvDate, includeSettlementDateFlows))
        {
            presentValue = AddFinite(presentValue, weighted.PresentValue, nameof(leg), "Cash-flow present value total must be finite.");
            weightedTime = AddFinite(
                weightedTime,
                MultiplyFinite(weighted.Time, weighted.PresentValue, nameof(leg), "Cash-flow weighted-time contribution must be finite."),
                nameof(leg),
                "Cash-flow weighted-time total must be finite.");
        }

        return FiniteRatio(weightedTime, presentValue, nameof(leg), "Cash-flow duration must be finite.");
    }

    private static double ModifiedDuration(
        CashFlowLeg leg,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly? settlementDate,
        DateOnly? npvDate,
        bool includeSettlementDateFlows)
    {
        var presentValue = 0.0;
        var derivative = 0.0;

        foreach (var weighted in ActiveDiscountedFlows(leg, yield, referenceDate, dayCountConvention, settlementDate, npvDate, includeSettlementDateFlows))
        {
            presentValue = AddFinite(presentValue, weighted.PresentValue, nameof(leg), "Cash-flow present value total must be finite.");
            derivative = AddFinite(
                derivative,
                MultiplyFinite(weighted.Amount, DiscountDerivative(yield, weighted.Time), nameof(leg), "Cash-flow duration derivative contribution must be finite."),
                nameof(leg),
                "Cash-flow duration derivative total must be finite.");
        }

        return presentValue == 0.0
            ? 0.0
            : FiniteRatio(-derivative, presentValue, nameof(leg), "Cash-flow duration must be finite.");
    }

    private static double MacaulayDuration(
        CashFlowLeg leg,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly? settlementDate,
        DateOnly? npvDate,
        bool includeSettlementDateFlows)
    {
        if (yield.Compounding != CompoundingConvention.Compounded)
            throw new ArgumentOutOfRangeException(nameof(yield), "Macaulay duration requires compounded yield.");

        return (1.0 + yield.Rate / yield.Frequency)
            * ModifiedDuration(leg, yield, referenceDate, dayCountConvention, settlementDate, npvDate, includeSettlementDateFlows);
    }

    private static IEnumerable<DiscountedCashFlow> ActiveDiscountedFlows(
        CashFlowLeg leg,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly? settlementDate,
        DateOnly? npvDate,
        bool includeSettlementDateFlows)
    {
        var settlement = settlementDate ?? referenceDate;
        var npv = npvDate ?? settlement;

        foreach (var cashFlow in leg.Flows)
        {
            if (CashFlowLeg.HasOccurred(cashFlow.PaymentDate, settlement, includeSettlementDateFlows))
                continue;

            var time = DayCounts.YearFraction(npv, cashFlow.PaymentDate, dayCountConvention).Value;
            if (time < 0.0)
                throw new ArgumentOutOfRangeException(nameof(npvDate), "Payment time from NPV date must be nonnegative.");

            var discount = yield.DiscountFactor(time);
            var presentValue = cashFlow.Amount * discount;
            if (!double.IsFinite(presentValue))
                throw new ArgumentOutOfRangeException(nameof(leg), "Discounted cash-flow amount must be finite.");

            yield return new DiscountedCashFlow(cashFlow.Amount, time, presentValue);
        }
    }

    private static double AddDiscountedAmount(double total, double amount, double discountFactor)
    {
        var presentValue = amount * discountFactor;
        if (!double.IsFinite(presentValue))
            throw new ArgumentOutOfRangeException(nameof(amount), "Discounted cash-flow amount must be finite.");

        return AddFinite(total, presentValue, nameof(total), "Cash-flow NPV total must be finite.");
    }

    private static double AddFinite(double left, double right, string parameterName, string message)
    {
        var result = left + right;
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(parameterName, message);

        return result;
    }

    private static double MultiplyFinite(double left, double right, string parameterName, string message)
    {
        var result = left * right;
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(parameterName, message);

        return result;
    }

    private static double FiniteRatio(double numerator, double denominator, string parameterName, string message)
    {
        if (denominator == 0.0)
            return 0.0;

        var result = numerator / denominator;
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(parameterName, message);

        return result;
    }

    private static double DiscountDerivative(InterestRate yield, double time)
    {
        var discount = yield.DiscountFactor(time);
        return yield.Compounding switch
        {
            CompoundingConvention.Simple => -time * discount * discount,
            CompoundingConvention.Continuous => -time * discount,
            CompoundingConvention.Compounded => -time * discount / (1.0 + yield.Rate / yield.Frequency),
            CompoundingConvention.SimpleThenCompounded => time <= 1.0 / yield.Frequency
                ? SimpleDiscountDerivative(yield, time, discount)
                : CompoundedDiscountDerivative(yield, time, discount),
            CompoundingConvention.CompoundedThenSimple => time > 1.0 / yield.Frequency
                ? SimpleDiscountDerivative(yield, time, discount)
                : CompoundedDiscountDerivative(yield, time, discount),
            _ => throw new ArgumentOutOfRangeException(nameof(yield), yield.Compounding, "Unsupported compounding convention.")
        };
    }

    private static double SecondDiscountDerivative(InterestRate yield, double time)
    {
        var discount = yield.DiscountFactor(time);
        return yield.Compounding switch
        {
            CompoundingConvention.Simple => 2.0 * discount * discount * discount * time * time,
            CompoundingConvention.Continuous => discount * time * time,
            CompoundingConvention.Compounded => discount * time * (yield.Frequency * time + 1.0)
                / (yield.Frequency * Math.Pow(1.0 + yield.Rate / yield.Frequency, 2.0)),
            CompoundingConvention.SimpleThenCompounded => time <= 1.0 / yield.Frequency
                ? SimpleSecondDiscountDerivative(time, discount)
                : CompoundedSecondDiscountDerivative(yield, time, discount),
            CompoundingConvention.CompoundedThenSimple => time > 1.0 / yield.Frequency
                ? SimpleSecondDiscountDerivative(time, discount)
                : CompoundedSecondDiscountDerivative(yield, time, discount),
            _ => throw new ArgumentOutOfRangeException(nameof(yield), yield.Compounding, "Unsupported compounding convention.")
        };
    }

    private static double SimpleDiscountDerivative(InterestRate yield, double time, double discount) =>
        -time * discount * discount;

    private static double CompoundedDiscountDerivative(InterestRate yield, double time, double discount) =>
        -time * discount / (1.0 + yield.Rate / yield.Frequency);

    private static double SimpleSecondDiscountDerivative(double time, double discount) =>
        2.0 * discount * discount * discount * time * time;

    private static double CompoundedSecondDiscountDerivative(InterestRate yield, double time, double discount) =>
        discount * time * (yield.Frequency * time + 1.0)
        / (yield.Frequency * Math.Pow(1.0 + yield.Rate / yield.Frequency, 2.0));


    private static (double Lower, double Upper) AdjustYieldBracket(
        CompoundingConvention compounding,
        int frequency,
        double lower,
        double upper,
        IReadOnlyList<double> paymentTimes)
    {
        if (!double.IsFinite(lower) || !double.IsFinite(upper) || lower >= upper)
            throw new ArgumentOutOfRangeException(nameof(lower), "Yield bracket must be finite and ordered.");

        if (RequiresFrequency(compounding) && frequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(frequency), "This compounding convention requires a positive frequency.");

        var minimum = MinimumYieldRate(compounding, frequency, paymentTimes);
        var adjustedLower = Math.Max(lower, minimum);
        if (adjustedLower >= upper)
            throw new ArgumentOutOfRangeException(nameof(lower), "Yield bracket lower bound is at or above the upper bound after applying compounding-domain constraints.");

        return (adjustedLower, upper);
    }

    private static IReadOnlyList<YieldCashFlow> ActiveYieldFlows(
        CashFlowLeg leg,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly? settlementDate,
        DateOnly? npvDate,
        bool includeSettlementDateFlows)
    {
        var settlement = settlementDate ?? referenceDate;
        var npv = npvDate ?? settlement;
        var flows = new List<YieldCashFlow>();

        foreach (var cashFlow in leg.Flows)
        {
            if (CashFlowLeg.HasOccurred(cashFlow.PaymentDate, settlement, includeSettlementDateFlows))
                continue;

            var time = DayCounts.YearFraction(npv, cashFlow.PaymentDate, dayCountConvention).Value;
            if (time < 0.0)
                throw new ArgumentOutOfRangeException(nameof(npvDate), "Payment time from NPV date must be nonnegative.");

            flows.Add(new YieldCashFlow(cashFlow.Amount, time));
        }

        return flows;
    }

    private static bool HasYieldSignChange(double targetNpv, IReadOnlyList<YieldCashFlow> activeFlows)
    {
        var lastSign = Math.Sign(-targetNpv);
        var signChanges = 0;

        foreach (var cashFlow in activeFlows)
        {
            var sign = Math.Sign(cashFlow.Amount);
            if (lastSign * sign < 0)
                signChanges++;

            if (sign != 0)
                lastSign = sign;
        }

        return signChanges > 0;
    }

    private static CashFlowYieldResult FailedYieldResult(RootStatus status, double lower, double upper)
    {
        return new CashFlowYieldResult(
            Converged: false,
            Yield: double.NaN,
            NpvResidual: double.NaN,
            Root: new RootResult(
                Converged: false,
                Root: double.NaN,
                FunctionValue: double.NaN,
                Iterations: 0,
                FunctionEvaluations: 0,
                Lower: lower,
                Upper: upper,
                Status: status));
    }

    private static double MinimumYieldRate(
        CompoundingConvention compounding,
        int frequency,
        IReadOnlyList<double> paymentTimes)
    {
        const double epsilon = 1e-12;
        return compounding switch
        {
            CompoundingConvention.Simple => SimpleMinimumRate(paymentTimes, epsilon),
            CompoundingConvention.Compounded => -frequency + epsilon,
            CompoundingConvention.SimpleThenCompounded => TransitionMinimumRate(
                paymentTimes,
                frequency,
                time => time <= 1.0 / frequency,
                epsilon),
            CompoundingConvention.CompoundedThenSimple => TransitionMinimumRate(
                paymentTimes,
                frequency,
                time => time > 1.0 / frequency,
                epsilon),
            _ => double.NegativeInfinity
        };
    }

    private static double SimpleMinimumRate(IReadOnlyList<double> paymentTimes, double epsilon)
    {
        var maxTime = paymentTimes.Count == 0 ? 0.0 : paymentTimes.Max();
        return maxTime > 0.0 ? -1.0 / maxTime + epsilon : double.NegativeInfinity;
    }

    private static double TransitionMinimumRate(
        IReadOnlyList<double> paymentTimes,
        int frequency,
        Func<double, bool> simpleWhen,
        double epsilon)
    {
        var minimum = -frequency + epsilon;
        var maxSimpleTime = paymentTimes.Where(simpleWhen).DefaultIfEmpty(0.0).Max();
        if (maxSimpleTime > 0.0)
            minimum = Math.Max(minimum, -1.0 / maxSimpleTime + epsilon);

        return minimum;
    }

    private static bool RequiresFrequency(CompoundingConvention compounding) =>
        compounding is CompoundingConvention.Compounded
            or CompoundingConvention.SimpleThenCompounded
            or CompoundingConvention.CompoundedThenSimple;

    private readonly record struct DiscountedCashFlow(double Amount, double Time, double PresentValue);

    private readonly record struct YieldCashFlow(double Amount, double Time);
}
