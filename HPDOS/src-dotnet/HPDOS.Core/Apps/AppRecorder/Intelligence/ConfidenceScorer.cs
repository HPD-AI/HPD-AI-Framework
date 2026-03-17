namespace HPDOS.Apps.AppRecorder.Intelligence;

/// <summary>
/// Task 19 — Combined confidence scoring.
///
/// Takes raw <see cref="SignalCandidate"/>s from all detectors (dwell, click burst,
/// silence) and merges overlapping windows into ranked <see cref="MergedCandidate"/>s.
///
/// Scoring rules:
///   1. Group candidates by action (trim vs zoom/highlight — never mix actions).
///   2. Merge time-overlapping candidates of the same action into one window.
///   3. Combined score = weighted average of source confidences + overlap bonus.
///   4. Sort descending by score. Top results fed to SmartEdit.
///
/// Weights per signal kind (tunable):
///   CursorDwell   → 1.0 (strongest zoom signal)
///   ClickBurst    → 0.7 (weaker — can be accidental movement)
///   AudioSilence  → 1.0 (strong trim signal)
/// </summary>
public static class ConfidenceScorer
{
    private static readonly Dictionary<SignalKind, double> Weights = new()
    {
        [SignalKind.CursorDwell]  = 1.0,
        [SignalKind.ClickBurst]   = 0.7,
        [SignalKind.AudioSilence] = 1.0,
    };

    /// Bonus added to score when multiple signal kinds corroborate the same window.
    private const double MultiSignalBonus = 0.15;

    /// Two candidates are considered overlapping if they share at least this many ms.
    private const long OverlapThresholdMs = 100;

    /// <summary>
    /// Merge and score all raw candidates. Returns ranked list, best first.
    /// </summary>
    public static IReadOnlyList<MergedCandidate> Score(
        IEnumerable<SignalCandidate> allCandidates)
    {
        var byAction = allCandidates
            .GroupBy(c => c.Action)
            .ToDictionary(g => g.Key, g => g.ToList());

        var merged = new List<MergedCandidate>();

        foreach (var (action, group) in byAction)
        {
            // Sort by start time, then merge overlapping windows
            var sorted = group.OrderBy(c => c.StartMs).ToList();
            var groups = ClusterOverlapping(sorted);

            foreach (var cluster in groups)
            {
                var candidate = MergeCluster(cluster, action);
                merged.Add(candidate);
            }
        }

        return merged.OrderByDescending(c => c.Score).ToList();
    }

    // ── Clustering ────────────────────────────────────────────────────────────

    private static List<List<SignalCandidate>> ClusterOverlapping(List<SignalCandidate> sorted)
    {
        var clusters = new List<List<SignalCandidate>>();
        if (sorted.Count == 0) return clusters;

        var current = new List<SignalCandidate> { sorted[0] };
        long clusterEnd = sorted[0].EndMs;

        for (int i = 1; i < sorted.Count; i++)
        {
            var c = sorted[i];
            // Overlap if this candidate starts before current cluster ends (minus threshold)
            if (c.StartMs <= clusterEnd - OverlapThresholdMs)
            {
                current.Add(c);
                clusterEnd = Math.Max(clusterEnd, c.EndMs);
            }
            else
            {
                clusters.Add(current);
                current = [c];
                clusterEnd = c.EndMs;
            }
        }
        clusters.Add(current);
        return clusters;
    }

    // ── Merging ───────────────────────────────────────────────────────────────

    private static MergedCandidate MergeCluster(List<SignalCandidate> cluster, CandidateAction action)
    {
        long startMs = cluster.Min(c => c.StartMs);
        long endMs = cluster.Max(c => c.EndMs);

        // Weighted average confidence across sources
        double totalWeight = 0;
        double weightedScore = 0;
        foreach (var c in cluster)
        {
            var w = Weights.GetValueOrDefault(c.Kind, 0.5);
            weightedScore += c.Confidence * w;
            totalWeight += w;
        }

        double baseScore = totalWeight > 0 ? weightedScore / totalWeight : 0;

        // Multi-signal bonus: distinct signal kinds present
        var distinctKinds = cluster.Select(c => c.Kind).Distinct().Count();
        double bonus = distinctKinds > 1 ? MultiSignalBonus * (distinctKinds - 1) : 0;

        double finalScore = Math.Min(1.0, baseScore + bonus);

        // Focus point: weighted centroid of zoom-type candidates
        double? focusCx = null, focusCy = null;
        var zoomSources = cluster.Where(c => c.FocusCx.HasValue).ToList();
        if (zoomSources.Count > 0)
        {
            focusCx = zoomSources.Average(c => c.FocusCx!.Value);
            focusCy = zoomSources.Average(c => c.FocusCy!.Value);
        }

        return new MergedCandidate(
            StartMs: startMs,
            EndMs: endMs,
            Action: action,
            Score: finalScore,
            FocusCx: focusCx,
            FocusCy: focusCy,
            Sources: cluster
        );
    }
}
