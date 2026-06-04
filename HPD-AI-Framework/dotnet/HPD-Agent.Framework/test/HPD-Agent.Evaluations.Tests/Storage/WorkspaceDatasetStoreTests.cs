// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Storage;

namespace HPD.Agent.Evaluations.Tests.Storage;

public sealed class WorkspaceDatasetStoreTests
{
    [Fact]
    public async Task RegisterDatasetVersionAsync_PersistsDatasetVersionInWorkspace()
    {
        var workspace = new InMemoryWorkspaceStore();
        var store = new WorkspaceDatasetStore(workspace);
        var dataset = MakeDataset("support-bench", "2026.02",
        [
            new EvalCase<string>
            {
                CaseId = "case-1",
                Version = "1",
                Input = "hi",
                GroundTruth = "hello",
            },
        ]);

        var record = await store.RegisterDatasetVersionAsync(dataset, new()
        {
            Description = "Support benchmark",
            RegisteredAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            Metadata = new Dictionary<string, object> { ["owner"] = "evals" },
        });

        record.DatasetId.Should().Be("support-bench");
        record.Version.Should().Be("2026.02");
        record.CaseCount.Should().Be(1);

        var persistedStore = new WorkspaceDatasetStore(workspace);
        var roundTripped = await persistedStore.GetDatasetVersionAsync<string>("support-bench", "2026.02");
        roundTripped.Should().NotBeNull();
        roundTripped!.Cases.Should().ContainSingle().Which.GroundTruth.Should().Be("hello");

        var catalogRecord = await persistedStore.GetDatasetAsync("support-bench");
        catalogRecord.Should().NotBeNull();
        catalogRecord!.CurrentVersion.Should().Be("2026.02");
        catalogRecord.Description.Should().Be("Support benchmark");

        var datasetSpace = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceDatasetStore.DatasetKind,
                ExternalId = "support-bench"
            });
        datasetSpace.Should().NotBeNull();

        var attachments = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            datasetSpace!.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceDatasetStore.DatasetVersionRole });
        var attachment = attachments.Should().ContainSingle().Subject;
        attachment.ContentVersion.Should().NotBeNullOrWhiteSpace();
        attachment.Metadata.Should().ContainKey("document_type").WhoseValue.Should().Be("dataset_version");
        attachment.Metadata.Should().ContainKey("dataset_id").WhoseValue.Should().Be("support-bench");
        attachment.Metadata.Should().ContainKey("version").WhoseValue.Should().Be("2026.02");
        attachment.Metadata.Should().ContainKey("content_hash").WhoseValue.Should().Be(record.ContentHash);
        attachment.Metadata.Should().ContainKey("case_count").WhoseValue.Should().Be("1");
    }

    [Fact]
    public async Task RegisterDatasetVersionAsync_WhenSameVersionHasDifferentContent_Throws()
    {
        var store = new WorkspaceDatasetStore(new InMemoryWorkspaceStore());
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v1",
        [
            new EvalCase<string> { CaseId = "case-1", Input = "old" },
        ]));

        var act = async () => await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v1",
        [
            new EvalCase<string> { CaseId = "case-1", Input = "new" },
        ]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already registered with different content*");
    }

    [Fact]
    public async Task GetCaseHistoryAsync_HydratesHistoryFromWorkspaceVersions()
    {
        var workspace = new InMemoryWorkspaceStore();
        var store = new WorkspaceDatasetStore(workspace);
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v1",
        [
            new EvalCase<string> { CaseId = "case-1", Input = "old" },
        ]), new() { RegisteredAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") });
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v2",
        [
            new EvalCase<string> { CaseId = "case-1", Input = "new" },
        ]), new() { RegisteredAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z") });

        var persistedStore = new WorkspaceDatasetStore(workspace);
        var history = await ToListAsync(persistedStore.GetCaseHistoryAsync<string>("bench", "case-1"));

        history.Should().HaveCount(2);
        history[0].Input.Should().Be("old");
        history[0].ValidTo.Should().Be(DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        history[1].Input.Should().Be("new");
        history[1].ValidTo.Should().BeNull();
    }

    private static Dataset<string> MakeDataset(
        string datasetId,
        string version,
        IReadOnlyList<EvalCase<string>> cases) =>
        new()
        {
            DatasetId = datasetId,
            Version = version,
            Cases = cases,
        };

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
