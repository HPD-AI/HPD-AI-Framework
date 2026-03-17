using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HPDOS.Apps.AppRecorder.Intelligence;

/// <summary>
/// Task 18 — Audio silence detection → trim candidates.
///
/// Uses ffmpeg <c>silencedetect</c> filter to find silent regions in the audio track.
/// Each region longer than <see cref="MinSilenceMs"/> becomes an
/// <see cref="CandidateAction.Trim"/> candidate.
///
/// The leading and trailing <see cref="PadMs"/> of each silence window are preserved
/// as "breath room" — only the core silence is trimmed.
///
/// Confidence formula:
///   longer silence = higher confidence (capped at 1.0 at <see cref="MaxSilenceMs"/>).
///   Silence below noise floor → lower confidence (user may have been thinking, not done).
/// </summary>
public static class SilenceDetector
{
    /// Silence durations shorter than this are ignored (natural pauses, not dead air).
    public const int MinSilenceMs = 1500;

    /// Silence durations at or above this length get maximum confidence.
    public const int MaxSilenceMs = 8000;

    /// Audio level threshold in dBFS below which the track is considered silent.
    /// -40dB is a typical "noise floor" for screen recordings.
    public const double NoiseLevelDb = -40.0;

    /// Milliseconds of silence to preserve at each end (breathing room).
    public const int PadMs = 250;

    // ffmpeg silencedetect output pattern:
    //   [silencedetect @ 0x...] silence_start: 12.345
    //   [silencedetect @ 0x...] silence_end: 15.678 | silence_duration: 3.333
    private static readonly Regex StartRe = new(@"silence_start:\s*([\d.]+)", RegexOptions.Compiled);
    private static readonly Regex EndRe = new(@"silence_end:\s*([\d.]+)\s*\|\s*silence_duration:\s*([\d.]+)", RegexOptions.Compiled);

    /// <summary>
    /// Run ffmpeg silencedetect on <paramref name="videoPath"/> and return trim candidates.
    /// Returns an empty list if the video has no audio track or ffmpeg is unavailable.
    /// </summary>
    public static async Task<IReadOnlyList<SignalCandidate>> DetectAsync(
        string videoPath,
        string ffmpegBinary = "ffmpeg",
        CancellationToken ct = default)
    {
        var output = await RunSilenceDetectAsync(ffmpegBinary, videoPath, ct);
        return ParseSilenceOutput(output);
    }

    // ── ffmpeg runner ─────────────────────────────────────────────────────────

    private static async Task<string> RunSilenceDetectAsync(
        string binary, string videoPath, CancellationToken ct)
    {
        // silencedetect emits to stderr. -af applies the filter, -f null discards video output.
        var args = $"-i \"{videoPath}\" -af \"silencedetect=noise={NoiseLevelDb}dB:duration={MinSilenceMs / 1000.0:F3}\" -f null -";

        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo(binary, args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            proc.Start();
            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            return stderr;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ── Parser ────────────────────────────────────────────────────────────────

    internal static IReadOnlyList<SignalCandidate> ParseSilenceOutput(string ffmpegOutput)
    {
        if (string.IsNullOrEmpty(ffmpegOutput)) return [];

        var candidates = new List<SignalCandidate>();
        double? pendingStart = null;

        foreach (var line in ffmpegOutput.Split('\n'))
        {
            var startMatch = StartRe.Match(line);
            if (startMatch.Success)
            {
                if (double.TryParse(startMatch.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var sec))
                {
                    pendingStart = sec;
                }
                continue;
            }

            var endMatch = EndRe.Match(line);
            if (endMatch.Success && pendingStart.HasValue)
            {
                if (double.TryParse(endMatch.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var endSec)
                    && double.TryParse(endMatch.Groups[2].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var durSec))
                {
                    var durationMs = (long)(durSec * 1000);

                    if (durationMs >= MinSilenceMs)
                    {
                        // Apply pad: trim only the core silence, preserve edges
                        var rawStartMs = (long)(pendingStart.Value * 1000);
                        var rawEndMs = (long)(endSec * 1000);

                        var trimStartMs = rawStartMs + PadMs;
                        var trimEndMs = rawEndMs - PadMs;

                        if (trimEndMs > trimStartMs + 100) // at least 100ms of trimmed content
                        {
                            var trimDurMs = trimEndMs - trimStartMs;
                            var confidence = Math.Min(1.0, (double)trimDurMs / MaxSilenceMs);

                            candidates.Add(new SignalCandidate(
                                StartMs: trimStartMs,
                                EndMs: trimEndMs,
                                Kind: SignalKind.AudioSilence,
                                Action: CandidateAction.Trim,
                                Confidence: confidence,
                                Reason: $"Audio silence: {durationMs}ms raw, trimming {trimDurMs}ms (padded {PadMs}ms each side)"
                            ));
                        }
                    }

                    pendingStart = null;
                }
            }
        }

        return candidates.OrderByDescending(c => c.Confidence).ToList();
    }
}
