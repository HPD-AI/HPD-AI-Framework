namespace Rhodium.Platform.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class ParamAttribute : Attribute
{
    public string? Name { get; init; }
}
