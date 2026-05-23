namespace Rhodium.Platform.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class WindowAttribute : Attribute
{
    public WindowAttribute(params int[] lengths)
    {
        Lengths = lengths;
    }

    public int[] Lengths { get; }
}
