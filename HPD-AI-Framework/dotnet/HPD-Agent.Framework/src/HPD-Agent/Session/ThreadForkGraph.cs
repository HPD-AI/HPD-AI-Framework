using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text.Json;

namespace HPD.Agent;

/// <summary>
/// Projects durable thread lineage into user-visible fork choice groups.
/// </summary>
public static class ThreadForkGraph
{
    private const string InputMessageIdMetadataKey = "inputMessageId";

    /// <summary>
    /// Builds fork groups for ordinary user-visible branches.
    /// Direct lineage remains on <see cref="Thread.ForkedFrom"/>; groups are semantic
    /// choice points based on canonical shared context.
    /// </summary>
    public static IReadOnlyList<ThreadForkGroup> BuildVisibleForkGroups(IReadOnlyList<Thread> threads)
    {
        var threadsById = threads.ToDictionary(thread => thread.Id, StringComparer.Ordinal);
        var groups = threads
            .Where(IsVisibleBranchThread)
            .Where(thread => thread.ForkedFrom != null)
            .Select(thread => new
            {
                Thread = thread,
                Key = ResolveForkGroupKey(thread, threadsById)
            })
            .Where(entry => threadsById.TryGetValue(entry.Key.SourceThreadId, out var sourceThread) &&
                IsVisibleBranchThread(sourceThread))
            .GroupBy(
                entry => entry.Key,
                entry => entry.Thread,
                ForkGroupKeyComparer.Instance)
            .OrderBy(group => group.Key.SourceThreadId, StringComparer.Ordinal)
            .ThenBy(group => group.Min(thread => thread.ForkedAtMessageIndex ?? -1))
            .ThenBy(group => group.Key.ForkedAtMessageId ?? string.Empty, StringComparer.Ordinal);

        var forkGroups = new List<ThreadForkGroup>();
        foreach (var group in groups)
        {
            if (!threadsById.TryGetValue(group.Key.SourceThreadId, out var sourceThread))
                continue;
            if (!IsVisibleBranchThread(sourceThread))
                continue;

            var forks = group
                .OrderBy(thread => thread.CreatedAt)
                .ThenBy(thread => thread.Id, StringComparer.Ordinal)
                .ToList();

            var sourceChoice = ResolveMemberChoiceMessage(sourceThread, group.Key.ForkedAtMessageId);
            var members = new List<ThreadForkGroupMember>
            {
                new(
                    sourceThread,
                    Index: 0,
                    IsSource: true,
                    sourceChoice.MessageId,
                    sourceChoice.Index)
            };

            for (var i = 0; i < forks.Count; i++)
            {
                var forkChoice = ResolveMemberChoiceMessage(forks[i], group.Key.ForkedAtMessageId);
                members.Add(new ThreadForkGroupMember(
                    forks[i],
                    i + 1,
                    IsSource: false,
                    forkChoice.MessageId,
                    forkChoice.Index));
            }

            var forkedAtMessageIndex = forks
                .Select(thread => thread.ForkedAtMessageIndex)
                .FirstOrDefault(index => index != null);

            forkGroups.Add(new ThreadForkGroup(
                CreateForkGroupId(group.Key.SourceThreadId, group.Key.ForkedAtMessageId),
                group.Key.SourceThreadId,
                group.Key.ForkedAtMessageId,
                forkedAtMessageIndex,
                ResolveChoiceMessageIndex(forkedAtMessageIndex),
                members));
        }

        return forkGroups;
    }

    private static ForkGroupKey ResolveForkGroupKey(
        Thread forkThread,
        IReadOnlyDictionary<string, Thread> threadsById)
    {
        var sourceThread = ResolveForkGroupSourceThread(forkThread, threadsById);
        return new ForkGroupKey(sourceThread.Id, forkThread.ForkedAtMessageId);
    }

    private static Thread ResolveForkGroupSourceThread(
        Thread forkThread,
        IReadOnlyDictionary<string, Thread> threadsById)
    {
        if (!threadsById.TryGetValue(forkThread.ForkedFrom ?? string.Empty, out var directSource))
            return forkThread;

        if (forkThread.ForkedAtMessageId is not { Length: > 0 } forkedAtMessageId)
            return ResolveRootThread(forkThread, threadsById) ?? directSource;

        foreach (var ancestor in EnumerateForkGroupSourceCandidates(forkThread, directSource, threadsById))
        {
            if (ancestor.Messages.Any(message => string.Equals(message.MessageId, forkedAtMessageId, StringComparison.Ordinal)))
                return ancestor;
        }

        return directSource;
    }

    private static Thread? ResolveRootThread(
        Thread thread,
        IReadOnlyDictionary<string, Thread> threadsById)
    {
        foreach (var ancestor in EnumerateOrderedAncestors(thread, threadsById))
        {
            if (IsVisibleBranchThread(ancestor))
                return ancestor;
        }

        var current = thread;
        while (current.ForkedFrom is { Length: > 0 } parentId &&
            threadsById.TryGetValue(parentId, out var parent))
        {
            current = parent;
        }

        return IsVisibleBranchThread(current) ? current : null;
    }

    private static IEnumerable<Thread> EnumerateForkGroupSourceCandidates(
        Thread forkThread,
        Thread directSource,
        IReadOnlyDictionary<string, Thread> threadsById)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ancestor in EnumerateOrderedAncestors(forkThread, threadsById))
        {
            if (seen.Add(ancestor.Id) && IsVisibleBranchThread(ancestor))
                yield return ancestor;
        }

        if (seen.Add(directSource.Id) && IsVisibleBranchThread(directSource))
            yield return directSource;
    }

    private static IEnumerable<Thread> EnumerateOrderedAncestors(
        Thread thread,
        IReadOnlyDictionary<string, Thread> threadsById)
    {
        if (thread.Ancestors is null)
            yield break;

        foreach (var ancestorId in thread.Ancestors
            .OrderBy(pair => ParseAncestorDepth(pair.Key))
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value))
        {
            if (threadsById.TryGetValue(ancestorId, out var ancestor))
                yield return ancestor;
        }
    }

    private static bool IsVisibleBranchThread(Thread thread) =>
        thread.Kind == ThreadKind.MainAgent &&
        thread.Visibility == ThreadVisibility.Visible;

    private static string CreateForkGroupId(string sourceThreadId, string? forkedAtMessageId) =>
        $"{sourceThreadId}@{forkedAtMessageId ?? "root"}";

    private static int ResolveChoiceMessageIndex(int? forkedAtMessageIndex) =>
        forkedAtMessageIndex is null ? 0 : forkedAtMessageIndex.Value + 1;

    private static (string? MessageId, int? Index) ResolveMemberChoiceMessage(
        Thread thread,
        string? forkedAtMessageId)
    {
        if (thread.Messages.Count == 0)
            return (null, null);

        if (TryResolveMetadataInputMessage(thread, out var metadataChoice))
            return metadataChoice;

        if (forkedAtMessageId is null)
            return ResolveFirstUserMessageAtOrAfter(thread, 0) ??
                (thread.Messages[0].MessageId, 0);

        if (!TryFindMessageIndex(thread, forkedAtMessageId, out var boundaryIndex))
            return (null, null);

        return ResolveFirstUserMessageAtOrAfter(thread, boundaryIndex) ??
            ResolveMessageAt(thread, boundaryIndex + 1);
    }

    private static bool TryResolveMetadataInputMessage(
        Thread thread,
        out (string? MessageId, int? Index) choice)
    {
        choice = (null, null);
        if (!TryGetMetadataString(thread, InputMessageIdMetadataKey, out var inputMessageId))
            return false;

        if (!TryFindMessageIndex(thread, inputMessageId, out var index))
            return false;

        choice = (inputMessageId, index);
        return true;
    }

    private static (string? MessageId, int? Index)? ResolveFirstUserMessageAtOrAfter(Thread thread, int startIndex)
    {
        for (var index = Math.Max(0, startIndex); index < thread.Messages.Count; index++)
        {
            if (thread.Messages[index].Role == ChatRole.User)
                return (thread.Messages[index].MessageId, index);
        }

        return null;
    }

    private static (string? MessageId, int? Index) ResolveMessageAt(Thread thread, int index)
    {
        if (index < 0 || index >= thread.Messages.Count)
            return (null, null);

        return (thread.Messages[index].MessageId, index);
    }

    private static bool TryFindMessageIndex(Thread thread, string messageId, out int index)
    {
        for (index = 0; index < thread.Messages.Count; index++)
        {
            if (string.Equals(thread.Messages[index].MessageId, messageId, StringComparison.Ordinal))
                return true;
        }

        index = -1;
        return false;
    }

    private static bool TryGetMetadataString(Thread thread, string key, out string value)
    {
        value = string.Empty;
        if (!thread.Metadata.TryGetValue(key, out var rawValue))
            return false;

        value = rawValue switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
            JsonElement { ValueKind: JsonValueKind.Number } element => element.GetRawText(),
            _ => Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? string.Empty
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static int ParseAncestorDepth(string value) =>
        int.TryParse(value, out var depth) ? depth : int.MaxValue;

    private readonly record struct ForkGroupKey(string SourceThreadId, string? ForkedAtMessageId);

    private sealed class ForkGroupKeyComparer : IEqualityComparer<ForkGroupKey>
    {
        public static readonly ForkGroupKeyComparer Instance = new();

        public bool Equals(ForkGroupKey x, ForkGroupKey y) =>
            StringComparer.Ordinal.Equals(x.SourceThreadId, y.SourceThreadId) &&
            StringComparer.Ordinal.Equals(x.ForkedAtMessageId, y.ForkedAtMessageId);

        public int GetHashCode(ForkGroupKey obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.SourceThreadId),
                obj.ForkedAtMessageId is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.ForkedAtMessageId));
    }
}

public sealed record ThreadForkGroup(
    string Id,
    string SourceThreadId,
    string? ForkedAtMessageId,
    int? ForkedAtMessageIndex,
    int ChoiceMessageIndex,
    IReadOnlyList<ThreadForkGroupMember> Members);

public sealed record ThreadForkGroupMember(
    Thread Thread,
    int Index,
    bool IsSource,
    string? ChoiceMessageId,
    int? ChoiceMessageIndex);
