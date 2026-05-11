// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio;

namespace HPD.Agent.Audio.Interruption;

/// <summary>
/// Policy options for interruption and false-interruption recovery.
/// </summary>
public sealed record InterruptionControllerOptions
{
    /// <summary>Backchannel handling strategy.</summary>
    public BackchannelStrategy BackchannelStrategy { get; init; } = BackchannelStrategy.IgnoreShortUtterances;

    /// <summary>Minimum words required before speech counts as an interruption.</summary>
    public int MinWordsForInterruption { get; init; } = 2;

    /// <summary>Whether output should pause while awaiting transcript confirmation.</summary>
    public bool EnableFalseInterruptionRecovery { get; init; } = true;

    /// <summary>How long to wait for a confirming transcript before resuming output.</summary>
    public TimeSpan FalseInterruptionTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Whether paused output should resume after a false interruption.</summary>
    public bool ResumeFalseInterruption { get; init; } = true;
}
