namespace Helium.Primitives;

/// <summary>
/// Lazy infinite stream. Consumers must use finite observations such as Take.
/// </summary>
public sealed class Stream<T>
    where T : IDecidableEq<T>
{
    private readonly Lazy<Stream<T>> _tail;

    public Stream(T head, Func<Stream<T>> tail)
    {
        Head = head;
        _tail = new Lazy<Stream<T>>(tail ?? throw new ArgumentNullException(nameof(tail)));
    }

    public T Head { get; }
    public Stream<T> Tail => _tail.Value;

    public FiniteList<T> Take(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Take count must be nonnegative.");

        var values = new T[count];
        var current = this;
        for (int i = 0; i < count; i++)
        {
            values[i] = current.Head;
            current = current.Tail;
        }
        return FiniteList<T>.FromEnumerable(values);
    }

    public Stream<U> Map<U>(Func<T, U> f)
        where U : IDecidableEq<U> =>
        new(f(Head), () => Tail.Map(f));

    public Stream<Pair<T, U>> Zip<U>(Stream<U> other)
        where U : IDecidableEq<U> =>
        new(new Pair<T, U>(Head, other.Head), () => Tail.Zip(other.Tail));
}
