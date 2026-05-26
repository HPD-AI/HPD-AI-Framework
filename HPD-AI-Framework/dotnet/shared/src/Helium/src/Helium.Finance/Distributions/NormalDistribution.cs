namespace Helium.Finance.Distributions;

public static class NormalDistribution
{
    private const double InvSqrtTwoPi = 0.39894228040143267793994605993438;
    private const double InvSqrtTwo = 0.70710678118654752440084436210485;

    public static double Pdf(double x)
    {
        if (double.IsNaN(x))
            return double.NaN;

        if (double.IsInfinity(x))
            return 0.0;

        return InvSqrtTwoPi * Math.Exp(-0.5 * x * x);
    }

    public static double PdfDerivative(double x)
    {
        if (double.IsNaN(x))
            return double.NaN;

        if (double.IsInfinity(x))
            return 0.0;

        return -x * Pdf(x);
    }

    public static double Cdf(double x)
    {
        if (double.IsNaN(x))
            return double.NaN;

        if (x == double.PositiveInfinity)
            return 1.0;

        if (x == double.NegativeInfinity)
            return 0.0;

        if (x < -6.0)
            return LowerTailCdf(x);

        if (x > 6.0)
            return 1.0 - LowerTailCdf(-x);

        return 0.5 * (1.0 + Erf(x * InvSqrtTwo));
    }

    public static double CdfDerivative(double x) => Pdf(x);

    public static double InverseCdf(double probability)
    {
        if (double.IsNaN(probability) || probability <= 0.0 || probability >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(probability), "Probability must be strictly between 0 and 1.");

        var x = AcklamInverseCdf(probability);

        // One Halley refinement step against the actual CDF/PDF keeps the public result tied
        // to this implementation's CDF, not just the rational starting approximation.
        var error = Cdf(x) - probability;
        var density = Pdf(x);
        var correction = error / (density + 0.5 * x * error);
        var refined = x - correction;
        return double.IsFinite(refined) ? refined : x;
    }

    private static double AcklamInverseCdf(double p)
    {
        ReadOnlySpan<double> a =
        [
            -3.969683028665376e+01,
             2.209460984245205e+02,
            -2.759285104469687e+02,
             1.383577518672690e+02,
            -3.066479806614716e+01,
             2.506628277459239e+00
        ];

        ReadOnlySpan<double> b =
        [
            -5.447609879822406e+01,
             1.615858368580409e+02,
            -1.556989798598866e+02,
             6.680131188771972e+01,
            -1.328068155288572e+01
        ];

        ReadOnlySpan<double> c =
        [
            -7.784894002430293e-03,
            -3.223964580411365e-01,
            -2.400758277161838e+00,
            -2.549732539343734e+00,
             4.374664141464968e+00,
             2.938163982698783e+00
        ];

        ReadOnlySpan<double> d =
        [
             7.784695709041462e-03,
             3.224671290700398e-01,
             2.445134137142996e+00,
             3.754408661907416e+00
        ];

        const double lower = 0.02425;
        const double upper = 1.0 - lower;

        if (p < lower)
        {
            var q = Math.Sqrt(-2.0 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0);
        }

        if (p <= upper)
        {
            var q = p - 0.5;
            var r = q * q;
            return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q
                / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1.0);
        }

        {
            var q = Math.Sqrt(-2.0 * Math.Log(1.0 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0);
        }
    }

    private static double LowerTailCdf(double z)
    {
        var density = Pdf(z);
        if (density == 0.0)
            return 0.0;

        var sum = 1.0;
        var zSquared = z * z;
        var i = 1.0;
        var g = 1.0;
        var previousAbsTerm = double.PositiveInfinity;

        while (true)
        {
            var x = (4.0 * i - 3.0) / zSquared;
            var y = x * ((4.0 * i - 1.0) / zSquared);
            var term = g * (x - y);
            sum -= term;
            g *= y;
            i++;

            var absTerm = Math.Abs(term);
            if (previousAbsTerm <= absTerm || absTerm < Math.Abs(sum * double.Epsilon))
                break;

            previousAbsTerm = absTerm;
        }

        var result = -density / z * sum;
        return double.IsFinite(result) && result > 0.0 ? result : 0.0;
    }

    private static double Erf(double x)
    {
        if (double.IsNaN(x))
            return double.NaN;

        if (double.IsInfinity(x))
            return x > 0.0 ? 1.0 : -1.0;

        const double tiny = double.Epsilon;
        const double one = 1.0;
        const double erx = 8.45062911510467529297e-01;
        const double efx = 1.28379167095512586316e-01;
        const double efx8 = 1.02703333676410069053e+00;
        const double pp0 = 1.28379167095512558561e-01;
        const double pp1 = -3.25042107247001499370e-01;
        const double pp2 = -2.84817495755985104766e-02;
        const double pp3 = -5.77027029648944159157e-03;
        const double pp4 = -2.37630166566501626084e-05;
        const double qq1 = 3.97917223959155352819e-01;
        const double qq2 = 6.50222499887672944485e-02;
        const double qq3 = 5.08130628187576562776e-03;
        const double qq4 = 1.32494738004321644526e-04;
        const double qq5 = -3.96022827877536812320e-06;
        const double pa0 = -2.36211856075265944077e-03;
        const double pa1 = 4.14856118683748331666e-01;
        const double pa2 = -3.72207876035701323847e-01;
        const double pa3 = 3.18346619901161753674e-01;
        const double pa4 = -1.10894694282396677476e-01;
        const double pa5 = 3.54783043256182359371e-02;
        const double pa6 = -2.16637559486879084300e-03;
        const double qa1 = 1.06420880400844228286e-01;
        const double qa2 = 5.40397917702171048937e-01;
        const double qa3 = 7.18286544141962662868e-02;
        const double qa4 = 1.26171219808761642112e-01;
        const double qa5 = 1.36370839120290507362e-02;
        const double qa6 = 1.19844998467991074170e-02;
        const double ra0 = -9.86494403484714822705e-03;
        const double ra1 = -6.93858572707181764372e-01;
        const double ra2 = -1.05586262253232909814e+01;
        const double ra3 = -6.23753324503260060396e+01;
        const double ra4 = -1.62396669462573470355e+02;
        const double ra5 = -1.84605092906711035994e+02;
        const double ra6 = -8.12874355063065934246e+01;
        const double ra7 = -9.81432934416914548592e+00;
        const double sa1 = 1.96512716674392571292e+01;
        const double sa2 = 1.37657754143519042600e+02;
        const double sa3 = 4.34565877475229228821e+02;
        const double sa4 = 6.45387271733267880336e+02;
        const double sa5 = 4.29008140027567833386e+02;
        const double sa6 = 1.08635005541779435134e+02;
        const double sa7 = 6.57024977031928170135e+00;
        const double sa8 = -6.04244152148580987438e-02;
        const double rb0 = -9.86494292470009928597e-03;
        const double rb1 = -7.99283237680523006574e-01;
        const double rb2 = -1.77579549177547519889e+01;
        const double rb3 = -1.60636384855821916062e+02;
        const double rb4 = -6.37566443368389627722e+02;
        const double rb5 = -1.02509513161107724954e+03;
        const double rb6 = -4.83519191608651397019e+02;
        const double sb1 = 3.03380607434824582924e+01;
        const double sb2 = 3.25792512996573918826e+02;
        const double sb3 = 1.53672958608443695994e+03;
        const double sb4 = 3.19985821950859553908e+03;
        const double sb5 = 2.55305040643316442583e+03;
        const double sb6 = 4.74528541206955367215e+02;
        const double sb7 = -2.24409524465858183362e+01;

        var ax = Math.Abs(x);

        if (ax < 0.84375)
        {
            if (ax < 3.7252902984e-09)
                return ax < double.MinValue * 16.0 ? 0.125 * (8.0 * x + efx8 * x) : x + efx * x;

            var z = x * x;
            var r = pp0 + z * (pp1 + z * (pp2 + z * (pp3 + z * pp4)));
            var s = one + z * (qq1 + z * (qq2 + z * (qq3 + z * (qq4 + z * qq5))));
            return x + x * r / s;
        }

        if (ax < 1.25)
        {
            var s = ax - one;
            var p = pa0 + s * (pa1 + s * (pa2 + s * (pa3 + s * (pa4 + s * (pa5 + s * pa6)))));
            var q = one + s * (qa1 + s * (qa2 + s * (qa3 + s * (qa4 + s * (qa5 + s * qa6)))));
            return x >= 0.0 ? erx + p / q : -erx - p / q;
        }

        if (ax >= 6.0)
            return x >= 0.0 ? one - tiny : tiny - one;

        var invSquare = one / (ax * ax);
        double numerator;
        double denominator;

        if (ax < 2.85714285714285)
        {
            numerator = ra0 + invSquare * (ra1 + invSquare * (ra2 + invSquare * (ra3 + invSquare * (ra4 + invSquare * (ra5 + invSquare * (ra6 + invSquare * ra7))))));
            denominator = one + invSquare * (sa1 + invSquare * (sa2 + invSquare * (sa3 + invSquare * (sa4 + invSquare * (sa5 + invSquare * (sa6 + invSquare * (sa7 + invSquare * sa8)))))));
        }
        else
        {
            numerator = rb0 + invSquare * (rb1 + invSquare * (rb2 + invSquare * (rb3 + invSquare * (rb4 + invSquare * (rb5 + invSquare * rb6)))));
            denominator = one + invSquare * (sb1 + invSquare * (sb2 + invSquare * (sb3 + invSquare * (sb4 + invSquare * (sb5 + invSquare * (sb6 + invSquare * sb7))))));
        }

        var tail = Math.Exp(-ax * ax - 0.5625 + numerator / denominator);
        return x >= 0.0 ? one - tail / ax : tail / ax - one;
    }
}
