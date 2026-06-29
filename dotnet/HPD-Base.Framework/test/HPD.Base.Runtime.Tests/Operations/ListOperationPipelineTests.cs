using HPD.Base.Query;
using HPD.Base.Results;
using HPD.Base.Runtime.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Operations;

public sealed class ListOperationPipelineTests
{
    [Fact]
    public async Task OverLimitQueryFailsBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            "items",
            new RecordQuery { Page = new QueryPage { Limit = 10_000 } },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.List),
            CancellationToken.None);

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal(0, store.ListCalls);
    }

    [Fact]
    public async Task PolicyDeniedListFailsBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store, new DenyPolicyEvaluator());

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            "items",
            new RecordQuery(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.List),
            CancellationToken.None);

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal(0, store.ListCalls);
    }

    [Fact]
    public async Task PolicyRecordFilterComposesBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store, new ConstrainedPolicyEvaluator(Compare("tenantId", "tenant_1")));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            "items",
            new RecordQuery { Filter = Compare("title", "hello") },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.List),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(1, store.ListCalls);
        Assert.Equal(FilterNodeKind.And, store.LastListQuery!.Filter!.Kind);
        Assert.Collection(
            store.LastListQuery.Filter.Children!,
            child => Assert.Equal("tenantId", child.Field),
            child => Assert.Equal("title", child.Field));
    }

    [Fact]
    public async Task InvalidPolicyRecordFilterFailsBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store, new ConstrainedPolicyEvaluator(new FilterExpression
        {
            Kind = FilterNodeKind.Compare,
            Field = "tenantId",
            Operator = FilterOperator.Equal
        }));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            "items",
            new RecordQuery(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.List),
            CancellationToken.None);

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal(0, store.ListCalls);
    }

    [Fact]
    public async Task KnownStoreFailureMapsToStoreError()
    {
        var store = new ThrowingRecordStore("primary", new TimeoutException("timed out"));
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            "items",
            new RecordQuery(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.List),
            CancellationToken.None);

        Assert.Equal(OperationStatus.StoreError, result.Status);
        Assert.Equal("base.runtime.store.dependencyFailure", result.Error!.Code);
        Assert.True(result.Error.Store!.Retryable);
    }

    [Fact]
    public async Task UnknownStoreFailureIsNotSwallowed()
    {
        var store = new ThrowingRecordStore("primary", new InvalidOperationException("programmer error"));
        using var provider = OperationTestServices.Build(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
                "items",
                new RecordQuery(),
                RuntimeTestData.AnonymousPrincipal,
                RuntimeTestData.Operation(BaseOperationKind.List),
                CancellationToken.None).AsTask());
    }

    private static FilterExpression Compare(string field, string value) => new()
    {
        Kind = FilterNodeKind.Compare,
        Field = field,
        Operator = FilterOperator.Equal,
        Value = new QueryValue
        {
            Kind = QueryValueKind.String,
            String = value
        }
    };
}
