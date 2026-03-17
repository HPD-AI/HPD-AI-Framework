namespace Rhodium.Primitives;

/// <summary>
/// Buy or Sell. The most fundamental trading concept.
/// </summary>
public enum Side : sbyte
{
    Sell = -1,
    None = 0,
    Buy = 1
}

public static class SideExtensions
{
    public static Side Opposite(this Side side) => side switch
    {
        Side.Buy => Side.Sell,
        Side.Sell => Side.Buy,
        _ => Side.None
    };

    public static int Sign(this Side side) => (int)side;

    public static Side FromSign(int sign) => sign switch
    {
        > 0 => Side.Buy,
        < 0 => Side.Sell,
        _ => Side.None
    };

    public static Side FromQty(Qty qty) => qty.Value switch
    {
        > 0 => Side.Buy,
        < 0 => Side.Sell,
        _ => Side.None
    };
}
