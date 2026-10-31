using System.Collections.Immutable;
using System.Text.Json;
using HPD.Agent;
using HPDOS.ToolHarnesses.Middleware;

/// <summary>Portable, deeply immutable settings for the complete coding capability.</summary>
public record CodingHarnessConfig : IHarnessSettingsValidator
{
    [HarnessRunSetting] public ExecuteCommandOptions Commands { get; init; } = new();
    [HarnessRunSetting] public GlobSearchOptions Glob { get; init; } = new();
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
