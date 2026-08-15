using HPD.Payments.Contracts.Valuation;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Tests.Valuation;

internal static class ValuationContractTests
{
    internal static void Run()
    {
        var scope = ScopeId.Create("tenant-a", "test", "valuation");
        var valuation = SemanticId.Create(scope, "contract-test", "valuation", "v1");
        var manifestId = SemanticId.Create(scope, "contract-test", "manifest", "m1");
        var generation = SemanticId.Create(ScopeId.Create("tenant-a", "test", "measurement-generation"), "contract-test", "generation", "g1");
        var cut = new HistoricalCut(HistoricalFrameKind.AsActedUpon, At(TimeKind.Record, 0), Array.Empty<OwnerCut>(), ContractVersion.Create(1, 0));
        var rounding = new RoundingContract(2, MidpointRounding.ToEven, "line");
        var manifest = new ValuationInputManifest(manifestId, generation, cut, Revision.Create("pricing", 1), Revision.Create("algorithm", 1), rounding,
            ReproducibilityKind.ExactRecomputable, new[] { generation }, Digest());
        var value = new EconomicValue(1.005m, 1.00m, "USD", rounding);
        var command = new AdmitValuationCommand(valuation, manifest, value, OwnerGeneration.Create(1), At(TimeKind.Calculated, 1));
        Equal(1.00m, command.Result.Rounded);
        Throws<ArgumentException>(() => Consume(new EconomicValue(1.005m, 1.01m, "USD", rounding)));
        Throws<ArgumentException>(() => Consume(new EconomicValue(1m, 1m, "usd", rounding)));
        var receipt = new ValuationVerification(valuation, OwnerGeneration.Create(1), ReproducibilityKind.Unverifiable, false, "missing-tax-evidence", At(TimeKind.Verify, 2));
        True(!receipt.Passed && receipt.Reproducibility == ReproducibilityKind.Unverifiable);
    }

    private static NamedTime At(TimeKind kind, int minutes) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddMinutes(minutes));
    private static CanonicalDigest Digest() => CanonicalDigest.Sha256(new("valuation-manifest", ContractVersion.Create(1, 0), "fields", "none", "decimal-time-v1", "ordered", "sha256-keyless"), "manifest"u8);
    private static void Consume<T>(T value) { _ = value; }
    private static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
    private static void Equal(decimal expected, decimal actual) { if (expected != actual) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Throws<T>(Action body) where T : Exception { try { body(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
