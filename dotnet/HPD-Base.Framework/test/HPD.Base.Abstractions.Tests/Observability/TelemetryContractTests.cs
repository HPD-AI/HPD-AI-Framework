using HPD.Base.Observability;

namespace HPD.Base.Abstractions.Tests.Observability;

public sealed class TelemetryContractTests
{
    [Fact]
    public void SourceAndMeterNamesExposeFirstSliceAndDeferredModules()
    {
        Assert.Contains(HPDBaseActivitySourceNames.Runtime, HPDBaseActivitySourceNames.Core);
        Assert.Contains(HPDBaseActivitySourceNames.InMemory, HPDBaseActivitySourceNames.Stores);
        Assert.Contains(HPDBaseActivitySourceNames.Sqlite, HPDBaseActivitySourceNames.Stores);
        Assert.Contains(HPDBaseActivitySourceNames.Files, HPDBaseActivitySourceNames.OptionalModules);
        Assert.Contains(HPDBaseActivitySourceNames.Realtime, HPDBaseActivitySourceNames.OptionalModules);
        Assert.Contains(HPDBaseActivitySourceNames.HPDAuth, HPDBaseActivitySourceNames.OptionalModules);

        Assert.Contains(HPDBaseMeterNames.Runtime, HPDBaseMeterNames.Core);
        Assert.Contains(HPDBaseMeterNames.InMemory, HPDBaseMeterNames.Stores);
        Assert.Contains(HPDBaseMeterNames.Sqlite, HPDBaseMeterNames.Stores);
        Assert.Contains(HPDBaseMeterNames.Files, HPDBaseMeterNames.OptionalModules);
        Assert.Contains(HPDBaseMeterNames.Realtime, HPDBaseMeterNames.OptionalModules);
        Assert.Contains(HPDBaseMeterNames.HPDAuth, HPDBaseMeterNames.OptionalModules);
    }

    [Fact]
    public void ContractUsesDottedTagsAndStableSpanNames()
    {
        Assert.Equal("hpd.base.module.id", HPDBaseTelemetryTags.ModuleId);
        Assert.Equal("hpd.base.operation.kind", HPDBaseTelemetryTags.OperationKind);
        Assert.Equal("hpd.base.collection.id", HPDBaseTelemetryTags.CollectionId);
        Assert.Equal("hpd.base.correlation_id.present", HPDBaseTelemetryTags.CorrelationIdPresent);

        Assert.Equal("hpd.base.runtime.records.create", HPDBaseTelemetrySpans.RuntimeRecordsCreate);
        Assert.Equal("hpd.base.runtime.policy.evaluate", HPDBaseTelemetrySpans.RuntimePolicyEvaluate);
        Assert.Equal("hpd.base.store.patch_if_revision", HPDBaseTelemetrySpans.StorePatchIfRevision);
        Assert.Equal("hpd.base.sqlite.connection.open", HPDBaseTelemetrySpans.SqliteConnectionOpen);
        Assert.Equal("hpd.base.auth.hpd_auth.policy.evaluate", HPDBaseTelemetrySpans.AuthPolicyEvaluate);
    }

    [Fact]
    public void BucketHelpersReturnBoundedValues()
    {
        Assert.Equal("none", HPDBaseTelemetryBuckets.PageSize(null));
        Assert.Equal("1-25", HPDBaseTelemetryBuckets.PageSize(25));
        Assert.Equal("gt500", HPDBaseTelemetryBuckets.PageSize(501));

        Assert.Equal("unknown", HPDBaseTelemetryBuckets.PayloadSize(null));
        Assert.Equal("1-1KiB", HPDBaseTelemetryBuckets.PayloadSize(1024));
        Assert.Equal("gt100MiB", HPDBaseTelemetryBuckets.FileSize(101L * 1024 * 1024));

        Assert.Equal("unknown", HPDBaseTelemetryBuckets.Count(null));
        Assert.Equal("2-5", HPDBaseTelemetryBuckets.Count(5));
        Assert.Equal("gt100", HPDBaseTelemetryBuckets.Count(101));
    }

    [Fact]
    public void BaseProjectsDoNotReferenceOpenTelemetryPackages()
    {
        var root = FindSolutionRoot();
        var unexpected = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "HPD.Base*.csproj", SearchOption.AllDirectories)
            .Select(project => new { Project = project, Text = File.ReadAllText(project) })
            .Where(project => project.Text.Contains("OpenTelemetry", StringComparison.Ordinal))
            .Select(project => Path.GetRelativePath(root, project.Project))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpected);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HPD-Base.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
