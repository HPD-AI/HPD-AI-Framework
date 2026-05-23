using System.Runtime.CompilerServices;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform.Extensions;

public static class Fields
{
    public static readonly VectorField<FactorF64> RSI_14 = new("RSI_14");
}

public static class DataExtensions
{
    extension(in MarketKernel market)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetClose(AssetId id)
            => market.GetScalar(Field.Close, id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetOpen(AssetId id)
            => market.GetScalar(Field.Open, id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetHigh(AssetId id)
            => market.GetScalar(Field.High, id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetLow(AssetId id)
            => market.GetScalar(Field.Low, id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetVolume(AssetId id)
            => market.GetScalar(Field.Volume, id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetRsi14(AssetId id)
            => market.GetScalar(Fields.RSI_14, id);
    }
}
