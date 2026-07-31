using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HPD.Base.Sqlite.Tests.Query;

public sealed class SqliteQueryPlanExplainerTests
{
    [Fact]
    public async Task UnsupportedPlanReportsReasonWithoutExecutableSql()
    {
        var services = new ServiceCollection().AddLogging().AddHPDBaseSqliteStore(options => options.CollectionIds = ["items"]);
        await using var provider = services.BuildServiceProvider();
        var explainer = provider.GetRequiredService<IRelationalQueryPlanExplainer>();

        var result = await explainer.ExplainAsync(
            Collection(),
            Operation(BaseOperationKind.List),
            new RecordQuery { Include = [new QueryInclude { Path = "owner" }] },
            VisibilityLevel.Public);

        result.Value!.Status.Should().Be(RelationalQueryPlanStatus.Unsupported);
        result.Value.UnsupportedParts.Should().Contain("include");
        result.Value.Diagnostics!.Single().Message.Contains("SELECT", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    private static CollectionDefinition Collection() => new() { Id = "items", Name = "items", Kind = BaseCollectionKinds.Document, SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve };
    private static OperationContext Operation(BaseOperationKind kind) => new() { Operation = kind, CollectionId = "items", Now = DateTimeOffset.UnixEpoch };
}
