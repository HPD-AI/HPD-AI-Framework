namespace HPDOS.Apps.AppRecorder.Export;

/// <summary>
/// Task 11 — Dynamic bitrate calculation.
/// Formula: width × height × fps × bpp (bits per pixel per frame).
/// bpp tiers per the build plan:
///   medium = 0.055, good = 0.08, source = 0.12
/// Floor values prevent absurdly low bitrates for tiny resolutions.
/// </summary>
public static class BitrateCalculator
{
    public static int VideoKbps(int width, int height, int fps, string quality)
    {
        var bpp = quality switch
        {
            "medium" => 0.055,
            "source" => 0.12,
            _        => 0.08   // "good" is the default
        };

        var bitsPerSecond = width * height * fps * bpp;

        // Floor per quality tier (build plan values)
        var floorKbps = quality switch
        {
            "medium" => 384,
            "source" => 15_000,
            _        => 5_000
        };

        return Math.Max(floorKbps, (int)(bitsPerSecond / 1000));
    }

    public static int AudioKbps(string quality) => quality switch
    {
        "medium" => 96,
        "source" => 192,
        _        => 128   // "good"
    };
}
