using System.Globalization;
using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Rhodium-owned venue routing policy presets.
/// These are conservative simulation defaults, not a substitute for live broker rule feeds.
/// </summary>
public static class VenueRoutingPolicyCatalog
{
    private const string CryptoSpotDatasetId = "routing-crypto-spot";
    private const string USListedEquitiesDatasetId = "routing-us-listed-equities";

    private static readonly Dictionary<string, string> BundledDatasets = new(StringComparer.OrdinalIgnoreCase)
    {
        [CryptoSpotDatasetId] = CryptoSpotDatasetId,
        [USListedEquitiesDatasetId] = USListedEquitiesDatasetId
    };

    private static readonly IReadOnlySet<TimeInForce> CryptoMarketTimeInForce = new HashSet<TimeInForce>
    {
        TimeInForce.Day,
        TimeInForce.GTC,
        TimeInForce.IOC,
        TimeInForce.FOK
    };

    private static readonly IReadOnlySet<TimeInForce> ListedEquityMarketTimeInForce = new HashSet<TimeInForce>
    {
        TimeInForce.Day,
        TimeInForce.IOC,
        TimeInForce.FOK
    };

    public static VenueRoutingPolicy BinanceCrypto() => VenueRoutingPolicy.Default with
    {
        AllowedMarketTimeInForce = CryptoMarketTimeInForce,
        MinMarketRoutingQuantity = new Qty(0.000001m),
        MinMarketRoutingNotional = Money.USD(5m)
    };

    public static VenueRoutingPolicy CoinbaseCrypto() => VenueRoutingPolicy.Default with
    {
        AllowedMarketTimeInForce = CryptoMarketTimeInForce,
        MinMarketRoutingQuantity = new Qty(0.00000001m),
        MinMarketRoutingNotional = Money.USD(1m)
    };

    public static VenueRoutingPolicy InteractiveBrokersListedEquity() => VenueRoutingPolicy.Default with
    {
        AllowedMarketTimeInForce = ListedEquityMarketTimeInForce,
        MinMarketRoutingQuantity = new Qty(1m),
        MinMarketRoutingNotional = Money.USD(1m)
    };

    public static IReadOnlyDictionary<Venue, VenueRoutingPolicy> CryptoSpot() => new Dictionary<Venue, VenueRoutingPolicy>
    {
        [Venue.Binance] = BinanceCrypto(),
        [Venue.Coinbase] = CoinbaseCrypto()
    };

    public static IReadOnlyDictionary<Venue, VenueRoutingPolicy> USListedEquities() => new Dictionary<Venue, VenueRoutingPolicy>
    {
        [Venue.NASDAQ] = InteractiveBrokersListedEquity(),
        [Venue.NYSE] = InteractiveBrokersListedEquity()
    };

    public static IReadOnlyDictionary<Venue, VenueRoutingPolicy> KnownVenues()
    {
        var policies = new Dictionary<Venue, VenueRoutingPolicy>();
        foreach (var (venue, policy) in CryptoSpot())
            policies[venue] = policy;
        foreach (var (venue, policy) in USListedEquities())
            policies[venue] = policy;

        return policies;
    }

    public static IReadOnlyCollection<string> BundledDatasetIds => BundledDatasets
        .Keys
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static string BundledPolicyFeedDataset(string datasetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        return ReadBundledPolicyFeedDataset(datasetId);
    }

    public static IReadOnlyDictionary<Venue, VenueRoutingPolicy> FromBundledPolicyFeed(
        string datasetId,
        IReadOnlyDictionary<Venue, VenueRoutingPolicy>? basePolicies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        return FromPolicyFeed(ReadBundledPolicyFeedDataset(datasetId), basePolicies);
    }

    public static IReadOnlyDictionary<Venue, VenueRoutingPolicy> FromPolicyFeed(
        string feedText,
        IReadOnlyDictionary<Venue, VenueRoutingPolicy>? basePolicies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedText);
        var policies = basePolicies is null
            ? new Dictionary<Venue, VenueRoutingPolicy>()
            : new Dictionary<Venue, VenueRoutingPolicy>(basePolicies);

        using var reader = new StringReader(feedText);
        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (IsCommentOrBlank(line))
                continue;

            var cells = line.Split(',').Select(static cell => cell.Trim()).ToArray();
            if (LooksLikePolicyFeedHeader(cells))
                continue;

            if (cells.Length < 1 || string.IsNullOrWhiteSpace(cells[0]))
                throw new FormatException($"Routing policy feed line {lineNumber} is missing a venue.");

            var venue = new Venue(cells[0]);
            var policy = policies.TryGetValue(venue, out var existing)
                ? existing
                : VenueRoutingPolicy.Default;

            policies[venue] = policy with
            {
                AllowBestVenueMarketRouting = ParseOptionalBool(cells, 1) ?? policy.AllowBestVenueMarketRouting,
                AllowMarketSweepRouting = ParseOptionalBool(cells, 2) ?? policy.AllowMarketSweepRouting,
                AllowedMarketTimeInForce = ParseOptionalTimeInForceSet(cells, 3) ?? policy.AllowedMarketTimeInForce,
                MinMarketRoutingQuantity = ParseOptionalQty(cells, 4) ?? policy.MinMarketRoutingQuantity,
                MinMarketRoutingNotional = ParseOptionalMoney(cells, 5, 6) ?? policy.MinMarketRoutingNotional,
                MaxMarketSweepQuantity = ParseOptionalQty(cells, 7) ?? policy.MaxMarketSweepQuantity
            };
        }

        return policies;
    }

    public static IReadOnlyDictionary<Venue, VenueRoutingPolicy> FromPolicyFeedFile(
        string path,
        IReadOnlyDictionary<Venue, VenueRoutingPolicy>? basePolicies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromPolicyFeed(File.ReadAllText(path), basePolicies);
    }

    private static bool IsCommentOrBlank(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length == 0 || trimmed.StartsWith('#');
    }

    private static bool LooksLikePolicyFeedHeader(IReadOnlyList<string> cells)
        => cells.Count > 0 && cells[0].Equals("venue", StringComparison.OrdinalIgnoreCase);

    private static bool? ParseOptionalBool(IReadOnlyList<string> cells, int index)
    {
        if (index >= cells.Count || string.IsNullOrWhiteSpace(cells[index]))
            return null;

        return cells[index].ToLowerInvariant() switch
        {
            "true" or "yes" or "y" or "1" => true,
            "false" or "no" or "n" or "0" => false,
            _ => throw new FormatException($"Unsupported boolean value '{cells[index]}'.")
        };
    }

    private static IReadOnlySet<TimeInForce>? ParseOptionalTimeInForceSet(IReadOnlyList<string> cells, int index)
    {
        if (index >= cells.Count || string.IsNullOrWhiteSpace(cells[index]))
            return null;

        var values = cells[index]
            .Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseTimeInForce)
            .ToHashSet();

        if (values.Count == 0)
            throw new FormatException("Routing policy time-in-force set cannot be empty.");

        return values;
    }

    private static TimeInForce ParseTimeInForce(string value)
        => Enum.TryParse<TimeInForce>(value, ignoreCase: true, out var timeInForce)
            ? timeInForce
            : throw new FormatException($"Unsupported time-in-force value '{value}'.");

    private static Qty? ParseOptionalQty(IReadOnlyList<string> cells, int index)
    {
        if (index >= cells.Count || string.IsNullOrWhiteSpace(cells[index]))
            return null;

        return new Qty(decimal.Parse(cells[index], NumberStyles.Number, CultureInfo.InvariantCulture));
    }

    private static Money? ParseOptionalMoney(IReadOnlyList<string> cells, int amountIndex, int currencyIndex)
    {
        if (amountIndex >= cells.Count || string.IsNullOrWhiteSpace(cells[amountIndex]))
            return null;

        var currency = currencyIndex < cells.Count && !string.IsNullOrWhiteSpace(cells[currencyIndex])
            ? new Currency(cells[currencyIndex])
            : Currency.USD;

        return new Money(decimal.Parse(cells[amountIndex], NumberStyles.Number, CultureInfo.InvariantCulture), currency);
    }

    private static string ReadBundledPolicyFeedDataset(string datasetId)
    {
        if (!BundledDatasets.TryGetValue(datasetId, out var resourceDatasetId))
            throw new ArgumentException($"Unknown bundled routing policy feed dataset '{datasetId}'.", nameof(datasetId));

        var resourceName = $"Rhodium.Connectivity.Data.PolicyFeeds.{resourceDatasetId}.csv";
        var assembly = typeof(VenueRoutingPolicyCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Bundled routing policy feed dataset resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
