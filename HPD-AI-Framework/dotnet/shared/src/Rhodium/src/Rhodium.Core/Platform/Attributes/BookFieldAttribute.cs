namespace Rhodium.Platform.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class BookFieldAttribute : Attribute
{
    public string? Name { get; set; }
    public bool ReadOnly { get; set; } = false;
}
