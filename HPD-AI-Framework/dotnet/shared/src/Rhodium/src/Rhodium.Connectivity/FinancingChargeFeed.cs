using System.Globalization;
using System.Reflection;
using Rhodium.Events;
using Rhodium.Primitives;
using Rhodium.Simulation;

namespace Rhodium.Connectivity;

/// <summary>
/// Imports deterministic replay financing cash flows from provider or reconciliation feeds.
/// Rows are account-slice commands, not generic market-rate curves.
/// </summary>
public static class FinancingChargeFeed
{
    private const string CryptoFundingDatasetId = "financing-crypto-funding";
    private const string CashBorrowDatasetId = "financing-cash-borrow";
    private const string RateCurveDatasetId = "financing-rate-curves";

    private static readonly Dictionary<string, string> BundledDatasets = new(StringComparer.OrdinalIgnoreCase)
    {
        [CryptoFundingDatasetId] = CryptoFundingDatasetId,
        [CashBorrowDatasetId] = CashBorrowDatasetId,
        [RateCurveDatasetId] = RateCurveDatasetId
    };

    public static IReadOnlyCollection<string> BundledDatasetIds => BundledDatasets
        .Keys
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static string BundledFinancingFeedDataset(string datasetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        return ReadBundledFinancingFeedDataset(datasetId);
    }

    public static IReadOnlyList<FinancingChargeCommand> FromBundledFinancingFeed(
        string datasetId,
        StrategyId? defaultStrategyId = null,
        int defaultVariantId = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        return FromFinancingFeed(
            ReadBundledFinancingFeedDataset(datasetId),
            defaultStrategyId,
            defaultVariantId);
    }

    public static IReadOnlyList<FinancingChargeCommand> FromFinancingFeedFile(
        string path,
        StrategyId? defaultStrategyId = null,
        int defaultVariantId = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromFinancingFeed(File.ReadAllText(path), defaultStrategyId, defaultVariantId);
    }

    public static IReadOnlyList<FinancingChargeCommand> FromFinancingFeed(
        string feedText,
        StrategyId? defaultStrategyId = null,
        int defaultVariantId = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedText);
        if (defaultVariantId < 0)
            throw new ArgumentOutOfRangeException(nameof(defaultVariantId), "Default variant id cannot be negative.");

        var commands = new List<FinancingChargeCommand>();
        using var reader = new StringReader(feedText);
        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (IsCommentOrBlank(line))
                continue;

            var cells = line.Split(',').Select(static cell => cell.Trim()).ToArray();
            if (LooksLikeFinancingFeedHeader(cells))
                continue;

            commands.Add(ParseCommand(cells, lineNumber, defaultStrategyId, defaultVariantId));
        }

        return commands;
    }

    public static IReadOnlyList<FinancingChargeCommand> FromBundledRateCurveFeed(
        string datasetId,
        StrategyId? defaultStrategyId = null,
        int defaultVariantId = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        return FromRateCurveFeed(
            ReadBundledFinancingFeedDataset(datasetId),
            defaultStrategyId,
            defaultVariantId);
    }

    public static IReadOnlyList<FinancingChargeCommand> FromRateCurveFeedFile(
        string path,
        StrategyId? defaultStrategyId = null,
        int defaultVariantId = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromRateCurveFeed(File.ReadAllText(path), defaultStrategyId, defaultVariantId);
    }

    public static IReadOnlyList<FinancingChargeCommand> FromRateCurveFeed(
        string feedText,
        StrategyId? defaultStrategyId = null,
        int defaultVariantId = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedText);
        if (defaultVariantId < 0)
            throw new ArgumentOutOfRangeException(nameof(defaultVariantId), "Default variant id cannot be negative.");

        var commands = new List<FinancingChargeCommand>();
        using var reader = new StringReader(feedText);
        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (IsCommentOrBlank(line))
                continue;

            var cells = line.Split(',').Select(static cell => cell.Trim()).ToArray();
            if (LooksLikeFinancingFeedHeader(cells))
                continue;

            commands.Add(ParseRateCurveCommand(cells, lineNumber, defaultStrategyId, defaultVariantId));
        }

        return commands;
    }

    public static IReadOnlyList<FinancingChargeCommand> FromPositionRateFeed(
        string feedText,
        IEnumerable<CustodyPositionSnapshot> positions,
        StrategyId? defaultStrategyId = null,
        int defaultVariantId = 0)
    {
        ArgumentNullException.ThrowIfNull(positions);
        return FromPositionRateFeed(
            feedText,
            positions.Select(FinancingRateBasis.FromPosition),
            defaultStrategyId,
            defaultVariantId);
    }

    public static IReadOnlyList<FinancingChargeCommand> FromPositionRateFeed(
        string feedText,
        IEnumerable<FinancingRateBasis> bases,
        StrategyId? defaultStrategyId = null,
        int defaultVariantId = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedText);
        ArgumentNullException.ThrowIfNull(bases);
        if (defaultVariantId < 0)
            throw new ArgumentOutOfRangeException(nameof(defaultVariantId), "Default variant id cannot be negative.");

        var basisByKey = bases.ToDictionary(
            static basis => (
                basis.StrategyId,
                basis.VariantId,
                Instrument: basis.Instrument ?? Instrument.Unknown,
                basis.Currency));

        var commands = new List<FinancingChargeCommand>();
        using var reader = new StringReader(feedText);
        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (IsCommentOrBlank(line))
                continue;

            var cells = line.Split(',').Select(static cell => cell.Trim()).ToArray();
            if (LooksLikeFinancingFeedHeader(cells))
                continue;

            commands.Add(ParsePositionRateCommand(cells, lineNumber, basisByKey, defaultStrategyId, defaultVariantId));
        }

        return commands;
    }

    private static FinancingChargeCommand ParseCommand(
        IReadOnlyList<string> cells,
        int lineNumber,
        StrategyId? defaultStrategyId,
        int defaultVariantId)
    {
        if (cells.Count < 6)
            throw new FormatException($"Financing feed line {lineNumber} must include at least charge_type,strategy_id,variant_id,effective_at,amount,currency.");

        var chargeType = ParseChargeType(Required(cells, 0, "charge type", lineNumber));
        var strategyId = ParseStrategyId(cells, 1, defaultStrategyId, lineNumber);
        var variantId = ParseOptionalNonNegativeInt(cells, 2, "variant id", lineNumber) ?? defaultVariantId;
        var effectiveAt = ParseOptionalInstant(cells, 3, lineNumber);
        var amount = new Money(
            ParseDecimal(Required(cells, 4, "amount", lineNumber), "amount", lineNumber),
            new Currency(Required(cells, 5, "currency", lineNumber).ToUpperInvariant()));
        var instrument = ParseOptionalInstrument(cells, lineNumber);
        var quantity = ParseOptionalQty(cells, 9, lineNumber) ?? default;
        var rate = ParseOptionalDecimal(cells, 10, "rate", lineNumber) ?? 0m;
        var externalReference = Optional(cells, 11);

        return chargeType switch
        {
            FinancingChargeType.CashInterestCredit => FinancingChargeCommand.CashInterestCredit(
                strategyId,
                RequirePositive(amount, "cash interest credit", lineNumber),
                variantId,
                effectiveAt,
                rate,
                externalReference),

            FinancingChargeType.CashInterestDebit => FinancingChargeCommand.CashInterestDebit(
                strategyId,
                RequireNegativeCashFlowMagnitude(amount, "cash interest debit", lineNumber),
                variantId,
                effectiveAt,
                rate,
                externalReference),

            FinancingChargeType.BorrowFee => FinancingChargeCommand.BorrowFee(
                strategyId,
                RequireInstrument(instrument, chargeType, lineNumber),
                RequireNegativeCashFlowMagnitude(amount, "borrow fee", lineNumber),
                quantity,
                variantId,
                effectiveAt,
                rate,
                externalReference),

            FinancingChargeType.PerpetualFunding => FinancingChargeCommand.PerpetualFunding(
                strategyId,
                RequireInstrument(instrument, chargeType, lineNumber),
                amount,
                quantity,
                variantId,
                effectiveAt,
                rate,
                externalReference),

            FinancingChargeType.ForexRollover => FinancingChargeCommand.ForexRollover(
                strategyId,
                RequireInstrument(instrument, chargeType, lineNumber),
                amount,
                quantity,
                variantId,
                effectiveAt,
                rate,
                externalReference),

            _ => throw new FormatException($"Unsupported financing charge type '{chargeType}' on line {lineNumber}.")
        };
    }

    private static FinancingChargeCommand ParseRateCurveCommand(
        IReadOnlyList<string> cells,
        int lineNumber,
        StrategyId? defaultStrategyId,
        int defaultVariantId)
    {
        if (cells.Count < 11)
            throw new FormatException($"Rate curve feed line {lineNumber} must include at least charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,quantity,base_amount,rate.");

        var chargeType = ParseChargeType(Required(cells, 0, "charge type", lineNumber));
        var strategyId = ParseStrategyId(cells, 1, defaultStrategyId, lineNumber);
        var variantId = ParseOptionalNonNegativeInt(cells, 2, "variant id", lineNumber) ?? defaultVariantId;
        var effectiveAt = ParseOptionalInstant(cells, 3, lineNumber);
        var currency = new Currency(Required(cells, 4, "currency", lineNumber).ToUpperInvariant());
        var instrument = ParseOptionalInstrument(cells, lineNumber, venueIndex: 5, symbolIndex: 6, assetClassIndex: 7);
        var quantity = ParseOptionalQty(cells, 8, lineNumber) ?? default;
        var baseAmount = ParseDecimal(Required(cells, 9, "base amount", lineNumber), "base amount", lineNumber);
        var rate = ParseDecimal(Required(cells, 10, "rate", lineNumber), "rate", lineNumber);
        var effectiveRate = EffectiveRate(
            rate,
            cells,
            lineNumber,
            accrualDaysIndex: 12,
            dayCountBasisIndex: 13,
            accrualStartIndex: 14,
            accrualEndIndex: 15,
            accrualDayModeIndex: 16,
            instrument);
        var signedAmount = new Money(baseAmount * effectiveRate, currency);
        var externalReference = Optional(cells, 11);

        return chargeType switch
        {
            FinancingChargeType.CashInterestCredit => FinancingChargeCommand.CashInterestCredit(
                strategyId,
                RequirePositive(MoneyFromAbsolute(signedAmount), "cash interest credit", lineNumber),
                variantId,
                effectiveAt,
                effectiveRate,
                externalReference),

            FinancingChargeType.CashInterestDebit => FinancingChargeCommand.CashInterestDebit(
                strategyId,
                RequirePositive(MoneyFromAbsolute(signedAmount), "cash interest debit", lineNumber),
                variantId,
                effectiveAt,
                effectiveRate,
                externalReference),

            FinancingChargeType.BorrowFee => FinancingChargeCommand.BorrowFee(
                strategyId,
                RequireInstrument(instrument, chargeType, lineNumber),
                RequirePositive(MoneyFromAbsolute(signedAmount), "borrow fee", lineNumber),
                quantity,
                variantId,
                effectiveAt,
                effectiveRate,
                externalReference),

            FinancingChargeType.PerpetualFunding => FinancingChargeCommand.PerpetualFunding(
                strategyId,
                RequireInstrument(instrument, chargeType, lineNumber),
                signedAmount,
                quantity,
                variantId,
                effectiveAt,
                effectiveRate,
                externalReference),

            FinancingChargeType.ForexRollover => FinancingChargeCommand.ForexRollover(
                strategyId,
                RequireInstrument(instrument, chargeType, lineNumber),
                signedAmount,
                quantity,
                variantId,
                effectiveAt,
                effectiveRate,
                externalReference),

            _ => throw new FormatException($"Unsupported financing charge type '{chargeType}' on line {lineNumber}.")
        };
    }

    private static FinancingChargeCommand ParsePositionRateCommand(
        IReadOnlyList<string> cells,
        int lineNumber,
        IReadOnlyDictionary<(StrategyId StrategyId, int VariantId, Instrument Instrument, Currency Currency), FinancingRateBasis> basisByKey,
        StrategyId? defaultStrategyId,
        int defaultVariantId)
    {
        if (cells.Count < 9)
            throw new FormatException($"Position rate feed line {lineNumber} must include at least charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,rate.");

        var chargeType = ParseChargeType(Required(cells, 0, "charge type", lineNumber));
        var strategyId = ParseStrategyId(cells, 1, defaultStrategyId, lineNumber);
        var variantId = ParseOptionalNonNegativeInt(cells, 2, "variant id", lineNumber) ?? defaultVariantId;
        var effectiveAt = ParseOptionalInstant(cells, 3, lineNumber);
        var currency = new Currency(Required(cells, 4, "currency", lineNumber).ToUpperInvariant());
        var instrument = ParseOptionalInstrument(cells, lineNumber, venueIndex: 5, symbolIndex: 6, assetClassIndex: 7);
        var rate = ParseDecimal(Required(cells, 8, "rate", lineNumber), "rate", lineNumber);
        var effectiveRate = EffectiveRate(
            rate,
            cells,
            lineNumber,
            accrualDaysIndex: 10,
            dayCountBasisIndex: 11,
            accrualStartIndex: 12,
            accrualEndIndex: 13,
            accrualDayModeIndex: 14,
            instrument);
        var externalReference = Optional(cells, 9);

        var basisKey = (strategyId, variantId, instrument ?? Instrument.Unknown, currency);
        if (!basisByKey.TryGetValue(basisKey, out var basis))
        {
            throw new FormatException(
                $"Position rate feed line {lineNumber} does not match a financing basis for strategy {strategyId}, variant {variantId}, instrument {instrument}, currency {currency}.");
        }

        var signedAmount = new Money(basis.BaseAmount.Amount * effectiveRate, currency);
        return chargeType switch
        {
            FinancingChargeType.BorrowFee => FinancingChargeCommand.BorrowFee(
                strategyId,
                RequireInstrument(instrument, chargeType, lineNumber),
                RequirePositive(MoneyFromAbsolute(signedAmount), "borrow fee", lineNumber),
                basis.Quantity,
                variantId,
                effectiveAt,
                effectiveRate,
                externalReference),

            FinancingChargeType.PerpetualFunding => FinancingChargeCommand.PerpetualFunding(
                strategyId,
                RequireInstrument(instrument, chargeType, lineNumber),
                signedAmount,
                basis.Quantity,
                variantId,
                effectiveAt,
                effectiveRate,
                externalReference),

            FinancingChargeType.ForexRollover => FinancingChargeCommand.ForexRollover(
                strategyId,
                RequireInstrument(instrument, chargeType, lineNumber),
                signedAmount,
                basis.Quantity,
                variantId,
                effectiveAt,
                effectiveRate,
                externalReference),

            _ => throw new FormatException($"{chargeType} is not position-rate based on financing feed line {lineNumber}.")
        };
    }

    private static bool IsCommentOrBlank(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length == 0 || trimmed.StartsWith('#');
    }

    private static bool LooksLikeFinancingFeedHeader(IReadOnlyList<string> cells)
        => cells.Count > 0 && cells[0].Equals("charge_type", StringComparison.OrdinalIgnoreCase);

    private static string Required(IReadOnlyList<string> cells, int index, string name, int lineNumber)
    {
        var value = Optional(cells, index);
        return string.IsNullOrWhiteSpace(value)
            ? throw new FormatException($"Financing feed line {lineNumber} is missing {name}.")
            : value;
    }

    private static string? Optional(IReadOnlyList<string> cells, int index)
        => index < cells.Count && !string.IsNullOrWhiteSpace(cells[index])
            ? cells[index]
            : null;

    private static FinancingChargeType ParseChargeType(string value)
        => Enum.TryParse<FinancingChargeType>(value, ignoreCase: true, out var chargeType)
            ? chargeType
            : throw new FormatException($"Unsupported financing charge type '{value}'.");

    private static StrategyId ParseStrategyId(
        IReadOnlyList<string> cells,
        int index,
        StrategyId? defaultStrategyId,
        int lineNumber)
    {
        var raw = Optional(cells, index);
        if (raw is null)
        {
            return defaultStrategyId
                ?? throw new FormatException($"Financing feed line {lineNumber} is missing strategy id and no default was provided.");
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0)
            throw new FormatException($"Unsupported strategy id '{raw}' on financing feed line {lineNumber}.");

        return new StrategyId(value);
    }

    private static int? ParseOptionalNonNegativeInt(
        IReadOnlyList<string> cells,
        int index,
        string name,
        int lineNumber)
    {
        var raw = Optional(cells, index);
        if (raw is null)
            return null;

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new FormatException($"Unsupported {name} '{raw}' on financing feed line {lineNumber}.");

        return value;
    }

    private static decimal ParseDecimal(string value, string name, int lineNumber)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new FormatException($"Unsupported {name} '{value}' on financing feed line {lineNumber}.");

    private static decimal? ParseOptionalDecimal(
        IReadOnlyList<string> cells,
        int index,
        string name,
        int lineNumber)
    {
        var raw = Optional(cells, index);
        return raw is null ? null : ParseDecimal(raw, name, lineNumber);
    }

    private static Instant ParseOptionalInstant(IReadOnlyList<string> cells, int index, int lineNumber)
    {
        var raw = Optional(cells, index);
        if (raw is null)
            return default;

        if (!DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value))
        {
            throw new FormatException($"Unsupported effective_at '{raw}' on financing feed line {lineNumber}.");
        }

        return Instant.FromDateTimeOffset(value);
    }

    private static Instrument? ParseOptionalInstrument(
        IReadOnlyList<string> cells,
        int lineNumber,
        int venueIndex = 6,
        int symbolIndex = 7,
        int assetClassIndex = 8)
    {
        var venue = Optional(cells, venueIndex);
        var symbol = Optional(cells, symbolIndex);
        var assetClass = Optional(cells, assetClassIndex);
        if (venue is null && symbol is null && assetClass is null)
            return null;

        if (venue is null || symbol is null || assetClass is null)
            throw new FormatException($"Financing feed line {lineNumber} must include venue, symbol, and asset_class together.");

        if (!Enum.TryParse<AssetClass>(assetClass, ignoreCase: true, out var parsedAssetClass))
            throw new FormatException($"Unsupported asset class '{assetClass}' on financing feed line {lineNumber}.");

        return new Instrument(new Asset(symbol, parsedAssetClass), new Venue(venue));
    }

    private static Qty? ParseOptionalQty(IReadOnlyList<string> cells, int index, int lineNumber)
    {
        var raw = Optional(cells, index);
        if (raw is null)
            return null;

        var value = ParseDecimal(raw, "quantity", lineNumber);
        return new Qty(value);
    }

    private static decimal EffectiveRate(
        decimal rate,
        IReadOnlyList<string> cells,
        int lineNumber,
        int accrualDaysIndex,
        int dayCountBasisIndex,
        int accrualStartIndex,
        int accrualEndIndex,
        int accrualDayModeIndex,
        Instrument? instrument)
    {
        var accrualDays = ParseOptionalDecimal(cells, accrualDaysIndex, "accrual days", lineNumber)
            ?? TryCalculateAccrualDays(cells, accrualStartIndex, accrualEndIndex, accrualDayModeIndex, instrument, lineNumber);
        var dayCountBasis = ParseOptionalDayCountBasis(cells, dayCountBasisIndex, lineNumber);

        if (!accrualDays.HasValue && !dayCountBasis.HasValue)
            return rate;

        if (!accrualDays.HasValue || !dayCountBasis.HasValue)
            throw new FormatException($"Financing feed line {lineNumber} must include both accrual_days and day_count_basis for annualized rates.");

        if (accrualDays.Value <= 0m)
            throw new FormatException($"accrual days must be positive on financing feed line {lineNumber}.");

        return rate * accrualDays.Value / dayCountBasis.Value;
    }

    private static decimal? TryCalculateAccrualDays(
        IReadOnlyList<string> cells,
        int startIndex,
        int endIndex,
        int modeIndex,
        Instrument? instrument,
        int lineNumber)
    {
        var startRaw = Optional(cells, startIndex);
        var endRaw = Optional(cells, endIndex);
        var modeRaw = Optional(cells, modeIndex);
        if (startRaw is null && endRaw is null && modeRaw is null)
            return null;

        if (startRaw is null || endRaw is null)
            throw new FormatException($"Financing feed line {lineNumber} must include both accrual_start and accrual_end.");

        var start = ParseDateOnly(startRaw, "accrual_start", lineNumber);
        var end = ParseDateOnly(endRaw, "accrual_end", lineNumber);
        if (end <= start)
            throw new FormatException($"accrual_end must be after accrual_start on financing feed line {lineNumber}.");

        var mode = modeRaw ?? "Calendar";
        return mode.ToUpperInvariant() switch
        {
            "CALENDAR" or "CALENDAR_DAYS" or "DAYS" => end.DayNumber - start.DayNumber,
            "BUSINESS" or "BUSINESS_DAYS" => CountBusinessDays(start, end, instrument),
            _ => throw new FormatException($"Unsupported accrual day mode '{mode}' on financing feed line {lineNumber}.")
        };
    }

    private static decimal CountBusinessDays(DateOnly start, DateOnly end, Instrument? instrument)
    {
        var calendar = instrument is null
            ? ClearingCalendar.Weekdays()
            : ClearingCalendar.ForVenue(instrument.Value.Venue);
        var days = 0;
        for (var date = start.AddDays(1); date <= end; date = date.AddDays(1))
        {
            if (calendar.IsBusinessDay(date))
                days++;
        }

        return days;
    }

    private static DateOnly ParseDateOnly(string value, string name, int lineNumber)
    {
        string[] formats =
        [
            "yyyy-MM-dd",
            "yyyyMMdd",
            "MM/dd/yyyy",
            "M/d/yyyy"
        ];

        return DateOnly.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result)
            ? result
            : throw new FormatException($"Unsupported {name} '{value}' on financing feed line {lineNumber}.");
    }

    private static decimal? ParseOptionalDayCountBasis(IReadOnlyList<string> cells, int index, int lineNumber)
    {
        var raw = Optional(cells, index);
        if (raw is null)
            return null;

        return raw.ToUpperInvariant() switch
        {
            "ACT/360" or "ACTUAL/360" or "A/360" or "360" => 360m,
            "ACT/365" or "ACTUAL/365" or "A/365" or "365" => 365m,
            "ACT/365.25" or "ACTUAL/365.25" or "A/365.25" or "365.25" => 365.25m,
            _ => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > 0m
                ? parsed
                : throw new FormatException($"Unsupported day count basis '{raw}' on financing feed line {lineNumber}.")
        };
    }

    private static Money RequirePositive(Money amount, string name, int lineNumber)
    {
        if (amount.Amount <= 0m)
            throw new FormatException($"{name} amount must be positive on financing feed line {lineNumber}.");

        return amount;
    }

    private static Money MoneyFromAbsolute(Money amount)
        => new(Math.Abs(amount.Amount), amount.Currency);

    private static Money RequireNegativeCashFlowMagnitude(Money amount, string name, int lineNumber)
    {
        if (amount.Amount >= 0m)
            throw new FormatException($"{name} amount must be a negative cash flow on financing feed line {lineNumber}.");

        return new Money(Math.Abs(amount.Amount), amount.Currency);
    }

    private static Instrument RequireInstrument(Instrument? instrument, FinancingChargeType chargeType, int lineNumber)
        => instrument ?? throw new FormatException($"{chargeType} requires venue, symbol, and asset_class on financing feed line {lineNumber}.");

    private static string ReadBundledFinancingFeedDataset(string datasetId)
    {
        if (!BundledDatasets.TryGetValue(datasetId, out var resourceDatasetId))
            throw new ArgumentException($"Bundled financing feed dataset '{datasetId}' was not found.", nameof(datasetId));

        var resourceName = $"Rhodium.Connectivity.Data.FinancingFeeds.{resourceDatasetId}.csv";
        var assembly = typeof(FinancingChargeFeed).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new ArgumentException($"Bundled financing feed dataset '{datasetId}' was not found.", nameof(datasetId));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
