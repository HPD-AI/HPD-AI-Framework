namespace Rhodium.Platform.Attributes;

/// <summary>
/// Declares a generated multi-output bar indicator view.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class BarIndicatorGroupAttribute : Attribute
{
    public Type IndicatorType { get; }
    public object[] Parameters { get; }
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

    public BarIndicatorGroupAttribute(Type indicatorType, params object[] parameters)
    {
        IndicatorType = indicatorType;
        Parameters = parameters;
    }
}
