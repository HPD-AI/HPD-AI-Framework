using System.Collections.Concurrent;
using System.Diagnostics;
using HPD.Base.Tests.Volatile.TestDoubles;
using HPD.Base;
using HPD.Base.Tests.Observability;

namespace HPD.Base.Tests.Volatile.Observability;

public sealed class VolatileStoreTelemetryTests
{
    [Fact]
    public async Task StoreOperationsEmitUniversalSpansAndMetricsWithoutIdsOrPayloadValues()
    {
        using var activities = new ActivityCollector(HPDBaseActivitySourceNames.Volatile);
        using var metrics = new MeterCollector(HPDBaseMeterNames.Volatile);
        var store = new VolatileRecordStore(new HPDBaseVolatileStoreOptions { StoreId = "primary" });
        var collection = VolatileTestData.Collection();
        var context = VolatileTestData.Operation(BaseOperationKind.Create);

        var create = await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest
            {
                RequestedId = new RecordId("rec_secret"),
                Payload = VolatileTestData.Payload(("title", "payload-secret"))
            },
            context);
        await store.GetAsync(collection, new RecordId("rec_secret"), VolatileTestData.Operation(BaseOperationKind.Get));
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
            VolatileTestData.Operation(BaseOperationKind.List));

        Assert.Equal(OperationStatus.Created, create.Status);
        Assert.Contains(HPDBaseTelemetrySpans.StoreCreate, activities.Names);
        Assert.Contains(HPDBaseTelemetrySpans.StoreGet, activities.Names);
        Assert.Contains(HPDBaseTelemetrySpans.StoreList, activities.Names);
        Assert.Contains(HPDBaseTelemetryInstruments.StoreOperations, metrics.InstrumentNames);
        Assert.Contains(HPDBaseTelemetryInstruments.StoreOperationDuration, metrics.InstrumentNames);
        Assert.All(activities.Stopped, activity =>
        {
            Assert.Equal(HPDBaseTelemetryValues.ModuleVolatile, Tag(activity, HPDBaseTelemetryTags.ModuleId));
            Assert.Equal(HPDBaseTelemetryValues.ProviderVolatile, Tag(activity, HPDBaseTelemetryTags.ProviderKind));
        });
        Assert.DoesNotContain(activities.Stopped, activity => TagValues(activity).Any(value => value.Contains("rec_secret", StringComparison.Ordinal) || value.Contains("payload-secret", StringComparison.Ordinal)));
    }

    private static object? Tag(Activity activity, string key) =>
        activity.TagObjects.FirstOrDefault(tag => tag.Key == key).Value;

    private static string[] TagValues(Activity activity) =>
        activity.TagObjects.Select(tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToArray();

}
