namespace HPD.Math.Autodiff;

/// <summary>
/// One explicit reverse-mode tape node.
/// </summary>
public readonly struct ReverseNode<T>
{
    public ReverseNode(ReverseOpCode opCode, int left, int right, T primal)
    {
        OpCode = opCode;
        Left = left;
        Right = right;
        Primal = primal;
    }

    public ReverseOpCode OpCode { get; }

    public int Left { get; }

    public int Right { get; }

    public T Primal { get; }
}
