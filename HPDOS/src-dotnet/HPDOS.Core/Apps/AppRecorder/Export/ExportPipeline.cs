using System.Diagnostics;
using System.Text;
using HPDOS.Apps.AppRecorder.Project;

namespace HPDOS.Apps.AppRecorder.Export;

/// <summary>
/// Tasks 13 + 14 — Headless ffmpeg export pipeline.
/// Applies all active ProjectCommands as ffmpeg filter chains, then encodes to MP4 or GIF.
/// </summary>
public static class ExportPipeline
{
    // ── MP4 export (Task 13) ──────────────────────────────────────────────────

    /// <summary>
    /// Export the project as MP4. Quality: "medium", "good", or "source".
    /// Applies trim regions, speed changes, and zoom regions from the command log.
    /// </summary>
    public static async Task<string> ExportMp4Async(
        ProjectModel project,
        string quality,
        string? outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var caps = FfmpegProber.Capabilities;
        var dest = outputPath ?? DeriveOutputPath(project.VideoPath, ".mp4");

        var (width, height, fps, durationMs) = await ProbeVideoAsync(caps.FfmpegBinary, project.VideoPath, ct);
        var videoKbps = BitrateCalculator.VideoKbps(width, height, fps, quality);
        var audioKbps = BitrateCalculator.AudioKbps(quality);

        var filter = BuildVideoFilter(project, width, height, durationMs);
        var args = BuildMp4Args(caps, project.VideoPath, dest, filter, videoKbps, audioKbps, fps, width, height);

        await RunFfmpegAsync(caps.FfmpegBinary, args, durationMs, progress, ct);
        return dest;
    }

    // ── GIF export (Task 14) ─────────────────────────────────────────────────

    /// <summary>
    /// Export the project as an animated GIF.
    /// fps: 15, 20, 25, or 30. size: "medium" (480p), "large" (720p), "original".
    /// </summary>
    public static async Task<string> ExportGifAsync(
        ProjectModel project,
        int fps,
        string size,
        string? outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var caps = FfmpegProber.Capabilities;
        var dest = outputPath ?? DeriveOutputPath(project.VideoPath, ".gif");

        var (width, height, srcFps, durationMs) = await ProbeVideoAsync(caps.FfmpegBinary, project.VideoPath, ct);
        var gifWidth = GifWidth(width, height, size);

        var filter = BuildGifFilter(project, gifWidth, fps, durationMs);
        var args = BuildGifArgs(caps, project.VideoPath, dest, filter);

        await RunFfmpegAsync(caps.FfmpegBinary, args, durationMs, progress, ct);
        return dest;
    }

    // ── Video probing ─────────────────────────────────────────────────────────

    private static async Task<(int width, int height, int fps, long durationMs)> ProbeVideoAsync(
        string binary, string videoPath, CancellationToken ct)
    {
        // Use ffprobe to get stream info as JSON.
        var ffprobe = binary.Replace("ffmpeg", "ffprobe");
        if (!File.Exists(ffprobe)) ffprobe = "ffprobe";

        var args = $"-v quiet -print_format json -show_streams \"{videoPath}\"";
        var json = await RunCaptureAsync(ffprobe, args, ct);

        // Minimal parse — avoid full JSON dependency by regex on key fields.
        var width = ParseIntField(json, "width") ?? 1920;
        var height = ParseIntField(json, "height") ?? 1080;
        var fpsRaw = ParseStringField(json, "r_frame_rate") ?? "60/1";
        var fps = ParseFraction(fpsRaw);
        var durationSec = ParseDoubleField(json, "duration") ?? 0.0;

        return (width, height, fps, (long)(durationSec * 1000));
    }

    // ── Filter chain construction ─────────────────────────────────────────────

    private static string BuildVideoFilter(ProjectModel project, int width, int height, long durationMs)
    {
        // Build a ffmpeg filtergraph from active commands.
        // Order: trim cuts → speed changes → zoom (scale + crop + pad) → output.
        var active = project.ActiveCommands.ToList();
        var parts = new List<string>();

        // 1. Trim regions → select filter (invert: keep non-trimmed segments)
        var trims = active.OfType<RemoveTrimRegion>().ToList();  // no — trim is AddTrimRegion
        var trimRegions = active.OfType<AddTrimRegion>().ToList();
        if (trimRegions.Count > 0)
        {
            // Build a select expression that excludes trimmed ranges.
            var sb = new StringBuilder("select='");
            for (var i = 0; i < trimRegions.Count; i++)
            {
                var t = trimRegions[i];
                var startSec = t.StartMs / 1000.0;
                var endSec = t.EndMs / 1000.0;
                if (i > 0) sb.Append("*");
                sb.Append($"not(between(t,{startSec:F3},{endSec:F3}))");
            }
            sb.Append("',setpts=N/FRAME_RATE/TB");
            parts.Add(sb.ToString());
        }

        // 2. Speed changes → setpts
        var speeds = active.OfType<SetSpeed>().ToList();
        foreach (var s in speeds)
        {
            var startSec = s.StartMs / 1000.0;
            var endSec = s.EndMs / 1000.0;
            var factor = 1.0 / s.Multiplier;
            parts.Add($"setpts=if(between(t\\,{startSec:F3}\\,{endSec:F3})\\,{factor:F4}*(PTS-STARTPTS)+STARTPTS\\,PTS)");
        }

        // 3. Zoom regions → zoompan (smooth camera movement)
        var zooms = active.OfType<AddZoomRegion>().ToList();
        foreach (var z in zooms)
        {
            var startSec = z.StartMs / 1000.0;
            var endSec = z.EndMs / 1000.0;
            var zw = (int)(width / z.Depth);
            var zh = (int)(height / z.Depth);
            var x = (int)(z.Cx * width - zw / 2.0);
            var y = (int)(z.Cy * height - zh / 2.0);
            x = Math.Clamp(x, 0, width - zw);
            y = Math.Clamp(y, 0, height - zh);
            parts.Add($"crop=w=if(between(t\\,{startSec:F3}\\,{endSec:F3})\\,{zw}\\,{width}):h=if(between(t\\,{startSec:F3}\\,{endSec:F3})\\,{zh}\\,{height}):x=if(between(t\\,{startSec:F3}\\,{endSec:F3})\\,{x}\\,0):y=if(between(t\\,{startSec:F3}\\,{endSec:F3})\\,{y}\\,0),scale={width}:{height}");
        }

        // 4. Crop (SetCrop / AddCrop commands)
        var crops = active.OfType<AddCrop>().LastOrDefault();
        if (crops?.Options is { } co)
        {
            var cw = (int)(co.Width * width);
            var ch = (int)(co.Height * height);
            var cx = (int)(co.X * width);
            var cy = (int)(co.Y * height);
            parts.Add($"crop={cw}:{ch}:{cx}:{cy}");
        }

        return parts.Count == 0 ? "null" : string.Join(",", parts);
    }

    private static string BuildGifFilter(ProjectModel project, int gifWidth, int fps, long durationMs)
    {
        var videoFilter = BuildVideoFilter(project, gifWidth, gifWidth * 9 / 16, durationMs);
        // GIF palette generation for high quality output
        return $"{videoFilter},fps={fps},scale={gifWidth}:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=256[p];[s1][p]paletteuse=dither=bayer";
    }

    // ── Args builders ─────────────────────────────────────────────────────────

    private static string BuildMp4Args(
        FfmpegCapabilities caps, string input, string output,
        string filter, int videoKbps, int audioKbps, int fps, int width, int height)
    {
        var sb = new StringBuilder();
        sb.Append($"-y -i \"{input}\"");
        sb.Append($" -vf \"{filter}\"");
        sb.Append($" -c:v {caps.VideoEncoder}");

        // VideoToolbox uses -q:v (quality) not -b:v (bitrate) — different API
        if (caps.VideoEncoder == "h264_videotoolbox")
            sb.Append($" -q:v 65 -allow_sw 1");
        else
            sb.Append($" -b:v {videoKbps}k -maxrate {videoKbps * 2}k -bufsize {videoKbps * 4}k");

        sb.Append($" -c:a {caps.AudioEncoder} -b:a {audioKbps}k");
        sb.Append($" -movflags +faststart");  // web-optimized: moov atom at start
        sb.Append($" \"{output}\"");
        return sb.ToString();
    }

    private static string BuildGifArgs(
        FfmpegCapabilities caps, string input, string output, string filter)
    {
        // GIF uses a complex filtergraph with palette generation (no audio)
        return $"-y -i \"{input}\" -lavfi \"{filter}\" -an \"{output}\"";
    }

    // ── ffmpeg runner ─────────────────────────────────────────────────────────

    private static async Task RunFfmpegAsync(
        string binary, string args, long totalDurationMs,
        IProgress<double>? progress, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo(binary, args + " -progress pipe:2 -nostats")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        proc.Start();

        // Parse progress from stderr (ffmpeg -progress pipe:2 outputs key=value lines)
        var readTask = Task.Run(async () =>
        {
            while (!proc.StandardError.EndOfStream)
            {
                var line = await proc.StandardError.ReadLineAsync(ct);
                if (line is null) break;
                if (progress is not null && line.StartsWith("out_time_ms="))
                {
                    if (long.TryParse(line[12..], out var ms) && totalDurationMs > 0)
                        progress.Report(Math.Min(1.0, ms / 1000.0 / (totalDurationMs / 1000.0)));
                }
            }
        }, ct);

        await proc.WaitForExitAsync(ct);
        await readTask;

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with code {proc.ExitCode}. Args: {args}");

        progress?.Report(1.0);
    }

    private static async Task<string> RunCaptureAsync(string binary, string args, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo(binary, args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        proc.Start();
        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return output;
    }

    // ── Output path helpers ───────────────────────────────────────────────────

    private static string DeriveOutputPath(string videoPath, string ext)
    {
        var dir = Path.GetDirectoryName(videoPath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(videoPath);
        return Path.Combine(dir, stem + "_export" + ext);
    }

    private static int GifWidth(int width, int height, string size) => size switch
    {
        "large"    => Math.Min(width, 1280),
        "original" => width,
        _          => Math.Min(width, 854)   // "medium" ≈ 480p wide
    };

    // ── Minimal JSON field parsers (avoids pulling in System.Text.Json here) ──

    private static int? ParseIntField(string json, string key)
    {
        var marker = $"\"{key}\":";
        var idx = json.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var start = idx + marker.Length;
        while (start < json.Length && (json[start] == ' ' || json[start] == '"')) start++;
        var end = start;
        while (end < json.Length && char.IsDigit(json[end])) end++;
        return end > start && int.TryParse(json[start..end], out var v) ? v : null;
    }

    private static double? ParseDoubleField(string json, string key)
    {
        var marker = $"\"{key}\":";
        var idx = json.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var start = idx + marker.Length;
        while (start < json.Length && (json[start] == ' ' || json[start] == '"')) start++;
        var end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.')) end++;
        return end > start && double.TryParse(json[start..end], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string? ParseStringField(string json, string key)
    {
        var marker = $"\"{key}\":\"";
        var idx = json.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var start = idx + marker.Length;
        var end = json.IndexOf('"', start);
        return end > start ? json[start..end] : null;
    }

    private static int ParseFraction(string frac)
    {
        var parts = frac.Split('/');
        if (parts.Length == 2
            && int.TryParse(parts[0], out var num)
            && int.TryParse(parts[1], out var den)
            && den > 0)
            return num / den;
        return 60;
    }
}
