using HPD.Payments.Adapters.Sqlite;

var root = Path.Combine(Path.GetTempPath(), "hpd-payments-sqlite-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var path = Path.Combine(root, "adapter.db");
try
{
    await BasicAppendReplayConflict(path);
    await RaceAndGeneration(path);
    await PaginationAndRestore(path);
    await RelationAndDiscovery(path);
    await ConservationAndH7(path);
    return 0;
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    Directory.Delete(root, recursive: true);
}

static async Task BasicAppendReplayConflict(string path)
{
    await using var store = new SqliteLocalStore(path);
    var first = await store.CompareBindAppendAsync("owner/a", 0, new byte[] { 1 }, new byte[] { 10 });
    Equal(SqliteAppendOutcome.Appended, first.Outcome, "append"); Equal(1UL, first.Generation, "generation");
    Equal(SqliteAppendOutcome.Replay, (await store.CompareBindAppendAsync("owner/a", 0, new byte[] { 1 }, new byte[] { 10 })).Outcome, "replay");
    Equal(SqliteAppendOutcome.Conflict, (await store.CompareBindAppendAsync("owner/a", 0, new byte[] { 2 }, new byte[] { 11 })).Outcome, "digest conflict");
}

static async Task RaceAndGeneration(string path)
{
    await using var a = new SqliteLocalStore(path); await using var b = new SqliteLocalStore(path);
    var tasks = Enumerable.Range(0, 16).Select(i => (i & 1) == 0
        ? a.CompareBindAppendAsync("owner/race", 0, new byte[] { (byte)(i + 1) }, new byte[] { (byte)i }).AsTask()
        : b.CompareBindAppendAsync("owner/race", 0, new byte[] { (byte)(i + 1) }, new byte[] { (byte)i }).AsTask()).ToArray();
    var results = await Task.WhenAll(tasks);
    Equal(1, results.Count(x => x.Outcome == SqliteAppendOutcome.Appended), "single winner");
    Equal(15, results.Count(x => x.Outcome is SqliteAppendOutcome.Conflict or SqliteAppendOutcome.Indeterminate), "bounded losers");
}

static async Task PaginationAndRestore(string path)
{
    await using (var store = new SqliteLocalStore(path))
    {
        for (ulong generation = 0; generation < 5; generation++)
            Equal(SqliteAppendOutcome.Appended, (await store.CompareBindAppendAsync("owner/history", generation, new byte[] { (byte)(generation + 1) }, new byte[] { (byte)generation })).Outcome, "history append");
        var first = await store.ReadAsync("owner/history", 5, 2); Equal(2, first.Payloads.Count, "first page"); True(!first.Continuation.IsEmpty, "continuation");
        var second = await store.ReadAsync("owner/history", 5, 3, first.Continuation); Equal(3, second.Payloads.Count, "second page"); True(second.Continuation.IsEmpty, "history complete");
    }
    await using var restored = new SqliteLocalStore(path);
    Equal(5, (await restored.ReadAsync("owner/history", 5, 8)).Payloads.Count, "death restore");
}

static async Task RelationAndDiscovery(string path)
{
    await using var store = new SqliteLocalStore(path);
    await store.CompareBindAppendAsync("endpoint/source", 0, new byte[] { 7 }, new byte[] { 1 });
    await store.CompareBindAppendAsync("endpoint/target", 0, new byte[] { 8 }, new byte[] { 2 });
    True(await store.GuardedRelateAsync("relation/one", "endpoint/source", 1, "endpoint/target", 1, new byte[] { 9 }), "guarded relation");
    True(!await store.GuardedRelateAsync("relation/stale", "endpoint/source", 2, "endpoint/target", 1, new byte[] { 9 }), "stale endpoint rejected");
    True(await store.PutDiscoverableAsync("continuation", "one", new byte[] { 4 }), "continuation inserted");
    True(!await store.PutDiscoverableAsync("continuation", "one", new byte[] { 5 }), "continuation replay stable");
    Equal(1, (await store.SweepAsync("continuation", 8)).Count, "recovery sweep");
}

static async Task ConservationAndH7(string path)
{
    await using var store = new SqliteLocalStore(path);
    for (ulong i = 0; i < 32; i++)
        Equal(SqliteAppendOutcome.Appended, (await store.CompareBindAppendAsync("owner/conservation", i, BitConverter.GetBytes(i + 1), BitConverter.GetBytes(i))).Outcome, "conservation append");
    var page = await store.ReadAsync("owner/conservation", 32, 32);
    Equal(32, page.Payloads.Count, "no loss or duplication");
    using var cancellation = new CancellationTokenSource(); await cancellation.CancelAsync();
    Equal(SqliteAppendOutcome.Indeterminate, (await store.CompareBindAppendAsync("owner/cancel", 0, new byte[] { 1 }, new byte[] { 1 }, cancellation.Token)).Outcome, "H7 cancellation ambiguity");
}

static void Equal<T>(T expected, T actual, string name) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}"); }
static void True(bool value, string name) { if (!value) throw new InvalidOperationException(name); }
