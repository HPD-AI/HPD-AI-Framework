using System.Globalization;
using System.Reflection;
using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Rhodium-owned execution calibration presets and feed importers.
/// Provider feeds can replace or overlay these starter values without pushing string parsing into call sites.
/// </summary>
public static class ExecutionCalibrationCatalog
{
    private const string CryptoSpotDatasetId = "calibration-crypto-spot";
    private const string USListedEquitiesDatasetId = "calibration-us-listed-equities";

    private static readonly Dictionary<string, string> BundledDatasets = new(StringComparer.OrdinalIgnoreCase)
    {
        [CryptoSpotDatasetId] = CryptoSpotDatasetId,
        [USListedEquitiesDatasetId] = USListedEquitiesDatasetId
    };

    public static ExecutionCalibrationProfile BinanceCrypto() => new(
        Venue.Binance,
        SlippageParams.VolumeProportional(bpsPerLotSize: 0.15m, referenceQuantity: 10m),
        PriceImprovementParams.FixedBps(takerBps: 0m, makerBps: 0.20m));

    public static ExecutionCalibrationProfile CoinbaseCrypto() => new(
        Venue.Coinbase,
        SlippageParams.VolumeProportional(bpsPerLotSize: 0.20m, referenceQuantity: 10m),
        PriceImprovementParams.FixedBps(takerBps: 0m, makerBps: 0.10m));

    public static ExecutionCalibrationProfile InteractiveBrokersListedEquity() => new(
        Venue.NYSE,
        SlippageParams.VolatilityAdjusted(
            bpsPerLotSize: 0.05m,
            volatilityBps: 0.50m,
            referenceQuantity: 1_000m),
        PriceImprovementParams.FixedBps(takerBps: 0.05m, makerBps: 0.10m));

    public static IReadOnlyDictionary<Venue, ExecutionCalibrationProfile> CryptoSpot() => new Dictionary<Venue, ExecutionCalibrationProfile>
    {
        [Venue.Binance] = BinanceCrypto(),
        [Venue.Coinbase] = CoinbaseCrypto()
    };

    public static IReadOnlyDictionary<Venue, ExecutionCalibrationProfile> USListedEquities()
    {
        var ib = InteractiveBrokersListedEquity();
        return new Dictionary<Venue, ExecutionCalibrationProfile>
        {
            [Venue.NASDAQ] = ib with { Venue = Venue.NASDAQ },
            [Venue.NYSE] = ib
        };
    }

    public static IReadOnlyDictionary<Venue, ExecutionCalibrationProfile> KnownVenues()
    {
        var profiles = new Dictionary<Venue, ExecutionCalibrationProfile>();
        foreach (var (venue, profile) in CryptoSpot())
            profiles[venue] = profile;
        foreach (var (venue, profile) in USListedEquities())
            profiles[venue] = profile;

        return profiles;
    }

    public static IReadOnlyCollection<string> BundledDatasetIds => GetBundledDatasetIds();

    public static string BundledCalibrationFeedDataset(string datasetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        return ReadBundledCalibrationFeedDataset(datasetId);
    }

    public static IReadOnlyDictionary<Venue, ExecutionCalibrationProfile> FromBundledCalibrationFeed(
        string datasetId,
        IReadOnlyDictionary<Venue, ExecutionCalibrationProfile>? baseProfiles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        return FromCalibrationFeed(ReadBundledCalibrationFeedDataset(datasetId), baseProfiles);
    }

    public static IReadOnlyDictionary<Venue, ExecutionCalibrationProfile> FromCalibrationFeed(
        string feedText,
        IReadOnlyDictionary<Venue, ExecutionCalibrationProfile>? baseProfiles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedText);
        var profiles = baseProfiles is null
            ? new Dictionary<Venue, ExecutionCalibrationProfile>()
            : new Dictionary<Venue, ExecutionCalibrationProfile>(baseProfiles);

        using var reader = new StringReader(feedText);
        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (IsCommentOrBlank(line))
                continue;

            var row = new CalibrationFeedRow(line);
            if (LooksLikeCalibrationFeedHeader(row))
                continue;

            var venueCell = row.GetString(0);
            if (venueCell is null)
                throw new FormatException($"Execution calibration feed line {lineNumber} is missing a venue.");

            var venue = new Venue(venueCell);
            var existing = profiles.TryGetValue(venue, out var profile)
                ? profile
                : new ExecutionCalibrationProfile(venue, SlippageParams.None, PriceImprovementParams.None);

            var slippageModel = ParseOptionalSlippageModel(row, 1) ?? existing.Slippage.Model;
            var bpsPerLotSize = ParseOptionalDecimal(row, 2, "slippage bps per lot") ?? existing.Slippage.BpsPerLotSize;
            var referenceQuantity = ParseOptionalDecimal(row, 3, "slippage reference quantity") ?? existing.Slippage.ReferenceQuantity;
            var volatilityBps = ParseOptionalDecimal(row, 4, "volatility bps") ?? existing.Slippage.VolatilityBps;
            var takerImprovementBps = ParseOptionalDecimal(row, 5, "taker price improvement bps") ?? existing.PriceImprovement.TakerBps;
            var makerImprovementBps = ParseOptionalDecimal(row, 6, "maker price improvement bps") ?? existing.PriceImprovement.MakerBps;

            profiles[venue] = new ExecutionCalibrationProfile(
                venue,
                new SlippageParams(slippageModel, bpsPerLotSize, referenceQuantity, volatilityBps),
                PriceImprovementParams.FixedBps(takerImprovementBps, makerImprovementBps));
        }

        return profiles;
    }

    public static IReadOnlyDictionary<Venue, ExecutionCalibrationProfile> FromCalibrationFeedFile(
        string path,
        IReadOnlyDictionary<Venue, ExecutionCalibrationProfile>? baseProfiles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromCalibrationFeed(File.ReadAllText(path), baseProfiles);
    }

    private static bool IsCommentOrBlank(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length == 0 || trimmed.StartsWith('#');
    }

    private static bool LooksLikeCalibrationFeedHeader(CalibrationFeedRow row)
        => row.GetSpan(0).Equals("venue".AsSpan(), StringComparison.OrdinalIgnoreCase);

    private static SlippageModelType? ParseOptionalSlippageModel(CalibrationFeedRow row, int index)
    {
        var value = row.GetSpan(index);
        if (value.Length == 0)
            return null;

        return Enum.TryParse<SlippageModelType>(value, ignoreCase: true, out var model)
            ? model
            : throw new FormatException($"Unsupported slippage model '{value.ToString()}'.");
    }

    private static decimal? ParseOptionalDecimal(CalibrationFeedRow row, int index, string name)
    {
        var text = row.GetSpan(index);
        if (text.Length == 0)
            return null;

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Unsupported {name} value '{text.ToString()}'.");

        if (value < 0m)
            throw new FormatException($"{name} cannot be negative.");

        return value;
    }

    private static string ReadBundledCalibrationFeedDataset(string datasetId)
    {
        if (!BundledDatasets.TryGetValue(datasetId, out var resourceDatasetId))
            throw new ArgumentException($"Bundled execution-calibration dataset '{datasetId}' was not found.", nameof(datasetId));

        var resourceName = $"Rhodium.Simulation.Data.CalibrationFeeds.{resourceDatasetId}.csv";
        var assembly = typeof(ExecutionCalibrationCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new ArgumentException($"Bundled execution-calibration dataset '{datasetId}' was not found.", nameof(datasetId));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string[] GetBundledDatasetIds()
    {
        var ids = new string[BundledDatasets.Count];
        var index = 0;
        foreach (var id in BundledDatasets.Keys)
            ids[index++] = id;

        Array.Sort(ids, StringComparer.OrdinalIgnoreCase);
        return ids;
    }

    private readonly ref struct CalibrationFeedRow
    {
        private readonly ReadOnlySpan<char> _line;

        public CalibrationFeedRow(string line)
        {
            _line = line.AsSpan();
        }

        public string? GetString(int index)
        {
            var span = GetSpan(index);
            return span.Length == 0 ? null : span.ToString();
        }

        public ReadOnlySpan<char> GetSpan(int index)
        {
            var remaining = _line;
            for (var current = 0; ; current++)
            {
                var comma = remaining.IndexOf(',');
                ReadOnlySpan<char> field;
                if (comma < 0)
                {
                    field = remaining.Trim();
                    return current == index ? field : [];
                }

                field = remaining[..comma].Trim();
                if (current == index)
                    return field;

                remaining = remaining[(comma + 1)..];
            }
        }
    }
}
