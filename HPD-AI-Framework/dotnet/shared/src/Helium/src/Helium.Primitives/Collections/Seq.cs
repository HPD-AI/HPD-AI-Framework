namespace Helium.Primitives;

/// <summary>
/// Replayable sequence wrapper. Each enumeration asks the source factory for a fresh sequence.
/// </summary>
public readonly struct Seq<T> : IEnumerable<T>
    where T : IDecidableEq<T>
{
    private readonly Func<IEnumerable<T>>? _source;

    public Seq(Func<IEnumerable<T>> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    private IEnumerable<T> Source => _source?.Invoke() ?? [];

    public static Seq<T> Empty => new(() => []);

    public static Seq<T> FromEnumerable(IEnumerable<T> values) =>
        new(() => values);

    public Seq<U> Map<U>(Func<T, U> f)
        where U : IDecidableEq<U>
    {
        var source = _source;
        return new Seq<U>(() => (source?.Invoke() ?? []).Select(f));
    }

    public Seq<T> Filter(Func<T, bool> predicate)
    {
        var source = _source;
        return new Seq<T>(() => (source?.Invoke() ?? []).Where(predicate));
    }

    public FiniteList<T> Take(int count) =>
        FiniteList<T>.FromEnumerable(Source.Take(count));

    public Seq<T> Drop(int count)
    {
        var source = _source;
        return new Seq<T>(() => (source?.Invoke() ?? []).Skip(count));
    }

    public IEnumerator<T> GetEnumerator() => Source.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
