#pragma warning restore CS1591

namespace HPD.Events;

/// <summary>Reports a violated frame-source ordering or finality contract on the transport path.</summary>
public sealed class ReplayFrameContractException : InvalidOperationException
{
    /// <summary>Initializes a replay frame contract failure with stable source and key evidence.</summary>
    public ReplayFrameContractException(string message, ReplaySourceInfo replaySource, ReplayKey? previousKey = null, ReplayKey? offendingKey = null, long? lastExclusiveWatermarkTimestampNs = null)
        : base(message)
    {
        ReplaySource = replaySource;
        PreviousKey = previousKey;
        OffendingKey = offendingKey;
        LastExclusiveWatermarkTimestampNs = lastExclusiveWatermarkTimestampNs;
    }

    /// <summary>Gets the offending source.</summary>
    public ReplaySourceInfo ReplaySource { get; }
    /// <summary>Gets the previous source key, when applicable.</summary>
    public ReplayKey? PreviousKey { get; }
    /// <summary>Gets the offending source key, when applicable.</summary>
    public ReplayKey? OffendingKey { get; }
    /// <summary>Gets the last accepted exclusive watermark, when applicable.</summary>
    public long? LastExclusiveWatermarkTimestampNs { get; }
}
