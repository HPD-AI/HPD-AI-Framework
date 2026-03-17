using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace HPD.Agent.Adapters.Slack;

/// <summary>
/// Resolves Slack user IDs to display names with an in-process TTL cache.
/// Cache entries are held for one hour — sufficient for the process lifetime
/// of a typical bot deployment, with no external store required.
/// </summary>
public sealed class SlackUserCache(SlackApiClient api)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    // Regex for Slack user mentions in mrkdwn: <@U123> or <@U123|name>
    private static readonly Regex MentionRegex =
        new(@"<@([UW][A-Z0-9]+)(?:\|([^>]*))?>" , RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    /// <summary>
    /// Returns the display name for a Slack user ID.
    /// Falls back to the raw userId if the API call fails.
    /// </summary>
    public async Task<string> GetDisplayNameAsync(string userId, CancellationToken ct)
    {
        if (_cache.TryGetValue(userId, out var entry) && !entry.IsExpired)
            return entry.DisplayName;

        var info = await api.FetchUserInfoAsync(userId, ct);
        var name = info?.Profile?.DisplayName
            ?? info?.Profile?.RealName
            ?? info?.Name
            ?? userId;

        _cache[userId] = new CacheEntry(name);
        return name;
    }

    /// <summary>
    /// Resolves <c>&lt;@U123&gt;</c> and <c>&lt;@U123|name&gt;</c> patterns in inbound mrkdwn text.
    /// Skips the bot's own user ID so the adapter's mention-detection still works downstream.
    /// </summary>
    public async Task<string> ResolveInlineMentionsAsync(
        string text, string? botUserId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var matches = MentionRegex.Matches(text);
        if (matches.Count == 0) return text;

        // Prefetch all unique user IDs in parallel before replacing
        var userIds = matches
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Where(id => id != botUserId)
            .ToList();

        var names = await Task.WhenAll(
            userIds.Select(id => GetDisplayNameAsync(id, ct)));

        var lookup = userIds.Zip(names).ToDictionary(p => p.First, p => p.Second);

        return MentionRegex.Replace(text, m =>
        {
            var id = m.Groups[1].Value;
            if (id == botUserId) return m.Value; // preserve bot's own mention marker
            return lookup.TryGetValue(id, out var name) ? $"@{name}" : $"@{id}";
        });
    }

    private readonly record struct CacheEntry(string DisplayName, DateTime CachedAt)
    {
        public CacheEntry(string displayName) : this(displayName, DateTime.UtcNow) { }
        public bool IsExpired => DateTime.UtcNow - CachedAt > CacheTtl;
    }
}
