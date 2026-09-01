using System.Collections.Concurrent;
using System.Diagnostics;
using HPD.Base.Tests.InMemory.TestDoubles;
using HPD.Base;
using HPD.Base.Tests.Observability;

namespace HPD.Base.Tests.InMemory.Observability;

public sealed class InMemoryStoreTelemetryTests
{
    [Fact]
    public async Task StoreOperationsEmitUniversalSpansAndMetricsWithoutIdsOrPayloadValues()
    {
        using var activities = new ActivityCollector(HPDBaseActivitySourceNames.InMemory);
        using var metrics = new MeterCollector(HPDBaseMeterNames.InMemory);
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { StoreId = "primary" });
        var collection = InMemoryTestData.Collection();
        var context = InMemoryTestData.Operation(BaseOperationKind.Create);

        var create = await InMemoryMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest
            {
                RequestedId = RecordId.Create("rec_secret"),
                Payload = InMemoryTestData.Payload(("title", "payload-secret"))
            },
            context);
        await store.GetAsync(collection, RecordId.Create("rec_secret"), InMemoryTestData.Operation(BaseOperationKind.Get));
        await store.ListAsync(
            collection,
            new RecordQuery
            {
                Filter = new FilterExpression
                {
                    Kind = FilterNodeKind.Compare,
                    Field = "title",
                    Operator = FilterOperator.Equal,
                    Value = new QueryValue { Kind = QueryValueKind.String, String = "payload-secret" }
                }
            },
            InMemoryTestData.Operation(BaseOperationKind.List));

        Assert.Equal(OperationStatus.Created, create.Status);
        Assert.Contains(HPDBaseTelemetrySpans.StoreCreate, activities.Names);
        Assert.Contains(HPDBaseTelemetrySpans.StoreGet, activities.Names);
        Assert.Contains(HPDBaseTelemetrySpans.StoreList, activities.Names);
        Assert.Contains(HPDBaseTelemetryInstruments.StoreOperations, metrics.InstrumentNames);
        Assert.Contains(HPDBaseTelemetryInstruments.StoreOperationDuration, metrics.InstrumentNames);
        Assert.All(activities.Stopped, activity =>
        {
            Assert.Equal(HPDBaseTelemetryValues.ModuleInMemory, Tag(activity, HPDBaseTelemetryTags.ModuleId));
            Assert.Equal(HPDBaseTelemetryValues.ProviderInMemory, Tag(activity, HPDBaseTelemetryTags.ProviderKind));
        });
        Assert.DoesNotContain(activities.Stopped, activity => TagValues(activity).Any(value => value.Contains("rec_secret", StringComparison.Ordinal) || value.Contains("payload-secret", StringComparison.Ordinal)));
    }

    private static object? Tag(Activity activity, string key) =>
        activity.TagObjects.FirstOrDefault(tag => tag.Key == key).Value;

    private static string[] TagValues(Activity activity) =>
        activity.TagObjects.Select(tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToArray();

}
