namespace Helium.Finance.Volatility;

public sealed class VolatilitySurfaceValidationResult
{
    public VolatilitySurfaceValidationResult(IReadOnlyList<VolatilitySurfaceDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var snapshot = diagnostics.ToArray();
        foreach (var diagnostic in snapshot)
        {
            if (string.IsNullOrWhiteSpace(diagnostic.Message))
                throw new ArgumentException("Diagnostics must contain non-empty messages.", nameof(diagnostics));
        }

        Diagnostics = snapshot;
    }

    public IReadOnlyList<VolatilitySurfaceDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.Count == 0;

    public void ThrowIfInvalid()
    {
        if (IsValid)
            return;

        throw new InvalidOperationException(Diagnostics[0].Message);
    }
}
