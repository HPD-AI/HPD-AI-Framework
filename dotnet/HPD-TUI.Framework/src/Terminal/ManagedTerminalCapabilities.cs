namespace HPD.TUI.Terminal;

/// <summary>Declares terminal behavior required by managed split-footer publication.</summary>
[Flags]
public enum ManagedTerminalFeatures
{
    /// <summary>No managed-terminal behavior has been established.</summary>
    None = 0,
    /// <summary>Absolute cursor addressing is reliable within the live region.</summary>
    AbsoluteCursorAddressing = 1 << 0,
    /// <summary>Erase-in-line is supported.</summary>
    EraseInLine = 1 << 1,
    /// <summary>Automatic wrapping can be disabled and restored.</summary>
    ControllableAutowrap = 1 << 2,
    /// <summary>Synchronized-output brackets are supported.</summary>
    SynchronizedOutput = 1 << 3,
    /// <summary>The terminal supports clearing its saved scrollback with CSI 3 J.</summary>
    ClearScrollback = 1 << 4,
}

/// <summary>Immutable capability profile used to gate managed publication protocols.</summary>
/// <param name="Features">Behavior verified for the active terminal session.</param>
public readonly record struct ManagedTerminalCapabilityProfile(ManagedTerminalFeatures Features)
{
    /// <summary>Gets the behavior required for append-only history with a pinned live footer.</summary>
    public const ManagedTerminalFeatures SplitFooterRequirements =
        ManagedTerminalFeatures.AbsoluteCursorAddressing |
        ManagedTerminalFeatures.EraseInLine |
        ManagedTerminalFeatures.ControllableAutowrap |
        ManagedTerminalFeatures.SynchronizedOutput;

    /// <summary>Gets whether the split-footer protocol is safe for this profile.</summary>
    public bool SupportsSplitFooter => (Features & SplitFooterRequirements) == SplitFooterRequirements;

    /// <summary>Gets a profile suitable for a terminal whose complete managed protocol was verified.</summary>
    public static ManagedTerminalCapabilityProfile Verified { get; } = new(
        SplitFooterRequirements | ManagedTerminalFeatures.ClearScrollback);

    /// <summary>Detects capabilities explicitly reported by the active terminal session.</summary>
    public static ManagedTerminalCapabilityProfile Detect(ITerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        return terminal is IManagedTerminalCapabilitySource source ? source.ManagedTerminalCapabilities : default;
    }
}

/// <summary>Reports managed-terminal behavior established for a concrete terminal session.</summary>
public interface IManagedTerminalCapabilitySource
{
    /// <summary>Gets immutable capabilities detected or configured for this session.</summary>
    ManagedTerminalCapabilityProfile ManagedTerminalCapabilities { get; }
}

/// <summary>Controls behavior when managed split-footer requirements are unavailable.</summary>
public enum ManagedTerminalFallbackPolicy
{
    /// <summary>Use a bounded physical-screen compositor and reject scrollback publication.</summary>
    BoundedScreen,
    /// <summary>Reject renderer construction rather than silently weakening history semantics.</summary>
    Reject
}

/// <summary>Controls recovery when terminal-visible history or cursor state becomes uncertain.</summary>
public enum ManagedTerminalRecoveryPolicy
{
    /// <summary>Clear scrollback and replay durable history; requires explicit clear-scrollback capability.</summary>
    ClearAndReplay,
    /// <summary>Start a new visible epoch and preserve existing terminal history.</summary>
    VisibleEpochBoundary,
    /// <summary>Leave managed mode and initialize the alternate screen.</summary>
    SwitchToAlternateScreen,
    /// <summary>Refuse further output and terminate the managed presentation.</summary>
    Abort
}
