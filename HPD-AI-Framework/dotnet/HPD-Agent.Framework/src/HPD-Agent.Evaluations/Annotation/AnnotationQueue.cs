// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Concurrent;
using HPD.Agent.Evaluations.Integration;

namespace HPD.Agent.Evaluations.Annotation;

/// <summary>
/// Options for AnnotationQueue behavior.
/// </summary>
public sealed class AnnotationQueueOptions
{
    /// <summary>
    /// Maximum number of pending annotation items before new items are dropped.
    /// Default: 1000.
    /// </summary>
    public int MaxQueueSize { get; init; } = 1000;

    /// <summary>
    /// How long an annotation item can be locked (claimed) before it is
    /// released back to the queue for another reviewer. Default: 30 minutes.
    /// </summary>
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Minimum score threshold below which turns are automatically queued for
    /// human annotation. Items with score > threshold are not queued.
    /// Set to null to disable automatic queueing by score.
    /// </summary>
    public double? AutoQueueBelowScore { get; init; }
}

/// <summary>
/// Status of a queued annotation item.
/// </summary>
public enum AnnotationStatus
{
    /// <summary>Awaiting review — no reviewer has claimed it.</summary>
    Pending,

    /// <summary>Claimed by a reviewer — locked against concurrent claim.</summary>
    Locked,

    /// <summary>Review completed.</summary>
    Completed,
}

/// <summary>
/// An item queued for human annotation.
/// </summary>
public sealed class AnnotationItem
{
    public string AnnotationId { get; init; } = Guid.NewGuid().ToString();
    public string SessionId { get; init; } = string.Empty;
    public string BranchId { get; init; } = string.Empty;
    public int TurnIndex { get; init; }
    public string? TriggerEvaluatorName { get; init; }
    public double? TriggerScore { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public AnnotationStatus Status { get; internal set; } = AnnotationStatus.Pending;
    public DateTimeOffset? LockedAt { get; internal set; }
    public string? LockedBy { get; internal set; }
    public string? HumanLabel { get; internal set; }
    public string? HumanComment { get; internal set; }
    public DateTimeOffset? CompletedAt { get; internal set; }
}

/// <summary>
/// In-memory queue for human annotation of flagged agent turns.
/// Integrates with EvaluationMiddleware: when a TrackTrend evaluator scores below
/// AnnotationQueueOptions.AutoQueueBelowScore, the turn is automatically enqueued.
///
/// For production use, replace the in-memory backing store with a database-backed
/// implementation.
/// </summary>
public sealed class AnnotationQueue
{
    private readonly AnnotationQueueOptions _options;
    private readonly ConcurrentDictionary<string, AnnotationItem> _items = new();

    public AnnotationQueue(AnnotationQueueOptions? options = null)
    {
        _options = options ?? new AnnotationQueueOptions();
    }

    // ── Enqueue ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Add a turn to the annotation queue. Returns false if the queue is full.
    /// </summary>
    public bool Enqueue(AnnotationItem item)
    {
        if (_items.Count >= _options.MaxQueueSize)
            return false;

        _items[item.AnnotationId] = item;
        return true;
    }

    /// <summary>
    /// Enqueue a turn from an EvalScoreEvent when the score falls below the threshold.
    /// Called by EvaluationMiddleware when AutoQueueBelowScore is configured.
    /// Returns the annotation ID, or null if not queued (score above threshold or queue full).
    /// </summary>
    public string? TryEnqueueFromScore(
        string sessionId, string branchId, int turnIndex,
        string evaluatorName, double score)
    {
        if (_options.AutoQueueBelowScore.HasValue && score > _options.AutoQueueBelowScore.Value)
            return null;

        var item = new AnnotationItem
        {
            SessionId = sessionId,
            BranchId = branchId,
            TurnIndex = turnIndex,
            TriggerEvaluatorName = evaluatorName,
            TriggerScore = score,
        };

        return Enqueue(item) ? item.AnnotationId : null;
    }

    // ── Claim ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Claim the next pending item for review. Returns null if the queue is empty
    /// or all items are locked/completed.
    /// </summary>
    public AnnotationItem? ClaimNext(string reviewerId)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var item in _items.Values.OrderBy(i => i.CreatedAt))
        {
            if (item.Status == AnnotationStatus.Pending ||
                (item.Status == AnnotationStatus.Locked &&
                 item.LockedAt.HasValue &&
                 now - item.LockedAt.Value > _options.LockTimeout))
            {
                item.Status = AnnotationStatus.Locked;
                item.LockedAt = now;
                item.LockedBy = reviewerId;
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// Claim a specific item by annotation ID.
    /// Returns null if not found, already completed, or locked by another reviewer within timeout.
    /// </summary>
    public AnnotationItem? Claim(string annotationId, string reviewerId)
    {
        if (!_items.TryGetValue(annotationId, out var item))
            return null;

        var now = DateTimeOffset.UtcNow;
        if (item.Status == AnnotationStatus.Completed)
            return null;

        if (item.Status == AnnotationStatus.Locked &&
            item.LockedAt.HasValue &&
            now - item.LockedAt.Value <= _options.LockTimeout)
            return null; // locked by someone else

        item.Status = AnnotationStatus.Locked;
        item.LockedAt = now;
        item.LockedBy = reviewerId;
        return item;
    }

    // ── Complete ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Submit a human label for a claimed annotation item.
    /// Returns false if the item is not found or not locked by this reviewer.
    /// </summary>
    public bool Complete(string annotationId, string reviewerId, string label, string? comment = null)
    {
        if (!_items.TryGetValue(annotationId, out var item))
            return false;

        if (item.Status != AnnotationStatus.Locked || item.LockedBy != reviewerId)
            return false;

        item.Status = AnnotationStatus.Completed;
        item.HumanLabel = label;
        item.HumanComment = comment;
        item.CompletedAt = DateTimeOffset.UtcNow;
        return true;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public IReadOnlyList<AnnotationItem> GetPending()
        => _items.Values.Where(i => i.Status == AnnotationStatus.Pending)
            .OrderBy(i => i.CreatedAt).ToList();

    public IReadOnlyList<AnnotationItem> GetCompleted()
        => _items.Values.Where(i => i.Status == AnnotationStatus.Completed)
            .OrderBy(i => i.CompletedAt).ToList();

    public int PendingCount =>
        _items.Values.Count(i => i.Status == AnnotationStatus.Pending);

    public int TotalCount => _items.Count;
}
