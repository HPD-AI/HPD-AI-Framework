// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using System.Text.Json.Nodes;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Storage;

namespace HPD.Agent.Evaluations.Tests.Storage;

public sealed class InMemoryDatasetStoreTests
{
    [Fact]
    public async Task RegisterDatasetVersionAsync_RequiresDatasetIdAndVersion()
    {
        var store = new InMemoryDatasetStore();

        var act = async () => await store.RegisterDatasetVersionAsync(new Dataset<string>
        {
            Cases = [new EvalCase<string> { Input = "hello" }],
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*DatasetId*");
    }

    [Fact]
    public async Task RegisterDatasetVersionAsync_RoundTripsDatasetVersion()
    {
        var store = new InMemoryDatasetStore();
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

        var roundTripped = await store.GetDatasetVersionAsync<string>("support-bench", "2026.02");
        roundTripped.Should().NotBeNull();
        roundTripped!.Cases.Should().ContainSingle().Which.GroundTruth.Should().Be("hello");

        var catalogRecord = await store.GetDatasetAsync("support-bench");
        catalogRecord.Should().NotBeNull();
        catalogRecord!.CurrentVersion.Should().Be("2026.02");
        catalogRecord.Description.Should().Be("Support benchmark");
    }

    [Fact]
    public async Task RegisterDatasetVersionAsync_GeneratesMissingCaseIds()
    {
        var store = new InMemoryDatasetStore();

        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v1",
        [
            new EvalCase<string> { Input = "one" },
            new EvalCase<string> { Input = "two" },
        ]));

        var snapshot = await store.GetDatasetVersionAsync<string>("bench", "v1");
        snapshot!.Cases.Select(c => c.CaseId).Should().Equal("case-0", "case-1");
    }

    [Fact]
    public async Task RegisterDatasetVersionAsync_WhenSameVersionHasDifferentContent_Throws()
    {
        var store = new InMemoryDatasetStore();
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
    public async Task GetDatasetVersionsAsync_ReturnsRegisteredVersionsInOrder()
    {
        var store = new InMemoryDatasetStore();

        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v1",
        [
            new EvalCase<string> { CaseId = "case-1", Input = "old" },
        ]), new() { RegisteredAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") });
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v2",
        [
            new EvalCase<string> { CaseId = "case-1", Input = "new" },
        ]), new() { RegisteredAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z") });

        var versions = await ToListAsync(store.GetDatasetVersionsAsync("bench"));
        versions.Select(v => v.Version).Should().Equal("v1", "v2");
    }

    [Fact]
    public async Task GetCaseHistoryAsync_ReturnsAllRevisionsWithClosedValidityWindows()
    {
        var store = new InMemoryDatasetStore();
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v1",
        [
            new EvalCase<string> { CaseId = "case-1", Input = "old" },
        ]), new() { RegisteredAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") });
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v2",
        [
            new EvalCase<string> { CaseId = "case-1", Input = "new" },
        ]), new() { RegisteredAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z") });

        var history = await ToListAsync(store.GetCaseHistoryAsync<string>("bench", "case-1"));

        history.Should().HaveCount(2);
        history[0].Input.Should().Be("old");
        history[0].ValidTo.Should().Be(DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        history[1].Input.Should().Be("new");
        history[1].ValidTo.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveCasesAsync_ReturnsCaseActiveAtTime()
    {
        var store = new InMemoryDatasetStore();
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v1",
        [
            new EvalCase<string> { CaseId = "case-1", Input = "old" },
        ]), new() { RegisteredAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") });
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v2",
        [
            new EvalCase<string> { CaseId = "case-1", Input = "new" },
            new EvalCase<string> { CaseId = "case-2", Input = "added" },
        ]), new() { RegisteredAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z") });

        var january = await ToListAsync(store.GetActiveCasesAsync<string>(
            "bench",
            DateTimeOffset.Parse("2026-01-15T00:00:00Z")));
        var february = await ToListAsync(store.GetActiveCasesAsync<string>(
            "bench",
            DateTimeOffset.Parse("2026-02-15T00:00:00Z")));

        january.Should().ContainSingle().Which.Input.Should().Be("old");
        february.Select(c => c.Input).Should().BeEquivalentTo(["new", "added"]);
    }

    [Fact]
    public async Task CompareVersionsAsync_ReturnsAddedRemovedAndChangedCases()
    {
        var store = new InMemoryDatasetStore();
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v1",
        [
            new EvalCase<string> { CaseId = "changed", Input = "old" },
            new EvalCase<string> { CaseId = "removed", Input = "gone" },
            new EvalCase<string> { CaseId = "same", Input = "same" },
        ]), new() { RegisteredAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") });
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v2",
        [
            new EvalCase<string> { CaseId = "added", Input = "new" },
            new EvalCase<string> { CaseId = "changed", Input = "updated" },
            new EvalCase<string> { CaseId = "same", Input = "same" },
        ]), new() { RegisteredAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z") });

        var diff = await store.CompareVersionsAsync<string>("bench", "v1", "v2");

        diff.Added.Should().ContainSingle().Which.CaseId.Should().Be("added");
        diff.Removed.Should().ContainSingle().Which.CaseId.Should().Be("removed");
        diff.Changed.Should().ContainSingle().Which.CaseId.Should().Be("changed");
    }

    [Fact]
    public async Task CreateDiffReportAsync_ReturnsCountsAndChangedCases()
    {
        var store = new InMemoryDatasetStore();
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v1",
        [
            new EvalCase<string> { CaseId = "changed", Input = "old" },
            new EvalCase<string> { CaseId = "removed", Input = "gone" },
        ]));
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v2",
        [
            new EvalCase<string> { CaseId = "changed", Input = "new" },
            new EvalCase<string> { CaseId = "added", Input = "fresh" },
        ]));

        var report = await store.CreateDiffReportAsync<string>("bench", "v1", "v2");

        report.AddedCount.Should().Be(1);
        report.RemovedCount.Should().Be(1);
        report.ChangedCount.Should().Be(1);
        report.Changed.Should().ContainSingle().Which.Before.Input.Should().Be("old");
    }

    [Fact]
    public async Task ExportAndImportDatasetVersionToYaml_RoundTripsThroughStore()
    {
        var source = new InMemoryDatasetStore();
        await source.RegisterDatasetVersionAsync(MakeDataset("bench", "v1",
        [
            new EvalCase<string>
            {
                CaseId = "case-1",
                Name = "Case One",
                Input = "hello",
                GroundTruth = "hi",
                Metadata = new Dictionary<string, object> { ["difficulty"] = "easy" },
            },
        ]));

        var path = Path.Combine(Path.GetTempPath(), $"hpd-dataset-{Guid.NewGuid():N}.yaml");
        try
        {
            await source.ExportDatasetVersionToFileAsync<string>(
                "bench",
                "v1",
                path,
                input => JsonValue.Create(input));

            var target = new InMemoryDatasetStore();
            var imported = await target.ImportDatasetVersionFromFileAsync<string>(
                path,
                node => node?.GetValue<string>() ?? string.Empty);

            imported.DatasetId.Should().Be("bench");
            imported.Version.Should().Be("v1");

            var dataset = await target.GetDatasetVersionAsync<string>("bench", "v1");
            dataset.Should().NotBeNull();
            var evalCase = dataset!.Cases.Should().ContainSingle().Which;
            evalCase.CaseId.Should().Be("case-1");
            evalCase.Input.Should().Be("hello");
            evalCase.GroundTruth.Should().Be("hi");
            evalCase.Metadata.Should().ContainKey("difficulty");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportDatasetVersionsToYamlDirectory_WritesOneFilePerVersion()
    {
        var store = new InMemoryDatasetStore();
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v1",
        [
            new EvalCase<string> { CaseId = "case-1", Input = "old" },
        ]));
        await store.RegisterDatasetVersionAsync(MakeDataset("bench", "v2",
        [
            new EvalCase<string> { CaseId = "case-1", Input = "new" },
        ]));

        var directory = Path.Combine(Path.GetTempPath(), $"hpd-datasets-{Guid.NewGuid():N}");
        try
        {
            var paths = await store.ExportDatasetVersionsToDirectoryAsync<string>(
                "bench",
                directory,
                input => JsonValue.Create(input));

            paths.Should().HaveCount(2);
            paths.Should().OnlyContain(path => File.Exists(path));
            paths.Select(Path.GetFileName).Should().BeEquivalentTo(["bench-v1.yaml", "bench-v2.yaml"]);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
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
