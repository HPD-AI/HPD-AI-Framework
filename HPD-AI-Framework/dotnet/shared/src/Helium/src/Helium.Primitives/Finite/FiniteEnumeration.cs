namespace Helium.Primitives;

public static class FiniteEnumeration
{
    public static FiniteList<Pair<A, B>> Product<A, B>(FiniteList<A> a, FiniteList<B> b)
        where A : IDecidableEq<A>
        where B : IDecidableEq<B>
    {
        var result = new List<Pair<A, B>>();
        foreach (var left in a)
        foreach (var right in b)
            result.Add(new Pair<A, B>(left, right));
        return FiniteList<Pair<A, B>>.FromEnumerable(result);
    }

    public static FiniteList<FiniteList<T>> Permutations<T>(FiniteList<T> values)
        where T : IDecidableEq<T>
    {
        if (values.Length == 0)
            return FiniteList<FiniteList<T>>.Of(FiniteList<T>.Empty);

        var result = new List<FiniteList<T>>();
        for (int i = 0; i < values.Length; i++)
        {
            var head = values[i];
            var rest = RemoveAt(values, i);
            foreach (var tail in Permutations(rest))
                result.Add(tail.Cons(head));
        }

        return FiniteList<FiniteList<T>>.FromEnumerable(result);
    }

    private static FiniteList<T> RemoveAt<T>(FiniteList<T> values, int index)
        where T : IDecidableEq<T>
    {
        var result = new List<T>(Math.Max(0, values.Length - 1));
        for (int i = 0; i < values.Length; i++)
            if (i != index)
                result.Add(values[i]);
        return FiniteList<T>.FromEnumerable(result);
    }
}
