using FluentAssertions;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using HPD.Base.Sqlite.Configuration;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Query;

public sealed class SqliteQueryPushdownTests
{
    [Fact]
    public async Task UnsupportedStringOperatorFailsClosed()
    {
        var store = new SqliteRecordStore(new HPDBaseSqliteOptions { DataSource = TempPath() });
        var result = await store.ListAsync(
            Collection(),
            new RecordQuery { Filter = new FilterExpression { Kind = FilterNodeKind.Compare, Field = "title", Operator = FilterOperator.Contains, Value = new QueryValue { Kind = QueryValueKind.String, String = "a" } } },
            Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Unsupported);
        result.Error!.Code.Should().Be("sqlite.query.unsupported");
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-query-" + Guid.NewGuid().ToString("N") + ".db");
    private static CollectionDefinition Collection() => new() { Id = "items", Name = "items", Kind = BaseCollectionKinds.Document, SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve };
    private static OperationContext Operation(BaseOperationKind kind) => new() { Operation = kind, CollectionId = "items", Now = DateTimeOffset.UnixEpoch };
}
