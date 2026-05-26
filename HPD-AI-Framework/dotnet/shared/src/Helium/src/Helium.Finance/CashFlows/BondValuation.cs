using Helium.Finance.Conventions;
using Helium.Finance.Curves;
using Helium.Finance.Solvers;

namespace Helium.Finance.CashFlows;

public static class BondValuation
{
    public static double DirtyPrice(
        FixedRateBond bond,
        DiscountCurve discountCurve,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(bond);
        ArgumentNullException.ThrowIfNull(discountCurve);

        var currentNotional = bond.CurrentNotional(settlementDate);
        if (currentNotional == 0.0)
            return 0.0;

        var settlementValue = CashFlowValuation.NetPresentValue(
            ActiveBondCashFlows(bond, settlementDate, includeSettlementDateFlows),
            discountCurve,
            referenceDate,
            dayCountConvention,
            settlementDate,
            npvDate: settlementDate,
            includeSettlementDateFlows);

        return ScalePerHundredNotional(settlementValue, currentNotional, nameof(bond), "Dirty price must be finite.");
    }

    public static double DirtyPrice(
        FixedRateBond bond,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(bond);

        var currentNotional = bond.CurrentNotional(settlementDate);
        if (currentNotional == 0.0)
            return 0.0;

        var settlementValue = CashFlowValuation.NetPresentValue(
            ActiveBondCashFlows(bond, settlementDate, includeSettlementDateFlows),
            yield,
            referenceDate,
            dayCountConvention,
            settlementDate,
            npvDate: settlementDate,
            includeSettlementDateFlows);

        return ScalePerHundredNotional(settlementValue, currentNotional, nameof(bond), "Dirty price must be finite.");
    }

    public static double CleanPrice(
        FixedRateBond bond,
        DiscountCurve discountCurve,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(bond);
        ArgumentNullException.ThrowIfNull(discountCurve);

        return AddFinite(
            DirtyPrice(bond, discountCurve, referenceDate, dayCountConvention, settlementDate, includeSettlementDateFlows),
            -bond.AccruedAmount(settlementDate),
            nameof(bond),
            "Clean price must be finite.");
    }

    public static double CleanPrice(
        FixedRateBond bond,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(bond);

        return AddFinite(
            DirtyPrice(bond, yield, referenceDate, dayCountConvention, settlementDate, includeSettlementDateFlows),
            -bond.AccruedAmount(settlementDate),
            nameof(bond),
            "Clean price must be finite.");
    }

    public static double DirtyPriceWithContinuousSpread(
        FixedRateBond bond,
        DiscountCurve discountCurve,
        double continuousSpread,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(bond);
        ArgumentNullException.ThrowIfNull(discountCurve);

        return DirtyPriceWithSpread(
            bond,
            discountCurve,
            continuousSpread,
            CompoundingConvention.Continuous,
            frequency: 1,
            referenceDate,
            dayCountConvention,
            settlementDate,
            includeSettlementDateFlows);
    }

    public static double DirtyPriceWithSpread(
        FixedRateBond bond,
        DiscountCurve discountCurve,
        double spread,
        CompoundingConvention spreadCompounding,
        int frequency,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(bond);
        ArgumentNullException.ThrowIfNull(discountCurve);

        if (!double.IsFinite(spread))
            throw new ArgumentOutOfRangeException(nameof(spread), "Spread must be finite.");

        var spreadRate = new InterestRate(spread, spreadCompounding, frequency);
        _ = spreadRate.DiscountFactor(0.0);

        var currentNotional = bond.CurrentNotional(settlementDate);
        if (currentNotional == 0.0)
            return 0.0;

        var settlementTime = DayCounts.YearFraction(referenceDate, settlementDate, dayCountConvention).Value;
        var settlementDiscount = discountCurve.DiscountFactor(settlementTime);
        var settlementValue = 0.0;

        foreach (var cashFlow in ActiveBondCashFlows(bond, settlementDate, includeSettlementDateFlows).Flows)
        {
            var paymentTime = DayCounts.YearFraction(referenceDate, cashFlow.PaymentDate, dayCountConvention).Value;
            var timeFromSettlement = DayCounts.YearFraction(settlementDate, cashFlow.PaymentDate, dayCountConvention).Value;
            if (timeFromSettlement < 0.0)
                throw new ArgumentOutOfRangeException(nameof(settlementDate), "Payment time from settlement must be nonnegative.");

            var baseForwardDiscount = discountCurve.DiscountFactor(paymentTime) / settlementDiscount;
            if (!double.IsFinite(baseForwardDiscount))
                throw new ArgumentOutOfRangeException(nameof(discountCurve), "Base forward discount factor must be finite.");

            var spreadDiscount = spreadRate.DiscountFactor(timeFromSettlement);
            var contribution = cashFlow.Amount * baseForwardDiscount * spreadDiscount;
            settlementValue = AddFinite(settlementValue, contribution, nameof(bond), "Spread-adjusted settlement value must be finite.");
        }

        return ScalePerHundredNotional(settlementValue, currentNotional, nameof(bond), "Dirty price must be finite.");
    }

    public static double CleanPriceWithContinuousSpread(
        FixedRateBond bond,
        DiscountCurve discountCurve,
        double continuousSpread,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(bond);
        ArgumentNullException.ThrowIfNull(discountCurve);

        return AddFinite(
            DirtyPriceWithContinuousSpread(
                bond,
                discountCurve,
                continuousSpread,
                referenceDate,
                dayCountConvention,
                settlementDate,
                includeSettlementDateFlows),
            -bond.AccruedAmount(settlementDate),
            nameof(bond),
            "Clean price must be finite.");
    }

    public static double CleanPriceWithSpread(
        FixedRateBond bond,
        DiscountCurve discountCurve,
        double spread,
        CompoundingConvention spreadCompounding,
        int frequency,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(bond);
        ArgumentNullException.ThrowIfNull(discountCurve);

        return AddFinite(
            DirtyPriceWithSpread(
                bond,
                discountCurve,
                spread,
                spreadCompounding,
                frequency,
                referenceDate,
                dayCountConvention,
                settlementDate,
                includeSettlementDateFlows),
            -bond.AccruedAmount(settlementDate),
            nameof(bond),
            "Clean price must be finite.");
    }

    public static double SettlementValue(FixedRateBond bond, double cleanPrice, DateOnly settlementDate)
    {
        ArgumentNullException.ThrowIfNull(bond);

        if (!double.IsFinite(cleanPrice))
            throw new ArgumentOutOfRangeException(nameof(cleanPrice), "Clean price must be finite.");

        var currentNotional = bond.CurrentNotional(settlementDate);
        if (currentNotional == 0.0)
            return 0.0;

        var dirtyPrice = AddFinite(cleanPrice, bond.AccruedAmount(settlementDate), nameof(cleanPrice), "Dirty price must be finite.");
        var settlementValue = dirtyPrice / 100.0 * currentNotional;
        if (!double.IsFinite(settlementValue))
            throw new ArgumentOutOfRangeException(nameof(cleanPrice), "Settlement value must be finite.");

        return settlementValue;
    }

    public static double Duration(
        FixedRateBond bond,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DurationType type,
        DateOnly settlementDate)
    {
        ArgumentNullException.ThrowIfNull(bond);

        if (!bond.IsTradable(settlementDate))
            return 0.0;

        return CashFlowValuation.Duration(
            ActiveBondCashFlows(bond, settlementDate),
            yield,
            referenceDate,
            dayCountConvention,
            type,
            settlementDate,
            npvDate: settlementDate,
            includeSettlementDateFlows: false);
    }

    public static double Convexity(
        FixedRateBond bond,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate)
    {
        ArgumentNullException.ThrowIfNull(bond);

        if (!bond.IsTradable(settlementDate))
            return 0.0;

        return CashFlowValuation.Convexity(
            ActiveBondCashFlows(bond, settlementDate),
            yield,
            referenceDate,
            dayCountConvention,
            settlementDate,
            npvDate: settlementDate,
            includeSettlementDateFlows: false);
    }

    public static double BasisPointValue(
        FixedRateBond bond,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate)
    {
        ArgumentNullException.ThrowIfNull(bond);

        var currentNotional = bond.CurrentNotional(settlementDate);
        if (currentNotional == 0.0)
            return 0.0;

        var currencyBpv = CashFlowValuation.BasisPointValue(
            ActiveBondCashFlows(bond, settlementDate),
            yield,
            referenceDate,
            dayCountConvention,
            settlementDate,
            npvDate: settlementDate,
            includeSettlementDateFlows: false);

        return ScalePerHundredNotional(currencyBpv, currentNotional, nameof(bond), "Bond basis-point value must be finite.");
    }

    public static double YieldValueBasisPoint(
        FixedRateBond bond,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate)
    {
        ArgumentNullException.ThrowIfNull(bond);

        var priceBpv = BasisPointValue(bond, yield, referenceDate, dayCountConvention, settlementDate);
        if (priceBpv == 0.0)
            throw new InvalidOperationException("Cannot compute yield value of a basis point with zero price basis-point value.");

        var value = 1e-4 / -priceBpv;
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(bond), "Yield value basis point must be finite.");

        return value;
    }

    public static double CouponBasisPointValue(
        FixedRateBond bond,
        DiscountCurve discountCurve,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(bond);
        ArgumentNullException.ThrowIfNull(discountCurve);

        var currentNotional = bond.CurrentNotional(settlementDate);
        if (currentNotional == 0.0)
            return 0.0;

        var settlementTime = DayCounts.YearFraction(referenceDate, settlementDate, dayCountConvention).Value;
        var settlementDiscount = discountCurve.DiscountFactor(settlementTime);
        var bps = 0.0;
        foreach (var coupon in bond.Coupons.Coupons)
        {
            if (CashFlowLeg.HasOccurred(coupon.PaymentDate, settlementDate, includeSettlementDateFlows)
                || coupon.TradingExCoupon(settlementDate))
            {
                continue;
            }

            var paymentTime = DayCounts.YearFraction(referenceDate, coupon.PaymentDate, dayCountConvention).Value;
            var contribution = CouponRateDerivative(coupon, coupon.AccrualYearFraction)
                * discountCurve.DiscountFactor(paymentTime)
                / settlementDiscount;
            bps = AddFinite(bps, contribution, nameof(bond), "Coupon basis-point annuity must be finite.");
        }

        return ScalePerHundredNotional(bps * 1e-4, currentNotional, nameof(bond), "Coupon basis-point value must be finite.");
    }

    public static double CouponBasisPointValue(
        FixedRateBond bond,
        InterestRate yield,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(bond);

        var currentNotional = bond.CurrentNotional(settlementDate);
        if (currentNotional == 0.0)
            return 0.0;

        var bps = 0.0;
        foreach (var coupon in bond.Coupons.Coupons)
        {
            if (CashFlowLeg.HasOccurred(coupon.PaymentDate, settlementDate, includeSettlementDateFlows)
                || coupon.TradingExCoupon(settlementDate))
            {
                continue;
            }

            var timeFromSettlement = DayCounts.YearFraction(settlementDate, coupon.PaymentDate, dayCountConvention).Value;
            if (timeFromSettlement < 0.0)
                throw new ArgumentOutOfRangeException(nameof(settlementDate), "Payment time from settlement must be nonnegative.");

            var contribution = CouponRateDerivative(coupon, coupon.AccrualYearFraction) * yield.DiscountFactor(timeFromSettlement);
            bps = AddFinite(bps, contribution, nameof(bond), "Coupon basis-point annuity must be finite.");
        }

        return ScalePerHundredNotional(bps * 1e-4, currentNotional, nameof(bond), "Coupon basis-point value must be finite.");
    }

    public static double CouponAnnuity(
        FixedRateBond bond,
        DiscountCurve discountCurve,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        ArgumentNullException.ThrowIfNull(bond);
        ArgumentNullException.ThrowIfNull(discountCurve);

        if (!bond.IsTradable(settlementDate))
            return 0.0;

        var settlementTime = DayCounts.YearFraction(referenceDate, settlementDate, dayCountConvention).Value;
        var settlementDiscount = discountCurve.DiscountFactor(settlementTime);
        var annuity = 0.0;
        foreach (var coupon in bond.Coupons.Coupons)
        {
            if (CashFlowLeg.HasOccurred(coupon.PaymentDate, settlementDate, includeSettlementDateFlows)
                || coupon.TradingExCoupon(settlementDate))
            {
                continue;
            }

            var paymentTime = DayCounts.YearFraction(referenceDate, coupon.PaymentDate, dayCountConvention).Value;
            var contribution = CouponRateDerivative(coupon, coupon.AccrualYearFraction)
                * discountCurve.DiscountFactor(paymentTime)
                / settlementDiscount;
            annuity = AddFinite(annuity, contribution, nameof(bond), "Coupon annuity must be finite.");
        }

        return annuity;
    }

    public static double ParCouponRate(
        FixedRateBond bond,
        double price,
        BondPriceType priceType,
        DiscountCurve discountCurve,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate)
    {
        ArgumentNullException.ThrowIfNull(bond);
        ArgumentNullException.ThrowIfNull(discountCurve);

        if (!double.IsFinite(price))
            throw new ArgumentOutOfRangeException(nameof(price), "Bond price must be finite.");

        _ = priceType switch
        {
            BondPriceType.Clean or BondPriceType.Dirty => priceType,
            _ => throw new ArgumentOutOfRangeException(nameof(priceType), priceType, "Unsupported bond price type.")
        };

        var currentNotional = bond.CurrentNotional(settlementDate);
        if (currentNotional == 0.0)
            return 0.0;

        if (!UsesLinearSimpleCoupons(bond))
            return SolveParCouponRate(bond, price, priceType, discountCurve, referenceDate, dayCountConvention, settlementDate);

        var targetSettlementValue = price / 100.0 * currentNotional;
        var annuity = CouponAnnuity(bond, discountCurve, referenceDate, dayCountConvention, settlementDate);
        if (priceType == BondPriceType.Clean)
            annuity -= AccruedSettlementValuePerUnitRate(bond, settlementDate);

        if (annuity == 0.0)
            throw new InvalidOperationException("Cannot solve par coupon rate with zero coupon annuity.");

        var redemptionSettlementValue = RedemptionSettlementValue(bond, discountCurve, referenceDate, dayCountConvention, settlementDate);
        var parRate = (targetSettlementValue - redemptionSettlementValue) / annuity;
        if (!double.IsFinite(parRate))
            throw new ArgumentOutOfRangeException(nameof(price), "Par coupon rate must be finite.");

        return parRate;
    }

    public static CashFlowYieldResult Yield(
        FixedRateBond bond,
        double price,
        BondPriceType priceType,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        CompoundingConvention compounding,
        int frequency,
        DateOnly settlementDate,
        double lower = -0.95,
        double upper = 1.0,
        double absoluteTolerance = 1e-12,
        int maxIterations = 100)
    {
        ArgumentNullException.ThrowIfNull(bond);

        if (!double.IsFinite(price))
            throw new ArgumentOutOfRangeException(nameof(price), "Bond price must be finite.");

        var currentNotional = bond.CurrentNotional(settlementDate);
        if (currentNotional == 0.0)
        {
            return new CashFlowYieldResult(
                Converged: true,
                Yield: 0.0,
                NpvResidual: 0.0,
                Root: new Solvers.RootResult(
                    Converged: true,
                    Root: 0.0,
                    FunctionValue: 0.0,
                    Iterations: 0,
                    FunctionEvaluations: 0,
                    Lower: 0.0,
                    Upper: 0.0,
                    Status: Solvers.RootStatus.Converged));
        }

        var dirtyPrice = DirtyPriceFromQuotedPrice(bond, price, priceType, settlementDate);

        var targetSettlementValue = dirtyPrice / 100.0 * currentNotional;
        return CashFlowValuation.Yield(
            ActiveBondCashFlows(bond, settlementDate),
            targetSettlementValue,
            referenceDate,
            dayCountConvention,
            compounding,
            frequency,
            settlementDate,
            npvDate: settlementDate,
            includeSettlementDateFlows: false,
            lower: lower,
            upper: upper,
            absoluteTolerance: absoluteTolerance,
            maxIterations: maxIterations);
    }

    public static BondSpreadResult ContinuousZSpread(
        FixedRateBond bond,
        double price,
        BondPriceType priceType,
        DiscountCurve discountCurve,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        double lower = -0.10,
        double upper = 0.10,
        double absoluteTolerance = 1e-12,
        int maxIterations = 100)
    {
        ArgumentNullException.ThrowIfNull(bond);
        ArgumentNullException.ThrowIfNull(discountCurve);

        return ZSpread(
            bond,
            price,
            priceType,
            discountCurve,
            CompoundingConvention.Continuous,
            frequency: 1,
            referenceDate,
            dayCountConvention,
            settlementDate,
            lower,
            upper,
            absoluteTolerance,
            maxIterations);
    }

    public static BondSpreadResult ZSpread(
        FixedRateBond bond,
        double price,
        BondPriceType priceType,
        DiscountCurve discountCurve,
        CompoundingConvention spreadCompounding,
        int frequency,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate,
        double lower = -0.10,
        double upper = 0.10,
        double absoluteTolerance = 1e-12,
        int maxIterations = 100)
    {
        ArgumentNullException.ThrowIfNull(bond);
        ArgumentNullException.ThrowIfNull(discountCurve);

        if (!double.IsFinite(price))
            throw new ArgumentOutOfRangeException(nameof(price), "Bond price must be finite.");

        if (!double.IsFinite(lower) || !double.IsFinite(upper) || lower >= upper)
            throw new ArgumentOutOfRangeException(nameof(lower), "Spread bracket must be finite and ordered.");

        var currentNotional = bond.CurrentNotional(settlementDate);
        if (currentNotional == 0.0)
        {
            return new BondSpreadResult(
                Converged: true,
                Spread: 0.0,
                PriceResidual: 0.0,
                Root: new RootResult(
                    Converged: true,
                    Root: 0.0,
                    FunctionValue: 0.0,
                    Iterations: 0,
                    FunctionEvaluations: 0,
                    Lower: 0.0,
                    Upper: 0.0,
                    Status: RootStatus.Converged));
        }

        var targetDirtyPrice = DirtyPriceFromQuotedPrice(bond, price, priceType, settlementDate);

        double Objective(double spread) => DirtyPriceWithSpread(
            bond,
            discountCurve,
            spread,
            spreadCompounding,
            frequency,
            referenceDate,
            dayCountConvention,
            settlementDate) - targetDirtyPrice;

        var root = RootFinders.Brent(Objective, lower, upper, absoluteTolerance, maxIterations);
        return new BondSpreadResult(
            root.Converged,
            root.Converged ? root.Root : double.NaN,
            root.Converged ? root.FunctionValue : double.NaN,
            root);
    }

    private static double DirtyPriceFromQuotedPrice(
        FixedRateBond bond,
        double price,
        BondPriceType priceType,
        DateOnly settlementDate)
    {
        return priceType switch
        {
            BondPriceType.Clean => AddFinite(
                price,
                bond.AccruedAmount(settlementDate),
                nameof(price),
                "Dirty price must be finite."),
            BondPriceType.Dirty => price,
            _ => throw new ArgumentOutOfRangeException(nameof(priceType), priceType, "Unsupported bond price type.")
        };
    }

    private static double RedemptionSettlementValue(
        FixedRateBond bond,
        DiscountCurve discountCurve,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate)
    {
        if (!bond.IsTradable(settlementDate))
            return 0.0;

        var settlementTime = DayCounts.YearFraction(referenceDate, settlementDate, dayCountConvention).Value;
        var settlementDiscount = discountCurve.DiscountFactor(settlementTime);
        var value = 0.0;
        foreach (var redemption in bond.RedemptionCashFlows())
        {
            if (CashFlowLeg.HasOccurred(redemption.PaymentDate, settlementDate))
                continue;

            var redemptionTime = DayCounts.YearFraction(referenceDate, redemption.PaymentDate, dayCountConvention).Value;
            var contribution = redemption.Amount * discountCurve.DiscountFactor(redemptionTime) / settlementDiscount;
            value = AddFinite(value, contribution, nameof(bond), "Redemption settlement value must be finite.");
        }

        return value;
    }

    private static double AccruedSettlementValuePerUnitRate(FixedRateBond bond, DateOnly settlementDate)
    {
        var nextCoupon = bond.Coupons.NextCoupon(settlementDate);
        if (nextCoupon is null)
            return 0.0;

        return CouponRateDerivative(nextCoupon.Value, nextCoupon.Value.AccruedYearFraction(settlementDate));
    }

    private static double CouponRateDerivative(FixedRateCashFlow coupon, double time)
    {
        if (time == 0.0)
            return 0.0;

        var sign = time < 0.0 ? -1.0 : 1.0;
        var magnitude = Math.Abs(time);
        var derivative = coupon.Compounding switch
        {
            CompoundingConvention.Simple => magnitude,
            CompoundingConvention.Continuous => magnitude * Math.Exp(coupon.Rate * magnitude),
            CompoundingConvention.Compounded => CompoundedCouponRateDerivative(coupon, magnitude),
            CompoundingConvention.SimpleThenCompounded => magnitude <= 1.0 / coupon.Frequency
                ? magnitude
                : CompoundedCouponRateDerivative(coupon, magnitude),
            CompoundingConvention.CompoundedThenSimple => magnitude > 1.0 / coupon.Frequency
                ? magnitude
                : CompoundedCouponRateDerivative(coupon, magnitude),
            _ => throw new ArgumentOutOfRangeException(nameof(coupon), coupon.Compounding, "Unsupported coupon compounding convention.")
        };

        if (!double.IsFinite(derivative))
            throw new ArgumentOutOfRangeException(nameof(coupon), "Coupon rate derivative must be finite.");

        var result = sign * coupon.Nominal * derivative;
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(nameof(coupon), "Coupon rate derivative amount must be finite.");

        return result;
    }

    private static double CompoundedCouponRateDerivative(FixedRateCashFlow coupon, double time)
    {
        if (coupon.Frequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(coupon), "Compounded coupons require a positive frequency.");

        var derivative = time * Math.Pow(1.0 + coupon.Rate / coupon.Frequency, coupon.Frequency * time - 1.0);
        if (!double.IsFinite(derivative))
            throw new ArgumentOutOfRangeException(nameof(coupon), "Compounded coupon rate derivative must be finite.");

        return derivative;
    }

    private static double AddFinite(double left, double right, string parameterName, string message)
    {
        var result = left + right;
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(parameterName, message);

        return result;
    }

    private static double ScalePerHundredNotional(double amount, double currentNotional, string parameterName, string message)
    {
        var result = amount * 100.0 / currentNotional;
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(parameterName, message);

        return result;
    }

    private static bool UsesLinearSimpleCoupons(FixedRateBond bond)
    {
        foreach (var coupon in bond.Coupons.Coupons)
        {
            if (coupon.Compounding != CompoundingConvention.Simple)
                return false;
        }

        return true;
    }

    private static double SolveParCouponRate(
        FixedRateBond bond,
        double price,
        BondPriceType priceType,
        DiscountCurve discountCurve,
        DateOnly referenceDate,
        DayCountConvention dayCountConvention,
        DateOnly settlementDate)
    {
        double Objective(double couponRate)
        {
            var repriced = WithCouponRate(bond, couponRate);
            var modelPrice = priceType switch
            {
                BondPriceType.Clean => CleanPrice(repriced, discountCurve, referenceDate, dayCountConvention, settlementDate),
                BondPriceType.Dirty => DirtyPrice(repriced, discountCurve, referenceDate, dayCountConvention, settlementDate),
                _ => throw new ArgumentOutOfRangeException(nameof(priceType), priceType, "Unsupported bond price type.")
            };

            return modelPrice - price;
        }

        var lower = -0.95;
        var upper = 1.0;
        var root = RootFinders.Brent(Objective, lower, upper, absoluteTolerance: 1e-12, maxIterations: 100);
        if (!root.Converged)
            throw new InvalidOperationException("Par coupon rate solver failed to converge.");

        return root.Root;
    }

    private static FixedRateBond WithCouponRate(FixedRateBond bond, double couponRate)
    {
        var coupons = bond.Coupons.Coupons
            .Select(coupon => coupon with { Rate = couponRate })
            .ToArray();

        return new FixedRateBond(
            bond.FaceAmount,
            new FixedRateCashFlowLeg(coupons),
            bond.MaturityDate,
            bond.IssueDate,
            bond.RedemptionAmount);
    }

    private static CashFlowLeg ActiveBondCashFlows(
        FixedRateBond bond,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        var flows = new List<SimpleCashFlow>();
        foreach (var coupon in bond.Coupons.Coupons)
        {
            if (CashFlowLeg.HasOccurred(coupon.PaymentDate, settlementDate, includeSettlementDateFlows)
                || coupon.TradingExCoupon(settlementDate))
            {
                continue;
            }

            flows.Add(coupon.ToSimpleCashFlow());
        }

        foreach (var redemption in bond.RedemptionCashFlows())
        {
            if (!CashFlowLeg.HasOccurred(redemption.PaymentDate, settlementDate, includeSettlementDateFlows))
                flows.Add(redemption);
        }

        return new CashFlowLeg(flows);
    }
}
