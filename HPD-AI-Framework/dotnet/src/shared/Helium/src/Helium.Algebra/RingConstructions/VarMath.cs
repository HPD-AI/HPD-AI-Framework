using Helium.Primitives;
using Double = Helium.Primitives.Double;

namespace Helium.Algebra;

/// <summary>
/// Transcendental functions for Var&lt;Double&gt; and Var&lt;Complex&gt;.
/// These are not part of IField&lt;T&gt; — they are concrete extensions on float types.
/// Each function pushes the appropriate backward closure on the active tape.
/// </summary>
public static class VarMath
{
    extension(Var<Double> _)
    {
        public static Var<Double> Exp(Var<Double> x)
        {
            // d/dx exp(x) = exp(x)
            int xi = x.Index;
            double expVal = Math.Exp((double)x.Value);
            return Var<Double>.Op(new Double(expVal), (grads, ri) =>
            {
                var g = ri >= 0 ? (double)grads[ri] : 0.0;
                if (xi >= 0) grads[xi] = new Double((double)grads[xi] + g * expVal);
            });
        }

        public static Var<Double> Log(Var<Double> x)
        {
            // d/dx ln(x) = 1/x
            int xi = x.Index;
            double xv = (double)x.Value;
            double logVal = Math.Log(xv);
            return Var<Double>.Op(new Double(logVal), (grads, ri) =>
            {
                var g = ri >= 0 ? (double)grads[ri] : 0.0;
                if (xi >= 0) grads[xi] = new Double((double)grads[xi] + g / xv);
            });
        }

        public static Var<Double> Sin(Var<Double> x)
        {
            // d/dx sin(x) = cos(x)
            int xi = x.Index;
            double xv = (double)x.Value;
            double cosv = Math.Cos(xv);
            return Var<Double>.Op(new Double(Math.Sin(xv)), (grads, ri) =>
            {
                var g = ri >= 0 ? (double)grads[ri] : 0.0;
                if (xi >= 0) grads[xi] = new Double((double)grads[xi] + g * cosv);
            });
        }

        public static Var<Double> Cos(Var<Double> x)
        {
            // d/dx cos(x) = -sin(x)
            int xi = x.Index;
            double xv = (double)x.Value;
            double neg_sinv = -Math.Sin(xv);
            return Var<Double>.Op(new Double(Math.Cos(xv)), (grads, ri) =>
            {
                var g = ri >= 0 ? (double)grads[ri] : 0.0;
                if (xi >= 0) grads[xi] = new Double((double)grads[xi] + g * neg_sinv);
            });
        }

        public static Var<Double> Sqrt(Var<Double> x)
        {
            // d/dx sqrt(x) = 1 / (2 * sqrt(x))
            int xi = x.Index;
            double sqrtv = Math.Sqrt((double)x.Value);
            double denom = 2.0 * sqrtv; // 0 when x == 0: gradient is +∞, follow convention of returning 0
            return Var<Double>.Op(new Double(sqrtv), (grads, ri) =>
            {
                var g = ri >= 0 ? (double)grads[ri] : 0.0;
                if (xi >= 0 && denom != 0.0)
                    grads[xi] = new Double((double)grads[xi] + g / denom);
            });
        }
    }
}
