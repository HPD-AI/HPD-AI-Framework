using System;
using System.Text;

namespace HPD.VCS.Core;

/// <summary>
/// Simple helper class for creating content-hashable objects from basic data types.
/// Useful for generating IDs from simple string or byte content where a full data structure is not needed.
/// </summary>
public readonly record struct SimpleContentHashable : IContentHashable
{
    private readonly byte[] _bytes;

    /// <summary>
    /// Creates a SimpleContentHashable from a string.
    /// </summary>
    /// <param name="content">The string content to hash</param>
    public SimpleContentHashable(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _bytes = Encoding.UTF8.GetBytes(content);
    }

    /// <summary>
    /// Creates a SimpleContentHashable from byte data.
    /// </summary>
    /// <param name="bytes">The byte data to hash</param>
    public SimpleContentHashable(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        _bytes = new byte[bytes.Length];
        Array.Copy(bytes, _bytes, bytes.Length);
    }

    /// <summary>
    /// Creates a SimpleContentHashable from a read-only span of bytes.
    /// </summary>
    /// <param name="bytes">The byte data to hash</param>
    public SimpleContentHashable(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes.ToArray();
    }

    /// <summary>
    /// Returns the canonical byte representation for content hashing.
    /// For SimpleContentHashable, this is just the raw bytes.
    /// </summary>
    public byte[] GetBytesForHashing()
    {
        // Return a defensive copy to maintain immutability
        var copy = new byte[_bytes.Length];
        Array.Copy(_bytes, copy, _bytes.Length);
        return copy;
    }

    /// <summary>
    /// Gets the size of the content in bytes.
    /// </summary>
    public int Size => _bytes.Length;

    /// <summary>
    /// Gets whether the content is empty.
    /// </summary>
    public bool IsEmpty => _bytes.Length == 0;

    /// <summary>
    /// Creates a ChangeId from the given string content.
    /// Convenience method for generating change IDs.
    /// </summary>
    /// <param name="content">The content to generate a change ID from</param>
    /// <returns>A ChangeId based on the content</returns>
    public static ChangeId CreateChangeId(string content)
    {
        var hashable = new SimpleContentHashable(content);
        return ObjectHasher.ComputeId<SimpleContentHashable, ChangeId>(hashable, ObjectHasher.ChangeTypePrefix);
    }

    /// <summary>
    /// Creates a ChangeId from the given byte content.
    /// Convenience method for generating change IDs.
    /// </summary>
    /// <param name="bytes">The content to generate a change ID from</param>
    /// <returns>A ChangeId based on the content</returns>
    public static ChangeId CreateChangeId(byte[] bytes)
    {
        var hashable = new SimpleContentHashable(bytes);
        return ObjectHasher.ComputeId<SimpleContentHashable, ChangeId>(hashable, ObjectHasher.ChangeTypePrefix);
    }
}
