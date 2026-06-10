namespace Rhodium.Tensor;

/// <summary>
/// Rectangular projection of jagged universes for kernel consumption.
/// </summary>
public readonly struct TensorBasis
{
    public int AssetDimension { get; }
    public int VariantDimension { get; }
    public int Rank => AssetDimension * VariantDimension;

    public TensorBasis(int assetDim, int variantDim)
    {
        AssetDimension = assetDim;
        VariantDimension = variantDim;
    }

    /// <summary>
    /// Convert (Asset, Variant) to linear VirtualIndex.
    /// </summary>
    public int ToLinear(int assetIdx, int variantIdx) =>
        assetIdx * VariantDimension + variantIdx;
}
