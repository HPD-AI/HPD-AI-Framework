using System.Collections.ObjectModel;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.VoiceActivity;

internal sealed record VoiceActivityCompositionIdentity
{
    internal VoiceActivityCompositionIdentity(string tenantKey, SessionAuthorityStampV1 session)
    {
        TenantKey = ActivitySourceRequest.RequireAscii(tenantKey, nameof(tenantKey));
        if (!session.IsValid) throw new ArgumentException("A live session authority is required.", nameof(session));
        Session = session;
    }

    internal string TenantKey { get; }
    internal SessionAuthorityStampV1 Session { get; }
}

internal sealed record VoiceActivityCompositionCandidateStatus
{
    private readonly VoiceActivityInputFormat[] _formats;

    internal VoiceActivityCompositionCandidateStatus(VoiceActivitySourceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        SourceKey = candidate.SourceKey;
        Kind = candidate.Kind;
        Available = candidate.Available;
        ProviderVisibility = candidate.ProviderVisibility;
        InputOwnership = candidate.Capabilities.InputOwnership;
        _formats = candidate.Capabilities.Formats.ToArray();
        Formats = new ReadOnlyCollection<VoiceActivityInputFormat>(_formats);
    }

    internal string SourceKey { get; }
    internal ActivitySourceKind Kind { get; }
    internal bool Available { get; }
    internal ProviderActivityVisibility ProviderVisibility { get; }
    internal VoiceActivityInputOwnership InputOwnership { get; }
    internal IReadOnlyList<VoiceActivityInputFormat> Formats { get; }
}

internal sealed record VoiceActivityCompositionSupportBundle
{
    private readonly VoiceActivityCompositionCandidateStatus[] _candidates;
    private readonly string[] _diagnostics;

    internal VoiceActivityCompositionSupportBundle(
        VoiceActivityCompositionIdentity identity,
        VoiceActivityRequest request,
        IReadOnlyList<VoiceActivitySourceCandidate> candidates,
        VoiceActivitySnapshot? effective,
        string? safeCode)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidates);
        if (safeCode is not null) safeCode = ActivitySourceRequest.RequireAscii(safeCode, nameof(safeCode));
        Identity = identity;
        Request = request;
        _candidates = candidates.Select(static candidate => new VoiceActivityCompositionCandidateStatus(candidate))
            .OrderBy(static candidate => candidate.SourceKey, StringComparer.Ordinal).ToArray();
        Candidates = new ReadOnlyCollection<VoiceActivityCompositionCandidateStatus>(_candidates);
        Effective = effective;
        SafeCode = safeCode;
        _diagnostics = BuildDiagnostics(request, _candidates, effective, safeCode);
        Diagnostics = Array.AsReadOnly(_diagnostics);
    }

    internal VoiceActivityCompositionIdentity Identity { get; }
    internal VoiceActivityRequest Request { get; }
    internal IReadOnlyList<VoiceActivityCompositionCandidateStatus> Candidates { get; }
    internal VoiceActivitySnapshot? Effective { get; }
    internal string? SafeCode { get; }
    internal IReadOnlyList<string> Diagnostics { get; }

    private static string[] BuildDiagnostics(
        VoiceActivityRequest request,
        IReadOnlyList<VoiceActivityCompositionCandidateStatus> candidates,
        VoiceActivitySnapshot? effective,
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
        return rows.Select(static row => ActivitySourceRequest.RequireAscii(row, nameof(rows))).ToArray();
    }
}

internal abstract record VoiceActivityCompositionPreparation
{
    private VoiceActivityCompositionPreparation() { }
    internal sealed record Prepared(VoiceActivityCompositionHost Host) : VoiceActivityCompositionPreparation;
    internal sealed record Rejected(VoiceActivityCompositionSupportBundle Support) :
        VoiceActivityCompositionPreparation;
}

internal sealed class VoiceActivityCompositionHost
{
    private readonly VoiceActivityPromoter _promoter;
    private readonly IReadOnlyDictionary<string, ulong> _sourceGenerations;

    private VoiceActivityCompositionHost(
        VoiceActivityCompositionSupportBundle support,
        GraphDirection direction,
        VoiceActivityEffectivePlan plan,
        IReadOnlyDictionary<string, ulong> sourceGenerations)
    {
        Support = support;
        Direction = direction;
        _sourceGenerations = new ReadOnlyDictionary<string, ulong>(
            sourceGenerations.ToDictionary(static row => row.Key, static row => row.Value, StringComparer.Ordinal));
        _promoter = new VoiceActivityPromoter(plan, support.Identity.Session, direction, _sourceGenerations);
    }

    internal VoiceActivityCompositionSupportBundle Support { get; }
    internal GraphDirection Direction { get; }
    internal VoiceActivitySnapshot Snapshot => Support.Effective!;
    internal VoiceActivityPromotionState State => _promoter.State;
    internal IReadOnlyList<VoiceActivityPromotionFact> Facts => _promoter.Facts;

    internal static VoiceActivityCompositionSupportBundle InspectFinite(
        ulong planGeneration,
        ulong configRevision,
        VoiceActivityCompositionIdentity identity,
        VoiceActivityRequest request,
        IReadOnlyList<VoiceActivitySourceCandidate> candidates)
    {
        var compiled = VoiceActivityPlanCompiler.Compile(planGeneration, configRevision, request, candidates);
        return compiled switch
        {
            VoiceActivityPlanCompilationResult.Compiled accepted =>
                new VoiceActivityCompositionSupportBundle(identity, request, candidates, accepted.Plan.ToSnapshot(), null),
            VoiceActivityPlanCompilationResult.Rejected rejected =>
                new VoiceActivityCompositionSupportBundle(identity, request, candidates, null, rejected.SafeCode),
            _ => throw new InvalidOperationException("Unknown voice-activity compilation result."),
        };
    }

    internal static VoiceActivityCompositionPreparation Start(
        ulong planGeneration,
        ulong configRevision,
        VoiceActivityCompositionIdentity identity,
        GraphDirection direction,
        VoiceActivityRequest request,
        IReadOnlyList<VoiceActivitySourceCandidate> candidates,
        IReadOnlyDictionary<string, ulong> sourceGenerations)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        ArgumentNullException.ThrowIfNull(sourceGenerations);
        var compiled = VoiceActivityPlanCompiler.Compile(planGeneration, configRevision, request, candidates);
        if (compiled is VoiceActivityPlanCompilationResult.Rejected rejected)
            return new VoiceActivityCompositionPreparation.Rejected(
                new VoiceActivityCompositionSupportBundle(identity, request, candidates, null, rejected.SafeCode));
        var plan = ((VoiceActivityPlanCompilationResult.Compiled)compiled).Plan;
        var support = new VoiceActivityCompositionSupportBundle(identity, request, candidates, plan.ToSnapshot(), null);
        return new VoiceActivityCompositionPreparation.Prepared(
            new VoiceActivityCompositionHost(support, direction, plan, sourceGenerations));
    }

    internal VoiceActivityPromotionResult Apply(
        string sourceKey,
        ulong sourceSequence,
        VoiceActivityPromotionEdge edge,
        VoiceActivitySourceOutcome? outcome,
        ulong correctsPromotionSequence = 0)
    {
        sourceKey = ActivitySourceRequest.RequireAscii(sourceKey, nameof(sourceKey));
        if (!_sourceGenerations.TryGetValue(sourceKey, out var sourceGeneration))
            return new VoiceActivityPromotionResult.Rejected(State, "source-not-in-promotion-authority");
        return _promoter.Apply(new VoiceActivityPromotionInput(
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
