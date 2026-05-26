using Helium.Finance.Solvers;

namespace Helium.Finance.CashFlows;

public readonly record struct CashFlowYieldResult
{
    public CashFlowYieldResult(
        bool Converged,
        double Yield,
        double NpvResidual,
        RootResult Root)
    {
        ValidateRoot(nameof(Root), Root);

        if (Converged)
        {
            if (!Root.Converged)
                throw new ArgumentOutOfRangeException(nameof(Root), "Converged yield results require a converged root result.");

            if (!double.IsFinite(Yield))
                throw new ArgumentOutOfRangeException(nameof(Yield), "Converged yield must be finite.");

            if (!double.IsFinite(NpvResidual))
                throw new ArgumentOutOfRangeException(nameof(NpvResidual), "Converged yield residual must be finite.");
        }
        else if (Root.Converged)
        {
            throw new ArgumentOutOfRangeException(nameof(Root), "Non-converged yield results cannot contain a converged root result.");
        }
        else
        {
            if (double.IsFinite(Yield))
                throw new ArgumentOutOfRangeException(nameof(Yield), "Non-converged yield results cannot contain a finite yield.");

            if (double.IsFinite(NpvResidual))
                throw new ArgumentOutOfRangeException(nameof(NpvResidual), "Non-converged yield results cannot contain a finite residual.");
        }

        this.Converged = Converged;
        this.Yield = Yield;
        this.NpvResidual = NpvResidual;
        this.Root = Root;
    }

    public bool Converged { get; }

    public double Yield { get; }

    public double NpvResidual { get; }

    public RootResult Root { get; }

    public void Deconstruct(
        out bool Converged,
        out double Yield,
        out double NpvResidual,
        out RootResult Root)
    {
        Converged = this.Converged;
        Yield = this.Yield;
        NpvResidual = this.NpvResidual;
        Root = this.Root;
    }

    private static void ValidateRoot(string parameterName, RootResult root)
    {
        if (root.Converged != (root.Status == RootStatus.Converged))
            throw new ArgumentOutOfRangeException(parameterName, "Root convergence flag and status must agree.");
    }
}
