namespace HPD.TUI.Layout;

public readonly record struct SizePolicy
{
    private SizePolicy(SizePolicyKind kind, int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Kind = kind;
        Value = value;
    }

    public SizePolicyKind Kind { get; }

    public int Value { get; }

    public static SizePolicy Content() => new(SizePolicyKind.Content, 0);

    public static SizePolicy Fixed(int cells) => new(SizePolicyKind.Fixed, cells);

    public static SizePolicy Fill(int weight = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weight);
        return new SizePolicy(SizePolicyKind.Fill, weight);
    }
}

public enum SizePolicyKind
{
    Content,
    Fixed,
    Fill
}
