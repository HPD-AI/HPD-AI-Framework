using HPD.Payments.Contracts.IssuanceFact;
using HPD.Payments.Contracts.Obligation;
using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.Billing;

namespace HPD.Payments.Runtime.Tests.Billing;

internal static class BillingInvoiceProofs
{
    internal static void Run(List<string> failures)
    {
        void Check(bool condition, string message) { if (!condition) failures.Add(message); }
        var profile = new CanonicalDigestProfileId("billing", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none");
        ScopeId obligationScope = ScopeId.Create("tenant", "runtime", "obligation");
        SemanticId ObligationId(string kind, string local) => SemanticId.Create(obligationScope, "billing", kind, local);
        CanonicalDigest sourceDigest = CanonicalDigest.Sha256(profile, "valuation-manifest"u8);
        var initial = new AdmitObligationCommand(ObligationId("fact", "initial"), ObligationId("obligation", "one"), ObligationId("manifest", "valuation"),
            sourceDigest, ObligationFactKind.Initial, ObligationDirection.Due, ObligationQuantity.Create(12.34m, "usd"),
            At(TimeKind.Effective, 0), At(TimeKind.Source, 0), new ObligationGuard(OwnerGeneration.Create(1), null));
        var initialFact = new ObligationFact(initial, OwnerGeneration.Create(2), CanonicalDigest.Sha256(profile, "initial"u8), At(TimeKind.Record, 1));
        var credit = new AdmitObligationCommand(ObligationId("fact", "credit"), ObligationId("obligation", "one"), ObligationId("manifest", "credit"),
            CanonicalDigest.Sha256(profile, "credit-source"u8), ObligationFactKind.Adjustment, ObligationDirection.Credit,
            ObligationQuantity.Create(2.34m, "usd"), At(TimeKind.Effective, 1), At(TimeKind.Source, 1),
            new ObligationGuard(OwnerGeneration.Create(2), initialFact.FactDigest), initial.FactId);
        var creditFact = new ObligationFact(credit, OwnerGeneration.Create(3), CanonicalDigest.Sha256(profile, "credit"u8), At(TimeKind.Record, 2));
        Check(BillingInvoicePlanner.CalculateNet([initialFact, creditFact], "usd") == 10.00m, "billing net did not preserve due/credit direction");
        Throws<ArgumentException>(() => BillingInvoicePlanner.CalculateNet([initialFact], "eur"), failures, "mixed-unit invoice calculation was admitted");

        AdmitObligationCommand correction = BillingInvoicePlanner.Correct(ObligationId("fact", "correction"), initial.ObligationId,
            ObligationId("manifest", "correction"), CanonicalDigest.Sha256(profile, "correction-source"u8), ObligationDirection.Credit,
            ObligationQuantity.Create(0.01m, "usd"), At(TimeKind.Effective, 0), At(TimeKind.Source, 2), OwnerGeneration.Create(3),
            creditFact.FactDigest, credit.FactId);
        Check(correction.Kind == ObligationFactKind.Correction && correction.PredecessorFactId == credit.FactId,
            "billing correction did not preserve append-only predecessor lineage");

        ScopeId issuanceScope = ScopeId.Create("tenant", "runtime", "issuance-fact");
        SemanticId IssuanceId(string kind, string local) => SemanticId.Create(issuanceScope, "billing", kind, local);
        var cut = new HistoricalCut(HistoricalFrameKind.AsKnownAt, At(TimeKind.Record, 2), [], ContractVersion.Create(1, 0));
        var manifest = new BillingManifest(IssuanceId("manifest", "invoice-one"), [initial.FactId, credit.FactId], cut,
            Revision.Create("tax", 1), Revision.Create("fx", 3), Revision.Create("rounding", 2), BillingClosureKind.Progressive,
            CanonicalDigest.Sha256(profile, "invoice-manifest"u8));
        byte[] artifact = "invoice-v1"u8.ToArray();
        CanonicalDigest artifactDigest = CanonicalDigest.Sha256(profile, artifact);
        RecordIssuanceCommand issuance = BillingInvoicePlanner.Issue(IssuanceId("fact", "issued"), IssuanceId("artifact", "invoice-one"), manifest,
            new IssuanceNumberClaim(IssuanceId("issuer", "one"), Revision.Create("numbers", 1), "inv-0001", OwnerGeneration.Create(1)),
            artifact, ClassificationMark.Create(DataClassification.Confidential, RetentionKind.Durable), artifactDigest, At(TimeKind.Issue, 3));
        artifact[0] ^= 0xff;
        Check(issuance.ArtifactDigest == artifactDigest && issuance.ArtifactBytes.CopyBytes()[0] == (byte)'i',
            "invoice artifact did not retain exact digest-bound owned bytes");
    }

    private static NamedTime At(TimeKind kind, int hours) => NamedTime.Create(kind, DateTimeOffset.UnixEpoch.AddHours(hours));
    private static void Throws<T>(Action action, List<string> failures, string message) where T : Exception
    { try { action(); } catch (T) { return; } failures.Add(message); }
}
