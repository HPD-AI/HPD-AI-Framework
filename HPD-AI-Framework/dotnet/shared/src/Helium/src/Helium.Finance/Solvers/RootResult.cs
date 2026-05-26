namespace Helium.Finance.Solvers;

public readonly record struct RootResult
{
    public RootResult(
        bool Converged,
        double Root,
        double FunctionValue,
        int Iterations,
        int FunctionEvaluations,
        double Lower,
        double Upper,
        RootStatus Status)
    {
        ValidateStatus(Status);

        if (Iterations < 0)
            throw new ArgumentOutOfRangeException(nameof(Iterations), "Iteration count must be nonnegative.");

        if (FunctionEvaluations < 0)
            throw new ArgumentOutOfRangeException(nameof(FunctionEvaluations), "Function evaluation count must be nonnegative.");

        if (Converged)
        {
            if (Status != RootStatus.Converged)
                throw new ArgumentOutOfRangeException(nameof(Status), "Converged root results must use Converged status.");

            if (!double.IsFinite(Root))
                throw new ArgumentOutOfRangeException(nameof(Root), "Converged root must be finite.");

            if (!double.IsFinite(FunctionValue))
                throw new ArgumentOutOfRangeException(nameof(FunctionValue), "Converged function value must be finite.");

            if (!double.IsFinite(Lower) || !double.IsFinite(Upper) || Lower > Upper)
                throw new ArgumentOutOfRangeException(nameof(Lower), "Converged root bracket must be finite and ordered.");
        }
        else if (Status == RootStatus.Converged)
        {
            throw new ArgumentOutOfRangeException(nameof(Status), "Non-converged root results cannot use Converged status.");
        }
        else
        {
            ValidateFailurePayload(Status, Root, FunctionValue);
        }

        this.Converged = Converged;
        this.Root = Root;
        this.FunctionValue = FunctionValue;
        this.Iterations = Iterations;
        this.FunctionEvaluations = FunctionEvaluations;
        this.Lower = Lower;
        this.Upper = Upper;
        this.Status = Status;
    }

    public bool Converged { get; }

    public double Root { get; }

    public double FunctionValue { get; }

    public int Iterations { get; }

    public int FunctionEvaluations { get; }

    public double Lower { get; }

    public double Upper { get; }

    public RootStatus Status { get; }

    public void Deconstruct(
        out bool converged,
        out double root,
        out double functionValue,
        out int iterations,
        out int functionEvaluations,
        out double lower,
        out double upper,
        out RootStatus status)
    {
        converged = Converged;
        root = Root;
        functionValue = FunctionValue;
        iterations = Iterations;
        functionEvaluations = FunctionEvaluations;
        lower = Lower;
        upper = Upper;
        status = Status;
    }

    private static void ValidateStatus(RootStatus status)
    {
        if (status is not (RootStatus.Converged
            or RootStatus.NoBracket
            or RootStatus.MaxIterations
            or RootStatus.NonFiniteInput
            or RootStatus.NonFiniteFunctionValue
            or RootStatus.FlatDerivative))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported root status.");
        }
    }

    private static void ValidateFailurePayload(RootStatus status, double root, double functionValue)
    {
        if (status is RootStatus.NoBracket or RootStatus.NonFiniteInput)
        {
            if (double.IsFinite(root))
                throw new ArgumentOutOfRangeException(nameof(root), "Unbracketed or invalid-input root results cannot contain a finite root.");

            if (double.IsFinite(functionValue))
                throw new ArgumentOutOfRangeException(nameof(functionValue), "Unbracketed or invalid-input root results cannot contain a finite function value.");
        }

        if (status == RootStatus.NonFiniteFunctionValue && double.IsFinite(functionValue))
            throw new ArgumentOutOfRangeException(nameof(functionValue), "Nonfinite-function root results require a nonfinite function value.");
    }
}
