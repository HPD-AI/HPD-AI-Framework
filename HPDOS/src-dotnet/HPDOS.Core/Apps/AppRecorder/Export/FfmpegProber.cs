namespace HPDOS.Apps.AppRecorder.Export;

/// <summary>
/// Probes the ffmpeg binary at startup to discover which encoders are available.
/// Results are cached — call ProbeAsync() once at app start.
/// Encoder preference order per the build plan:
///   macOS video:  h264_videotoolbox → libx264 → libvpx-vp9
///   Windows:      h264_nvenc → h264_amf → h264_qsv → libx264
///   Audio:        aac → aac_at → libopus
/// </summary>
public static class FfmpegProber
{
    private static FfmpegCapabilities? _cached;

    public static FfmpegCapabilities Capabilities =>
        _cached ?? throw new InvalidOperationException("Call ProbeAsync() before using capabilities.");

    public static async Task<FfmpegCapabilities> ProbeAsync(string? ffmpegPath = null, CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var binary = ffmpegPath ?? FindFfmpeg();
        var encoders = await RunAsync(binary, "-encoders -v quiet", ct);
        _cached = new FfmpegCapabilities(binary, encoders);
        return _cached;
    }

    private static async Task<HashSet<string>> RunAsync(string binary, string args, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo(binary, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            proc.Start();
            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            foreach (var line in stdout.Split('\n'))
            {
                // Lines look like: " V....D libx264  ..."
                var trimmed = line.TrimStart();
                if (trimmed.Length < 8) continue;
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    set.Add(parts[1]);
            }
        }
        catch { /* ffmpeg not found — capabilities will be empty */ }
        return set;
    }

    private static string FindFfmpeg()
    {
        // Check common locations before falling back to PATH.
        string[] candidates = [
            "/opt/homebrew/bin/ffmpeg",
            "/usr/local/bin/ffmpeg",
            "/usr/bin/ffmpeg",
            "ffmpeg"
        ];
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return "ffmpeg";
    }
}

public sealed class FfmpegCapabilities
{
    private readonly HashSet<string> _encoders;

    public string FfmpegBinary { get; }

    public string VideoEncoder { get; }
    public string AudioEncoder { get; }

    public bool HasVideoToolbox => _encoders.Contains("h264_videotoolbox");
    public bool HasLibx264 => _encoders.Contains("libx264");
    public bool HasNvenc => _encoders.Contains("h264_nvenc");

    internal FfmpegCapabilities(string binary, HashSet<string> encoders)
    {
        FfmpegBinary = binary;
        _encoders = encoders;

        // Video encoder — prefer HW-accelerated
        VideoEncoder = PickFirst(encoders,
            "h264_videotoolbox",   // macOS VideoToolbox (HW)
            "h264_nvenc",          // NVIDIA (HW)
            "h264_amf",            // AMD (HW)
            "h264_qsv",            // Intel Quick Sync (HW)
            "libx264",             // SW fallback
            "libvpx-vp9"           // last resort
        ) ?? "libx264";

        // Audio encoder
        AudioEncoder = PickFirst(encoders,
            "aac_at",              // AudioToolbox AAC (macOS HW)
            "aac",                 // built-in AAC
            "libopus"              // fallback
        ) ?? "aac";
    }

    private static string? PickFirst(HashSet<string> set, params string[] candidates)
    {
        foreach (var c in candidates)
            if (set.Contains(c)) return c;
        return null;
    }
}
