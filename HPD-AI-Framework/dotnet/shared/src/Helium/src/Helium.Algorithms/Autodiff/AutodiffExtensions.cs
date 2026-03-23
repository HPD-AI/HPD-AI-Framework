using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// C# 14 extension members providing a functional API for autodiff.
///
/// Usage:
///   var g = loss.Grad()(params);           // 
///   var J = predict.Jacobian()(params);    // LinearMap with .Compose available
///   var H = loss.HessianExact()(params);   // BilinearForm with .IsSymmetric available
/// </summary>
public static class AutodiffExtensions
{
    extension<T>(Func<Var<T>, Var<T>> f) where T : IField<T>
    {
        /// <summary>Differentiates f once. Returns x ↦ f'(x).</summary>
        public Func<T, T> Grad() => x => Algorithms.Grad.Scalar(f, x);

        /// <summary>Returns x ↦ (f(x), f'(x)) in one pass.</summary>
        public Func<T, (T Value, T Grad)> ValueAndGrad() =>
            x => Algorithms.Grad.ValueAndGrad(f, x);

        /// <summary>Differentiates f twice. Returns x ↦ f''(x).</summary>
        public Func<T, T> Grad2() =>
            throw new NotSupportedException("Grad2 requires a forward-over-reverse implementation. Use Grad.HessianExact for second derivatives.");
    }

    extension<T>(Func<Vector<Var<T>>, Var<T>> f) where T : IField<T>
    {
        /// <summary>Returns x ↦ ∇f(x) as a Vector&lt;T&gt;.</summary>
        public Func<Vector<T>, Vector<T>> Grad() => x => Algorithms.Grad.Of(f, x);

        /// <summary>Returns x ↦ (f(x), ∇f(x)) in one pass.</summary>
        public Func<Vector<T>, (T Value, Vector<T> Grad)> ValueAndGrad() =>
            x => Algorithms.Grad.ValueAndGrad(f, x);
    }

    extension<T>(Func<Vector<Var<FormalPowerSeries<T>>>, Var<FormalPowerSeries<T>>> f)
        where T : IField<T>
    {
        /// <summary>
        /// Returns x ↦ Hessian of the underlying scalar function at x, as a BilinearForm&lt;T&gt;.
        /// Uses forward-over-reverse (exact, n passes).
        /// </summary>
        public Func<Vector<T>, BilinearForm<T>> HessianExact() =>
            x => Algorithms.Grad.HessianExact(f, x);
    }

    extension<T>(Func<Vector<Var<T>>, Vector<Var<T>>> f) where T : IField<T>
    {
        /// <summary>Returns x ↦ Jacobian of f at x as a LinearMap&lt;T&gt;.</summary>
        public Func<Vector<T>, LinearMap<T>> Jacobian() => x => Algorithms.Grad.Jacobian(f, x);
    }
}
