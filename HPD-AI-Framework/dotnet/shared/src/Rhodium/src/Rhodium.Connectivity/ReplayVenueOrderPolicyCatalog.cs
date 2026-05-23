using System.Globalization;
using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Rhodium-owned replay order-admission policy presets and provider-feed import helpers.
/// </summary>
public static class ReplayVenueOrderPolicyCatalog
{
    private const string CryptoSpotDatasetId = "replay-order-crypto-spot";
    private const string USListedEquitiesDatasetId = "replay-order-us-listed-equities";

    private static readonly Dictionary<string, string> BundledDatasets = new(StringComparer.OrdinalIgnoreCase)
    {
        [CryptoSpotDatasetId] = CryptoSpotDatasetId,
        [USListedEquitiesDatasetId] = USListedEquitiesDatasetId
    };

    private static readonly IReadOnlySet<OrderType> CoreOrderTypes = new HashSet<OrderType>
    {
        OrderType.Market,
        OrderType.Limit,
        OrderType.StopMarket,
        OrderType.StopLimit,
        OrderType.MarketIfTouched,
        OrderType.LimitIfTouched,
        OrderType.MarketToLimit,
        OrderType.TrailingStopMarket,
        OrderType.TrailingStopLimit
    };

    private static readonly IReadOnlySet<TimeInForce> CryptoTimeInForce = new HashSet<TimeInForce>
    {
        TimeInForce.Day,
        TimeInForce.GTC,
        TimeInForce.IOC,
        TimeInForce.FOK,
        TimeInForce.GTD
    };

    private static readonly IReadOnlySet<TimeInForce> ListedEquityTimeInForce = new HashSet<TimeInForce>
    {
        TimeInForce.Day,
        TimeInForce.IOC,
        TimeInForce.FOK,
        TimeInForce.GTD
    };

    public static ReplayVenueOrderPolicy BinanceCrypto() => ReplayVenueOrderPolicy.Default with
    {
        AllowedOrderTypes = CoreOrderTypes,
        AllowedTimeInForce = CryptoTimeInForce,
        AllowPostOnly = true,
        MinOrderQuantity = new Qty(0.000001m),
        MinOrderNotional = Money.USD(5m)
    };

    public static ReplayVenueOrderPolicy CoinbaseCrypto() => ReplayVenueOrderPolicy.Default with
    {
        AllowedOrderTypes = CoreOrderTypes,
        AllowedTimeInForce = CryptoTimeInForce,
        AllowPostOnly = true,
        MinOrderQuantity = new Qty(0.00000001m),
        MinOrderNotional = Money.USD(1m)
    };

    public static ReplayVenueOrderPolicy InteractiveBrokersListedEquity() => ReplayVenueOrderPolicy.Default with
    {
        AllowedOrderTypes = CoreOrderTypes,
        AllowedTimeInForce = ListedEquityTimeInForce,
        AllowPostOnly = true,
        MinOrderQuantity = new Qty(1m),
        MinOrderNotional = Money.USD(1m)
    };

    public static IReadOnlyDictionary<Venue, ReplayVenueOrderPolicy> CryptoSpot() => new Dictionary<Venue, ReplayVenueOrderPolicy>
    {
        [Venue.Binance] = BinanceCrypto(),
        [Venue.Coinbase] = CoinbaseCrypto()
    };

    public static IReadOnlyDictionary<Venue, ReplayVenueOrderPolicy> USListedEquities() => new Dictionary<Venue, ReplayVenueOrderPolicy>
    {
        [Venue.NASDAQ] = InteractiveBrokersListedEquity(),
        [Venue.NYSE] = InteractiveBrokersListedEquity()
    };

    public static IReadOnlyDictionary<Venue, ReplayVenueOrderPolicy> KnownVenues()
    {
        var policies = new Dictionary<Venue, ReplayVenueOrderPolicy>();
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

    public static IReadOnlyDictionary<Venue, ReplayVenueOrderPolicy> FromBundledPolicyFeed(
        string datasetId,
        IReadOnlyDictionary<Venue, ReplayVenueOrderPolicy>? basePolicies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        return FromPolicyFeed(ReadBundledPolicyFeedDataset(datasetId), basePolicies);
    }

    public static IReadOnlyDictionary<Venue, ReplayVenueOrderPolicy> FromPolicyFeed(
        string feedText,
        IReadOnlyDictionary<Venue, ReplayVenueOrderPolicy>? basePolicies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedText);
        var policies = basePolicies is null
            ? new Dictionary<Venue, ReplayVenueOrderPolicy>()
            : new Dictionary<Venue, ReplayVenueOrderPolicy>(basePolicies);

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
                throw new FormatException($"Replay order policy feed line {lineNumber} is missing a venue.");

            var venue = new Venue(cells[0]);
            var policy = policies.TryGetValue(venue, out var existing)
                ? existing
                : ReplayVenueOrderPolicy.Default;

            policies[venue] = policy with
            {
                AllowedOrderTypes = ParseOptionalOrderTypeSet(cells, 1) ?? policy.AllowedOrderTypes,
                AllowedTimeInForce = ParseOptionalTimeInForceSet(cells, 2) ?? policy.AllowedTimeInForce,
                AllowPostOnly = ParseOptionalBool(cells, 3) ?? policy.AllowPostOnly,
                MinOrderQuantity = ParseOptionalQty(cells, 4) ?? policy.MinOrderQuantity,
                MinOrderNotional = ParseOptionalMoney(cells, 5, 6) ?? policy.MinOrderNotional
            };
        }

        return policies;
    }

    public static IReadOnlyDictionary<Venue, ReplayVenueOrderPolicy> FromPolicyFeedFile(
        string path,
        IReadOnlyDictionary<Venue, ReplayVenueOrderPolicy>? basePolicies = null)
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

    private static IReadOnlySet<OrderType>? ParseOptionalOrderTypeSet(IReadOnlyList<string> cells, int index)
    {
        if (index >= cells.Count || string.IsNullOrWhiteSpace(cells[index]))
            return null;

        var values = cells[index]
            .Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseOrderType)
            .ToHashSet();

        if (values.Count == 0)
            throw new FormatException("Replay order policy allowed order type set cannot be empty.");

        return values;
    }

    private static OrderType ParseOrderType(string value)
        => Enum.TryParse<OrderType>(value, ignoreCase: true, out var orderType)
            ? orderType
            : throw new FormatException($"Unsupported order type value '{value}'.");

    private static IReadOnlySet<TimeInForce>? ParseOptionalTimeInForceSet(IReadOnlyList<string> cells, int index)
    {
        if (index >= cells.Count || string.IsNullOrWhiteSpace(cells[index]))
            return null;

        var values = cells[index]
            .Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseTimeInForce)
            .ToHashSet();

        if (values.Count == 0)
            throw new FormatException("Replay order policy time-in-force set cannot be empty.");

        return values;
    }

    private static TimeInForce ParseTimeInForce(string value)
        => Enum.TryParse<TimeInForce>(value, ignoreCase: true, out var timeInForce)
            ? timeInForce
            : throw new FormatException($"Unsupported time-in-force value '{value}'.");

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
            throw new ArgumentException($"Unknown bundled replay order policy feed dataset '{datasetId}'.", nameof(datasetId));

        var resourceName = $"Rhodium.Connectivity.Data.PolicyFeeds.{resourceDatasetId}.csv";
        var assembly = typeof(ReplayVenueOrderPolicyCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Bundled replay order policy feed dataset resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
