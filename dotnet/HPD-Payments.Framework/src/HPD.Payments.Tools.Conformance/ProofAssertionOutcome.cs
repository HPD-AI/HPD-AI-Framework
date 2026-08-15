using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Binds exact assertion inventory and non-passing execution dispositions.</summary>
internal sealed record ProofAssertionOutcome(string InventoryDigest, int Total, int Executed, int Failed, int Skipped,
    int Quarantined, int Flaky, int TimedOut)
{
    private static readonly SearchValues<char> LowerHex = SearchValues.Create("0123456789abcdef");
    internal string EvidenceDigest => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        ProofCanonical.Join(InventoryDigest, Total.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Executed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Failed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Skipped.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Quarantined.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Flaky.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TimedOut.ToString(System.Globalization.CultureInfo.InvariantCulture)))));

    internal bool IsPassing => Total > 0 && Executed == Total && Failed == 0 && Skipped == 0 &&
        Quarantined == 0 && Flaky == 0 && TimedOut == 0;

    internal void Validate()
    {
        var counts = new[] { Total, Executed, Failed, Skipped, Quarantined, Flaky, TimedOut };
        if (InventoryDigest.Length != 71 || !InventoryDigest.StartsWith("sha256:", StringComparison.Ordinal) ||
            InventoryDigest.AsSpan(7).ContainsAnyExcept(LowerHex) ||
            counts.Any(static x => x < 0) || Executed + Failed + Skipped + Quarantined + Flaky + TimedOut != Total)
            throw new InvalidDataException("Assertion outcome is malformed or does not conserve its inventory.");
    }
}
