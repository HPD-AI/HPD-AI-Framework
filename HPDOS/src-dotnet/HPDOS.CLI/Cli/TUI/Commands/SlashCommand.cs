namespace HPDOS.Shell.Cli.TUI.Commands;

/// <summary>
/// Represents a slash command that can be executed in the console.
/// </summary>
public class SlashCommand
{
    public string Name { get; set; } = "";
    public List<string> AltNames { get; set; } = new();
    public string Description { get; set; } = "";
    public bool AutoExecute { get; set; } = false;
    public bool Hidden { get; set; } = false;
    public string Category { get; set; } = "Built-in";
    public Func<CommandContext, Task<CommandResult>>? Action { get; set; }

    public bool Matches(string normalizedQuery)
    {
        if (Name.ToLowerInvariant() == normalizedQuery) return true;
        return AltNames.Exists(alt => alt.ToLowerInvariant() == normalizedQuery);
    }
}

/// <summary>Context passed to command actions.</summary>
public class CommandContext
{
    public string RawInput { get; set; } = "";
    public string CommandName { get; set; } = "";
    public string Arguments { get; set; } = "";
    public IConsoleSession Session { get; set; } = null!;
    public AgentUIRenderer? UIRenderer { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>Result returned from command execution.</summary>
public class CommandResult
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public bool ShouldExit { get; set; } = false;
    public bool ShouldClearHistory { get; set; } = false;

    public static CommandResult Ok(string? message = null) => new() { Success = true, Message = message };
    public static CommandResult Error(string message) => new() { Success = false, Message = message };
    public static CommandResult Exit(string? message = null) => new() { Success = true, ShouldExit = true, Message = message };
}

/// <summary>A command suggestion with match information.</summary>
public class CommandSuggestion
{
    public SlashCommand Command { get; set; } = null!;
    public int MatchScore { get; set; }
    public string DisplayName { get; set; } = "";
    public List<int> MatchedIndices { get; set; } = new();
}
