using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class ExecuteCommandOutputReconciliationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"hpd-command-reconciliation-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReconcileOrphans_DeletesOnlyJournaledContentInTheRequestedScope()
    {
        var store = new LocalFileContentStore(Path.Combine(_root, "content"));
        var sessionScope = ContentScope.Create("session-a");
        var otherScope = ContentScope.Create("session-b");
        var owned = await WriteAsync(store, sessionScope, "commands/orphan/stdout.txt");
        var foreign = await WriteAsync(store, otherScope, "commands/foreign/stdout.txt");
        var spoolRoot = Path.Combine(_root, "spool");
        var orphan = Path.Combine(spoolRoot, "orphan");
        Directory.CreateDirectory(orphan);
        await File.WriteAllLinesAsync(Path.Combine(orphan, ".pending-content.jsonl"),
        [
            JsonSerializer.Serialize(owned.Address, CodingToolHarnessJsonContext.Default.ContentAddress),
            JsonSerializer.Serialize(foreign.Address, CodingToolHarnessJsonContext.Default.ContentAddress)
        ]);

        await ExecuteCommandOutputStoreSession.ReconcileOrphansAsync(
            spoolRoot, store, sessionScope, maxDirectories: 32, CancellationToken.None);

        (await store.StatAsync(owned.Address)).Should().BeNull();
        (await store.StatAsync(foreign.Address)).Should().NotBeNull();
        Directory.Exists(orphan).Should().BeTrue();
        var remaining = await File.ReadAllLinesAsync(Path.Combine(orphan, ".pending-content.jsonl"));
        remaining.Should().ContainSingle().Which.Should().Be(
            JsonSerializer.Serialize(foreign.Address, CodingToolHarnessJsonContext.Default.ContentAddress));

        await ExecuteCommandOutputStoreSession.ReconcileOrphansAsync(
            spoolRoot, store, otherScope, maxDirectories: 32, CancellationToken.None);

        (await store.StatAsync(foreign.Address)).Should().BeNull();
        Directory.Exists(orphan).Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileOrphans_SkipsDirectoryWithAnActiveLease()
    {
        var spoolRoot = Path.Combine(_root, "spool");
        var active = Path.Combine(spoolRoot, "active");
        Directory.CreateDirectory(active);
        await using var lease = new FileStream(
            Path.Combine(active, ".lease"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);

        await ExecuteCommandOutputStoreSession.ReconcileOrphansAsync(
            spoolRoot, contentStore: null, ContentScope.Create("session-a"),
            maxDirectories: 32, CancellationToken.None);

        Directory.Exists(active).Should().BeTrue();
    }

    [Fact]
    public async Task ReconcileOrphans_PreservesPendingJournalWithoutContentStoreAuthority()
    {
        var spoolRoot = Path.Combine(_root, "spool");
        var orphan = Path.Combine(spoolRoot, "orphan");
        Directory.CreateDirectory(orphan);
        await File.WriteAllTextAsync(Path.Combine(orphan, ".pending-content.jsonl"), "not-an-address");

        await ExecuteCommandOutputStoreSession.ReconcileOrphansAsync(
            spoolRoot, contentStore: null, ContentScope.Create("session-a"),
            maxDirectories: 32, CancellationToken.None);

        Directory.Exists(orphan).Should().BeTrue();
    }

    [Fact]
    public async Task ReconcileOrphans_CommittedMarkerWinsOverAResidualPendingJournal()
    {
        var store = new LocalFileContentStore(Path.Combine(_root, "content"));
        var scope = ContentScope.Create("session-a");
        var referenced = await WriteAsync(store, scope, "commands/committed/stdout.txt");
        var spoolRoot = Path.Combine(_root, "spool");
        var committed = Path.Combine(spoolRoot, "committed");
        Directory.CreateDirectory(committed);
        await File.WriteAllTextAsync(Path.Combine(committed, ".committed"), "committed");
        await File.WriteAllTextAsync(
            Path.Combine(committed, ".pending-content.jsonl"),
            JsonSerializer.Serialize(referenced.Address, CodingToolHarnessJsonContext.Default.ContentAddress));

        await ExecuteCommandOutputStoreSession.ReconcileOrphansAsync(
            spoolRoot, store, scope, maxDirectories: 32, CancellationToken.None);

        (await store.StatAsync(referenced.Address)).Should().NotBeNull();
        Directory.Exists(committed).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static async ValueTask<ContentInfo> WriteAsync(
        IContentStore store,
        ContentScope scope,
        string contentId)
    {
        await using var data = new MemoryStream(Encoding.UTF8.GetBytes(contentId));
        return await store.WriteAsync(
            scope,
            data,
            new ContentMetadata { ContentType = "text/plain", Name = contentId },
            new ContentWriteOptions { Mode = ContentWriteMode.Create, ContentId = contentId });
    }
}
