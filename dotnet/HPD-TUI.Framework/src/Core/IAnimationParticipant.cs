namespace HPD.TUI.Core;

/// <summary>Declares that a retained component currently requires admitted animation frames.</summary>
public interface IAnimationParticipant
{
    /// <summary>Gets whether animation frames are currently required.</summary>
    bool IsAnimationActive { get; }

    /// <summary>Gets the preferred interval between admitted animation frames.</summary>
    TimeSpan AnimationInterval { get; }
}

internal static class AnimationParticipants
{
    public static TimeSpan? ResolveInterval(IComponent? root, TimeSpan? configuredMaximum)
    {
        if (root is null) return null;
        TimeSpan? interval = null;
        Visit(root, ref interval);
        if (interval is null) return null;
        return configuredMaximum is { } maximum && maximum > TimeSpan.Zero && maximum < interval
            ? maximum
            : interval;
    }

    private static void Visit(IComponent component, ref TimeSpan? interval)
    {
        if (component is IAnimationParticipant { IsAnimationActive: true } participant)
        {
            var requested = participant.AnimationInterval <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(16)
                : participant.AnimationInterval;
            interval = interval is null || requested < interval ? requested : interval;
        }

        if (component is not Component owner) return;
        foreach (var child in owner.OwnedChildren) Visit(child, ref interval);
    }
}
