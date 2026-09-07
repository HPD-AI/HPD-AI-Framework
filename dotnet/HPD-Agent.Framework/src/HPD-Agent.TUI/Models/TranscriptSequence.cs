using System.Collections;

namespace HPD.Agent.TUI.Models;

/// <summary>
/// Represents an immutable, indexed transcript sequence whose snapshots share unchanged storage.
/// </summary>
public sealed class TranscriptSequence : IReadOnlyList<TranscriptEntry>
{
    private const int ChunkCapacity = 64;
    private readonly TranscriptEntry[][] _chunks;

    private TranscriptSequence(TranscriptEntry[][] chunks, int count)
    {
        _chunks = chunks;
        Count = count;
    }

    /// <summary>Gets the empty transcript sequence.</summary>
    public static TranscriptSequence Empty { get; } = new([], 0);

    /// <inheritdoc />
    public int Count { get; }

    /// <inheritdoc />
    public TranscriptEntry this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _chunks[index / ChunkCapacity][index % ChunkCapacity];
        }
    }

    internal TranscriptSequence Append(TranscriptEntry entry)
    {
        var offset = Count % ChunkCapacity;
        if (offset == 0)
        {
            var chunks = new TranscriptEntry[_chunks.Length + 1][];
            Array.Copy(_chunks, chunks, _chunks.Length);
            chunks[^1] = new TranscriptEntry[ChunkCapacity];
            chunks[^1][0] = entry;
            return new TranscriptSequence(chunks, Count + 1);
        }

        var updated = (TranscriptEntry[][])_chunks.Clone();
        updated[^1] = (TranscriptEntry[])updated[^1].Clone();
        updated[^1][offset] = entry;
        return new TranscriptSequence(updated, Count + 1);
    }

    internal TranscriptSequence Replace(int index, TranscriptEntry entry)
    {
        _ = this[index];
        var updated = (TranscriptEntry[][])_chunks.Clone();
        var chunkIndex = index / ChunkCapacity;
        updated[chunkIndex] = (TranscriptEntry[])updated[chunkIndex].Clone();
        updated[chunkIndex][index % ChunkCapacity] = entry;
        return new TranscriptSequence(updated, Count);
    }

    internal static TranscriptSequence Create(IEnumerable<TranscriptEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sequence = Empty;
        foreach (var entry in entries)
            sequence = sequence.Append(entry);
        return sequence;
    }

    /// <inheritdoc />
    public IEnumerator<TranscriptEntry> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
