namespace Helium.Validated.Autodiff;

public sealed class IntervalVar
{
    public Interval Value { get; }
    internal int Index { get; }

    public IntervalVar(Interval value)
    {
        Value = value;
        Index = ValidatedTape.Current?.AllocSlot() ?? -1;
    }

    private IntervalVar(Interval value, int index)
    {
        Value = value;
        Index = index;
    }

    public static IntervalVar Constant(Interval value) => new(value, -1);

    public static IntervalVar operator +(IntervalVar a, IntervalVar b)
    {
        var ai = a.Index;
        var bi = b.Index;
        return Op(a.Value + b.Value, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : Interval.Point(0.0);
            if (ai >= 0) grads[ai] = grads[ai] + g;
            if (bi >= 0) grads[bi] = grads[bi] + g;
        });
    }

    public static IntervalVar operator -(IntervalVar a, IntervalVar b)
    {
        var ai = a.Index;
        var bi = b.Index;
        return Op(a.Value - b.Value, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : Interval.Point(0.0);
            if (ai >= 0) grads[ai] = grads[ai] + g;
            if (bi >= 0) grads[bi] = grads[bi] - g;
        });
    }

    public static IntervalVar operator -(IntervalVar a)
    {
        var ai = a.Index;
        return Op(-a.Value, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : Interval.Point(0.0);
            if (ai >= 0) grads[ai] = grads[ai] - g;
        });
    }

    public static IntervalVar operator *(IntervalVar a, IntervalVar b)
    {
        var ai = a.Index;
        var bi = b.Index;
        var av = a.Value;
        var bv = b.Value;
        return Op(av * bv, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : Interval.Point(0.0);
            if (ai >= 0) grads[ai] = grads[ai] + g * bv;
            if (bi >= 0) grads[bi] = grads[bi] + g * av;
        });
    }

    public static IntervalVar Divide(IntervalVar a, IntervalVar b)
    {
        var ai = a.Index;
        var bi = b.Index;
        var av = a.Value;
        var bv = b.Value;
        var result = Interval.Divide(av, bv);
        var invB = Interval.Divide(Interval.Point(1.0), bv);
        var negAOverBSq = -(av * invB * invB);

        return Op(result, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : Interval.Point(0.0);
            if (ai >= 0) grads[ai] = grads[ai] + g * invB;
            if (bi >= 0) grads[bi] = grads[bi] + g * negAOverBSq;
        });
    }

    internal static IntervalVar Op(Interval value, Action<Interval[], int> backward)
    {
        var tape = ValidatedTape.Current;
        if (tape is null)
            return new IntervalVar(value, -1);

        var ri = tape.AllocSlot();
        tape.PushClosure(grads => backward(grads, ri));
        return new IntervalVar(value, ri);
    }
}
