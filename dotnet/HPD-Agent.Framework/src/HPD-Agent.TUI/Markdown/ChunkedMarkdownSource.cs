namespace HPD.Agent.TUI.Markdown;

internal sealed class ChunkedMarkdownSource
{
    private readonly List<string> _chunks = [];

    public int Length { get; private set; }

    public void Append(string value)
    {
        if (value.Length == 0) return;
        _chunks.Add(value);
        Length = checked(Length + value.Length);
    }

    public int FindFinalNewline()
    {
        var absolute = Length;
        for (var chunkIndex = _chunks.Count - 1; chunkIndex >= 0; chunkIndex--)
        {
            var chunk = _chunks[chunkIndex];
            absolute -= chunk.Length;
            var local = chunk.LastIndexOf('\n');
            if (local >= 0) return absolute + local;
        }
        return -1;
    }

    public string Slice(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start > Length - length) throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0) return string.Empty;
        return string.Create(length, (Source: this, Start: start), static (destination, state) =>
        {
            var remainingStart = state.Start;
            var written = 0;
            foreach (var chunk in state.Source._chunks)
            {
                if (remainingStart >= chunk.Length) { remainingStart -= chunk.Length; continue; }
                var count = Math.Min(chunk.Length - remainingStart, destination.Length - written);
                chunk.AsSpan(remainingStart, count).CopyTo(destination[written..]);
                written += count;
                remainingStart = 0;
                if (written == destination.Length) break;
            }
        });
    }
}
