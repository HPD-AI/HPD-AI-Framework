using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Wengert-list (tape) for reverse-mode automatic differentiation.
/// Thread-local: one tape per thread, no locking.
///
/// Model: the tape maintains two parallel lists:
///   - A gradient array (one slot per Var), grown by AllocSlot().
///   - A list of backward closures, grown by PushClosure().
///
/// Each arithmetic op allocates one result slot (AllocSlot) and pushes one closure
/// (PushClosure) that reads grads[resultSlot] and accumulates into grads[inputSlots].
/// Input Var nodes also get slots (via AllocSlot) but push no closure — their gradients
/// accumulate directly from the closures of ops that use them.
///
/// Usage:
///   using var session = Tape&lt;T&gt;.Begin();
///   var x = new Var&lt;T&gt;(value);   // AllocSlot called here
///   var y = f(x);                  // ops push closures
///   var grads = session.Backward(y);
///   T dfdx = grads[x.Index];
/// </summary>
public sealed class Tape<T> where T : IField<T>
{
    [ThreadStatic]
    public static Tape<T>? Current;

    private int _slotCount;
    private readonly List<Action<T[]>> _closures = new();

    private Tape() { }

    /// <summary>
    /// Start a differentiation session. Sets Tape&lt;T&gt;.Current for this thread.
    /// Dispose to end the session.
    /// </summary>
    public static Session Begin()
    {
        if (Current is not null)
            throw new InvalidOperationException(
                "A Tape<T> session is already active on this thread. Nested sessions are not supported.");
        var tape = new Tape<T>();
        Current = tape;
        return new Session(tape);
    }

    /// <summary>
    /// Allocates a gradient slot for a new Var node. Returns the slot index.
    /// </summary>
    public int AllocSlot() => _slotCount++;

    /// <summary>
    /// Appends a backward closure. Closures are executed in reverse order during Backward.
    /// </summary>
    public void PushClosure(Action<T[]> closure) => _closures.Add(closure);

    // -------------------------------------------------------------------------

    /// <summary>
    /// RAII handle for a tape session. Dispose unsets Tape&lt;T&gt;.Current.
    /// </summary>
    public sealed class Session : IDisposable
    {
        private readonly Tape<T> _tape;
        private bool _disposed;

        internal Session(Tape<T> tape) => _tape = tape;

        /// <summary>
        /// Runs the backward pass from the given output variable.
        /// Returns the gradient array: grads[x.Index] is the gradient with respect to x.
        /// </summary>
        public T[] Backward(Var<T> output)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var grads = new T[_tape._slotCount];
            Array.Fill(grads, T.AdditiveIdentity);
            if (output.Index >= 0)
                grads[output.Index] = T.MultiplicativeIdentity;
            for (int i = _tape._closures.Count - 1; i >= 0; i--)
                _tape._closures[i](grads);
            return grads;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Current = null;
                _disposed = true;
            }
        }
    }
}
