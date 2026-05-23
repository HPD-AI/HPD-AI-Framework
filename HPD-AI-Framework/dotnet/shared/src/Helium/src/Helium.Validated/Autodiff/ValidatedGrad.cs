namespace Helium.Validated.Autodiff;

public static class ValidatedGrad
{
    public static Interval Grad(Func<IntervalVar, IntervalVar> f, Interval x) =>
        ValueAndGrad(f, x).Grad;

    public static (Interval Value, Interval Grad) ValueAndGrad(Func<IntervalVar, IntervalVar> f, Interval x)
    {
        using var session = ValidatedTape.Begin();
        var xv = new IntervalVar(x);
        var y = f(xv);
        var grads = session.Backward(y);
        var grad = xv.Index >= 0 ? grads[xv.Index] : Interval.Point(0.0);
        return (y.Value, grad);
    }

    public static (Interval Value, IReadOnlyList<Interval> Grad) ValueAndGrad(
        Func<IReadOnlyList<IntervalVar>, IntervalVar> f,
        IReadOnlyList<Interval> x)
    {
        using var session = ValidatedTape.Begin();
        var vars = x.Select(v => new IntervalVar(v)).ToArray();
        var y = f(vars);
        var grads = session.Backward(y);
        var result = new Interval[vars.Length];
        for (var i = 0; i < vars.Length; i++)
            result[i] = vars[i].Index >= 0 ? grads[vars[i].Index] : Interval.Point(0.0);
        return (y.Value, result);
    }
}
