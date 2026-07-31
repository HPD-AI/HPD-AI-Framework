namespace HPD.Base.Descriptors;

public sealed record RuntimeDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? InstanceId { get; init; }
    public string? Environment { get; init; }
    public string? BasePath { get; init; }
    public RuntimeMode Mode { get; init; }
}

public enum RuntimeMode
{
    Development,
    Production,
    Test,
    ReadOnly,
    Custom
}
