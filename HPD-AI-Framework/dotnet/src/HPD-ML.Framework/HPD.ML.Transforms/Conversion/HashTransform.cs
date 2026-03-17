namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Hashes column values to a fixed-size integer range (feature hashing / hashing trick).
/// </summary>
public sealed class HashTransform : ITransform
{
    private readonly string _columnName;
    private readonly string? _outputColumnName;
    private readonly int _numBits;
    private readonly uint _seed;

    public HashTransform(string columnName, int numBits = 16, uint seed = 314489979, string? outputColumnName = null)
    {
        _columnName = columnName;
        _numBits = numBits;
        _seed = seed;
        _outputColumnName = outputColumnName;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var outName = _outputColumnName ?? _columnName;
        var columns = inputSchema.Columns.Select(c =>
            c.Name == _columnName && outName == _columnName
                ? new Column(outName, FieldType.Scalar<uint>(), c.Annotations)
                : c).ToList();

        if (outName != _columnName)
            columns.Add(new Column(outName, FieldType.Scalar<uint>()));

        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);
        var outCol = _outputColumnName ?? _columnName;
        uint mask = (1u << _numBits) - 1;

        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(
                input.GetCursor(columns.Append(_columnName).Distinct()),
                row =>
                {
                    var values = new Dictionary<string, object>();
                    foreach (var col in outputSchema.Columns)
                    {
                        if (col.Name == outCol)
                        {
                            var raw = row.GetValue<object>(_columnName)?.ToString() ?? "";
                            uint hash = MurmurHash3(raw, _seed) & mask;
                            values[outCol] = hash;
                        }
                        else
                        {
                            values[col.Name] = row.GetValue<object>(col.Name);
                        }
                    }
                    return new DictionaryRow(outputSchema, values);
                }),
            input.RowCount,
            input.Ordering);
    }

    internal static uint MurmurHash3(string key, uint seed)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(key);
        uint h = seed;
        int i = 0;

        while (i + 4 <= bytes.Length)
        {
            uint k = BitConverter.ToUInt32(bytes, i);
            k *= 0xcc9e2d51; k = (k << 15) | (k >> 17); k *= 0x1b873593;
            h ^= k; h = (h << 13) | (h >> 19); h = h * 5 + 0xe6546b64;
            i += 4;
        }

        uint remaining = 0;
        switch (bytes.Length - i)
        {
            case 3: remaining ^= (uint)bytes[i + 2] << 16; goto case 2;
            case 2: remaining ^= (uint)bytes[i + 1] << 8; goto case 1;
            case 1: remaining ^= bytes[i];
                remaining *= 0xcc9e2d51;
                remaining = (remaining << 15) | (remaining >> 17);
                remaining *= 0x1b873593;
                h ^= remaining;
                break;
        }

        h ^= (uint)bytes.Length;
        h ^= h >> 16; h *= 0x85ebca6b; h ^= h >> 13; h *= 0xc2b2ae35; h ^= h >> 16;
        return h;
    }
}
