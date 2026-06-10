namespace Rhodium.Platform.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class BarIndicatorAttribute : Attribute
{
    public Type IndicatorType { get; }
    public object[] Params { get; }
    public BarSource Source { get; set; } = BarSource.Close;
    public string? Param { get; set; }
    public string? Param0 { get; set; }
    public string? Param1 { get; set; }
    public string? Param2 { get; set; }
    public string? Param3 { get; set; }
    public string? Param4 { get; set; }
    public string? Param5 { get; set; }
    public string? Param6 { get; set; }
    public string? Param7 { get; set; }

    public BarIndicatorAttribute(Type indicatorType, params object[] @params)
    {
        IndicatorType = indicatorType;
        Params = @params;
    }
}

public enum BarSource
{
    Close,
    Open,
    High,
    Low,
    Volume,
    Bar
}
