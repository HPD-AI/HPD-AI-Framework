namespace HPD.ML.Abstractions;

public enum ConflictPolicy
{
    /// <summary>Later column shadows earlier; audit trail annotation added.</summary>
    LastWriterWins,
    ErrorOnConflict
}
