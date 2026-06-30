using System.Text.Json;
using HPD.Base;
using HPD.Base.AspNetCore.EndpointMapping;
using HPD.Base.AspNetCore.Configuration;
using HPD.Base.AspNetCore.Http;
using HPD.Base.AspNetCore.OpenApi;
using HPD.Base.AspNetCore.QueryBinding;
using HPD.Base.AspNetCore.Results;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore.EndpointMapping.Endpoints;

internal static class RecordEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/collections/{collectionId}/records", (RequestDelegate)ListRequest).WithHPDBaseOpenApi(BaseRouteIds.RecordsList).WithName(BaseRouteIds.RecordsList);
        endpoints.MapPost("/collections/{collectionId}/query", (RequestDelegate)QueryRequest).WithHPDBaseOpenApi(BaseRouteIds.RecordsQuery).WithName(BaseRouteIds.RecordsQuery);
        endpoints.MapGet("/collections/{collectionId}/records/{id}", (RequestDelegate)GetRequest).WithHPDBaseOpenApi(BaseRouteIds.RecordsGet).WithName(BaseRouteIds.RecordsGet);
        endpoints.MapPost("/collections/{collectionId}/records", (RequestDelegate)CreateRequest).WithHPDBaseOpenApi(BaseRouteIds.RecordsCreate).WithName(BaseRouteIds.RecordsCreate);
        endpoints.MapPatch("/collections/{collectionId}/records/{id}", (RequestDelegate)PatchRequest).WithHPDBaseOpenApi(BaseRouteIds.RecordsPatch).WithName(BaseRouteIds.RecordsPatch);
        endpoints.MapPut("/collections/{collectionId}/records/{id}", (RequestDelegate)ReplaceRequest).WithHPDBaseOpenApi(BaseRouteIds.RecordsReplace).WithName(BaseRouteIds.RecordsReplace);
        endpoints.MapDelete("/collections/{collectionId}/records/{id}", (RequestDelegate)DeleteRequest).WithHPDBaseOpenApi(BaseRouteIds.RecordsDelete).WithName(BaseRouteIds.RecordsDelete);
    }

    private static Task ListRequest(HttpContext httpContext) => Execute(httpContext,
        services => List(RouteValue(httpContext, "collectionId"), httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpQueryBinder>(), services.GetRequiredService<IBaseHttpResultMapper>(), httpContext.RequestAborted));

    private static async Task QueryRequest(HttpContext httpContext)
    {
        var query = await ReadOptionalBody(httpContext, HPDBaseJsonSerializerContext.Default.RecordQuery, httpContext.RequestAborted);
        await Execute(httpContext, services => Query(RouteValue(httpContext, "collectionId"), query, httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), httpContext.RequestAborted));
    }

    private static Task GetRequest(HttpContext httpContext) => Execute(httpContext,
        services => Get(RouteValue(httpContext, "collectionId"), RouteValue(httpContext, "id"), httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), httpContext.RequestAborted));

    private static async Task CreateRequest(HttpContext httpContext)
    {
        var request = await ReadRequiredBody(httpContext, HPDBaseJsonSerializerContext.Default.RecordCreateRequest, "base.http.body.required", "Create request body is required.", httpContext.RequestAborted);
        if (request.Value is null)
        {
            await request.Error!.ExecuteAsync(httpContext);
            return;
        }

        await Execute(httpContext, services => Create(RouteValue(httpContext, "collectionId"), request.Value, httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), httpContext.RequestAborted));
    }

    private static async Task PatchRequest(HttpContext httpContext)
    {
        var request = await ReadRequiredBody(httpContext, HPDBaseJsonSerializerContext.Default.RecordPatchRequest, "base.http.body.required", "Patch request body is required.", httpContext.RequestAborted);
        if (request.Value is null)
        {
            await request.Error!.ExecuteAsync(httpContext);
            return;
        }

        await Execute(httpContext, services => Patch(RouteValue(httpContext, "collectionId"), RouteValue(httpContext, "id"), request.Value, httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), httpContext.RequestAborted));
    }

    private static async Task ReplaceRequest(HttpContext httpContext)
    {
        var request = await ReadRequiredBody(httpContext, HPDBaseJsonSerializerContext.Default.RecordReplaceRequest, "base.http.body.required", "Replace request body is required.", httpContext.RequestAborted);
        if (request.Value is null)
        {
            await request.Error!.ExecuteAsync(httpContext);
            return;
        }

        await Execute(httpContext, services => Replace(RouteValue(httpContext, "collectionId"), RouteValue(httpContext, "id"), request.Value, httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), httpContext.RequestAborted));
    }

    private static async Task DeleteRequest(HttpContext httpContext)
    {
        var request = await ReadOptionalBody(httpContext, HPDBaseJsonSerializerContext.Default.RecordDeleteRequest, httpContext.RequestAborted);
        await Execute(httpContext, services => Delete(RouteValue(httpContext, "collectionId"), RouteValue(httpContext, "id"), request, httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), httpContext.RequestAborted));
    }

    private static async Task<IResult> List(
        string collectionId,
        HttpContext httpContext,
        IHPDBaseRuntime runtime,
        IBaseHttpPrincipalContextFactory principalFactory,
        IBaseHttpOperationContextFactory operationFactory,
        IBaseHttpQueryBinder queryBinder,
        IBaseHttpResultMapper resultMapper,
        CancellationToken cancellationToken)
    {
        var principal = await principalFactory.CreateAsync(httpContext, HPDBaseEndpointKind.Records, cancellationToken);
        var operation = operationFactory.Create(httpContext, principal, BaseOperationKind.List, collectionId);

        var query = await queryBinder.BindListQueryAsync(httpContext, cancellationToken);
        if (query.Status != OperationStatus.Ok)
            return resultMapper.ToHttpResult(query, httpContext, Mapping(operation));

        var result = await runtime.Records.ListAsync(collectionId, query.Value, principal, operation, cancellationToken);
        return resultMapper.ToHttpResult(result, httpContext, Mapping(operation));
    }

    private static Task<IResult> Query(
        string collectionId,
        RecordQuery? query,
        HttpContext httpContext,
        IHPDBaseRuntime runtime,
        IBaseHttpPrincipalContextFactory principalFactory,
        IBaseHttpOperationContextFactory operationFactory,
        IBaseHttpResultMapper resultMapper,
        CancellationToken cancellationToken) =>
        QueryCore(collectionId, query, httpContext, runtime, principalFactory, operationFactory, resultMapper, cancellationToken);

    private static async Task<IResult> QueryCore(string collectionId, RecordQuery? query, HttpContext httpContext, IHPDBaseRuntime runtime, IBaseHttpPrincipalContextFactory principalFactory, IBaseHttpOperationContextFactory operationFactory, IBaseHttpResultMapper resultMapper, CancellationToken cancellationToken)
    {
        var principal = await principalFactory.CreateAsync(httpContext, HPDBaseEndpointKind.Records, cancellationToken);
        var operation = operationFactory.Create(httpContext, principal, BaseOperationKind.Query, collectionId);
        var result = await runtime.Records.ListAsync(collectionId, query, principal, operation, cancellationToken);
        return resultMapper.ToHttpResult(result, httpContext, Mapping(operation));
    }

    private static async Task<IResult> Get(
        string collectionId,
        string id,
        HttpContext httpContext,
        IHPDBaseRuntime runtime,
        IBaseHttpPrincipalContextFactory principalFactory,
        IBaseHttpOperationContextFactory operationFactory,
        IBaseHttpResultMapper resultMapper,
        CancellationToken cancellationToken)
    {
        var principal = await principalFactory.CreateAsync(httpContext, HPDBaseEndpointKind.Records, cancellationToken);
        var operation = operationFactory.Create(httpContext, principal, BaseOperationKind.Get, collectionId, id);
        if (!TryBindRecordId(id, Limits(httpContext), out var recordId, out var validation))
            return resultMapper.ToHttpResult(validation!, httpContext, Mapping(operation));

        var result = await runtime.Records.GetAsync(collectionId, recordId, principal, operation, cancellationToken);
        return resultMapper.ToHttpResult(result, httpContext, Mapping(operation));
    }

    private static async Task<IResult> Create(
        string collectionId,
        RecordCreateRequest request,
        HttpContext httpContext,
        IHPDBaseRuntime runtime,
        IBaseHttpPrincipalContextFactory principalFactory,
        IBaseHttpOperationContextFactory operationFactory,
        IBaseHttpResultMapper resultMapper,
        CancellationToken cancellationToken)
    {
        var principal = await principalFactory.CreateAsync(httpContext, HPDBaseEndpointKind.Records, cancellationToken);
        var operation = operationFactory.Create(httpContext, principal, BaseOperationKind.Create, collectionId);
        var headerKey = BaseIdempotencyKeyBinder.Bind(httpContext);
        if (!string.IsNullOrWhiteSpace(headerKey))
        {
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey) && !string.Equals(request.IdempotencyKey, headerKey, StringComparison.Ordinal))
                return resultMapper.ToHttpResult(Validation<RecordEnvelope>("base.http.idempotency.conflict", "Idempotency-Key header conflicts with request body.", "idempotencyKey"), httpContext, Mapping(operation));

            request = request with { IdempotencyKey = headerKey };
        }

        var result = await runtime.Records.CreateAsync(collectionId, request, principal, operation, cancellationToken);
        var location = result.Value is null ? null : $"{httpContext.Request.Path}/{Uri.EscapeDataString(result.Value.Id.Value)}";
        return resultMapper.ToHttpResult(result, httpContext, Mapping(operation) with { Location = location });
    }

    private static async Task<IResult> Patch(
        string collectionId,
        string id,
        RecordPatchRequest request,
        HttpContext httpContext,
        IHPDBaseRuntime runtime,
        IBaseHttpPrincipalContextFactory principalFactory,
        IBaseHttpOperationContextFactory operationFactory,
        IBaseHttpResultMapper resultMapper,
        CancellationToken cancellationToken)
    {
        var principal = await principalFactory.CreateAsync(httpContext, HPDBaseEndpointKind.Records, cancellationToken);
        var operation = operationFactory.Create(httpContext, principal, BaseOperationKind.Patch, collectionId, id);
        if (!TryBindRecordId(id, Limits(httpContext), out var recordId, out var validation))
            return resultMapper.ToHttpResult(validation!, httpContext, Mapping(operation));

        var headerRevision = BaseIfMatchHeaderBinder.Bind(httpContext);
        if (!TryMergeRevision<RecordEnvelope>(request.ExpectedRevision, headerRevision, out var expectedRevision, out var revisionError))
            return resultMapper.ToHttpResult(revisionError!, httpContext, Mapping(operation));

        request = request with { ExpectedRevision = expectedRevision };
        var result = await runtime.Records.PatchAsync(collectionId, recordId, request, principal, operation, cancellationToken);
        return resultMapper.ToHttpResult(result, httpContext, Mapping(operation));
    }

    private static async Task<IResult> Replace(
        string collectionId,
        string id,
        RecordReplaceRequest request,
        HttpContext httpContext,
        IHPDBaseRuntime runtime,
        IBaseHttpPrincipalContextFactory principalFactory,
        IBaseHttpOperationContextFactory operationFactory,
        IBaseHttpResultMapper resultMapper,
        CancellationToken cancellationToken)
    {
        var principal = await principalFactory.CreateAsync(httpContext, HPDBaseEndpointKind.Records, cancellationToken);
        var operation = operationFactory.Create(httpContext, principal, BaseOperationKind.Replace, collectionId, id);
        if (!TryBindRecordId(id, Limits(httpContext), out var recordId, out var validation))
            return resultMapper.ToHttpResult(validation!, httpContext, Mapping(operation));

        var headerRevision = BaseIfMatchHeaderBinder.Bind(httpContext);
        if (!TryMergeRevision<RecordEnvelope>(request.ExpectedRevision, headerRevision, out var expectedRevision, out var revisionError))
            return resultMapper.ToHttpResult(revisionError!, httpContext, Mapping(operation));

        request = request with { ExpectedRevision = expectedRevision };
        var result = await runtime.Records.ReplaceAsync(collectionId, recordId, request, principal, operation, cancellationToken);
        return resultMapper.ToHttpResult(result, httpContext, Mapping(operation));
    }

    private static async Task<IResult> Delete(
        string collectionId,
        string id,
        RecordDeleteRequest? request,
        HttpContext httpContext,
        IHPDBaseRuntime runtime,
        IBaseHttpPrincipalContextFactory principalFactory,
        IBaseHttpOperationContextFactory operationFactory,
        IBaseHttpResultMapper resultMapper,
        CancellationToken cancellationToken)
    {
        var principal = await principalFactory.CreateAsync(httpContext, HPDBaseEndpointKind.Records, cancellationToken);
        var operation = operationFactory.Create(httpContext, principal, BaseOperationKind.Delete, collectionId, id);
        if (!TryBindRecordId(id, Limits(httpContext), out var recordId, out var validation))
            return resultMapper.ToHttpResult(validation!, httpContext, Mapping(operation));

        request ??= new RecordDeleteRequest();
        var headerRevision = BaseIfMatchHeaderBinder.Bind(httpContext);
        if (!TryMergeRevision<DeleteResult>(request.ExpectedRevision, headerRevision, out var expectedRevision, out var revisionError))
            return resultMapper.ToHttpResult(revisionError!, httpContext, Mapping(operation));

        request = request with { ExpectedRevision = expectedRevision };
        var result = await runtime.Records.DeleteAsync(collectionId, recordId, request, principal, operation, cancellationToken);
        return resultMapper.ToHttpResult(result, httpContext, Mapping(operation));
    }

    private static bool TryBindRecordId(string id, HPDBaseHttpLimitOptions limits, out RecordId recordId, out OperationResult<RecordEnvelope>? validation)
    {
        recordId = default;
        validation = null;

        if (string.IsNullOrWhiteSpace(id) || id.Any(char.IsControl) || id.Length > limits.MaxRouteIdLength)
        {
            validation = Validation<RecordEnvelope>("base.http.recordId.invalid", "Record id cannot be empty, exceed the configured maximum length, or contain control characters.", "id");
            return false;
        }

        recordId = new RecordId(id);
        return true;
    }

    private static bool TryMergeRevision<T>(
        RevisionToken? bodyRevision,
        RevisionToken? headerRevision,
        out RevisionToken? expectedRevision,
        out OperationResult<T>? validation)
    {
        expectedRevision = bodyRevision ?? headerRevision;
        validation = null;

        if (bodyRevision is not null && headerRevision is not null && bodyRevision.Value.Value != headerRevision.Value.Value)
        {
            validation = Validation<T>("base.http.revision.conflict", "If-Match header conflicts with request body expected revision.", "expectedRevision");
            return false;
        }

        return true;
    }

    private static OperationResult<T> Validation<T>(string code, string message, string? target) =>
        new()
        {
            Status = OperationStatus.ValidationFailed,
            Error = new BaseError
            {
                Code = code,
                Message = message,
                Target = target,
                Category = ErrorCategory.Validation
            }
        };

    private static string RouteValue(HttpContext httpContext, string key) =>
        httpContext.Request.RouteValues[key]?.ToString() ?? string.Empty;

    private static HPDBaseHttpLimitOptions Limits(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<IOptions<HPDBaseAspNetCoreOptions>>().Value.Limits;

    private static HPDBaseHttpResultMappingContext Mapping(OperationContext operation) =>
        new() { CorrelationId = operation.CorrelationId };

    private static async Task Execute(HttpContext httpContext, Func<IServiceProvider, Task<IResult>> handler)
    {
        var result = await handler(httpContext.RequestServices);
        await result.ExecuteAsync(httpContext);
    }

    private static async ValueTask<T?> ReadOptionalBody<T>(
        HttpContext httpContext,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.ContentLength is 0)
            return default;
        if (httpContext.Request.ContentLength is { } contentLength
            && contentLength > Limits(httpContext).MaxRequestBodyLength)
            throw new JsonException("Request body exceeds the configured maximum length.");
        if (httpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == false)
            return default;

        return await JsonSerializer.DeserializeAsync(httpContext.Request.Body, jsonTypeInfo, cancellationToken);
    }

    private static async ValueTask<(T? Value, IResult? Error)> ReadRequiredBody<T>(
        HttpContext httpContext,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await ReadOptionalBody(httpContext, jsonTypeInfo, cancellationToken);
            if (value is null)
            {
                return (default, BodyValidationProblem(httpContext, errorCode, errorMessage));
            }

            return (value, null);
        }
        catch (JsonException ex)
        {
            return (default, BodyValidationProblem(httpContext, "base.http.body.invalidJson", ex.Message));
        }
    }

    private static IResult BodyValidationProblem(HttpContext httpContext, string code, string message)
    {
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Type = "urn:hpd:base:error:validation",
            Detail = message,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["hpd.status"] = "validationFailed";
        problem.Extensions["hpd.error.code"] = code;
        problem.Extensions["hpd.error.category"] = "validation";
        return TypedResults.Problem(problem);
    }
}
