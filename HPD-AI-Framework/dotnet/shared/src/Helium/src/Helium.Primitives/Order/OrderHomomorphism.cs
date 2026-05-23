namespace Helium.Primitives;

/// <summary>
/// Function advertised as order-preserving. Preservation is a contract.
/// </summary>
public readonly struct OrderHomomorphism<A, B>
    where A : IPartialOrder<A>
    where B : IPartialOrder<B>
{
    private readonly Func<A, B> _apply;

    public OrderHomomorphism(Func<A, B> apply)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    public B Apply(A value) => _apply(value);

    public static OrderHomomorphism<A, C> Compose<C>(
        OrderHomomorphism<A, B> first,
        OrderHomomorphism<B, C> second)
        where C : IPartialOrder<C> =>
        new(value => second.Apply(first.Apply(value)));
}
