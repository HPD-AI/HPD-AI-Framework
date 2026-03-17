namespace HPDOS.Shell.Cli.TUI.Commands;

/// <summary>
/// Registry for all slash commands. Provides lookup, fuzzy matching, and suggestions.
/// </summary>
public class CommandRegistry
{
    private readonly List<SlashCommand> _commands = new();

    public void Register(SlashCommand command) => _commands.Add(command);

    public void RegisterMany(params SlashCommand[] commands) => _commands.AddRange(commands);

    public List<SlashCommand> GetVisibleCommands() => _commands.Where(c => !c.Hidden).ToList();

    public SlashCommand? FindExact(string normalizedName) =>
        _commands.FirstOrDefault(c => c.Matches(normalizedName));

    public List<CommandSuggestion> FindSuggestions(string normalizedQuery, int maxResults = 10)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return GetVisibleCommands()
                .Take(maxResults)
                .Select(c => new CommandSuggestion { Command = c, MatchScore = 100, DisplayName = c.Name })
                .ToList();
        }

        var suggestions = new List<CommandSuggestion>();

        foreach (var command in GetVisibleCommands())
        {
            var nameMatch = FuzzyMatch(command.Name.ToLowerInvariant(), normalizedQuery);
            if (nameMatch != null)
            {
                suggestions.Add(new CommandSuggestion { Command = command, MatchScore = nameMatch.Score, DisplayName = command.Name, MatchedIndices = nameMatch.MatchedIndices });
                continue;
            }

            foreach (var alias in command.AltNames)
            {
                var aliasMatch = FuzzyMatch(alias.ToLowerInvariant(), normalizedQuery);
                if (aliasMatch != null)
                {
                    suggestions.Add(new CommandSuggestion { Command = command, MatchScore = aliasMatch.Score, DisplayName = alias, MatchedIndices = aliasMatch.MatchedIndices });
                    break;
                }
            }
        }

        return suggestions.OrderByDescending(s => s.MatchScore).ThenBy(s => s.DisplayName).Take(maxResults).ToList();
    }

    private static FuzzyMatchResult? FuzzyMatch(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return null;

        var matchedIndices = new List<int>();
        int textIndex = 0, queryIndex = 0, score = 0, consecutive = 0;

        while (textIndex < text.Length && queryIndex < query.Length)
        {
            if (text[textIndex] == query[queryIndex])
            {
                matchedIndices.Add(textIndex);
                consecutive++;
                score += 10 + (consecutive * 5);
                if (textIndex == 0 || text[textIndex - 1] == '-' || text[textIndex - 1] == '_')
                    score += 15;
                queryIndex++;
            }
            else
            {
                consecutive = 0;
            }
            textIndex++;
        }

        if (queryIndex != query.Length) return null;

        score += 100 - text.Length;
        if (matchedIndices.Count > 1)
            score -= (matchedIndices[^1] - matchedIndices[0] - matchedIndices.Count + 1) * 2;

        return new FuzzyMatchResult { Score = Math.Max(0, score), MatchedIndices = matchedIndices };
    }

    private class FuzzyMatchResult
    {
        public int Score { get; set; }
        public List<int> MatchedIndices { get; set; } = new();
    }
}
