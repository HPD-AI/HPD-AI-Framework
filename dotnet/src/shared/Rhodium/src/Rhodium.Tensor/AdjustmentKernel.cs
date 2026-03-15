using System.Numerics.Tensors;

namespace Rhodium.Tensor;

/// <summary>
/// Idempotent adjustment kernel that applies corporate action factors.
/// Computes: Adjusted = Raw * PriceScale (for prices) or Raw * VolumeScale (for volume).
/// PriceScale and VolumeScale must be precomputed during ingestion.
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

        ApplyVolume(store, pageIndex, volScales);
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

        TensorPrimitives.Multiply(raw, scales, adj);
    }

    private static void ApplyVolume(ITensorStore store, int page, ReadOnlySpan<double> scales)
    {
        var raw = TensorMarshal.AsReadOnlyDoubles(store.GetPage(Field.VolumeRaw, page));
        var adj = TensorMarshal.AsDoubles(store.GetPage(Field.Volume, page));

        TensorPrimitives.Multiply(raw, scales, adj);
    }
}
