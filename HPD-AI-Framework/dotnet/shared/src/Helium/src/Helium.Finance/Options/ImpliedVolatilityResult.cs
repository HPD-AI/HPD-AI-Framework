using Helium.Finance.Solvers;

namespace Helium.Finance.Options;

public readonly record struct ImpliedVolatilityResult
{
    public ImpliedVolatilityResult(
        bool converged,
        double volatility,
        double priceResidual,
        int iterations,
        ImpliedVolatilityStatus status,
        RootResult root)
    {
        ValidateStatus(status);
        ValidateRoot(converged, root);

        if (iterations < 0)
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iteration count must be nonnegative.");

        if (converged)
        {
            if (status != ImpliedVolatilityStatus.Converged)
                throw new ArgumentOutOfRangeException(nameof(status), "Converged implied-volatility results must use Converged status.");

            if (!double.IsFinite(volatility) || volatility < 0.0)
                throw new ArgumentOutOfRangeException(nameof(volatility), "Converged implied volatility must be finite and nonnegative.");

            if (!double.IsFinite(priceResidual))
                throw new ArgumentOutOfRangeException(nameof(priceResidual), "Converged implied-volatility residual must be finite.");
        }
        else if (status == ImpliedVolatilityStatus.Converged)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Non-converged implied-volatility results cannot use Converged status.");
        }
        else
        {
            if (double.IsFinite(volatility))
                throw new ArgumentOutOfRangeException(nameof(volatility), "Non-converged implied-volatility results cannot contain a finite volatility.");

            if (double.IsFinite(priceResidual))
                throw new ArgumentOutOfRangeException(nameof(priceResidual), "Non-converged implied-volatility results cannot contain a finite residual.");
        }

        Converged = converged;
        Volatility = volatility;
        PriceResidual = priceResidual;
        Iterations = iterations;
        Status = status;
        Root = root;
    }

    public bool Converged { get; }

    public double Volatility { get; }

    public double PriceResidual { get; }

    public int Iterations { get; }

    public ImpliedVolatilityStatus Status { get; }

    public RootResult Root { get; }

    public void Deconstruct(
        out bool converged,
        out double volatility,
        out double priceResidual,
        out int iterations,
        out ImpliedVolatilityStatus status,
        out RootResult root)
    {
        converged = Converged;
        volatility = Volatility;
        priceResidual = PriceResidual;
        iterations = Iterations;
        status = Status;
        root = Root;
    }

    private static void ValidateStatus(ImpliedVolatilityStatus status)
    {
        if (status is not (ImpliedVolatilityStatus.Converged
            or ImpliedVolatilityStatus.BelowIntrinsic
            or ImpliedVolatilityStatus.AboveUpperBound
            or ImpliedVolatilityStatus.NoBracket
            or ImpliedVolatilityStatus.MaxIterations
            or ImpliedVolatilityStatus.NonFiniteInput
            or ImpliedVolatilityStatus.NonFiniteFunctionValue
            or ImpliedVolatilityStatus.FlatVega))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported implied-volatility status.");
        }
    }

    private static void ValidateRoot(bool converged, RootResult root)
    {
        if (converged && !root.Converged)
            throw new ArgumentOutOfRangeException(nameof(root), "Converged implied-volatility results require a converged root result.");

        if (!converged && root.Converged)
            throw new ArgumentOutOfRangeException(nameof(root), "Non-converged implied-volatility results cannot contain a converged root result.");
    }
}
