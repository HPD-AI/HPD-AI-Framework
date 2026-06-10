namespace Rhodium.Platform.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class BarFieldAttribute : Attribute
{
    public string? Name { get; set; }
    public bool ReadOnly { get; set; } = false;
}
