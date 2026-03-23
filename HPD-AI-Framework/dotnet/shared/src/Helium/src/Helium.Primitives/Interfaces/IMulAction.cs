namespace Helium.Primitives;

/// <summary>
/// A monoid action: g · t with 1 · t = t and (g * h) · t = g · (h · t).
/// </summary>
public interface IMulAction<G, T>
    where T : IMulAction<G, T>
{
    static abstract T Smul(G g, T t);
}
