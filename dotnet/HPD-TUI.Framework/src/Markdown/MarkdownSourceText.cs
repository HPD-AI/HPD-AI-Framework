namespace HPD.TUI.Markdown;

/// <summary>Persistent chunked canonical Markdown text shared by parse snapshots.</summary>
internal sealed class MarkdownSourceText
{
    private readonly MarkdownSourceText? _prefix;
    private readonly ReadOnlyMemory<char> _suffix;
    private string? _materialized;

    private MarkdownSourceText(MarkdownSourceText? prefix, ReadOnlyMemory<char> suffix)
    {
        _prefix = prefix;
        _suffix = suffix;
        Length = checked((prefix?.Length ?? 0) + suffix.Length);
    }

    internal static MarkdownSourceText Empty { get; } = new(null, ReadOnlyMemory<char>.Empty);
    internal int Length { get; }
    internal long RetainedBytes => (long)Length * sizeof(char);

    internal MarkdownSourceText Append(ReadOnlyMemory<char> suffix)
        => suffix.IsEmpty ? this : new(this, suffix.ToArray());

    internal char this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= Length) throw new ArgumentOutOfRangeException(nameof(index));
            var node = this;
            while (true)
            {
                var prefixLength = node._prefix?.Length ?? 0;
                if (index >= prefixLength) return node._suffix.Span[index - prefixLength];
                node = node._prefix!;
            }
        }
    }

    internal string Slice(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start > Length - length) throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0) return string.Empty;
        return string.Create(length, (Text: this, Start: start), static (destination, state) =>
            state.Text.CopyTo(state.Start, destination));
    }

    internal string Materialize()
        => _materialized ??= Slice(0, Length);

    private void CopyTo(int start, Span<char> destination)
    {
        if (destination.IsEmpty) return;
        var end = checked(start + destination.Length);
        var segments = new List<(ReadOnlyMemory<char> Memory, int AbsoluteStart)>();
        for (var node = this; node is not null; node = node._prefix)
        {
            var absoluteStart = (node._prefix?.Length ?? 0);
            if (node._suffix.Length > 0 && absoluteStart < end && absoluteStart + node._suffix.Length > start)
                segments.Add((node._suffix, absoluteStart));
        }
        for (var index = segments.Count - 1; index >= 0; index--)
        {
            var (memory, absoluteStart) = segments[index];
            var copyStart = Math.Max(start, absoluteStart);
            var copyEnd = Math.Min(end, absoluteStart + memory.Length);
            memory.Span.Slice(copyStart - absoluteStart, copyEnd - copyStart)
                .CopyTo(destination[(copyStart - start)..]);
        }
    }
}
