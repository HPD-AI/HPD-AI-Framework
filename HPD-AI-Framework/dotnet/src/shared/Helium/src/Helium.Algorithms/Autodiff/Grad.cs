using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// Static gradient computation methods using reverse-mode autodiff (Tape/Var).
///
/// - Scalar:   f : T → T         → T              gradient at a point
/// - Of:       f : R^n → T       → Vector&lt;T&gt;      gradient vector
/// - Jacobian: f : R^n → R^m    → LinearMap&lt;T&gt;   Jacobian via m reverse-mode passes
/// - Hessian:  f : R^n → T      → BilinearForm&lt;T&gt; Hessian via n forward-over-reverse passes
/// </summary>
public static class Grad
{
    /// <summary>
    /// Gradient of a scalar-to-scalar function. Returns df/dx at x.
    /// </summary>
    public static T Scalar<T>(Func<Var<T>, Var<T>> f, T x)
        where T : IField<T>
    {
        using var session = Tape<T>.Begin();
        var xv = new Var<T>(x);
        var y = f(xv);
        var grads = session.Backward(y);
        return xv.Index >= 0 ? grads[xv.Index] : T.AdditiveIdentity;
    }

    /// <summary>
    /// Gradient of a vector-to-scalar function. Returns ∇f(x) as a Vector&lt;T&gt;.
    /// </summary>
    public static Vector<T> Of<T>(Func<Vector<Var<T>>, Var<T>> f, Vector<T> x)
        where T : IField<T>
    {
        using var session = Tape<T>.Begin();
        var xvars = MakeInputs(x);
        var y = f(Vector<Var<T>>.FromArray(xvars));
        var grads = session.Backward(y);
        return ReadGrads(xvars, grads);
    }

    /// <summary>
    /// Returns (f(x), f'(x)) in a single forward+backward pass.
    /// </summary>
    public static (T Value, T Grad) ValueAndGrad<T>(Func<Var<T>, Var<T>> f, T x)
        where T : IField<T>
    {
        using var session = Tape<T>.Begin();
        var xv = new Var<T>(x);
        var y = f(xv);
        var grads = session.Backward(y);
        var grad = xv.Index >= 0 ? grads[xv.Index] : T.AdditiveIdentity;
        return (y.Value, grad);
    }

    /// <summary>
    /// Returns (f(x), ∇f(x)) in a single forward+backward pass.
    /// </summary>
    public static (T Value, Vector<T> Grad) ValueAndGrad<T>(
        Func<Vector<Var<T>>, Var<T>> f, Vector<T> x)
        where T : IField<T>
    {
        using var session = Tape<T>.Begin();
        var xvars = MakeInputs(x);
        var y = f(Vector<Var<T>>.FromArray(xvars));
        var grads = session.Backward(y);
        return (y.Value, ReadGrads(xvars, grads));
    }

    /// <summary>
    /// Jacobian of a vector-to-vector function, returned as a LinearMap&lt;T&gt;.
    /// Computed via m reverse-mode passes (one per output component).
    /// J[i, j] = ∂f_i/∂x_j.
    /// </summary>
    public static LinearMap<T> Jacobian<T>(Func<Vector<Var<T>>, Vector<Var<T>>> f, Vector<T> x)
        where T : IField<T>
    {
        int n = x.Length;

        // Determine output dimension with a probe pass.
        int m;
        {
            using var session = Tape<T>.Begin();
            var probe = MakeInputs(x);
            m = f(Vector<Var<T>>.FromArray(probe)).Length;
        }

        var rows = new T[m][];
        for (int i = 0; i < m; i++)
        {
            using var session = Tape<T>.Begin();
            var xvars = MakeInputs(x);
            var yvars = f(Vector<Var<T>>.FromArray(xvars));
            var grads = session.Backward(yvars[i]);
            rows[i] = ReadGradsToArray(xvars, grads);
        }

        var flat = new T[m * n];
        for (int i = 0; i < m; i++)
            for (int j = 0; j < n; j++)
                flat[i * n + j] = rows[i][j];

        return LinearMap<T>.FromMatrix(Matrix<T>.FromArray(m, n, flat));
    }

    /// <summary>
    /// Hessian of a vector-to-scalar function, returned as a BilinearForm&lt;T&gt;.
    /// H[i, j] = ∂²f/∂x_i∂x_j.
    ///
    /// Computed via forward-over-reverse: for each direction j, perturb x[j] by ε
    /// using FormalPowerSeries&lt;T&gt;, evaluate the gradient Of&lt;FPS&lt;T&gt;&gt; at the dual point,
    /// and read the ε coefficient to obtain column j of H.
    ///
    /// This requires the caller to supply a function that is polymorphic over the ring
    /// parameter via <see cref="HessianExact{T}"/>. The present overload accepts
    /// Func&lt;Vector&lt;Var&lt;T&gt;&gt;, Var&lt;T&gt;&gt; and cannot be lifted to FPS without caller
    /// cooperation; it returns the zero Hessian and throws if called directly.
    /// Use <see cref="HessianExact{T}"/> for exact computation.
    /// </summary>
    public static BilinearForm<T> Hessian<T>(Func<Vector<Var<T>>, Var<T>> f, Vector<T> x)
        where T : IField<T>
    {
        throw new NotSupportedException(
            "Exact Hessian requires a polymorphic function parameter. Use Grad.HessianExact instead.");
    }

    /// <summary>
    /// Exact Hessian via forward-over-reverse.
    /// The function <paramref name="f"/> must be expressed using only IField&lt;R&gt; operations
    /// so it can be instantiated at R = Var&lt;FPS&lt;T&gt;&gt;.
    /// H[i, j] = ∂²f/∂x_i∂x_j, computed in n passes (one per direction j).
    /// </summary>
    public static BilinearForm<T> HessianExact<T>(
        Func<Vector<Var<FormalPowerSeries<T>>>, Var<FormalPowerSeries<T>>> f,
        Vector<T> x)
        where T : IField<T>
    {
        int n = x.Length;
        var hFlat = new T[n * n];

        for (int j = 0; j < n; j++)
        {
            // Build FPS-valued input: x perturbed in direction j by ε.
            // dual_x[j] has coefficient 0 = x[j], coefficient 1 = 1.
            // All other inputs are constants.
            var dualX = BuildDualFpsVector(x, j);

            // Evaluate ∇f at the dual point using reverse mode over FPS<T>.
            // Of returns Vector<FPS<T>>.  Coefficient 1 of each component = H[*, j].
            var grad_j = Of(f, dualX);
            for (int i = 0; i < n; i++)
                hFlat[i * n + j] = grad_j[i].Coefficient(1);
        }

        return BilinearForm<T>.FromGramMatrix(Matrix<T>.FromArray(n, n, hFlat));
    }

    /// <summary>
    /// Creates a Var&lt;T&gt; node with a user-supplied backward rule.
    /// The backward function receives the output cotangent and returns
    /// cotangents for each input, in the same order as <paramref name="inputs"/>.
    ///
    /// This enables efficient custom VJPs (e.g., IFT gradients through LinearSolve)
    /// that replace O(n²) tape entries with a single solve.
    ///
    /// If no inputs are active (all Index == -1) or no tape is active, returns a constant.
    /// </summary>
    public static Var<T> CustomVjp<T>(T primalValue, Var<T>[] inputs, Func<T, T[]> backward)
        where T : IField<T>
    {
        if (inputs.All(v => v.Index < 0) || Tape<T>.Current is null)
            return Var<T>.Constant(primalValue);

        int[] inputIndices = inputs.Select(v => v.Index).ToArray();
        return Var<T>.Op(primalValue, (grads, ri) =>
        {
            var g = ri >= 0 ? grads[ri] : T.AdditiveIdentity;
            var cts = backward(g);
            for (int i = 0; i < inputIndices.Length; i++)
                if (inputIndices[i] >= 0)
                    grads[inputIndices[i]] = grads[inputIndices[i]] + cts[i];
        });
    }

    /// <summary>
    /// Evaluates segment(input) on the active tape without storing the segment's internal
    /// closures. During the backward pass, the segment is re-executed from scratch on a
    /// fresh inner tape to recompute intermediates, then differentiated inline.
    ///
    /// Memory: O(1) per checkpoint node instead of O(segment length).
    /// Compute: segment is evaluated twice — once forward, once during backward.
    ///
    /// The segment function must be re-entrant (no external mutable state).
    /// </summary>
    public static Var<T> Checkpoint<T>(Var<T> input, Func<Var<T>, Var<T>> segment)
        where T : IField<T>
    {
        if (Tape<T>.Current is null)
            return segment(input);

        // Primal forward: suspend outer tape so segment ops don't pollute it.
        var outer = Tape<T>.Current;
        T primalOutput;
        Tape<T>.Current = null;
        try   { primalOutput = segment(Var<T>.Constant(input.Value)).Value; }
        finally { Tape<T>.Current = outer; }

        T inputValue = input.Value;
        return CustomVjp(primalOutput, [input], g =>
        {
            var saved = Tape<T>.Current;
            Tape<T>.Current = null;
            try
            {
                using var inner = Tape<T>.Begin();
                var innerInput  = new Var<T>(inputValue);
                var innerOutput = segment(innerInput);
                var innerGrads  = inner.Backward(innerOutput);
                var localGrad   = innerInput.Index >= 0
                    ? innerGrads[innerInput.Index] : T.AdditiveIdentity;
                return [g * localGrad];
            }
            finally { Tape<T>.Current = saved; }
        });
    }

    /// <summary>
    /// Vector-input form of Checkpoint. Evaluates segment(inputs) storing only one
    /// meta-closure. During backward, re-runs the segment on a fresh inner tape and
    /// accumulates per-input cotangents via the chain rule.
    /// </summary>
    public static Var<T> Checkpoint<T>(Vector<Var<T>> inputs, Func<Vector<Var<T>>, Var<T>> segment)
        where T : IField<T>
    {
        if (Tape<T>.Current is null)
            return segment(inputs);

        int n = inputs.Length;

        // Primal forward: suspend outer tape.
        var outer = Tape<T>.Current;
        T primalOutput;
        Tape<T>.Current = null;
        try
        {
            var probes = new Var<T>[n];
            for (int i = 0; i < n; i++)
                probes[i] = Var<T>.Constant(inputs[i].Value);
            primalOutput = segment(Vector<Var<T>>.FromArray(probes)).Value;
        }
        finally { Tape<T>.Current = outer; }

        // Snapshot primal values for re-instantiation inside the backward closure.
        var inputValues = new T[n];
        for (int i = 0; i < n; i++)
            inputValues[i] = inputs[i].Value;

        var inputsArr = new Var<T>[n];
        for (int i = 0; i < n; i++)
            inputsArr[i] = inputs[i];

        return CustomVjp(primalOutput, inputsArr, g =>
        {
            var saved = Tape<T>.Current;
            Tape<T>.Current = null;
            try
            {
                using var inner = Tape<T>.Begin();
                var innerInputs = new Var<T>[n];
                for (int i = 0; i < n; i++)
                    innerInputs[i] = new Var<T>(inputValues[i]);

                var innerOutput = segment(Vector<Var<T>>.FromArray(innerInputs));
                var innerGrads  = inner.Backward(innerOutput);

                var cotangents = new T[n];
                for (int i = 0; i < n; i++)
                    cotangents[i] = innerInputs[i].Index >= 0
                        ? g * innerGrads[innerInputs[i].Index]
                        : T.AdditiveIdentity;
                return cotangents;
            }
            finally { Tape<T>.Current = saved; }
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Var<T>[] MakeInputs<T>(Vector<T> x) where T : IField<T>
    {
        var vars = new Var<T>[x.Length];
        for (int i = 0; i < x.Length; i++)
            vars[i] = new Var<T>(x[i]);
        return vars;
    }

    private static Vector<T> ReadGrads<T>(Var<T>[] xvars, T[] grads) where T : IField<T>
    {
        var result = new T[xvars.Length];
        for (int i = 0; i < xvars.Length; i++)
            result[i] = xvars[i].Index >= 0 ? grads[xvars[i].Index] : T.AdditiveIdentity;
        return Vector<T>.FromArray(result);
    }

    private static T[] ReadGradsToArray<T>(Var<T>[] xvars, T[] grads) where T : IField<T>
    {
        var result = new T[xvars.Length];
        for (int i = 0; i < xvars.Length; i++)
            result[i] = xvars[i].Index >= 0 ? grads[xvars[i].Index] : T.AdditiveIdentity;
        return result;
    }

    private static Vector<FormalPowerSeries<T>> BuildDualFpsVector<T>(Vector<T> x, int j)
        where T : IField<T>
    {
        var fps = new FormalPowerSeries<T>[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            T xi = x[i];
            fps[i] = i == j
                ? FormalPowerSeries<T>.FromGenerator(n =>
                    n == 0 ? xi :
                    n == 1 ? T.MultiplicativeIdentity :
                             T.AdditiveIdentity)
                : FormalPowerSeries<T>.Constant(xi);
        }
        return Vector<FormalPowerSeries<T>>.FromArray(fps);
    }
}
