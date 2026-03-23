using System.Numerics;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Differentiable variable for reverse-mode automatic differentiation.
///
/// Var&lt;T&gt; is a class (not struct): gradient accumulation is mutable and multiple
/// tape nodes share gradient slots by index. The Value component is immutable.
///
/// Var&lt;T&gt; implements IField&lt;Var&lt;T&gt;&gt;, so every algorithm constrained to IField&lt;R&gt;
/// (LUDecomposition, LinearSolve, MatrixInverse, Determinant.ComputeOverField)
/// accepts Matrix&lt;Var&lt;T&gt;&gt; without modification. Gradients through Gaussian
/// elimination and LU factorization are obtained for free.
///
/// Equality and IsZero are value-based (tape index ignored), so pivot detection in
/// LU and linear solvers behaves identically to the unwrapped type.
///
/// Scope: dense field algorithms (Matrix, Vector). Not sound for Polynomial/MvPolynomial
/// because Finsupp drops zero-primal entries that may carry active gradients.
/// </summary>
public sealed class Var<T> : IField<Var<T>> where T : IField<T>
{
    /// <summary>Primal value. Immutable.</summary>
    public T Value { get; }

    /// <summary>
    /// Index into the tape's gradient array. -1 for constants (no tape node).
    /// </summary>
    public int Index { get; }

    private Var(T value, int index)
    {
        Value = value;
        Index = index;
    }

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates an input variable. If a tape is active, allocates a gradient slot.
    /// If no tape is active, creates a constant (Index = -1).
    /// </summary>
    public Var(T value)
    {
        Value = value;
        var tape = Tape<T>.Current;
        Index = tape is not null ? tape.AllocSlot() : -1;
    }

    /// <summary>
    /// Creates a constant node (Index = -1) regardless of whether a tape is active.
    /// Used when an operation has no active inputs and its output carries no gradient.
    /// </summary>
    public static Var<T> Constant(T value) => new(value, -1);

    // -------------------------------------------------------------------------
    // Arithmetic — each operation allocates one result slot and pushes one closure.
    // Closures capture indices and value snapshots only (not Var<T> references).
    // -------------------------------------------------------------------------

    public static Var<T> operator +(Var<T> a, Var<T> b)
    {
        // ∂/∂a = 1, ∂/∂b = 1
        int ai = a.Index, bi = b.Index;
        if (ai < 0 && bi < 0) return new Var<T>(a.Value + b.Value, -1);
        return Op(a.Value + b.Value, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : T.AdditiveIdentity;
            if (ai >= 0) grads[ai] = grads[ai] + g;
            if (bi >= 0) grads[bi] = grads[bi] + g;
        });
    }

    public static Var<T> operator -(Var<T> a, Var<T> b)
    {
        // ∂/∂a = 1, ∂/∂b = -1
        int ai = a.Index, bi = b.Index;
        if (ai < 0 && bi < 0) return new Var<T>(a.Value - b.Value, -1);
        return Op(a.Value - b.Value, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : T.AdditiveIdentity;
            if (ai >= 0) grads[ai] = grads[ai] + g;
            if (bi >= 0) grads[bi] = grads[bi] - g;
        });
    }

    public static Var<T> operator *(Var<T> a, Var<T> b)
    {
        // ∂/∂a = b.Value, ∂/∂b = a.Value  — capture value snapshots
        int ai = a.Index, bi = b.Index;
        T av = a.Value, bv = b.Value;
        if (ai < 0 && bi < 0) return new Var<T>(av * bv, -1);
        return Op(av * bv, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : T.AdditiveIdentity;
            if (ai >= 0) grads[ai] = grads[ai] + g * bv;
            if (bi >= 0) grads[bi] = grads[bi] + g * av;
        });
    }

    public static Var<T> operator -(Var<T> a)
    {
        // ∂/∂a = -1
        int ai = a.Index;
        if (ai < 0) return new Var<T>(-a.Value, -1);
        return Op(-a.Value, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : T.AdditiveIdentity;
            if (ai >= 0) grads[ai] = grads[ai] - g;
        });
    }

    // Division is derived from field axioms: a / b = a * Invert(b)
    public static Var<T> operator /(Var<T> a, Var<T> b) => a * Invert(b);

    /// <summary>
    /// Multiplicative inverse. Invert(0) = 0 (total function convention).
    /// ∂/∂a of (1/a) = -1/a².
    /// </summary>
    public static Var<T> Invert(Var<T> a)
    {
        int ai = a.Index;
        var inv = T.Invert(a.Value);
        if (ai < 0) return new Var<T>(inv, -1);
        T neg_inv_sq = -(inv * inv);
        return Op(inv, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : T.AdditiveIdentity;
            if (ai >= 0) grads[ai] = grads[ai] + g * neg_inv_sq;
        });
    }

    // -------------------------------------------------------------------------
    // IField<Var<T>> static identity members.
    // Constants (Index = -1) have zero gradient, which is mathematically correct.
    // -------------------------------------------------------------------------

    public static Var<T> AdditiveIdentity      => new(T.AdditiveIdentity, -1);
    public static Var<T> MultiplicativeIdentity => new(T.MultiplicativeIdentity, -1);

    static Var<T> IAdditiveIdentity<Var<T>, Var<T>>.AdditiveIdentity        => AdditiveIdentity;
    static Var<T> IMultiplicativeIdentity<Var<T>, Var<T>>.MultiplicativeIdentity => MultiplicativeIdentity;

    // IRing.FromInt — constant, no tape node
    static Var<T> IRing<Var<T>>.FromInt(int n) => new(T.FromInt(n), -1);

    // -------------------------------------------------------------------------
    // Equality: value-based. Tape index is not part of equality.
    // Required so pivot detection in LU/Gauss works on primal values.
    // -------------------------------------------------------------------------

    public bool Equals(Var<T>? other) => other is not null && Value.Equals(other.Value);
    public override bool Equals(object? obj) => obj is Var<T> other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(Var<T> a, Var<T> b) => a.Equals(b);
    public static bool operator !=(Var<T> a, Var<T> b) => !a.Equals(b);

    public bool IsZero => Value.Equals(T.AdditiveIdentity);

    public override string ToString() => Value.ToString() ?? "";

    // -------------------------------------------------------------------------
    // Core helper: allocate result slot, push closure, return result node.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Allocates a result gradient slot on the active tape, pushes a backward closure,
    /// and returns the result Var. If no tape is active, returns a constant node.
    /// The closure receives (grads, resultIndex) so it can read grads[resultIndex].
    /// </summary>
    public static Var<T> Op(T value, Action<T[], int> backward)
    {
        var tape = Tape<T>.Current;
        if (tape is null)
            return new Var<T>(value, -1);
        int ri = tape.AllocSlot();
        tape.PushClosure(grads => backward(grads, ri));
        return new Var<T>(value, ri);
    }
}
