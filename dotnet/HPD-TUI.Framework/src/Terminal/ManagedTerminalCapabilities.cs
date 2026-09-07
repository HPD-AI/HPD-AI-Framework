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
        ManagedTerminalFeatures.ControllableAutowrap;

    /// <summary>Gets whether the split-footer protocol is safe for this profile.</summary>
    public bool SupportsSplitFooter => (Features & SplitFooterRequirements) == SplitFooterRequirements;

    /// <summary>Gets a profile suitable for a terminal whose complete managed protocol was verified.</summary>
    public static ManagedTerminalCapabilityProfile Verified { get; } = new(
        SplitFooterRequirements | ManagedTerminalFeatures.ClearScrollback | ManagedTerminalFeatures.SynchronizedOutput);

    /// <summary>Selects the normal-screen protocol for a recognized terminal environment.</summary>
    /// <param name="environment">Reads a terminal environment variable without changing process state.</param>
    /// <param name="outputRedirected">Whether output is a pipe or file rather than a terminal.</param>
    /// <returns>A profile using full-screen scrolling, or no capabilities for unsupported output.</returns>
    /// <remarks>Unknown terminals are not promoted to a verified profile. Synchronized output is optional;
    /// the full-screen insertion protocol also works with ordered unsynchronized writes.</remarks>
    public static ManagedTerminalCapabilityProfile FromEnvironment(
        Func<string, string?> environment, bool outputRedirected)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (outputRedirected) return default;
        var term = environment("TERM") ?? string.Empty;
        if (term == "dumb") return default;
        var program = environment("TERM_PROGRAM") ?? string.Empty;
        var recognized = term.StartsWith("xterm", StringComparison.OrdinalIgnoreCase) ||
            term.StartsWith("screen", StringComparison.OrdinalIgnoreCase) ||
            term.StartsWith("tmux", StringComparison.OrdinalIgnoreCase) ||
            program is "Apple_Terminal" or "iTerm.app" or "WezTerm" or "vscode" or "ghostty" ||
            !string.IsNullOrEmpty(environment("WT_SESSION")) ||
            !string.IsNullOrEmpty(environment("KITTY_WINDOW_ID"));
        if (!recognized) return default;
        var features = SplitFooterRequirements | ManagedTerminalFeatures.ClearScrollback;
        if (program is "iTerm.app" or "WezTerm" or "ghostty" ||
            !string.IsNullOrEmpty(environment("KITTY_WINDOW_ID")))
            features |= ManagedTerminalFeatures.SynchronizedOutput;
        return new(features);
    }

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

/// <summary>Identifies the outcome of an explicit committed-history rebase.</summary>
public enum ManagedHistoryRebaseStatus
{
    /// <summary>The recovery payload was completely accepted.</summary>
    Written,
    /// <summary>No recovery bytes were accepted and the caller may retry after writability.</summary>
    Backpressured,
    /// <summary>Recovery failed and terminal state remains uncertain.</summary>
    Failed,
    /// <summary>The selected policy deliberately aborted managed presentation.</summary>
    Aborted
}

/// <summary>Reports the explicit terminal consequence of a committed-history mutation.</summary>
/// <param name="Status">The recovery disposition.</param>
/// <param name="PresentationEpoch">The active epoch after the attempt.</param>
/// <param name="Error">The recovery error, when unsuccessful.</param>
public readonly record struct ManagedHistoryRebaseResult(
    ManagedHistoryRebaseStatus Status,
    long PresentationEpoch,
    Exception? Error = null);
