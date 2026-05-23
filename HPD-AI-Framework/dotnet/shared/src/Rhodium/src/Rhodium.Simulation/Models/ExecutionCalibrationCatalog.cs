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

    public static IReadOnlyCollection<string> BundledDatasetIds => BundledDatasets
        .Keys
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

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

            var cells = line.Split(',').Select(static cell => cell.Trim()).ToArray();
            if (LooksLikeCalibrationFeedHeader(cells))
                continue;

            if (cells.Length < 1 || string.IsNullOrWhiteSpace(cells[0]))
                throw new FormatException($"Execution calibration feed line {lineNumber} is missing a venue.");

            var venue = new Venue(cells[0]);
            var existing = profiles.TryGetValue(venue, out var profile)
                ? profile
                : new ExecutionCalibrationProfile(venue, SlippageParams.None, PriceImprovementParams.None);

            var slippageModel = ParseOptionalSlippageModel(cells, 1) ?? existing.Slippage.Model;
            var bpsPerLotSize = ParseOptionalDecimal(cells, 2, "slippage bps per lot") ?? existing.Slippage.BpsPerLotSize;
            var referenceQuantity = ParseOptionalDecimal(cells, 3, "slippage reference quantity") ?? existing.Slippage.ReferenceQuantity;
            var volatilityBps = ParseOptionalDecimal(cells, 4, "volatility bps") ?? existing.Slippage.VolatilityBps;
            var takerImprovementBps = ParseOptionalDecimal(cells, 5, "taker price improvement bps") ?? existing.PriceImprovement.TakerBps;
            var makerImprovementBps = ParseOptionalDecimal(cells, 6, "maker price improvement bps") ?? existing.PriceImprovement.MakerBps;

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

    private static bool LooksLikeCalibrationFeedHeader(IReadOnlyList<string> cells)
        => cells.Count > 0 && cells[0].Equals("venue", StringComparison.OrdinalIgnoreCase);

    private static SlippageModelType? ParseOptionalSlippageModel(IReadOnlyList<string> cells, int index)
    {
        if (index >= cells.Count || string.IsNullOrWhiteSpace(cells[index]))
            return null;

        return Enum.TryParse<SlippageModelType>(cells[index], ignoreCase: true, out var model)
            ? model
            : throw new FormatException($"Unsupported slippage model '{cells[index]}'.");
    }

    private static decimal? ParseOptionalDecimal(IReadOnlyList<string> cells, int index, string name)
    {
        if (index >= cells.Count || string.IsNullOrWhiteSpace(cells[index]))
            return null;

        if (!decimal.TryParse(cells[index], NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Unsupported {name} value '{cells[index]}'.");

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
}
