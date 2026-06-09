namespace HPD.Math.Autodiff;

/// <summary>
/// Read-only view over the written portion of an explicit reverse-mode tape.
/// </summary>
public readonly ref struct ReverseTapeView<T>
{
    public ReverseTapeView(ReadOnlySpan<ReverseNode<T>> nodes)
    {
        Nodes = nodes;
    }

    public ReadOnlySpan<ReverseNode<T>> Nodes { get; }

    public int Count => Nodes.Length;

    public ReverseNode<T> this[int index] => Nodes[index];
}
