using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Cold-path provider metadata normalizer. This is deliberately separate from
/// INormalizer, which is the hot path for raw market/execution payload events.
/// </summary>
public static class ProviderInstrumentNormalizer
{
    public static ProviderInstrumentNormalizationResult Normalize(ProviderInstrumentMetadata metadata)
    {
        var diagnostics = new List<ProviderInstrumentDiagnostic>();

        if (string.IsNullOrWhiteSpace(metadata.Symbol))
            diagnostics.Add(ProviderInstrumentDiagnostic.Required(nameof(metadata.Symbol)));

        if (string.IsNullOrWhiteSpace(metadata.Venue.Name))
            diagnostics.Add(ProviderInstrumentDiagnostic.Required(nameof(metadata.Venue)));

        metadata = ApplyParsedOptionSymbol(metadata, diagnostics);

        var contract = metadata.Kind switch
        {
            ProviderInstrumentKind.Equity => NormalizeEquity(metadata, diagnostics),
            ProviderInstrumentKind.Forex => NormalizeForex(metadata, diagnostics),
            ProviderInstrumentKind.CryptoSpot => NormalizeCryptoSpot(metadata, diagnostics),
            ProviderInstrumentKind.Future => NormalizeFuture(metadata, diagnostics),
            ProviderInstrumentKind.CryptoFuture => NormalizeCryptoFuture(metadata, diagnostics),
            ProviderInstrumentKind.CryptoPerpetual => NormalizeCryptoPerpetual(metadata, diagnostics),
            ProviderInstrumentKind.Option => NormalizeOption(metadata, diagnostics),
            ProviderInstrumentKind.IndexOption => NormalizeIndexOption(metadata, diagnostics),
            ProviderInstrumentKind.FutureOption => NormalizeFutureOption(metadata, diagnostics),
            ProviderInstrumentKind.LinearCryptoOption => NormalizeLinearCryptoOption(metadata, diagnostics),
            ProviderInstrumentKind.InverseCryptoOption => NormalizeInverseCryptoOption(metadata, diagnostics),
            ProviderInstrumentKind.QuantoCryptoOption => NormalizeQuantoCryptoOption(metadata, diagnostics),
            ProviderInstrumentKind.BinaryOption => NormalizeBinaryOption(metadata, diagnostics),
            ProviderInstrumentKind.Cfd => NormalizeCfd(metadata, diagnostics),
            ProviderInstrumentKind.BettingInstrument => NormalizeBetting(metadata, diagnostics),
            ProviderInstrumentKind.Observable => NormalizeObservable(metadata, diagnostics),
            ProviderInstrumentKind.Index => NormalizeIndex(metadata, diagnostics),
            ProviderInstrumentKind.CommoditySpot => NormalizeCommoditySpot(metadata, diagnostics),
            ProviderInstrumentKind.TokenizedAsset => NormalizeTokenizedAsset(metadata, diagnostics),
            ProviderInstrumentKind.Unsupported => null,
            _ => null
        };

        if (metadata.Kind is ProviderInstrumentKind.Unsupported)
            diagnostics.Add(new ProviderInstrumentDiagnostic(
                "provider.kind.unsupported",
                $"Provider instrument kind '{metadata.ProviderKind ?? metadata.Kind.ToString()}' is not supported by the Rhodium contract normalizer."));
        else if (contract is null && diagnostics.Count == 0)
            diagnostics.Add(new ProviderInstrumentDiagnostic(
                "provider.kind.unmapped",
                $"Provider instrument kind '{metadata.Kind}' has no Rhodium contract mapping."));

        if (contract is not null)
        {
            var validation = InstrumentContractValidator.Validate(contract);
            diagnostics.AddRange(validation.Issues.Select(static issue =>
                new ProviderInstrumentDiagnostic($"contract.{issue.Code}", issue.Message)));

            if (!validation.IsValid)
                contract = null;
        }

        return new ProviderInstrumentNormalizationResult(contract, diagnostics);
    }

    public static InstrumentContract NormalizeOrThrow(ProviderInstrumentMetadata metadata)
    {
        var result = Normalize(metadata);
        if (result.Contract is not null)
            return result.Contract;

        throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
    }

    private static InstrumentContract? NormalizeEquity(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireCurrency(metadata.QuoteCurrency, nameof(metadata.QuoteCurrency), diagnostics, out var quoteCurrency))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.Equity(
            metadata.Symbol,
            metadata.Venue,
            quoteCurrency,
            Tick(metadata),
            Lot(metadata),
            metadata.ExternalIds.TryGetValue("isin", out var isin) ? isin : null));
    }

    private static InstrumentContract? NormalizeForex(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireCurrency(metadata.BaseCurrency, nameof(metadata.BaseCurrency), diagnostics, out var baseCurrency) ||
            !RequireCurrency(metadata.QuoteCurrency, nameof(metadata.QuoteCurrency), diagnostics, out var quoteCurrency))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.CurrencyPair(
            metadata.Symbol,
            metadata.Venue,
            baseCurrency,
            quoteCurrency,
            Tick(metadata),
            Lot(metadata)));
    }

    private static InstrumentContract? NormalizeCryptoSpot(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireCurrency(metadata.BaseCurrency, nameof(metadata.BaseCurrency), diagnostics, out var baseCurrency) ||
            !RequireCurrency(metadata.QuoteCurrency, nameof(metadata.QuoteCurrency), diagnostics, out var quoteCurrency))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.CryptoSpot(
            metadata.Symbol,
            metadata.Venue,
            baseCurrency,
            quoteCurrency,
            Tick(metadata),
            Lot(metadata)));
    }

    private static InstrumentContract? NormalizeCommoditySpot(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireCurrency(metadata.QuoteCurrency, nameof(metadata.QuoteCurrency), diagnostics, out var quoteCurrency))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.CommoditySpot(
            metadata.Symbol,
            metadata.Venue,
            quoteCurrency,
            Tick(metadata),
            Lot(metadata)));
    }

    private static InstrumentContract? NormalizeFuture(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireInstrument(metadata.UnderlyingSymbol, metadata.UnderlyingAssetClass, metadata.UnderlyingVenue ?? metadata.Venue, nameof(metadata.UnderlyingSymbol), diagnostics, out var underlying) ||
            !RequireCurrency(metadata.QuoteCurrency, nameof(metadata.QuoteCurrency), diagnostics, out var quoteCurrency) ||
            !RequireInstant(metadata.Expiry, nameof(metadata.Expiry), diagnostics, out var expiry))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.Future(
            metadata.Symbol,
            metadata.Venue,
            underlying,
            quoteCurrency,
            Tick(metadata),
            Lot(metadata),
            Multiplier(metadata),
            expiry));
    }

    private static InstrumentContract? NormalizeCryptoFuture(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireCurrency(metadata.BaseCurrency, nameof(metadata.BaseCurrency), diagnostics, out var baseCurrency) ||
            !RequireCurrency(metadata.QuoteCurrency, nameof(metadata.QuoteCurrency), diagnostics, out var quoteCurrency) ||
            !RequireCurrency(metadata.SettlementCurrency, nameof(metadata.SettlementCurrency), diagnostics, out var settlementCurrency) ||
            !RequireInstant(metadata.Expiry, nameof(metadata.Expiry), diagnostics, out var expiry))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.CryptoFuture(
            metadata.Symbol,
            metadata.Venue,
            baseCurrency,
            quoteCurrency,
            settlementCurrency,
            Tick(metadata),
            Lot(metadata),
            Multiplier(metadata),
            expiry,
            metadata.Inverse));
    }

    private static InstrumentContract? NormalizeCryptoPerpetual(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireCurrency(metadata.BaseCurrency, nameof(metadata.BaseCurrency), diagnostics, out var baseCurrency) ||
            !RequireCurrency(metadata.QuoteCurrency, nameof(metadata.QuoteCurrency), diagnostics, out var quoteCurrency) ||
            !RequireCurrency(metadata.SettlementCurrency, nameof(metadata.SettlementCurrency), diagnostics, out var settlementCurrency))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.CryptoPerpetual(
            metadata.Symbol,
            metadata.Venue,
            baseCurrency,
            quoteCurrency,
            settlementCurrency,
            Tick(metadata),
            Lot(metadata),
            Multiplier(metadata),
            metadata.Inverse));
    }

    private static InstrumentContract? NormalizeOption(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireOptionCore(metadata, diagnostics, out var underlying, out var quoteCurrency, out var strike, out var expiry, out var right))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.OptionContract(
            metadata.Symbol,
            metadata.Venue,
            underlying,
            quoteCurrency,
            Tick(metadata),
            Lot(metadata),
            Multiplier(metadata),
            strike,
            expiry,
            right,
            metadata.ExerciseStyle ?? ExerciseStyle.American,
            metadata.ContractUnitOfTrade,
            metadata.Activation,
            metadata.ExpirationCycle,
            metadata.PremiumStyle,
            metadata.ExercisePolicy,
            metadata.AssignmentPolicy,
            metadata.ExerciseDates));
    }

    private static InstrumentContract? NormalizeIndexOption(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireOptionCore(metadata, diagnostics, out var index, out var quoteCurrency, out var strike, out var expiry, out var right))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.IndexOption(
            metadata.Symbol,
            metadata.Venue,
            index,
            quoteCurrency,
            Tick(metadata),
            Lot(metadata),
            Multiplier(metadata),
            strike,
            expiry,
            right,
            metadata.ExerciseStyle ?? ExerciseStyle.European,
            metadata.ContractUnitOfTrade,
            metadata.Activation,
            metadata.ExpirationCycle,
            metadata.PremiumStyle,
            metadata.ExercisePolicy,
            metadata.AssignmentPolicy,
            PriceIncrementRule(metadata),
            metadata.ExerciseDates));
    }

    private static InstrumentContract? NormalizeFutureOption(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireOptionCore(metadata, diagnostics, out var future, out var quoteCurrency, out var strike, out var expiry, out var right))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.FutureOption(
            metadata.Symbol,
            metadata.Venue,
            future,
            quoteCurrency,
            Tick(metadata),
            Lot(metadata),
            Multiplier(metadata),
            strike,
            expiry,
            right,
            metadata.ExerciseStyle ?? ExerciseStyle.American,
            metadata.ContractUnitOfTrade,
            metadata.Activation,
            metadata.ExpirationCycle,
            metadata.PremiumStyle,
            metadata.ExercisePolicy,
            metadata.AssignmentPolicy,
            metadata.ExerciseDates));
    }

    private static InstrumentContract? NormalizeLinearCryptoOption(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        var valid = RequireOptionCore(metadata, diagnostics, out var underlying, out var quoteCurrency, out var strike, out var expiry, out var right);
        valid &= RequireCurrency(metadata.SettlementCurrency, nameof(metadata.SettlementCurrency), diagnostics, out var settlementCurrency);
        if (!valid)
            return null;

        return ApplyCommonMetadata(metadata, Contracts.LinearCryptoOption(
            metadata.Symbol,
            metadata.Venue,
            underlying,
            quoteCurrency,
            settlementCurrency,
            Tick(metadata),
            Lot(metadata),
            Multiplier(metadata),
            strike,
            expiry,
            right,
            metadata.ExerciseStyle ?? ExerciseStyle.European,
            metadata.ContractUnitOfTrade,
            metadata.Activation,
            metadata.ExpirationCycle,
            metadata.PremiumStyle,
            metadata.ExercisePolicy,
            metadata.AssignmentPolicy,
            metadata.ExerciseDates));
    }

    private static InstrumentContract? NormalizeInverseCryptoOption(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        var valid = RequireOptionCore(metadata, diagnostics, out var underlying, out var quoteCurrency, out var strike, out var expiry, out var right);
        valid &= RequireCurrency(metadata.BaseCurrency, nameof(metadata.BaseCurrency), diagnostics, out var baseCurrency);
        valid &= RequireCurrency(metadata.SettlementCurrency, nameof(metadata.SettlementCurrency), diagnostics, out var settlementCurrency);
        if (!valid)
            return null;

        return ApplyCommonMetadata(metadata, Contracts.InverseCryptoOption(
            metadata.Symbol,
            metadata.Venue,
            underlying,
            baseCurrency,
            quoteCurrency,
            settlementCurrency,
            Tick(metadata),
            Lot(metadata),
            Multiplier(metadata),
            strike,
            expiry,
            right,
            metadata.ExerciseStyle ?? ExerciseStyle.European,
            metadata.ContractUnitOfTrade,
            metadata.Activation,
            metadata.ExpirationCycle,
            metadata.PremiumStyle,
            metadata.ExercisePolicy,
            metadata.AssignmentPolicy,
            metadata.ExerciseDates));
    }

    private static InstrumentContract? NormalizeQuantoCryptoOption(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        var valid = RequireOptionCore(metadata, diagnostics, out var underlying, out var quoteCurrency, out var strike, out var expiry, out var right);
        valid &= RequireCurrency(metadata.BaseCurrency, nameof(metadata.BaseCurrency), diagnostics, out var baseCurrency);
        valid &= RequireCurrency(metadata.SettlementCurrency, nameof(metadata.SettlementCurrency), diagnostics, out var settlementCurrency);
        if (!valid)
            return null;

        if (metadata.QuantoConversionRate is not > 0m)
        {
            diagnostics.Add(ProviderInstrumentDiagnostic.Required(nameof(metadata.QuantoConversionRate)));
            return null;
        }

        return ApplyCommonMetadata(metadata, Contracts.QuantoCryptoOption(
            metadata.Symbol,
            metadata.Venue,
            underlying,
            baseCurrency,
            quoteCurrency,
            settlementCurrency,
            metadata.QuantoConversionRate.Value,
            Tick(metadata),
            Lot(metadata),
            Multiplier(metadata),
            strike,
            expiry,
            right,
            metadata.ExerciseStyle ?? ExerciseStyle.European,
            metadata.ContractUnitOfTrade,
            metadata.Activation,
            metadata.ExpirationCycle,
            metadata.PremiumStyle,
            metadata.ExercisePolicy,
            metadata.AssignmentPolicy,
            metadata.ExerciseDates));
    }

    private static InstrumentContract? NormalizeBinaryOption(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireString(metadata.EventKey, nameof(metadata.EventKey), diagnostics, out var eventKey) ||
            !RequireCurrency(metadata.SettlementCurrency ?? metadata.QuoteCurrency, nameof(metadata.SettlementCurrency), diagnostics, out var settlementCurrency))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.BinaryOption(
            metadata.Symbol,
            metadata.Venue,
            eventKey,
            settlementCurrency,
            metadata.Payout ?? new Money(1m, settlementCurrency),
            metadata.EventTime));
    }

    private static InstrumentContract? NormalizeBetting(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireString(metadata.MarketId, nameof(metadata.MarketId), diagnostics, out var marketId) ||
            !RequireString(metadata.SelectionId, nameof(metadata.SelectionId), diagnostics, out var selectionId) ||
            !RequireCurrency(metadata.SettlementCurrency ?? metadata.QuoteCurrency, nameof(metadata.SettlementCurrency), diagnostics, out var settlementCurrency))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.BettingInstrument(
            metadata.Symbol,
            metadata.Venue,
            marketId,
            selectionId,
            settlementCurrency,
            Tick(metadata),
            metadata.EventTime));
    }

    private static InstrumentContract? NormalizeCfd(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireInstrument(metadata.UnderlyingSymbol, metadata.UnderlyingAssetClass, metadata.UnderlyingVenue ?? metadata.Venue, nameof(metadata.UnderlyingSymbol), diagnostics, out var underlying) ||
            !RequireCurrency(metadata.QuoteCurrency, nameof(metadata.QuoteCurrency), diagnostics, out var quoteCurrency))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.Cfd(
            metadata.Symbol,
            metadata.Venue,
            underlying,
            quoteCurrency,
            Tick(metadata),
            Lot(metadata),
            Multiplier(metadata)));
    }

    private static InstrumentContract? NormalizeObservable(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (metadata.ObservableKind is null)
            diagnostics.Add(ProviderInstrumentDiagnostic.Required(nameof(metadata.ObservableKind)));

        return metadata.ObservableKind is null
            ? null
            : ApplyCommonMetadata(metadata, Contracts.Observable(
                metadata.Symbol,
                metadata.Venue,
                metadata.QuoteCurrency,
                metadata.ObservableKind.Value,
                metadata.SchemaId));
    }

    private static InstrumentContract? NormalizeIndex(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireCurrency(metadata.QuoteCurrency, nameof(metadata.QuoteCurrency), diagnostics, out var quoteCurrency))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.Index(metadata.Symbol, metadata.Venue, quoteCurrency, Tick(metadata)));
    }

    private static InstrumentContract? NormalizeTokenizedAsset(ProviderInstrumentMetadata metadata, List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!RequireCurrency(metadata.QuoteCurrency, nameof(metadata.QuoteCurrency), diagnostics, out var quoteCurrency) ||
            !RequireString(metadata.ChainId, nameof(metadata.ChainId), diagnostics, out var chainId) ||
            !RequireString(metadata.ContractAddress, nameof(metadata.ContractAddress), diagnostics, out var contractAddress))
            return null;

        return ApplyCommonMetadata(metadata, Contracts.TokenizedAsset(
            metadata.Symbol,
            metadata.Venue,
            metadata.AssetClass,
            quoteCurrency,
            Tick(metadata),
            Lot(metadata),
            chainId,
            contractAddress));
    }

    private static bool RequireOptionCore(
        ProviderInstrumentMetadata metadata,
        List<ProviderInstrumentDiagnostic> diagnostics,
        out Instrument underlying,
        out Currency quoteCurrency,
        out Price strike,
        out Instant expiry,
        out OptionRight right)
    {
        var valid = true;

        valid &= RequireInstrument(metadata.UnderlyingSymbol, metadata.UnderlyingAssetClass, metadata.UnderlyingVenue ?? metadata.Venue, nameof(metadata.UnderlyingSymbol), diagnostics, out underlying);
        valid &= RequireCurrency(metadata.QuoteCurrency, nameof(metadata.QuoteCurrency), diagnostics, out quoteCurrency);
        valid &= RequirePrice(metadata.Strike, nameof(metadata.Strike), diagnostics, out strike);
        valid &= RequireInstant(metadata.Expiry, nameof(metadata.Expiry), diagnostics, out expiry);

        if (metadata.OptionRight is null)
        {
            diagnostics.Add(ProviderInstrumentDiagnostic.Required(nameof(metadata.OptionRight)));
            right = default;
            valid = false;
        }
        else
        {
            right = metadata.OptionRight.Value;
        }

        return valid;
    }

    private static ProviderInstrumentMetadata ApplyParsedOptionSymbol(
        ProviderInstrumentMetadata metadata,
        List<ProviderInstrumentDiagnostic> diagnostics)
    {
        if (!IsOptionKind(metadata.Kind))
            return metadata;

        var symbol = metadata.RawSymbol ?? metadata.Symbol;
        if (TryParseOsiOptionSymbol(symbol, metadata.QuoteCurrency ?? Currency.USD, out var parsed) ||
            TryParseDeribitOptionSymbol(symbol, metadata.QuoteCurrency ?? Currency.USD, out parsed))
        {
            return metadata with
            {
                UnderlyingSymbol = metadata.UnderlyingSymbol ?? parsed.UnderlyingSymbol,
                Strike = metadata.Strike ?? parsed.Strike,
                Expiry = metadata.Expiry ?? parsed.Expiry,
                OptionRight = metadata.OptionRight ?? parsed.Right,
                CanonicalSymbol = metadata.CanonicalSymbol ?? parsed.CanonicalSymbol
            };
        }

        if (LooksLikeOsiOptionSymbol(symbol) || LooksLikeDeribitOptionSymbol(symbol))
        {
            diagnostics.Add(new ProviderInstrumentDiagnostic(
                "provider.optionSymbol.parseFailed",
                $"Provider option symbol '{symbol}' looked like a supported native option format but could not be parsed."));
        }

        return metadata;
    }

    private static bool LooksLikeOsiOptionSymbol(string symbol)
    {
        var compact = symbol.Replace(" ", "", StringComparison.Ordinal).Trim();
        if (compact.Length < 16)
            return false;

        var suffixStart = compact.Length - 15;
        return suffixStart > 0
            && compact.Length == suffixStart + 15
            && compact.AsSpan(suffixStart, 6).IndexOfAnyExceptInRange('0', '9') < 0
            && compact[suffixStart + 6] is 'C' or 'P' or 'c' or 'p';
    }

    private static bool LooksLikeDeribitOptionSymbol(string symbol)
    {
        var parts = symbol.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 4
            && parts[1].Length >= 5
            && parts[3].Length == 1
            && parts[3][0] is 'C' or 'P' or 'c' or 'p';
    }

    private static bool IsOptionKind(ProviderInstrumentKind kind) =>
        kind is ProviderInstrumentKind.Option
            or ProviderInstrumentKind.IndexOption
            or ProviderInstrumentKind.FutureOption
            or ProviderInstrumentKind.LinearCryptoOption
            or ProviderInstrumentKind.InverseCryptoOption
            or ProviderInstrumentKind.QuantoCryptoOption
            or ProviderInstrumentKind.BinaryOption;

    private static bool TryParseOsiOptionSymbol(
        string symbol,
        Currency strikeCurrency,
        out ParsedOptionSymbol parsed)
    {
        parsed = default;
        var compact = symbol.Replace(" ", "", StringComparison.Ordinal).Trim();
        if (compact.Length < 16)
            return false;

        var suffixStart = compact.Length - 15;
        var root = compact[..suffixStart];
        if (root.Length == 0)
            return false;

        var date = compact.Substring(suffixStart, 6);
        var rightText = compact[suffixStart + 6];
        var strikeText = compact.Substring(suffixStart + 7, 8);
        if (!TryParseYymmdd(date, out var expiry) ||
            !TryParseRight(rightText, out var right) ||
            !long.TryParse(strikeText, out var strikeScaled))
        {
            return false;
        }

        var canonical = $"{root}{date}{rightText}{strikeText}";
        parsed = new ParsedOptionSymbol(
            root,
            new Price(strikeScaled / 1000m, strikeCurrency),
            expiry,
            right,
            canonical);
        return true;
    }

    private static bool TryParseDeribitOptionSymbol(
        string symbol,
        Currency strikeCurrency,
        out ParsedOptionSymbol parsed)
    {
        parsed = default;
        var parts = symbol.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
            return false;

        if (!TryParseDeribitDate(parts[1], out var expiry) ||
            !decimal.TryParse(parts[2], out var strike) ||
            parts[3].Length != 1 ||
            !TryParseRight(parts[3][0], out var right))
        {
            return false;
        }

        parsed = new ParsedOptionSymbol(
            $"{parts[0]}-PERPETUAL",
            new Price(strike, strikeCurrency),
            expiry,
            right,
            $"{parts[0]}-{parts[1].ToUpperInvariant()}-{parts[2]}-{parts[3].ToUpperInvariant()}");
        return true;
    }

    private static bool TryParseRight(char value, out OptionRight right)
    {
        switch (char.ToUpperInvariant(value))
        {
            case 'C':
                right = OptionRight.Call;
                return true;
            case 'P':
                right = OptionRight.Put;
                return true;
            default:
                right = default;
                return false;
        }
    }

    private static bool TryParseYymmdd(string value, out Instant expiry)
    {
        expiry = default;
        if (value.Length != 6 ||
            !int.TryParse(value[..2], out var yy) ||
            !int.TryParse(value.Substring(2, 2), out var month) ||
            !int.TryParse(value.Substring(4, 2), out var day))
        {
            return false;
        }

        return TryCreateUtcDate(2000 + yy, month, day, out expiry);
    }

    private static bool TryParseDeribitDate(string value, out Instant expiry)
    {
        expiry = default;
        if (value.Length < 7 ||
            !int.TryParse(value[..2], out var day) ||
            !TryParseDeribitMonth(value.Substring(2, 3), out var month) ||
            !int.TryParse(value.Substring(5, 2), out var yy))
        {
            return false;
        }

        return TryCreateUtcDate(2000 + yy, month, day, out expiry);
    }

    private static bool TryParseDeribitMonth(string value, out int month)
    {
        month = value.ToUpperInvariant() switch
        {
            "JAN" => 1,
            "FEB" => 2,
            "MAR" => 3,
            "APR" => 4,
            "MAY" => 5,
            "JUN" => 6,
            "JUL" => 7,
            "AUG" => 8,
            "SEP" => 9,
            "OCT" => 10,
            "NOV" => 11,
            "DEC" => 12,
            _ => 0
        };

        return month != 0;
    }

    private static bool TryCreateUtcDate(int year, int month, int day, out Instant instant)
    {
        try
        {
            instant = Instant.FromDateTimeOffset(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            instant = default;
            return false;
        }
    }

    private readonly record struct ParsedOptionSymbol(
        string UnderlyingSymbol,
        Price Strike,
        Instant Expiry,
        OptionRight Right,
        string CanonicalSymbol);

    private static InstrumentContract ApplyCommonMetadata(ProviderInstrumentMetadata metadata, InstrumentContract contract)
    {
        var tags = new Dictionary<string, string>(contract.Tags, StringComparer.OrdinalIgnoreCase)
        {
            ["provider"] = metadata.Provider.Value
        };

        if (!string.IsNullOrWhiteSpace(metadata.ProviderKind))
            tags["providerKind"] = metadata.ProviderKind;

        foreach (var (key, value) in metadata.ExternalIds)
            tags[key] = value;

        return contract with
        {
            Identity = new ContractIdentity(
                metadata.Symbol,
                metadata.Venue,
                metadata.RawSymbol,
                metadata.ExchangeMic,
                metadata.CanonicalSymbol,
                metadata.SeriesId),
            Grid = contract.Grid with
            {
                PricePrecision = metadata.PricePrecision ?? contract.Grid.PricePrecision,
                SizePrecision = metadata.SizePrecision ?? contract.Grid.SizePrecision,
                PriceIncrementRule = PriceIncrementRule(metadata) ?? contract.Grid.PriceIncrementRule
            },
            Constraints = new TradingConstraints(
                metadata.MinQuantity,
                metadata.MaxQuantity,
                metadata.MinNotional,
                metadata.MaxNotional,
                metadata.MinPrice,
                metadata.MaxPrice),
            Tags = tags
        };
    }

    private static PriceIncrementRule? PriceIncrementRule(ProviderInstrumentMetadata metadata) =>
        metadata.PriceIncrementBands.Count == 0
            ? null
            : new PriceIncrementRule.Piecewise(metadata.PriceIncrementBands);

    private static decimal Tick(ProviderInstrumentMetadata metadata) => metadata.TickSize ?? 0.01m;
    private static decimal Lot(ProviderInstrumentMetadata metadata) => metadata.LotSize ?? 1m;
    private static decimal Multiplier(ProviderInstrumentMetadata metadata) => metadata.Multiplier ?? 1m;

    private static bool RequireCurrency(Currency? value, string field, List<ProviderInstrumentDiagnostic> diagnostics, out Currency currency)
    {
        if (value is { } required && !string.IsNullOrWhiteSpace(required.Code))
        {
            currency = required;
            return true;
        }

        diagnostics.Add(ProviderInstrumentDiagnostic.Required(field));
        currency = default;
        return false;
    }

    private static bool RequirePrice(Price? value, string field, List<ProviderInstrumentDiagnostic> diagnostics, out Price price)
    {
        if (value is { Value: > 0m } required)
        {
            price = required;
            return true;
        }

        diagnostics.Add(ProviderInstrumentDiagnostic.Required(field));
        price = default;
        return false;
    }

    private static bool RequireInstant(Instant? value, string field, List<ProviderInstrumentDiagnostic> diagnostics, out Instant instant)
    {
        if (value is { } required)
        {
            instant = required;
            return true;
        }

        diagnostics.Add(ProviderInstrumentDiagnostic.Required(field));
        instant = default;
        return false;
    }

    private static bool RequireString(string? value, string field, List<ProviderInstrumentDiagnostic> diagnostics, out string text)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text = value;
            return true;
        }

        diagnostics.Add(ProviderInstrumentDiagnostic.Required(field));
        text = string.Empty;
        return false;
    }

    private static bool RequireInstrument(
        string? symbol,
        AssetClass assetClass,
        Venue venue,
        string field,
        List<ProviderInstrumentDiagnostic> diagnostics,
        out Instrument instrument)
    {
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            instrument = new Instrument(new Asset(symbol, assetClass), venue);
            return true;
        }

        diagnostics.Add(ProviderInstrumentDiagnostic.Required(field));
        instrument = Instrument.Unknown;
        return false;
    }
}

public sealed record ProviderInstrumentMetadata
{
    public required ExchangeId Provider { get; init; }
    public required ProviderInstrumentKind Kind { get; init; }
    public string? ProviderKind { get; init; }
    public required string Symbol { get; init; }
    public required Venue Venue { get; init; }
    public string? RawSymbol { get; init; }
    public string? ExchangeMic { get; init; }
    public string? CanonicalSymbol { get; init; }
    public string? SeriesId { get; init; }
    public AssetClass AssetClass { get; init; } = AssetClass.Equity;
    public string? UnderlyingSymbol { get; init; }
    public AssetClass UnderlyingAssetClass { get; init; } = AssetClass.Equity;
    public Venue? UnderlyingVenue { get; init; }
    public Currency? BaseCurrency { get; init; }
    public Currency? QuoteCurrency { get; init; }
    public Currency? SettlementCurrency { get; init; }
    public decimal? TickSize { get; init; }
    public decimal? LotSize { get; init; }
    public int? PricePrecision { get; init; }
    public int? SizePrecision { get; init; }
    public decimal? Multiplier { get; init; }
    public decimal? ContractUnitOfTrade { get; init; }
    public bool Inverse { get; init; }
    public decimal? QuantoConversionRate { get; init; }
    public Instant? Activation { get; init; }
    public Instant? Expiry { get; init; }
    public Price? Strike { get; init; }
    public OptionRight? OptionRight { get; init; }
    public ExerciseStyle? ExerciseStyle { get; init; }
    public OptionExpirationCycle ExpirationCycle { get; init; } = OptionExpirationCycle.Standard;
    public OptionPremiumStyle PremiumStyle { get; init; } = OptionPremiumStyle.Upfront;
    public OptionExercisePolicy ExercisePolicy { get; init; } = OptionExercisePolicy.VenueDefined;
    public OptionAssignmentPolicy AssignmentPolicy { get; init; } = OptionAssignmentPolicy.VenueDefined;
    public IReadOnlyList<Instant> ExerciseDates { get; init; } = [];
    public Instant? EventTime { get; init; }
    public string? EventKey { get; init; }
    public string? MarketId { get; init; }
    public string? SelectionId { get; init; }
    public Money? Payout { get; init; }
    public ObservableKind? ObservableKind { get; init; }
    public string? SchemaId { get; init; }
    public string? ChainId { get; init; }
    public string? ContractAddress { get; init; }
    public Qty? MinQuantity { get; init; }
    public Qty? MaxQuantity { get; init; }
    public Money? MinNotional { get; init; }
    public Money? MaxNotional { get; init; }
    public Price? MinPrice { get; init; }
    public Price? MaxPrice { get; init; }
    public IReadOnlyList<PriceIncrementBand> PriceIncrementBands { get; init; } = [];
    public IReadOnlyDictionary<string, string> ExternalIds { get; init; } = new Dictionary<string, string>();
}

public enum ProviderInstrumentKind : byte
{
    Equity,
    Forex,
    CryptoSpot,
    CommoditySpot,
    Future,
    CryptoFuture,
    CryptoPerpetual,
    Option,
    IndexOption,
    FutureOption,
    LinearCryptoOption,
    InverseCryptoOption,
    QuantoCryptoOption,
    BinaryOption,
    Cfd,
    BettingInstrument,
    Observable,
    Index,
    TokenizedAsset,
    Unsupported
}

public sealed record ProviderInstrumentNormalizationResult(
    InstrumentContract? Contract,
    IReadOnlyList<ProviderInstrumentDiagnostic> Diagnostics)
{
    public bool IsSuccess => Contract is not null && Diagnostics.Count == 0;
}

public readonly record struct ProviderInstrumentDiagnostic(string Code, string Message)
{
    public static ProviderInstrumentDiagnostic Required(string field) =>
        new("provider.field.required", $"Provider instrument metadata is missing required field '{field}'.");
}
