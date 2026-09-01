#pragma warning restore CS1591

namespace HPD.Events;

/// <summary>A complete, deterministically ordered effective-timestamp frame.</summary>
/// <typeparam name="TEvent">The replayed event type.</typeparam>
/// <param name="TimestampNs">The effective <see cref="ReplayKey.TimestampNs"/> shared by every entry.</param>
/// <param name="Entries">The immutable transport entries in complete-key order.</param>
/// <param name="Boundary">Read-relative frame and limit evidence.</param>
public sealed record ReplayFrame<TEvent>(long TimestampNs, IReadOnlyList<ReplayEntry<TEvent>> Entries, ReplayFrameBoundary Boundary)
    where TEvent : Event;

/// <summary>Describes a frame's position and event-limit boundary in one read.</summary>
/// <param name="FrameOrdinal">The zero-based visible-frame ordinal.</param>
/// <param name="FirstEntryOrdinal">The zero-based ordinal of the frame's first visible entry.</param>
/// <param name="EntryCount">The number of visible entries in the frame.</param>
/// <param name="RequestedEventLimit">The requested event limit, if any.</param>
/// <param name="ActualCumulativeEntryCount">The visible cumulative count through this frame.</param>
/// <param name="CompletedRequestedLimit">Whether this complete frame reached or exceeded the requested limit.</param>
public sealed record ReplayFrameBoundary(long FrameOrdinal, long FirstEntryOrdinal, int EntryCount, int? RequestedEventLimit, long ActualCumulativeEntryCount, bool CompletedRequestedLimit);
