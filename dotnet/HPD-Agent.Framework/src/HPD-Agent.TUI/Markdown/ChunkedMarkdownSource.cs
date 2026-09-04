namespace HPD.Agent.TUI.Markdown;

/// <summary>Append-only canonical source with bounded chunks and indexed range lookup.</summary>
internal sealed class ChunkedMarkdownSource
{
    private const int PreferredChunkLength = 4096;
    private readonly List<Chunk> _chunks = [];
    public int Length { get; private set; }

    public char this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= Length) throw new ArgumentOutOfRangeException(nameof(index));
            var chunk = _chunks[FindChunk(index)];
            return chunk.Buffer[index - chunk.Start];
        }
    }

    public void Append(string value)
    {
        if (value.Length == 0) return;
        var consumed = 0;
        while (consumed < value.Length)
        {
            if (_chunks.Count == 0 || _chunks[^1].Count == _chunks[^1].Buffer.Length)
                _chunks.Add(new Chunk(Length, new char[Math.Max(PreferredChunkLength, value.Length - consumed)]));
            var chunk = _chunks[^1];
            var count = Math.Min(value.Length - consumed, chunk.Buffer.Length - chunk.Count);
            value.AsSpan(consumed, count).CopyTo(chunk.Buffer.AsSpan(chunk.Count));
            chunk.Count += count;
            consumed += count;
            Length = checked(Length + count);
        }
    }

    public int FindFinalNewline()
    {
        for (var index = _chunks.Count - 1; index >= 0; index--)
        {
            var chunk = _chunks[index];
            var local = chunk.Buffer.AsSpan(0, chunk.Count).LastIndexOf('\n');
            if (local >= 0) return chunk.Start + local;
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
            var chunkIndex = state.Source.FindChunk(state.Start);
            var sourceIndex = state.Start;
            var written = 0;
            while (written < destination.Length)
            {
                var chunk = state.Source._chunks[chunkIndex++];
                var local = sourceIndex - chunk.Start;
                var count = Math.Min(chunk.Count - local, destination.Length - written);
                chunk.Buffer.AsSpan(local, count).CopyTo(destination[written..]);
                sourceIndex += count;
                written += count;
            }
        });
    }

    private int FindChunk(int absoluteIndex)
    {
        var low = 0;
        var high = _chunks.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var chunk = _chunks[middle];
            if (absoluteIndex < chunk.Start) high = middle - 1;
            else if (absoluteIndex >= chunk.Start + chunk.Count) low = middle + 1;
            else return middle;
        }
        throw new InvalidOperationException("Canonical source index is outside retained chunks.");
    }

    private sealed class Chunk(int start, char[] buffer)
    {
        public int Start { get; } = start;
        public char[] Buffer { get; } = buffer;
        public int Count { get; set; }
    }
}
