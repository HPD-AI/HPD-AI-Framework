namespace HPDOS.Core.Platform.Resources;

public readonly record struct ResourceId
{
    private readonly Guid _value;

    public ResourceId() => _value = Guid.NewGuid();
    private ResourceId(Guid value) => _value = value;

    public static ResourceId New()          => new();
    public static ResourceId From(Guid g)   => new(g);

    public override string ToString() => _value.ToString();
    public Guid ToGuid()              => _value;
}
