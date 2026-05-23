namespace Helium.Validated.Autodiff;

internal sealed class ValidatedTape
{
    [ThreadStatic]
    public static ValidatedTape? Current;

    private int _slotCount;
    private readonly List<Action<Interval[]>> _closures = new();

    private ValidatedTape() { }

    public static Session Begin()
    {
        if (Current is not null)
            throw new InvalidOperationException("A validated autodiff session is already active on this thread.");

        var tape = new ValidatedTape();
        Current = tape;
        return new Session(tape);
    }

    public int AllocSlot() => _slotCount++;
    public void PushClosure(Action<Interval[]> closure) => _closures.Add(closure);

    public sealed class Session : IDisposable
    {
        private readonly ValidatedTape _tape;
        private bool _disposed;

        public Session(ValidatedTape tape) => _tape = tape;

        public Interval[] Backward(IntervalVar output)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var grads = new Interval[_tape._slotCount];
            Array.Fill(grads, Interval.Point(0.0));
            if (output.Index >= 0)
                grads[output.Index] = Interval.Point(1.0);

            for (var i = _tape._closures.Count - 1; i >= 0; i--)
                _tape._closures[i](grads);

            return grads;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Current = null;
            _disposed = true;
        }
    }
}
