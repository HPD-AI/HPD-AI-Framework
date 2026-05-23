namespace Rhodium.Tensor;

/// <summary>
/// Idempotent adjustment kernel that applies corporate action factors.
/// Computes: Adjusted = Raw * PriceScale (for prices) or Raw * VolumeScale (for volume).
/// Missing PriceScale and VolumeScale values are treated as identity scales.
/// Non-identity scales must be precomputed during ingestion.
/// </summary>
public readonly struct AdjustmentKernel : IComputeKernel
{
    public void Execute(ITensorStore store, int pageIndex)
    {
        // PriceScale = SplitFactor * DividendScale (precomputed during ingestion)
        // VolumeScale = 1 / SplitFactor (precomputed during ingestion)
        var priceScales = TensorMarshal.AsReadOnlyDoubles(store.GetPage(Field.PriceScale, pageIndex));
        var volScales = TensorMarshal.AsReadOnlyDoubles(store.GetPage(Field.VolumeScale, pageIndex));

        ApplyPrice(store, pageIndex, Field.OpenRaw, Field.Open, priceScales);
        ApplyPrice(store, pageIndex, Field.HighRaw, Field.High, priceScales);
        ApplyPrice(store, pageIndex, Field.LowRaw, Field.Low, priceScales);
        ApplyPrice(store, pageIndex, Field.CloseRaw, Field.Close, priceScales);
        ApplyPrice(store, pageIndex, Field.BidRaw, Field.Bid, priceScales);
        ApplyPrice(store, pageIndex, Field.AskRaw, Field.Ask, priceScales);

        ApplyVolume(store, pageIndex, volScales);
        ApplySize(store, pageIndex, Field.BidSizeRaw, Field.BidSize, volScales);
        ApplySize(store, pageIndex, Field.AskSizeRaw, Field.AskSize, volScales);
    }

    private static void ApplyPrice(
        ITensorStore store,
        int page,
        VectorField<PriceF64> rawF,
        VectorField<PriceF64> adjF,
        ReadOnlySpan<double> scales)
    {
        var raw = TensorMarshal.AsReadOnlyDoubles(store.GetPage(rawF, page));
        var adj = TensorMarshal.AsDoubles(store.GetPage(adjF, page));

        ApplyScale(raw, scales, adj);
    }

    private static void ApplyVolume(ITensorStore store, int page, ReadOnlySpan<double> scales)
    {
        var raw = TensorMarshal.AsReadOnlyDoubles(store.GetPage(Field.VolumeRaw, page));
        var adj = TensorMarshal.AsDoubles(store.GetPage(Field.Volume, page));

        ApplyScale(raw, scales, adj);
    }

    private static void ApplySize(
        ITensorStore store,
        int page,
        VectorField<SizeF64> rawF,
        VectorField<SizeF64> adjF,
        ReadOnlySpan<double> scales)
    {
        var raw = TensorMarshal.AsReadOnlyDoubles(store.GetPage(rawF, page));
        var adj = TensorMarshal.AsDoubles(store.GetPage(adjF, page));

        ApplyScale(raw, scales, adj);
    }

    private static void ApplyScale(ReadOnlySpan<double> raw, ReadOnlySpan<double> scales, Span<double> adjusted)
    {
        for (var i = 0; i < raw.Length; i++)
        {
            var scale = scales[i] == 0.0 ? 1.0 : scales[i];
            adjusted[i] = raw[i] * scale;
        }
    }
}
