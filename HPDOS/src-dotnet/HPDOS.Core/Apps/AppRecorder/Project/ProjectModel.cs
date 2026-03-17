using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace HPDOS.Apps.AppRecorder.Project;

public enum SourceType { Screen, Camera, Import }

public sealed record ScreenSourceMetadata(string SourceId, int DisplayWidth, int DisplayHeight);
public sealed record CameraSourceMetadata(string DeviceId);
public sealed record ImportSourceMetadata(string OriginalPath);

/// <summary>
/// Immutable command-stack project model. Every edit is an appended ProjectCommand.
/// Undo/redo move UndoIndex to transaction boundaries — commands sharing a TransactionId
/// are undone atomically.
/// </summary>
public sealed record ProjectModel
{
    public required string ProjectId { get; init; }

    // ── Source ────────────────────────────────────────────────────────────────
    public required SourceType SourceType { get; init; }

    // Exactly one of these is non-null, matching SourceType.
    public ScreenSourceMetadata? ScreenMetadata { get; init; }
    public CameraSourceMetadata? CameraMetadata { get; init; }
    public ImportSourceMetadata? ImportMetadata { get; init; }

    public required string VideoPath { get; init; }

    /// <summary>null for camera and import sources — no cursor data available.</summary>
    public string? TelemetryPath { get; init; }

    // ── Command log ───────────────────────────────────────────────────────────

    /// <summary>
    /// Append-only command log. Never mutated in-place — always replaced with a new
    /// ImmutableList via with-expressions when commands are appended or sliced.
    /// </summary>
    public ImmutableList<ProjectCommand> Commands { get; init; } = [];

    /// <summary>
    /// Points to the last applied command index (inclusive). -1 = nothing applied.
    /// Undo decrements to the previous transaction boundary; Redo increments forward.
    /// </summary>
    public int UndoIndex { get; init; } = -1;

    // ── Persistence ───────────────────────────────────────────────────────────

    /// <summary>null = unsaved (new project). Set on first SaveProjectAs.</summary>
    public string? CurrentPath { get; init; }

    /// <summary>UndoIndex at the time of the last save. isDirty = UndoIndex != SavedUndoIndex.</summary>
    public int SavedUndoIndex { get; init; } = -1;

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ModifiedAt { get; init; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    [JsonIgnore]
    public bool IsDirty => UndoIndex != SavedUndoIndex;

    /// <summary>The active command slice — everything at or before UndoIndex.</summary>
    [JsonIgnore]
    public IEnumerable<ProjectCommand> ActiveCommands =>
        UndoIndex < 0 ? [] : Commands.Take(UndoIndex + 1);

    /// <summary>Append a command and advance UndoIndex. Drops any redo history beyond current index.</summary>
    public ProjectModel Append(ProjectCommand command)
    {
        // Slice off any redo tail before appending.
        var trimmed = UndoIndex < Commands.Count - 1
            ? Commands.RemoveRange(UndoIndex + 1, Commands.Count - UndoIndex - 1)
            : Commands;

        return this with
        {
            Commands = trimmed.Add(command),
            UndoIndex = UndoIndex + 1,
            ModifiedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>Append a batch of commands sharing the same TransactionId atomically.</summary>
    public ProjectModel AppendTransaction(IEnumerable<ProjectCommand> commands, string transactionId)
    {
        var stamped = commands.Select(c => c with { TransactionId = transactionId });
        return stamped.Aggregate(this, (model, cmd) => model.Append(cmd));
    }

    /// <summary>Move UndoIndex back to the start of the previous transaction boundary.</summary>
    public ProjectModel Undo()
    {
        if (UndoIndex < 0) return this;

        var currentTxId = Commands[UndoIndex].TransactionId;
        var newIndex = UndoIndex - 1;

        // If the current command belongs to a transaction, keep stepping back
        // until we exit that transaction.
        if (currentTxId is not null)
        {
            while (newIndex >= 0 && Commands[newIndex].TransactionId == currentTxId)
                newIndex--;
        }

        return this with { UndoIndex = newIndex, ModifiedAt = DateTimeOffset.UtcNow };
    }

    /// <summary>Move UndoIndex forward to the end of the next transaction.</summary>
    public ProjectModel Redo()
    {
        if (UndoIndex >= Commands.Count - 1) return this;

        var nextIndex = UndoIndex + 1;
        var nextTxId = Commands[nextIndex].TransactionId;

        // Advance to the end of the next transaction if it has one.
        if (nextTxId is not null)
        {
            while (nextIndex + 1 < Commands.Count && Commands[nextIndex + 1].TransactionId == nextTxId)
                nextIndex++;
        }

        return this with { UndoIndex = nextIndex, ModifiedAt = DateTimeOffset.UtcNow };
    }

    public ProjectModel MarkSaved(string path) =>
        this with { CurrentPath = path, SavedUndoIndex = UndoIndex };
}
