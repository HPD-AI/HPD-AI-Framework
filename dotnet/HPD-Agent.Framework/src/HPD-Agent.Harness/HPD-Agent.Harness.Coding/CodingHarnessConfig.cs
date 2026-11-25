using System.Collections.Immutable;
using System.Text.Json;
using HPD.Agent;
using HPDOS.ToolHarnesses.Middleware;

/// <summary>Portable, deeply immutable settings for the complete coding capability.</summary>
public record CodingHarnessConfig : IHarnessSettingsValidator
{
    [HarnessRunSetting] public ExecuteCommandOptions Commands { get; init; } = new();
    [HarnessRunSetting] public GlobSearchOptions Glob { get; init; } = new();
    [HarnessRunSetting] public EditFileConfig EditFile { get; init; } = new();
    [HarnessRunSetting] public FileMutationConfig Files { get; init; } = new();
    public EnvironmentContextConfig Environment { get; init; } = new();
    public LanguageServerSettings LanguageServer { get; init; } = new();

    public void ValidateSettings()
    {
        if (Glob.MaxTraversalEntries < 1 || Glob.TraversalTimeoutMilliseconds < 1)
            throw new AgentRunConfigurationException("CodingGlobSettingsInvalid", "config.glob", "Traversal limits must be positive.");
        if (Commands.DefaultTimeout <= TimeSpan.Zero || Commands.MaxTimeout < Commands.DefaultTimeout ||
            Commands.MaxActiveBackgroundCommands < 1 || Commands.MaxInlineCommandOutputChars < 1 ||
            Commands.MaxPersistedOutputBytes < 1 || Commands.MaxOutputChunkEventChars < 1 ||
            Commands.MaxOutputChunkEventsPerSecond < 1 || Commands.MaxOutputChunkEventsPerCommand < 1 ||
            Commands.MaxReadOutputDelay < TimeSpan.Zero || Commands.BackgroundStartSettleDelay < TimeSpan.Zero ||
            Commands.ProgressAfter < TimeSpan.Zero || Commands.AutoBackgroundAfter < TimeSpan.Zero || Commands.InactivityTimeout < TimeSpan.Zero)
            throw new AgentRunConfigurationException("CodingCommandSettingsInvalid", "config.commands", "Invalid command timeout or output limit.");

        if (EditFile is null || Files is null)
            throw new AgentRunConfigurationException("CodingFileMutationSettingsInvalid", "config", "Edit and file mutation settings are required.");

        EditFile.Validate();
        Files.Validate();
    }
}

/// <summary>Limits and deterministic recovery policy for EditFile.</summary>
public sealed record EditFileConfig
{
    public int MaxEdits { get; init; } = 20;
    public int MaxNormalizationNotes { get; init; } = 50;
    public int MinRecoveryOldStringChars { get; init; } = 10;
    public int MaxRecoveryCandidates { get; init; } = 20;
    public int MaxReplacementCandidates { get; init; } = 20_000;
    public EditRecoveryConfig Recovery { get; init; } = new();

    public void Validate()
    {
        if (MaxEdits < 1 ||
            MaxNormalizationNotes < 1 ||
            MinRecoveryOldStringChars < 10 ||
            MaxRecoveryCandidates < 1 ||
            MaxReplacementCandidates < 1)
        {
            throw new AgentRunConfigurationException(
                "CodingEditSettingsInvalid",
                "config.editFile",
                "Edit limits must be positive and recovery minimum characters cannot be less than 10.");
        }

        Recovery?.Validate();
    }
}

/// <summary>Enabled deterministic recovery strategies and protocol token map.</summary>
public sealed record EditRecoveryConfig
{
    public static ImmutableDictionary<string, string> DefaultDesanitizationMap { get; } =
        CreateDefaultDesanitizationMap();

    public bool QuoteNormalization { get; init; } = true;
    public bool Desanitization { get; init; } = true;
    public bool EscapedStringNormalization { get; init; } = true;
    public bool TrimmedBoundary { get; init; } = true;
    public bool IndentationOnlyBlock { get; init; } = true;
    public ImmutableDictionary<string, string> DesanitizationMap { get; init; } =
        DefaultDesanitizationMap;

    public void Validate()
    {
        if (DesanitizationMap is null ||
            DesanitizationMap.Any(pair =>
                string.IsNullOrEmpty(pair.Key) ||
                string.IsNullOrEmpty(pair.Value)))
        {
            throw new AgentRunConfigurationException(
                "CodingEditRecoverySettingsInvalid",
                "config.editFile.recovery",
                "Desanitization entries must have non-empty keys and values.");
        }
    }

    private static ImmutableDictionary<string, string> CreateDefaultDesanitizationMap()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.Ordinal);
        builder["<fnr>"] = "<function_results>";
        builder["< META_START >"] = "<META_START>";
        builder["< META_END >"] = "<META_END>";
        builder["< EOT >"] = "<EOT>";
        builder["< META >"] = "<META>";
        builder["< SOS >"] = "<SOS>";
        return builder.ToImmutable();
    }
}

/// <summary>Shared limits for EditFile and WriteFile mutations and events.</summary>
public sealed record FileMutationConfig
{
    public long MaxMutableFileBytes { get; init; } = 50L * 1024 * 1024;
    public int MaxEventSnapshotChars { get; init; } = 500_000;
    public int MaxEventTextEditChars { get; init; } = 100_000;
    public int MaxEventHunks { get; init; } = 200;
    public int DiffContextLines { get; init; } = 3;
    public int MaxEventNotes { get; init; } = 50;

    public void Validate()
    {
        if (MaxMutableFileBytes is < 1 or > int.MaxValue ||
            MaxEventSnapshotChars < 1 ||
            MaxEventTextEditChars < 1 ||
            MaxEventHunks < 1 ||
            DiffContextLines < 0 ||
            MaxEventNotes < 1)
        {
            throw new AgentRunConfigurationException(
                "CodingFileMutationSettingsInvalid",
                "config.files",
                "File mutation limits must be positive, context must be non-negative, and mutable files must fit the workspace read contract.");
        }
    }
}

/// <summary>Settings only. Provider implementations remain in the language-server registry.</summary>
public sealed record LanguageServerSettings
{
    public bool Enabled { get; init; } = true;
    public ImmutableArray<string> EnabledServers { get; init; } = [];
    public ImmutableArray<string> DisabledServers { get; init; } = [];
    public ImmutableArray<string> EnabledExperimentalServers { get; init; } = [];
    public LanguageServerFeedbackOptions Feedback { get; init; } = new();
    public ImmutableArray<string> WorkspaceFolders { get; init; } = [];
    public ImmutableDictionary<string, JsonElement> WorkspaceConfiguration { get; init; } = ImmutableDictionary<string, JsonElement>.Empty;
    public int ConfigVersion { get; init; }

    public LanguageServerOptions ToRuntime()
    {
        var configurationIdentity = WorkspaceConfiguration.Count == 0 ? null : Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",
                WorkspaceConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + pair.Value.GetRawText())))));
        return new LanguageServerOptions
        {
            Enabled = Enabled, EnabledServers = EnabledServers.ToImmutableHashSet(StringComparer.Ordinal),
            DisabledServers = DisabledServers.ToImmutableHashSet(StringComparer.Ordinal),
            EnabledExperimentalServers = EnabledExperimentalServers.ToImmutableHashSet(StringComparer.Ordinal),
            Feedback = Feedback, WorkspaceFolders = WorkspaceFolders,
            WorkspaceConfiguration = WorkspaceConfiguration.ToDictionary(pair => pair.Key, pair => (object?)pair.Value.Clone(), StringComparer.Ordinal),
            WorkspaceConfigurationIdentity = configurationIdentity, ConfigVersion = ConfigVersion
        };
    }
}
