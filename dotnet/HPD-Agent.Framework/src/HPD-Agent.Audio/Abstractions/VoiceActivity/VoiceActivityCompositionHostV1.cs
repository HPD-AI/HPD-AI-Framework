using System.Collections.ObjectModel;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.VoiceActivity;

internal sealed record VoiceActivityCompositionIdentityV1
{
    internal VoiceActivityCompositionIdentityV1(string tenantKey, SessionAuthorityStampV1 session)
    {
        TenantKey = ActivitySourceRequestV1.RequireAscii(tenantKey, nameof(tenantKey));
        if (!session.IsValid) throw new ArgumentException("A live session authority is required.", nameof(session));
        Session = session;
    }

    internal string TenantKey { get; }
    internal SessionAuthorityStampV1 Session { get; }
}

internal sealed record VoiceActivityCompositionCandidateStatusV1
{
    private readonly VoiceActivityInputFormatV1[] _formats;

    internal VoiceActivityCompositionCandidateStatusV1(VoiceActivitySourceCandidateV1 candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        SourceKey = candidate.SourceKey;
        Kind = candidate.Kind;
        Available = candidate.Available;
        ProviderVisibility = candidate.ProviderVisibility;
        InputOwnership = candidate.Capabilities.InputOwnership;
        _formats = candidate.Capabilities.Formats.ToArray();
        Formats = new ReadOnlyCollection<VoiceActivityInputFormatV1>(_formats);
    }

    internal string SourceKey { get; }
    internal ActivitySourceKindV1 Kind { get; }
    internal bool Available { get; }
    internal ProviderActivityVisibilityV1 ProviderVisibility { get; }
    internal VoiceActivityInputOwnershipV1 InputOwnership { get; }
    internal IReadOnlyList<VoiceActivityInputFormatV1> Formats { get; }
}

internal sealed record VoiceActivityCompositionSupportBundleV1
{
    private readonly VoiceActivityCompositionCandidateStatusV1[] _candidates;
    private readonly string[] _diagnostics;

    internal VoiceActivityCompositionSupportBundleV1(
        VoiceActivityCompositionIdentityV1 identity,
        VoiceActivityRequestV1 request,
        IReadOnlyList<VoiceActivitySourceCandidateV1> candidates,
        VoiceActivitySnapshotV1? effective,
        string? safeCode)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidates);
        if (safeCode is not null) safeCode = ActivitySourceRequestV1.RequireAscii(safeCode, nameof(safeCode));
        Identity = identity;
        Request = request;
        _candidates = candidates.Select(static candidate => new VoiceActivityCompositionCandidateStatusV1(candidate))
            .OrderBy(static candidate => candidate.SourceKey, StringComparer.Ordinal).ToArray();
        Candidates = new ReadOnlyCollection<VoiceActivityCompositionCandidateStatusV1>(_candidates);
        Effective = effective;
        SafeCode = safeCode;
        _diagnostics = BuildDiagnostics(request, _candidates, effective, safeCode);
        Diagnostics = Array.AsReadOnly(_diagnostics);
    }

    internal VoiceActivityCompositionIdentityV1 Identity { get; }
    internal VoiceActivityRequestV1 Request { get; }
    internal IReadOnlyList<VoiceActivityCompositionCandidateStatusV1> Candidates { get; }
    internal VoiceActivitySnapshotV1? Effective { get; }
    internal string? SafeCode { get; }
    internal IReadOnlyList<string> Diagnostics { get; }

    private static string[] BuildDiagnostics(
        VoiceActivityRequestV1 request,
        IReadOnlyList<VoiceActivityCompositionCandidateStatusV1> candidates,
        VoiceActivitySnapshotV1? effective,
        string? safeCode)
    {
        var rows = new List<string>(candidates.Count + 3)
        {
            $"requested-profile:{request.Profile}",
            effective is null ? $"preparation-rejected:{safeCode}" : $"effective-health:{effective.Health}",
        };
        rows.AddRange(candidates.Select(static candidate =>
            $"source:{candidate.SourceKey}:{candidate.Kind}:{candidate.Available}:{candidate.ProviderVisibility}"));
        if (effective is not null) rows.AddRange(effective.RequestedEffectiveDifferences);
        return rows.Select(static row => ActivitySourceRequestV1.RequireAscii(row, nameof(rows))).ToArray();
    }
}

internal abstract record VoiceActivityCompositionPreparationV1
{
    private VoiceActivityCompositionPreparationV1() { }
    internal sealed record Prepared(VoiceActivityCompositionHostV1 Host) : VoiceActivityCompositionPreparationV1;
    internal sealed record Rejected(VoiceActivityCompositionSupportBundleV1 Support) :
        VoiceActivityCompositionPreparationV1;
}

internal sealed class VoiceActivityCompositionHostV1
{
    private readonly VoiceActivityPromoterV1 _promoter;
    private readonly IReadOnlyDictionary<string, ulong> _sourceGenerations;

    private VoiceActivityCompositionHostV1(
        VoiceActivityCompositionSupportBundleV1 support,
        GraphDirectionV1 direction,
        VoiceActivityEffectivePlanV1 plan,
        IReadOnlyDictionary<string, ulong> sourceGenerations)
    {
        Support = support;
        Direction = direction;
        _sourceGenerations = new ReadOnlyDictionary<string, ulong>(
            sourceGenerations.ToDictionary(static row => row.Key, static row => row.Value, StringComparer.Ordinal));
        _promoter = new VoiceActivityPromoterV1(plan, support.Identity.Session, direction, _sourceGenerations);
    }

    internal VoiceActivityCompositionSupportBundleV1 Support { get; }
    internal GraphDirectionV1 Direction { get; }
    internal VoiceActivitySnapshotV1 Snapshot => Support.Effective!;
    internal VoiceActivityPromotionStateV1 State => _promoter.State;
    internal IReadOnlyList<VoiceActivityPromotionFactV1> Facts => _promoter.Facts;

    internal static VoiceActivityCompositionSupportBundleV1 InspectFinite(
        ulong planGeneration,
        ulong configRevision,
        VoiceActivityCompositionIdentityV1 identity,
        VoiceActivityRequestV1 request,
        IReadOnlyList<VoiceActivitySourceCandidateV1> candidates)
    {
        var compiled = VoiceActivityPlanCompilerV1.Compile(planGeneration, configRevision, request, candidates);
        return compiled switch
        {
            VoiceActivityPlanCompilationResultV1.Compiled accepted =>
                new VoiceActivityCompositionSupportBundleV1(identity, request, candidates, accepted.Plan.ToSnapshot(), null),
            VoiceActivityPlanCompilationResultV1.Rejected rejected =>
                new VoiceActivityCompositionSupportBundleV1(identity, request, candidates, null, rejected.SafeCode),
            _ => throw new InvalidOperationException("Unknown voice-activity compilation result."),
        };
    }

    internal static VoiceActivityCompositionPreparationV1 Start(
        ulong planGeneration,
        ulong configRevision,
        VoiceActivityCompositionIdentityV1 identity,
        GraphDirectionV1 direction,
        VoiceActivityRequestV1 request,
        IReadOnlyList<VoiceActivitySourceCandidateV1> candidates,
        IReadOnlyDictionary<string, ulong> sourceGenerations)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        ArgumentNullException.ThrowIfNull(sourceGenerations);
        var compiled = VoiceActivityPlanCompilerV1.Compile(planGeneration, configRevision, request, candidates);
        if (compiled is VoiceActivityPlanCompilationResultV1.Rejected rejected)
            return new VoiceActivityCompositionPreparationV1.Rejected(
                new VoiceActivityCompositionSupportBundleV1(identity, request, candidates, null, rejected.SafeCode));
        var plan = ((VoiceActivityPlanCompilationResultV1.Compiled)compiled).Plan;
        var support = new VoiceActivityCompositionSupportBundleV1(identity, request, candidates, plan.ToSnapshot(), null);
        return new VoiceActivityCompositionPreparationV1.Prepared(
            new VoiceActivityCompositionHostV1(support, direction, plan, sourceGenerations));
    }

    internal VoiceActivityPromotionResultV1 Apply(
        string sourceKey,
        ulong sourceSequence,
        VoiceActivityPromotionEdgeV1 edge,
        VoiceActivitySourceOutcomeV1? outcome,
        ulong correctsPromotionSequence = 0)
    {
        sourceKey = ActivitySourceRequestV1.RequireAscii(sourceKey, nameof(sourceKey));
        if (!_sourceGenerations.TryGetValue(sourceKey, out var sourceGeneration))
            return new VoiceActivityPromotionResultV1.Rejected(State, "source-not-in-promotion-authority");
        return _promoter.Apply(new VoiceActivityPromotionInputV1(
            Snapshot.PlanGeneration,
            Snapshot.ConfigRevision,
            Support.Identity.Session,
            Direction,
            sourceKey,
            sourceGeneration,
            sourceSequence,
            edge,
            outcome,
            correctsPromotionSequence));
    }
}
