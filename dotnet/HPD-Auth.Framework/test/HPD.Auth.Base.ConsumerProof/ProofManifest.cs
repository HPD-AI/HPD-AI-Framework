using System.Text.Json;
using HPD.Base;

namespace HPD.Auth.Base.ConsumerProof;

internal static class ProofManifest
{
    private sealed record ExpectedRow(string RowId, string Construct, string Source,
        string HandleId, string HandleChecksum, string Terminal);

    internal static void Verify()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "l3b-proving-manifest-v1.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = document.RootElement;
        RequireProperties(root, "formatVersion", "applicationId", "evidence", "rows");
        Require(root.GetProperty("formatVersion").GetInt32() == 1, "formatVersion");
        RequireText(root, "applicationId", "hpd.auth.base.consumer-proof");

        JsonElement evidence = root.GetProperty("evidence");
        RequireProperties(evidence, "inMemory", "sqlite", "inMemoryAot", "sqliteAot");
        RequireText(evidence, "inMemory", "l3b.managed.inmemory.2026-08-26");
        RequireText(evidence, "sqlite", "l3b.managed.sqlite.2026-08-26");
        RequireText(evidence, "inMemoryAot", "l3b.native-aot.inmemory.osx-arm64.2026-08-26");
        RequireText(evidence, "sqliteAot", "l3b.native-aot.sqlite.osx-arm64.2026-08-26");

        ExpectedRow[] expected = CreateExpectedRows();
        JsonElement rows = root.GetProperty("rows");
        Require(rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() == expected.Length, "row inventory");
        for (int index = 0; index < expected.Length; index++)
            VerifyRow(rows[index], expected[index]);
    }

    private static ExpectedRow[] CreateExpectedRows() =>
    [
        new("l50-typed-crud-generations", "L50 typed CRUD and generations", "IdentityAndGenerationProof.cs:6", "proof.identity-and-generation.v1@1", BaseGeneratedGraphEvidence.ModuleMutation(IdentityAndGenerationProof.Identity), "BaseInstalledModuleMutationHandle<IdentityAndGenerationRequest, IdentityAndGenerationResult>.ExecuteAsync"),
        new("request-predicates-guarded-captures", "request predicates and guarded captures", "RequestControlProof.cs:5", "proof.request-control.v1@1", BaseGeneratedGraphEvidence.ModuleMutation(RequestControlProof.Identity), "BaseInstalledModuleMutationHandle<RequestControlRequest, RequestControlResult>.ExecuteAsync"),
        new("bounded-static-sets", "bounded static sets", "StaticSetProof.cs:5", "proof.static-set.v1@1", BaseGeneratedGraphEvidence.ModuleMutation(StaticSetProof.Identity), "BaseInstalledModuleMutationHandle<StaticSetRequest, StaticSetResult>.ExecuteAsync"),
        new("missing-removal-receipt-replay", "missing removal and receipt replay", "PresenceAndRemovalProof.cs:5", "proof.presence-and-removal.v1@1", BaseGeneratedGraphEvidence.ModuleMutation(PresenceAndRemovalProof.Identity), "BaseInstalledModuleMutationHandle<PresenceAndRemovalRequest, PresenceAndRemovalResult>.ExecuteAsync"),
        new("l53-ensure", "L53 ensure from activation context", "SemanticProof.cs:35", "proof.semantic.ensure.v1@1", BaseGeneratedGraphEvidence.ModuleMutation(SemanticEnsureProof.Identity), "BaseActivationContext.ExecuteModuleMutationAsync<SemanticProofRequest, SemanticEnsureProofResult>"),
        new("l53-retirement", "L53 retirement from activation context", "SemanticProof.cs:115", "proof.semantic.retire.v1@1", BaseGeneratedGraphEvidence.ModuleMutation(SemanticRetireProof.Identity), "BaseActivationContext.ExecuteModuleMutationAsync<SemanticProofRequest, SemanticRetireProofResult>"),
        new("guarded-l43", "guarded L43 positive and zero cohorts", "SelectionProof.cs:9", "proof.selection.delete.v1@1", BaseGeneratedGraphEvidence.SelectionProfile(SelectionProof.Identity), "BaseActivationContext.GuardSelectionMutation"),
        new("guarded-l47", "guarded L47 checkpoint and delivery", "LifecycleProof.cs:33", "proof.lifecycle.consumer.v1@1", BaseGeneratedGraphEvidence.LifecycleConsumer(LifecycleProof.LifecycleIdentity), "BaseActivationContext.GuardSubjectLifecycleCheckpoint"),
        new("guarded-l48", "guarded L48 acknowledgement", "LifecycleProof.cs:56", "proof.retirement.profile.v1@1", BaseGeneratedGraphEvidence.RetirementConsumer(LifecycleProof.RetirementIdentity), "BaseActivationContext.GuardSubjectRetirementAcknowledgement"),
        new("guarded-l50-child", "guarded L50 child and fingerprint conflict", "ProofActivation.cs:200", "proof.request-control.v1@1", BaseGeneratedGraphEvidence.ModuleMutation(RequestControlProof.Identity), "BaseActivationContext.ExecuteModuleMutationAsync<RequestControlRequest, RequestControlResult>"),
        new("l51-retry-stable-continuation", "L51 retry-stable continuation", "ProofActivation.cs:185", "proof.activation.v1@1", Convert.ToHexStringLower(ProofActivation.Registration.Identity.Checksum.Span), "BaseActivationContext.GuardModuleMutationAndCreateActivation"),
        new("l51-claim-fence-rejection", "L51 claim and hostile fence rejection", "ProofHost.cs:76", "proof.activation.v1@1", Convert.ToHexStringLower(ProofActivation.Registration.Identity.Checksum.Span), "BaseInstalledActivationWorkerHandle<ProofActivationInput, ProofActivationResult>.CompleteAsync"),
        new("l51-schedule-materialization", "L51 schedule materialization and static input", "ProofActivation.cs:68", "proof.schedule.v1@1", Convert.ToHexStringLower(ProofActivation.Schedule.Definition.Checksum.AsSpan()), "BaseInstalledScheduleHandle.CreateAsync"),
        new("l62-canonical-json-read", "L62 canonical JSON read", "ReadProof.cs:22", "proof.json.read", BaseGeneratedGraphEvidence.RegisteredRead(ProofJsonRead.Handle), "BaseReadSession.ToArrayAsync<ProofJsonRead, ProofJsonRead.Row>"),
        new("l63-compound-count-read", "L63 compound count read", "ReadProof.cs:55", "proof.count.summary", BaseGeneratedGraphEvidence.RegisteredRead(ProofCountSummary.Handle), "BaseReadSession.ToArrayAsync<ProofCountSummary, ProofCountSummary.Row>"),
    ];

    private static void VerifyRow(JsonElement row, ExpectedRow expected)
    {
        RequireProperties(row, "rowId", "construct", "source", "handleId", "handleChecksum", "terminal");
        RequireText(row, "rowId", expected.RowId);
        RequireText(row, "construct", expected.Construct);
        RequireText(row, "source", expected.Source);
        RequireText(row, "handleId", expected.HandleId);
        RequireText(row, "handleChecksum", expected.HandleChecksum);
        RequireText(row, "terminal", expected.Terminal);
    }

    private static void RequireProperties(JsonElement value, params string[] expected)
    {
        string[] actual = value.EnumerateObject().Select(static property => property.Name).ToArray();
        Require(actual.SequenceEqual(expected, StringComparer.Ordinal), $"properties [{string.Join(", ", expected)}]");
    }

    private static void RequireText(JsonElement value, string property, string expected)
    {
        string? actual = value.GetProperty(property).GetString();
        Require(string.Equals(actual, expected, StringComparison.Ordinal),
            $"{property} contract (committed '{actual}', generated '{expected}')");
    }

    private static void Require(bool condition, string contract)
    {
        if (!condition)
            throw new InvalidOperationException($"The committed L3B proving manifest violates its exact {contract} contract.");
    }
}
