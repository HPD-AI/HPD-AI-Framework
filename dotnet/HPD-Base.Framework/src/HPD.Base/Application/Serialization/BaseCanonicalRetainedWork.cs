using System.Text;

namespace HPD.Base;

/// <summary>Counts one deeply owned canonical graph without using CLR allocation estimates.</summary>
internal struct BaseCanonicalRetainedWork
{
    private long _bytes;

    internal readonly long Bytes => _bytes;

    internal void AddContainer() => Add(8);

    internal void AddSequence(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        Add(checked(8L + count * 8L));
    }

    internal void AddInteger() => Add(8);
    internal void AddBoolean() => Add(1);
    internal void AddFixed16() => Add(16);
    internal void AddFixed24() => Add(24);

    internal void AddString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Add(checked(4L + Encoding.UTF8.GetByteCount(value)));
    }

    internal void AddNullableString(string? value)
    {
        Add(1);
        if (value is not null) AddString(value);
    }

    internal void AddBytes(long length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Add(checked(4L + length));
    }

    internal void AddNullableFixed16(bool present)
    {
        Add(1);
        if (present) AddFixed16();
    }

    internal void AddNullableFixed24(bool present)
    {
        Add(1);
        if (present) AddFixed24();
    }

    internal void AddNullableBoolean(bool? value)
    {
        Add(1);
        if (value.HasValue) AddBoolean();
    }

    internal void AddNullableInteger(bool present)
    {
        Add(1);
        if (present) AddInteger();
    }

    internal void Add(long bytes) => _bytes = checked(_bytes + bytes);
}
