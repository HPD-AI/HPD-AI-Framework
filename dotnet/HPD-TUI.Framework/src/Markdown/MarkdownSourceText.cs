namespace HPD.TUI.Markdown;

/// <summary>Persistent balanced canonical Markdown text shared by parse snapshots.</summary>
internal sealed class MarkdownSourceText
{
    private readonly MarkdownSourceText? _left;
    private readonly MarkdownSourceText? _right;
    private readonly ReadOnlyMemory<char> _memory;
    private readonly int _height;

    private MarkdownSourceText(ReadOnlyMemory<char> memory)
    { _memory = memory; Length = memory.Length; _height = 1; }

    private MarkdownSourceText(MarkdownSourceText left, MarkdownSourceText right)
    { _left = left; _right = right; Length = checked(left.Length + right.Length); _height = Math.Max(left._height, right._height) + 1; }

    internal static MarkdownSourceText Empty { get; } = new(ReadOnlyMemory<char>.Empty);
    internal static MarkdownSourceText FromString(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Length == 0 ? Empty : new(source.AsMemory());
    }
    internal int Length { get; }
    internal long RetainedBytes => (long)Length * sizeof(char);
    internal MarkdownSourceText Append(ReadOnlyMemory<char> suffix)
        => suffix.IsEmpty ? this : Concat(this, new MarkdownSourceText(suffix.ToArray()));

    internal char this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= Length) throw new ArgumentOutOfRangeException(nameof(index));
            var node = this;
            while (node._left is not null)
            {
                if (index < node._left.Length) node = node._left;
                else { index -= node._left.Length; node = node._right!; }
            }
            return node._memory.Span[index];
        }
    }

    internal string Slice(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start > Length - length) throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0) return string.Empty;
        return string.Create(length, (Text: this, Start: start), static (destination, state) => state.Text.CopyTo(state.Start, destination));
    }

    internal string Materialize() => Slice(0, Length);

    private static MarkdownSourceText Concat(MarkdownSourceText left, MarkdownSourceText right)
    {
        if (left.Length == 0) return right;
        if (right.Length == 0) return left;
        if (left._height > right._height + 1)
        {
            var ll = left._left!; var lr = left._right!;
            return ll._height >= lr._height ? new(ll, new(lr, right)) : new(new(ll, lr._left!), new(lr._right!, right));
        }
        if (right._height > left._height + 1)
        {
            var rl = right._left!; var rr = right._right!;
            return rr._height >= rl._height ? new(new(left, rl), rr) : new(new(left, rl._left!), new(rl._right!, rr));
        }
        return new(left, right);
    }

    private void CopyTo(int start, Span<char> destination) => CopyRange(this, start, destination);
    private static void CopyRange(MarkdownSourceText node, int start, Span<char> destination)
    {
        if (destination.IsEmpty) return;
        if (node._left is null) { node._memory.Span.Slice(start, destination.Length).CopyTo(destination); return; }
        var leftLength = node._left.Length;
        if (start < leftLength)
        {
            var count = Math.Min(destination.Length, leftLength - start);
            CopyRange(node._left, start, destination[..count]);
            destination = destination[count..]; start = 0;
        }
        else start -= leftLength;
        if (!destination.IsEmpty) CopyRange(node._right!, start, destination);
    }
}
