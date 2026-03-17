using System.Collections.Immutable;
using HPDOS.Apps.AppRecorder.Project;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder;

public class ProjectModelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProjectModel Empty() => new()
    {
        ProjectId = "test",
        SourceType = SourceType.Screen,
        ScreenMetadata = new ScreenSourceMetadata("display:0", 1920, 1080),
        VideoPath = "/tmp/test.mp4",
        CreatedAt = DateTimeOffset.UtcNow,
        ModifiedAt = DateTimeOffset.UtcNow,
    };

    private static AddZoomRegion Zoom(string? txId = null) =>
        new(0, 1000, 1.5, 0.5, 0.5, txId);

    // ── Append / command log ──────────────────────────────────────────────────

    // #1 Append single command advances UndoIndex
    [Fact]
    public void Append_SingleCommand_AdvancesUndoIndex()
    {
        var model = Empty().Append(Zoom());
        Assert.Equal(0, model.UndoIndex);
        Assert.Equal(1, model.Commands.Count);
    }

    // #2 Append clears redo tail
    [Fact]
    public void Append_ClearsRedoTail()
    {
        var model = Empty()
            .Append(Zoom()).Append(Zoom()).Append(Zoom()) // 3 commands, index=2
            .Undo()                                       // index=1
            .Append(Zoom());                              // should drop tail, index=2

        Assert.Equal(2, model.UndoIndex);
        Assert.Equal(3, model.Commands.Count);
    }

    // #3 ModifiedAt updates on append
    [Fact]
    public void Append_UpdatesModifiedAt()
    {
        var model = Empty();
        var before = model.CreatedAt;
        var after = model.Append(Zoom());
        Assert.True(after.ModifiedAt >= before);
    }

    // #4 Append to empty model
    [Fact]
    public void Append_ToEmptyModel_UndoIndexBecomesZero()
    {
        var model = Empty();
        Assert.Equal(-1, model.UndoIndex);
        var after = model.Append(Zoom());
        Assert.Equal(0, after.UndoIndex);
    }

    // #5 ActiveCommands returns only up to UndoIndex
    [Fact]
    public void ActiveCommands_ReturnsUpToUndoIndex()
    {
        var model = Empty()
            .Append(Zoom()).Append(Zoom()).Append(Zoom()).Append(Zoom()).Append(Zoom()) // 5 cmds
            .Undo().Undo(); // UndoIndex = 2

        var active = model.ActiveCommands.ToList();
        Assert.Equal(3, active.Count);
    }

    // #6 ActiveCommands empty when UndoIndex = -1
    [Fact]
    public void ActiveCommands_EmptyWhenUndoIndexMinusOne()
    {
        var model = Empty();
        Assert.Equal(-1, model.UndoIndex);
        Assert.Empty(model.ActiveCommands);
    }

    // ── Undo / Redo — single commands ─────────────────────────────────────────

    // #7 Undo single command
    [Fact]
    public void Undo_SingleCommand_DecreasesUndoIndex()
    {
        var model = Empty().Append(Zoom()).Undo();
        Assert.Equal(-1, model.UndoIndex);
    }

    // #8 Undo at floor is no-op
    [Fact]
    public void Undo_AtFloor_ReturnsModelWithSameIndex()
    {
        var model = Empty();
        Assert.Equal(-1, model.UndoIndex);
        var after = model.Undo();
        Assert.Equal(-1, after.UndoIndex);
    }

    // #9 Redo single command
    [Fact]
    public void Redo_SingleCommand_AdvancesUndoIndex()
    {
        var model = Empty().Append(Zoom()).Undo().Redo();
        Assert.Equal(0, model.UndoIndex);
    }

    // #10 Redo at ceiling is no-op
    [Fact]
    public void Redo_AtCeiling_ReturnsModelWithSameIndex()
    {
        var model = Empty().Append(Zoom());
        var after = model.Redo();
        Assert.Equal(0, after.UndoIndex);
    }

    // #11 Undo then redo roundtrip
    [Fact]
    public void UndoRedoRoundtrip_RestoresUndoIndex()
    {
        var model = Empty()
            .Append(Zoom()).Append(Zoom()).Append(Zoom()); // index=2

        var undoneAll = model.Undo().Undo().Undo();
        Assert.Equal(-1, undoneAll.UndoIndex);

        var redoneAll = undoneAll.Redo().Redo().Redo();
        Assert.Equal(2, redoneAll.UndoIndex);
    }

    // ── Undo / Redo — transactions ────────────────────────────────────────────

    // #12 AppendTransaction stamps all commands with same ID
    [Fact]
    public void AppendTransaction_StampsAllCommandsWithSameTxId()
    {
        var cmds = new ProjectCommand[] { Zoom(), Zoom(), Zoom() };
        var model = Empty().AppendTransaction(cmds, "tx1");

        foreach (var cmd in model.Commands)
            Assert.Equal("tx1", cmd.TransactionId);
    }

    // #13 Undo from end of tx block lands at end of that block
    // [a, b(tx1), c(tx1), d(tx1), e] → index=4 → undo → index=3 (skip into tx1 is internal)
    // Actually: from e (index=4, no tx), undo steps back 1 to index=3.
    [Fact]
    public void Undo_NonTxCommand_StepsBackByOne()
    {
        // Build [a, b(tx1), c(tx1), d(tx1), e] all without tx except b/c/d
        var model = Empty()
            .Append(Zoom())              // index 0: a
            .AppendTransaction([Zoom(), Zoom(), Zoom()], "tx1") // indices 1,2,3
            .Append(Zoom());             // index 4: e

        Assert.Equal(4, model.UndoIndex);
        var after = model.Undo(); // e has no tx → step back 1
        Assert.Equal(3, after.UndoIndex);
    }

    // #14 Undo transaction skips entire transaction block
    [Fact]
    public void Undo_Transaction_SkipsEntireBlock()
    {
        // [a, b(tx1), c(tx1), d(tx1), e]
        // When UndoIndex is at end of tx1 block (index=3), undo should jump before tx1 (index=0)
        var model = Empty()
            .Append(Zoom())                                      // index 0: a
            .AppendTransaction([Zoom(), Zoom(), Zoom()], "tx1") // indices 1,2,3
            .Append(Zoom());                                     // index 4: e

        var atEndOfTx1 = model.Undo(); // from 4 → 3 (non-tx step)
        Assert.Equal(3, atEndOfTx1.UndoIndex);

        var beforeTx1 = atEndOfTx1.Undo(); // from 3 (tx1) → should land at 0
        Assert.Equal(0, beforeTx1.UndoIndex);
    }

    // #15 Redo transaction advances to end of block
    [Fact]
    public void Redo_Transaction_AdvancesToEndOfBlock()
    {
        // [a, b(tx1), c(tx1), d(tx1)] — undo all, then redo from -1
        var model = Empty()
            .Append(Zoom())                                      // index 0: a
            .AppendTransaction([Zoom(), Zoom(), Zoom()], "tx1"); // indices 1,2,3

        var undoneAll = model.Undo().Undo(); // 3→0→-1... no, undo from 3 jumps to 0, then from 0 goes to -1
        // Let's get to index=0 (after a) first
        var undoneToA = model.Undo(); // from 3 (tx1) → 0
        Assert.Equal(0, undoneToA.UndoIndex);

        var redone = undoneToA.Redo(); // redo from 0: next is index 1 which is tx1 → should advance to 3
        Assert.Equal(3, redone.UndoIndex);
    }

    // #16 Mixed transaction and non-transaction undo
    [Fact]
    public void Undo_NonTxCommand_StepsBackByOneNotTxBoundary()
    {
        // [a, b(tx1), c(tx1), d] — from d (index=3, no tx), undo → index=2
        var model = Empty()
            .Append(Zoom())                              // index 0: a
            .AppendTransaction([Zoom(), Zoom()], "tx1") // indices 1,2
            .Append(Zoom());                             // index 3: d (no tx)

        var after = model.Undo();
        Assert.Equal(2, after.UndoIndex);
    }

    // #17 SmartEdit simulation — 5 commands same txId, single undo removes all 5
    [Fact]
    public void SmartEdit_SingleUndo_RemovesEntireTransaction()
    {
        var cmds = Enumerable.Repeat<ProjectCommand>(Zoom(), 5).ToArray();
        var model = Empty().AppendTransaction(cmds, "smart1");

        Assert.Equal(4, model.UndoIndex);
        var afterUndo = model.Undo();
        Assert.Equal(-1, afterUndo.UndoIndex);
        Assert.Empty(afterUndo.ActiveCommands);
    }

    // ── Dirty tracking ────────────────────────────────────────────────────────

    // #18 IsDirty true after append
    [Fact]
    public void IsDirty_TrueAfterAppend()
    {
        var model = Empty().Append(Zoom());
        Assert.True(model.IsDirty);
    }

    // #19 IsDirty false after MarkSaved
    [Fact]
    public void IsDirty_FalseAfterMarkSaved()
    {
        var model = Empty().Append(Zoom()).MarkSaved("/tmp/foo.hpdrecorder");
        Assert.False(model.IsDirty);
    }

    // #20 IsDirty true after undo past save point
    [Fact]
    public void IsDirty_TrueAfterUndoPastSavePoint()
    {
        var model = Empty()
            .Append(Zoom()).Append(Zoom()).Append(Zoom()) // index=2
            .MarkSaved("/tmp/foo.hpdrecorder")            // savedIndex=2
            .Undo();                                      // index=1

        Assert.True(model.IsDirty);
    }

    // #21 IsDirty false after redo back to save point
    [Fact]
    public void IsDirty_FalseAfterRedoBackToSavePoint()
    {
        var model = Empty()
            .Append(Zoom()).Append(Zoom()).Append(Zoom()) // index=2
            .MarkSaved("/tmp/foo.hpdrecorder")
            .Undo()   // index=1 — dirty
            .Redo();  // index=2 — back to saved

        Assert.False(model.IsDirty);
    }

    // #22 CurrentPath set by MarkSaved
    [Fact]
    public void MarkSaved_SetsCurrentPath()
    {
        var model = Empty().Append(Zoom()).MarkSaved("/tmp/foo.hpdrecorder");
        Assert.Equal("/tmp/foo.hpdrecorder", model.CurrentPath);
    }

    // ── Source type invariants ────────────────────────────────────────────────

    // #23 Screen source has ScreenMetadata, others null
    [Fact]
    public void ScreenSource_HasScreenMetadata_OthersNull()
    {
        var model = new ProjectModel
        {
            ProjectId = "x",
            SourceType = SourceType.Screen,
            ScreenMetadata = new ScreenSourceMetadata("display:0", 1920, 1080),
            VideoPath = "/tmp/v.mp4",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.NotNull(model.ScreenMetadata);
        Assert.Null(model.CameraMetadata);
        Assert.Null(model.ImportMetadata);
    }

    // #24 Camera source has CameraMetadata, TelemetryPath null
    [Fact]
    public void CameraSource_HasCameraMetadata_TelemetryPathNull()
    {
        var model = new ProjectModel
        {
            ProjectId = "x",
            SourceType = SourceType.Camera,
            CameraMetadata = new CameraSourceMetadata("cam:0"),
            VideoPath = "/tmp/v.mp4",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.NotNull(model.CameraMetadata);
        Assert.Null(model.TelemetryPath);
    }

    // #25 Import source has ImportMetadata, TelemetryPath null
    [Fact]
    public void ImportSource_HasImportMetadata_TelemetryPathNull()
    {
        var model = new ProjectModel
        {
            ProjectId = "x",
            SourceType = SourceType.Import,
            ImportMetadata = new ImportSourceMetadata("/Downloads/clip.mp4"),
            VideoPath = "/tmp/v.mp4",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.NotNull(model.ImportMetadata);
        Assert.Null(model.TelemetryPath);
    }

    // #26 Screen source can have TelemetryPath set
    [Fact]
    public void ScreenSource_CanHaveTelemetryPath()
    {
        var model = new ProjectModel
        {
            ProjectId = "x",
            SourceType = SourceType.Screen,
            ScreenMetadata = new ScreenSourceMetadata("display:0", 1920, 1080),
            VideoPath = "/tmp/v.mp4",
            TelemetryPath = "/tmp/v.cursor.json",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.NotNull(model.TelemetryPath);
    }

    // ── TryGetProject gap ─────────────────────────────────────────────────────

    // (OpenShot gap #1) — covered at AppRecorderApp level, but model-level null returns
    // are also worth verifying via the project store directly.

    // ── Update then re-read consistency (OpenShot gap #2) ────────────────────
    // Covered in AppRecorderAppTests.

    // ── Edge cases ────────────────────────────────────────────────────────────

    // #86 AddZoomRegion with depth = 1.0 is valid
    [Fact]
    public void Append_ZoomDepthOne_IsValid()
    {
        var cmd = new AddZoomRegion(0, 1000, 1.0, 0.5, 0.5);
        var model = Empty().Append(cmd);
        Assert.Equal(1, model.Commands.Count);
    }

    // #90 AppendTransaction with empty sequence — UndoIndex unchanged
    [Fact]
    public void AppendTransaction_EmptySequence_UndoIndexUnchanged()
    {
        var model = Empty();
        var after = model.AppendTransaction([], "tx0");
        Assert.Equal(-1, after.UndoIndex);
        Assert.Empty(after.Commands);
    }

    // #91 Undo past save point, then redo back — IsDirty must be false
    [Fact]
    public void UndoPastSave_ThenRedoBack_IsDirtyFalse()
    {
        var model = Empty()
            .Append(Zoom()).Append(Zoom())
            .MarkSaved("/tmp/t.hpdrecorder")
            .Undo()
            .Redo();
        Assert.False(model.IsDirty);
    }

    // #92 ProjectModel with 1000+ commands — ActiveCommands does not degrade meaningfully
    [Fact]
    public void LargeCommandLog_ActiveCommandsAndUndoAreCorrect()
    {
        var model = Empty();
        for (int i = 0; i < 1000; i++)
            model = model.Append(Zoom());

        Assert.Equal(999, model.UndoIndex);
        Assert.Equal(1000, model.ActiveCommands.Count());

        var undone = model.Undo();
        Assert.Equal(998, undone.UndoIndex);
    }
}
