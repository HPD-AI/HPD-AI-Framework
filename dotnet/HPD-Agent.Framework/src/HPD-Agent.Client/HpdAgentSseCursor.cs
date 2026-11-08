using System.Globalization;
using System.Text.Json;

namespace HPD.Agent.Client;

/// <summary>A committed thread-journal cursor in generation:sequence form.</summary>
public readonly record struct HpdAgentSseAppliedCursor(long Generation, long SequenceNumber)
{
    /// <summary>The cursor used when beginning at the start of the current journal.</summary>
    public static HpdAgentSseAppliedCursor Initial { get; } = new(1, 0);

    /// <summary>Gets whether the cursor is valid as an input resume position.</summary>
    public bool IsValid => Generation > 0 && SequenceNumber >= 0;

    /// <summary>Parses a server cursor without silently accepting malformed values.</summary>
    public static bool TryParse(string? value, out HpdAgentSseAppliedCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1 ||
            value.IndexOf(':', separator + 1) >= 0 ||
            !long.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var generation) ||
            !long.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) ||
            generation <= 0 ||
            sequence < 0)
        {
            return false;
        }

        cursor = new HpdAgentSseAppliedCursor(generation, sequence);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Generation}:{SequenceNumber}");
}

/// <summary>
/// Validates and advances committed SSE cursors while admitting live-only events.
/// The same state instance can be reused across transport reconnects.
/// </summary>
public sealed class HpdAgentSseState
{
    private readonly object _sync = new();
    private HpdAgentSseAppliedCursor _appliedCursor;

    public HpdAgentSseState(HpdAgentSseAppliedCursor? appliedCursor = null)
    {
        _appliedCursor = appliedCursor ?? HpdAgentSseAppliedCursor.Initial;
        if (!_appliedCursor.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(appliedCursor),
                "The SSE cursor must have a positive generation and non-negative sequence.");
        }
    }

    /// <summary>Gets a thread-safe snapshot of the last delivered committed cursor.</summary>
    public HpdAgentSseAppliedCursor AppliedCursor
    {
        get
        {
            lock (_sync)
                return _appliedCursor;
        }
    }

    /// <summary>
    /// Applies one raw SSE message. Duplicate or replayed committed events return
    /// <see cref="HpdAgentSseDecision.Duplicate"/> and must not be dispatched.
    /// </summary>
    public HpdAgentSseDecision Apply(HpdAgentSseMessage message, long? eventSequenceNumber)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.Equals(message.EventName, "thread-journal-rebased", StringComparison.Ordinal))
        {
            var rebase = ParseRebase(message.Data);
            lock (_sync)
            {
                _appliedCursor = new HpdAgentSseAppliedCursor(rebase.CurrentGeneration, 0);
            }
            throw rebase;
        }

        if (string.Equals(message.EventName, "live-agent-event", StringComparison.Ordinal))
            return HpdAgentSseDecision.Live;

        if (!string.Equals(message.EventName, "agent-event", StringComparison.Ordinal))
            return HpdAgentSseDecision.Control;

        if (!HpdAgentSseAppliedCursor.TryParse(message.Id, out var cursor))
        {
            cursor = eventSequenceNumber is > 0
                ? new HpdAgentSseAppliedCursor(AppliedCursor.Generation, eventSequenceNumber.Value)
                : throw new HpdAgentStreamException(
                    "A committed SSE event did not include a valid generation:sequence cursor.");
        }
        else if (eventSequenceNumber is not null && cursor.SequenceNumber != eventSequenceNumber.Value)
        {
            throw new HpdAgentStreamException(
                $"SSE cursor '{cursor}' did not match event threadSequenceNumber '{eventSequenceNumber}'.");
        }

        lock (_sync)
        {
            if (cursor.Generation != _appliedCursor.Generation)
            {
                throw new HpdAgentJournalRebasedException(
                    _appliedCursor.Generation,
                    cursor.Generation);
            }
            if (cursor.SequenceNumber <= _appliedCursor.SequenceNumber)
                return HpdAgentSseDecision.Duplicate;
            _appliedCursor = cursor;
            return HpdAgentSseDecision.Committed;
        }
    }

    private static HpdAgentJournalRebasedException ParseRebase(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("previousGeneration", out var previousElement) ||
            !data.TryGetProperty("currentGeneration", out var currentElement) ||
            !TryPositiveInt64(previousElement, out var previous) ||
            !TryPositiveInt64(currentElement, out var current))
        {
            throw new HpdAgentStreamException(
                "The thread-journal-rebased control message had an invalid payload.");
        }
        return new HpdAgentJournalRebasedException(previous, current);
    }

    private static bool TryPositiveInt64(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetInt64(out value) && value > 0;
        value = default;
        return false;
    }
}

/// <summary>Classification assigned to one SSE message by <see cref="HpdAgentSseState"/>.</summary>
public enum HpdAgentSseDecision
{
    Committed,
    Live,
    Duplicate,
    Control
}

/// <summary>Raised when the server journal generation no longer matches local state.</summary>
public sealed class HpdAgentJournalRebasedException : HpdAgentStreamException
{
    public HpdAgentJournalRebasedException(long previousGeneration, long currentGeneration)
        : base($"Thread journal rebased from generation {previousGeneration} to {currentGeneration}.")
    {
        PreviousGeneration = previousGeneration;
        CurrentGeneration = currentGeneration;
    }

    public long PreviousGeneration { get; }
    public long CurrentGeneration { get; }
}
