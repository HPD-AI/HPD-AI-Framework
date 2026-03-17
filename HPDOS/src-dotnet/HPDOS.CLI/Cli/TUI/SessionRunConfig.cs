namespace HPDOS.Shell.Cli.TUI;

/// <summary>
/// Per-session run configuration set via /config.
/// Stored in contextData["RunConfig"] and merged into every StreamRunConfigDto.
/// </summary>
public class SessionRunConfig
{
    public double? Temperature           { get; set; }
    public int?    MaxOutputTokens       { get; set; }
    public double? TopP                  { get; set; }
    public double? FrequencyPenalty      { get; set; }
    public double? PresencePenalty       { get; set; }
    /// <summary>null = default, "none"/"low"/"medium"/"high"/"extra-high"</summary>
    public string? ReasoningEffort       { get; set; }
    public string? AdditionalSystemInstructions { get; set; }
    public bool    SkipTools             { get; set; } = false;

    public bool IsEmpty =>
        Temperature == null &&
        MaxOutputTokens == null &&
        TopP == null &&
        FrequencyPenalty == null &&
        PresencePenalty == null &&
        ReasoningEffort == null &&
        AdditionalSystemInstructions == null &&
        !SkipTools;
}
