namespace Rhodium.Platform.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class QuoteFieldAttribute : Attribute
{
    public string? Name { get; set; }
    public bool ReadOnly { get; set; } = false;
}
