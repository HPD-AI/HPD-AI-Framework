namespace HPD.Agent.Adapters;

/// <summary>
/// Declares the platform key format for a partial record type.
/// The source generator emits static <c>Format(…)</c> and <c>Parse(string)</c>
/// methods from the format string.
/// </summary>
/// <param name="format">
/// Format string using <c>{PropertyName}</c> slots that match record primary constructor
/// parameters (case-insensitive). Example: <c>"slack:{Channel}:{ThreadTs}"</c>.
/// </param>
/// <remarks>
/// The generator emits diagnostic <c>HPD-A007</c> if a slot name has no matching
/// property on the record.
/// </remarks>
/// <example>
/// <code>
/// [ThreadId("slack:{Channel}:{ThreadTs}")]
/// public partial record SlackThreadId(string Channel, string ThreadTs);
/// // Generator emits:
/// //   public static string Format(string channel, string threadTs)
/// //       => $"slack:{channel}:{threadTs}";
/// //   public static SlackThreadId Parse(string value) { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ThreadIdAttribute(string format) : Attribute
{
    /// <summary>Format string with <c>{PropertyName}</c> slots.</summary>
    public string Format => format;
}
