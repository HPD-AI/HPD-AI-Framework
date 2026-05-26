using Helium.Finance.Solvers;

namespace Helium.Finance.CashFlows;

public readonly record struct BondSpreadResult
{
    public BondSpreadResult(
        bool Converged,
        double Spread,
        double PriceResidual,
        RootResult Root)
    {
        ValidateRoot(nameof(Root), Root);

        if (Converged)
        {
            if (!Root.Converged)
                throw new ArgumentOutOfRangeException(nameof(Root), "Converged spread results require a converged root result.");

            if (!double.IsFinite(Spread))
                throw new ArgumentOutOfRangeException(nameof(Spread), "Converged spread must be finite.");

            if (!double.IsFinite(PriceResidual))
                throw new ArgumentOutOfRangeException(nameof(PriceResidual), "Converged spread residual must be finite.");
        }
        else if (Root.Converged)
        {
            throw new ArgumentOutOfRangeException(nameof(Root), "Non-converged spread results cannot contain a converged root result.");
        }
        else
        {
            if (double.IsFinite(Spread))
                throw new ArgumentOutOfRangeException(nameof(Spread), "Non-converged spread results cannot contain a finite spread.");

            if (double.IsFinite(PriceResidual))
                throw new ArgumentOutOfRangeException(nameof(PriceResidual), "Non-converged spread results cannot contain a finite residual.");
        }

        this.Converged = Converged;
        this.Spread = Spread;
        this.PriceResidual = PriceResidual;
        this.Root = Root;
    }

    public bool Converged { get; }

    public double Spread { get; }

    public double PriceResidual { get; }

    public RootResult Root { get; }

    public void Deconstruct(
        out bool Converged,
        out double Spread,
        out double PriceResidual,
        out RootResult Root)
    {
        Converged = this.Converged;
        Spread = this.Spread;
        PriceResidual = this.PriceResidual;
        Root = this.Root;
    }

    private static void ValidateRoot(string parameterName, RootResult root)
    {
        if (root.Converged != (root.Status == RootStatus.Converged))
            throw new ArgumentOutOfRangeException(parameterName, "Root convergence flag and status must agree.");
    }
}
