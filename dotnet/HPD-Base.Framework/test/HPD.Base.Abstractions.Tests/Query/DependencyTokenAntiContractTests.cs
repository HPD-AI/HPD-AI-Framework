using System.Text.Json;
using FluentAssertions;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Serialization;

namespace HPD.Base.Abstractions.Tests.Query;

public sealed class DependencyTokenAntiContractTests
{
    [Fact]
    public void CoreRecordContractsDoNotAdvertiseDependencyTokens()
    {
        typeof(RecordQuery).GetProperty("RequestDependencyToken").Should().BeNull();
        typeof(RecordPage).GetProperty("DependencyToken").Should().BeNull();

        var queryJson = JsonSerializer.Serialize(
            new RecordQuery(),
            HPDBaseJsonSerializerContext.Default.RecordQuery);
        var pageJson = JsonSerializer.Serialize(
            new RecordPage { Items = [], Page = new PageInfo() },
            HPDBaseJsonSerializerContext.Default.RecordPage);

        queryJson.ToLowerInvariant().Should().NotContain("dependency");
        pageJson.ToLowerInvariant().Should().NotContain("dependency");
    }
}
