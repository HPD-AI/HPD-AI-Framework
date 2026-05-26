namespace Helium.Finance.Options;

public sealed class OptionPriceValidationResult
{
    public OptionPriceValidationResult(IReadOnlyList<OptionPriceDiagnostic> diagnostics)
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

    public IReadOnlyList<OptionPriceDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.Count == 0;
}
