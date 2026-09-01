using System.Text.Json;
using System.Buffers;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

internal sealed class BaseRealtimeLiveQueryTransport(IServiceProvider services)
{
    internal async ValueTask<IBaseLiveQuerySubscription<JsonElement>> OpenAsync(
        BaseRealtimeLiveQueryJoinRequest request,
        PrincipalContext principal,
        string correlationId,
        CancellationToken cancellationToken)
    {
        IBaseLiveQueryCoordinator coordinator = services.GetService<IBaseLiveQueryCoordinator>()
            ?? throw new BaseLiveQueryException(BaseLiveQueryErrorCodes.RequestInvalid, "Live-query support is not installed.");
        IHPDBaseRuntime runtime = services.GetRequiredService<IHPDBaseRuntime>();
        IBaseDependencyReferenceFactory dependencies = services.GetService<IBaseDependencyReferenceFactory>()
            ?? throw new BaseLiveQueryException(BaseLiveQueryErrorCodes.RequestInvalid, "Dependency support is not installed.");
        TimeProvider timeProvider = services.GetRequiredService<TimeProvider>();
        if (request.Operation is BaseRealtimeRegisteredReadOperation registered)
            return await OpenRegisteredAsync(request, registered, coordinator, principal, correlationId, timeProvider, cancellationToken).ConfigureAwait(false);
        if (request.Operation is not BaseRealtimeCollectionQueryOperation collectionQuery || collectionQuery.CollectionId.Length is 0 or > 128 || collectionQuery.Take is < 1 or > 500)
            throw new BaseLiveQueryException(BaseLiveQueryErrorCodes.RequestInvalid, "The live-query operation is invalid.");
        string collectionId = collectionQuery.CollectionId;
        if (!string.Equals(request.ResultTypeId, $"collection.{collectionId}.recordPage", StringComparison.Ordinal))
            throw new BaseLiveQueryException(BaseLiveQueryErrorCodes.RequestInvalid, "The live-query result contract is invalid.");

        RecordQuery query = collectionQuery.Query;
        if (query.Page is not null)
            throw new BaseLiveQueryException(BaseLiveQueryErrorCodes.RequestInvalid, "Live queries do not accept page requests.");
        query = query with { Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = collectionQuery.Take } };

        return await coordinator.SubscribeAsync(new BaseLiveQueryRequest<JsonElement>
        {
            QueryId = correlationId,
            ExecuteAsync = async token =>
            {
                var operation = new OperationContext
                {
                    Operation = BaseOperationKind.Query,
                    CollectionId = collectionId,
                    TenantId = principal.CurrentTenantId,
                    Mode = OperationMode.User,
                    CorrelationId = correlationId,
                    Now = timeProvider.GetUtcNow()
                };
                OperationResult<RecordPage> result = await runtime.Records.ListAsync(collectionId, query, principal, operation, token).ConfigureAwait(false);
                if (!result.IsSuccess() || result.Value is null)
                    throw new BaseLiveQueryException(result.Error?.Code ?? BaseLiveQueryErrorCodes.ExecutionFailed, "The live query could not be evaluated.");
                BaseDependencyReference collectionDependency = dependencies.Create(
                    BaseDependencyIds.Collection,
                    new BaseDependencyParameter("tenant", principal.CurrentTenantId),
                    new BaseDependencyParameter("collection", collectionId));
                return new BaseLiveQueryEvaluation<JsonElement>
                {
                    Value = JsonSerializer.SerializeToElement(result.Value, HPDBaseJsonSerializerContext.Default.RecordPage),
                    Dependencies = dependencies.CreateSet(collectionDependency)
                };
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IBaseLiveQuerySubscription<JsonElement>> OpenRegisteredAsync(
        BaseRealtimeLiveQueryJoinRequest request,
        BaseRealtimeRegisteredReadOperation operationRequest,
        IBaseLiveQueryCoordinator coordinator,
        PrincipalContext principal,
        string correlationId,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        BaseReadRegistry registry = services.GetRequiredService<BaseReadRegistry>();
        if (!registry.Registrations.TryGetValue(operationRequest.ReadId, out IBaseReadRegistration? registration)
            || !string.Equals(request.ResultTypeId, $"read.{operationRequest.ReadId}.rowPage", StringComparison.Ordinal))
            throw new BaseLiveQueryException(BaseLiveQueryErrorCodes.RequestInvalid, "The registered live-query contract is invalid.");
        object? parameters;
        BaseSerializerMetadataOwner? metadata = services.GetService<BaseSerializerMetadataOwner>();
        var parameterMetadata = metadata?.Resolve(registration, registration.ParameterJsonTypeInfo.Type) ?? registration.ParameterJsonTypeInfo;
        var rowMetadata = metadata?.Resolve(registration, registration.RowJsonTypeInfo.Type) ?? registration.RowJsonTypeInfo;
        try { parameters = operationRequest.Parameters.Deserialize(parameterMetadata); }
        catch (JsonException) { throw new BaseLiveQueryException(BaseLiveQueryErrorCodes.RequestInvalid, "The registered live-query input is invalid."); }
        if (parameters is null) throw new BaseLiveQueryException(BaseLiveQueryErrorCodes.RequestInvalid, "The registered live-query input is invalid.");
        IBaseRegisteredReadRuntime runtime = services.GetRequiredService<IBaseRegisteredReadRuntime>();
        int maximum = registration.Plan.Topology == BaseRelationalReadTopology.CompoundCount
            ? registration.Plan.CompoundCountBranches.Length
            : Math.Clamp(registration.Plan.Budgets.MaxResultRows, 1, 500);
        return await coordinator.SubscribeAsync(new BaseLiveQueryRequest<JsonElement>
        {
            QueryId = correlationId,
            ExecuteAsync = async token =>
            {
                var context = new OperationContext { Operation = BaseOperationKind.Query, CollectionId = operationRequest.ReadId, TenantId = principal.CurrentTenantId, Mode = OperationMode.User, CorrelationId = correlationId, Now = timeProvider.GetUtcNow() };
                BaseUntypedRegisteredReadResult result = await registration.ExecuteAsync(runtime, parameters, BaseReadPageRequest.Create(1, maximum), principal, context, token).ConfigureAwait(false);
                if (!result.Status.IsSuccess() || result.Items is null || result.Page is null || result.Dependencies is null)
                    throw new BaseLiveQueryException(result.Error?.Code ?? BaseLiveQueryErrorCodes.ExecutionFailed, "The registered live query could not be evaluated.");
                return new BaseLiveQueryEvaluation<JsonElement> { Value = SerializeReadPage(result, rowMetadata), Dependencies = result.Dependencies };
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static JsonElement SerializeReadPage(BaseUntypedRegisteredReadResult result, System.Text.Json.Serialization.Metadata.JsonTypeInfo rowMetadata)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject(); writer.WritePropertyName("items"); writer.WriteStartArray();
            foreach (object item in result.Items!) JsonSerializer.Serialize(writer, item, rowMetadata);
            writer.WriteEndArray(); writer.WritePropertyName("page"); JsonSerializer.Serialize(writer, result.Page, HPDBaseJsonSerializerContext.Default.PageInfo);
            if (result.Count is not null) { writer.WritePropertyName("count"); JsonSerializer.Serialize(writer, result.Count, HPDBaseJsonSerializerContext.Default.CountInfo); }
            writer.WriteEndObject(); writer.Flush();
        }
        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}

internal sealed class BaseRealtimeLiveQueryOwner : IAsyncDisposable
{
    private readonly IBaseLiveQuerySubscription<JsonElement> _subscription;
    private readonly CancellationTokenSource _cancellation;
    private readonly Func<BaseLiveQueryTransition<JsonElement>, CancellationToken, Task> _send;
    private readonly TaskCompletionSource _activation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _run;

    internal BaseRealtimeLiveQueryOwner(
        IBaseLiveQuerySubscription<JsonElement> subscription,
        CancellationToken sessionCancellation,
        Func<BaseLiveQueryTransition<JsonElement>, CancellationToken, Task> send)
    {
        _subscription = subscription;
        _send = send;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
        _run = RunAsync();
    }

    internal bool IsCompleted => _run.IsCompleted;
    internal void Activate() => _activation.TrySetResult();

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);
        _activation.TrySetCanceled(_cancellation.Token);
        try { await _run.ConfigureAwait(false); } catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        await _subscription.DisposeAsync().ConfigureAwait(false);
        _cancellation.Dispose();
    }

    private async Task RunAsync()
    {
        await _activation.Task.WaitAsync(_cancellation.Token).ConfigureAwait(false);
        await foreach (BaseLiveQueryTransition<JsonElement> transition in _subscription.Transitions.WithCancellation(_cancellation.Token).ConfigureAwait(false))
        {
            await _send(transition, _cancellation.Token).ConfigureAwait(false);
            if (transition.Kind == BaseLiveQueryTransitionKind.Failed) return;
        }
    }
}
