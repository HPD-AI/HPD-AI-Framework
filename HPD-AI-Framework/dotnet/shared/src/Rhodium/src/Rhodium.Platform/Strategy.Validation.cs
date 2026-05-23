using System.Diagnostics;
using System.Runtime.CompilerServices;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform;

public abstract partial class Strategy
{
    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ValidateAssetBounds<T>(AssetId id, VectorField<T> field, in MarketKernel market)
        where T : unmanaged
    {
        if (id.VirtualIndex < 0 || id.VirtualIndex >= market.UniverseSize)
        {
            throw new TensorAccessException(
                $"Asset ID {id.VirtualIndex} out of bounds [0, {market.UniverseSize}) when accessing field '{field.Name}'.");
        }

        if (!market.HasField(field))
        {
            throw new TensorAccessException(
                $"Field '{field.Name}' has not been registered in the market tensor store.");
        }
    }
}
