namespace Helium.Validated.Autodiff;

public static class ValidatedVarMath
{
    public static IntervalVar Exp(IntervalVar x)
    {
        var xi = x.Index;
        var value = IntervalMath.Exp(x.Value);
        return IntervalVar.Op(value, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : Interval.Point(0.0);
            if (xi >= 0) grads[xi] = grads[xi] + g * value;
        });
    }

    public static IntervalVar Log(IntervalVar x)
    {
        var xi = x.Index;
        var value = IntervalMath.Log(x.Value);
        var derivative = Interval.Divide(Interval.Point(1.0), x.Value);
        return IntervalVar.Op(value, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : Interval.Point(0.0);
            if (xi >= 0) grads[xi] = grads[xi] + g * derivative;
        });
    }

    public static IntervalVar Sin(IntervalVar x)
    {
        var xi = x.Index;
        var value = IntervalMath.Sin(x.Value);
        var derivative = IntervalMath.Cos(x.Value);
        return IntervalVar.Op(value, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : Interval.Point(0.0);
            if (xi >= 0) grads[xi] = grads[xi] + g * derivative;
        });
    }

    public static IntervalVar Cos(IntervalVar x)
    {
        var xi = x.Index;
        var value = IntervalMath.Cos(x.Value);
        var derivative = -IntervalMath.Sin(x.Value);
        return IntervalVar.Op(value, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : Interval.Point(0.0);
            if (xi >= 0) grads[xi] = grads[xi] + g * derivative;
        });
    }

    public static IntervalVar Sqrt(IntervalVar x)
    {
        var xi = x.Index;
        var value = IntervalMath.Sqrt(x.Value);
        var derivative = Interval.Divide(Interval.Point(1.0), Interval.Point(2.0) * value);
        return IntervalVar.Op(value, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : Interval.Point(0.0);
            if (xi >= 0) grads[xi] = grads[xi] + g * derivative;
        });
    }
}
