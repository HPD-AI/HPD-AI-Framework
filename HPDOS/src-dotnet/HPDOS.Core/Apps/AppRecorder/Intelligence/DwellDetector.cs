using HPDOS.Apps.AppRecorder.Recording;

namespace HPDOS.Apps.AppRecorder.Intelligence;

/// <summary>
/// Task 17 (cursor signal) — Detects cursor dwell regions from telemetry.
///
/// A "dwell" is a window where the cursor stays within a radius threshold for
/// a minimum duration, suggesting the user is focused on that area. These become
/// <see cref="CandidateAction.AddZoom"/> candidates.
///
/// Algorithm:
///   Sliding window over 10Hz samples. For each window where all samples fall
///   within <see cref="DwellRadiusNorm"/> of the centroid, and the window spans
///   at least <see cref="MinDwellMs"/>, emit a candidate.
///   Adjacent/overlapping windows are merged into one candidate.
/// </summary>
public static class DwellDetector
{
    /// Minimum duration (ms) the cursor must stay in a region to count as a dwell.
    public const int MinDwellMs = 800;

    /// Maximum normalised radius from the centroid for the cursor to be "dwelling".
    /// 0.08 ≈ 8% of the display dimension — roughly a 150px radius on 1920px wide.
    public const double DwellRadiusNorm = 0.08;

    /// Zoom depth applied to all dwell candidates.
    public const double DefaultZoomDepth = 1.5;

    /// <summary>
    /// Analyse cursor telemetry and return zoom candidates ordered by confidence descending.
    /// </summary>
    public static IReadOnlyList<SignalCandidate> Detect(CursorTelemetry telemetry)
    {
        var samples = telemetry.Samples;
        if (samples.Count < 2) return [];

        var candidates = new List<SignalCandidate>();

        int i = 0;
        while (i < samples.Count)
        {
            // Try to extend a dwell window starting at sample i
            int j = i;
            double sumCx = samples[i].Cx, sumCy = samples[i].Cy;

            while (j + 1 < samples.Count)
            {
                int nextJ = j + 1;
                // Recompute centroid with next sample included
                double newSumCx = sumCx + samples[nextJ].Cx;
                double newSumCy = sumCy + samples[nextJ].Cy;
                int count = nextJ - i + 1;
                double centCx = newSumCx / count;
                double centCy = newSumCy / count;

                // Check all samples i..nextJ are within radius of new centroid
                bool allInside = true;
                for (int k = i; k <= nextJ; k++)
                {
                    double dx = samples[k].Cx - centCx;
                    double dy = samples[k].Cy - centCy;
                    if (Math.Sqrt(dx * dx + dy * dy) > DwellRadiusNorm)
                    {
                        allInside = false;
                        break;
                    }
                }

                if (!allInside) break;

                sumCx = newSumCx;
                sumCy = newSumCy;
                j = nextJ;
            }

            var startMs = samples[i].T;
            var endMs = samples[j].T;
            var durationMs = endMs - startMs;

            if (durationMs >= MinDwellMs)
            {
                int windowCount = j - i + 1;
                double cx = sumCx / windowCount;
                double cy = sumCy / windowCount;

                // Confidence: longer dwell = higher confidence, capped at 1.0
                // 800ms = 0.5, 2000ms = 1.0
                double confidence = Math.Min(1.0, durationMs / 2000.0);

                candidates.Add(new SignalCandidate(
                    StartMs: startMs,
                    EndMs: endMs,
                    Kind: SignalKind.CursorDwell,
                    Action: CandidateAction.AddZoom,
                    Confidence: confidence,
                    FocusCx: cx,
                    FocusCy: cy,
                    Reason: $"Cursor dwelt at ({cx:F2},{cy:F2}) for {durationMs}ms"
                ));

                i = j + 1;
            }
            else
            {
                i++;
            }
        }

        // Merge adjacent dwell candidates that are close in time and location
        return MergeAdjacent(candidates);
    }

    private static IReadOnlyList<SignalCandidate> MergeAdjacent(List<SignalCandidate> candidates)
    {
        if (candidates.Count <= 1) return candidates;

        var merged = new List<SignalCandidate>();
        var current = candidates[0];

        for (int i = 1; i < candidates.Count; i++)
        {
            var next = candidates[i];
            bool timeClose = next.StartMs - current.EndMs < 500;
            bool locationClose = FocusDistance(current, next) < DwellRadiusNorm * 1.5;

            if (timeClose && locationClose)
            {
                // Merge: extend window, average focus, take max confidence
                current = current with
                {
                    EndMs = next.EndMs,
                    Confidence = Math.Max(current.Confidence, next.Confidence),
                    FocusCx = (current.FocusCx + next.FocusCx) / 2.0,
                    FocusCy = (current.FocusCy + next.FocusCy) / 2.0,
                    Reason = $"Merged dwell at ({(current.FocusCx + next.FocusCx) / 2.0:F2},{(current.FocusCy + next.FocusCy) / 2.0:F2}) for {next.EndMs - current.StartMs}ms"
                };
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);

        return merged.OrderByDescending(c => c.Confidence).ToList();
    }

    private static double FocusDistance(SignalCandidate a, SignalCandidate b)
    {
        if (a.FocusCx is null || b.FocusCx is null) return double.MaxValue;
        double dx = a.FocusCx.Value - b.FocusCx.Value;
        double dy = (a.FocusCy ?? 0.5) - (b.FocusCy ?? 0.5);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// Task 17 (click burst signal) — Detects rapid cursor movement bursts.
///
/// A "click burst" is a short window (≤ <see cref="BurstWindowMs"/>) where the
/// cursor moves faster than <see cref="VelocityThresholdNormPerSec"/> and then
/// settles. These mark high-activity moments — good highlight / zoom candidates.
/// </summary>
public static class ClickBurstDetector
{
    /// Window size to measure burst velocity.
    public const int BurstWindowMs = 400;

    /// Normalised cursor velocity (units/sec) above which motion is "burst-like".
    /// 1.5 means the cursor covered 1.5× the display width in one second.
    public const double VelocityThresholdNormPerSec = 1.5;

    /// Minimum duration around the burst to pad the candidate window.
    public const int PadMs = 300;

    public static IReadOnlyList<SignalCandidate> Detect(CursorTelemetry telemetry)
    {
        var samples = telemetry.Samples;
        if (samples.Count < 3) return [];

        var candidates = new List<SignalCandidate>();

        for (int i = 1; i < samples.Count - 1; i++)
        {
            var prev = samples[i - 1];
            var curr = samples[i];
            var dtSec = Math.Max(0.001, (curr.T - prev.T) / 1000.0);

            double dx = curr.Cx - prev.Cx;
            double dy = curr.Cy - prev.Cy;
            double velocity = Math.Sqrt(dx * dx + dy * dy) / dtSec;

            if (velocity >= VelocityThresholdNormPerSec)
            {
                long windowStart = Math.Max(0, curr.T - BurstWindowMs / 2);
                long windowEnd = curr.T + BurstWindowMs / 2 + PadMs;

                // Compute centroid of samples within this window for focus point
                var windowSamples = samples
                    .SkipWhile(s => s.T < windowStart)
                    .TakeWhile(s => s.T <= windowEnd)
                    .ToList();

                double cx = windowSamples.Average(s => s.Cx);
                double cy = windowSamples.Average(s => s.Cy);

                double confidence = Math.Min(1.0, velocity / (VelocityThresholdNormPerSec * 2));

                candidates.Add(new SignalCandidate(
                    StartMs: windowStart,
                    EndMs: windowEnd,
                    Kind: SignalKind.ClickBurst,
                    Action: CandidateAction.Highlight,
                    Confidence: confidence,
                    FocusCx: cx,
                    FocusCy: cy,
                    Reason: $"Click burst: velocity={velocity:F2}/s at t={curr.T}ms"
                ));

                // Skip forward past the burst window to avoid duplicate candidates
                while (i + 1 < samples.Count && samples[i + 1].T < windowEnd) i++;
            }
        }

        return DeduplicateByTime(candidates);
    }

    private static IReadOnlyList<SignalCandidate> DeduplicateByTime(List<SignalCandidate> candidates)
    {
        if (candidates.Count <= 1) return candidates;
        var result = new List<SignalCandidate> { candidates[0] };
        foreach (var c in candidates.Skip(1))
        {
            var last = result[^1];
            if (c.StartMs < last.EndMs)
                result[^1] = last with { EndMs = Math.Max(last.EndMs, c.EndMs), Confidence = Math.Max(last.Confidence, c.Confidence) };
            else
                result.Add(c);
        }
        return result;
    }
}
