using System.Text.Json;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;

namespace HPD.Base;

/// <summary>Registers and maps bounded L38-aware vector HTTP endpoints.</summary>
public static class HPDBaseVectorAspNetCoreExtensions
{
    /// <summary>Registers bounded vector HTTP binding services.</summary>
    public static IServiceCollection AddHPDBaseVectorAspNetCore(this IServiceCollection services, Action<HPDBaseVectorHttpOptions>? configure = null)
    { ArgumentNullException.ThrowIfNull(services); var options = new HPDBaseVectorHttpOptions(); configure?.Invoke(options); if (options.MaxRequestBodyBytes is < 16 * 1024 or > 4 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(configure)); services.AddSingleton(new HPDBaseVectorHttpSnapshot(options.MaxRequestBodyBytes)); services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, VectorModuleStartupFilter>()); return services; }

    /// <summary>Maps the vector query into an already secured Application group.</summary>
    public static RouteGroupBuilder MapHPDBaseVectorApplicationApi(this RouteGroupBuilder group)
    { ArgumentNullException.ThrowIfNull(group); MapQuery(group, HPDBaseEndpointAudience.Application); return group; }

    /// <summary>Maps vector query and administration into an already secured ControlPlane group.</summary>
    public static RouteGroupBuilder MapHPDBaseVectorControlPlaneApi(this RouteGroupBuilder group, Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor> convention)
    {
        ArgumentNullException.ThrowIfNull(group); ArgumentNullException.ThrowIfNull(convention);
        MapQuery(group, HPDBaseEndpointAudience.ControlPlane, convention);
        group.MapGet("/vector/indexes", (RequestDelegate)ListIndexes).WithHPDBaseEndpoint("hpd.base.vector.metadata.list", HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.VectorMetadataRead, HPDBaseCapabilities.VectorMetadataRead, convention).WithVectorResponses(typeof(BaseVectorIndexStatus[]), 200, 424).WithName("hpd.base.vector.metadata.list");
        group.MapGet("/vector/indexes/{collectionId}/{vectorIndexId}/diagnostics", (RequestDelegate)GetDiagnostics).WithHPDBaseEndpoint("hpd.base.vector.diagnostics.read", HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.DiagnosticsRead, HPDBaseCapabilities.VectorDiagnosticsRead, convention).WithVectorResponses(typeof(BaseVectorIndexStatus), 200, 404, 424).WithName("hpd.base.vector.diagnostics.read");
        group.MapPost("/vector/indexes/{collectionId}/{vectorIndexId}/rebuild", (RequestDelegate)Rebuild).WithHPDBaseEndpoint("hpd.base.vector.rebuild", HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.VectorRebuild, HPDBaseCapabilities.VectorRebuild, convention).WithVectorRequest(typeof(BaseVectorHttpRebuildRequest)).WithVectorResponses(typeof(BaseVectorRebuildResult), 200, 400, 403, 404, 409, 413).WithName("hpd.base.vector.rebuild");
        return group;
    }

    private static void MapQuery(RouteGroupBuilder endpoints, HPDBaseEndpointAudience audience, Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor>? convention = null)
    {
        IEndpointConventionBuilder route = endpoints.MapPost("/vector/{collectionId}/{vectorIndexId}/query", (RequestDelegate)Query);
        route.WithHPDBaseEndpoint("hpd.base.vector.query", audience, HPDBaseEndpointOperation.VectorQuery, HPDBaseCapabilities.VectorQuery, convention)
            .WithHPDBaseOpenApi("hpd.base.vector.query")
            .WithVectorRequest(typeof(BaseVectorHttpQueryRequest))
            .WithVectorResponses(typeof(BaseVectorHttpQueryResponse), 200, 400, 403, 404, 408, 409, 410, 413, 422, 424, 502, 504)
            .WithName("hpd.base.vector.query");
    }

    private static async Task Query(HttpContext context)
    {
        HPDBaseVectorHttpSnapshot options = context.RequestServices.GetRequiredService<HPDBaseVectorHttpSnapshot>();
        if (context.Request.ContentLength > options.MaxRequestBodyBytes) { await Error(context, 413, "base.vector.limitExceeded", "The vector request body exceeds the configured limit."); return; }
        BaseVectorHttpQueryRequest? body;
        try { await using var limited = new LimitedRequestBodyStream(context.Request.Body, options.MaxRequestBodyBytes); body = await JsonSerializer.DeserializeAsync(limited, BaseVectorHttpJsonContext.Default.BaseVectorHttpQueryRequest, context.RequestAborted); }
        catch (RequestBodyTooLargeException) { await Error(context, 413, "base.vector.limitExceeded", "The vector request body exceeds the configured limit."); return; }
        catch (JsonException) { await Error(context, 400, "base.vector.invalid", "The vector request body is invalid."); return; }
        if (body?.Vector is null || !BaseVector.TryCreate(body.Vector, out BaseVector vector) || !Enum.IsDefined(body.MeasureDisclosure)) { await Error(context, 400, "base.vector.invalid", "The vector request body is invalid."); return; }
        string collectionId = Convert.ToString(context.Request.RouteValues["collectionId"], System.Globalization.CultureInfo.InvariantCulture) ?? ""; string indexId = Convert.ToString(context.Request.RouteValues["vectorIndexId"], System.Globalization.CultureInfo.InvariantCulture) ?? "";
        BaseCollectionRegistry registry = context.RequestServices.GetRequiredService<BaseCollectionRegistry>();
        if (!registry.Collections.TryGetValue(collectionId, out CollectionDefinition? collection) || (collection.VectorIndexes ?? []).SingleOrDefault(index => index.Id == indexId) is not { } index) { await Error(context, 404, "base.vector.indexNotFound", "The vector index was not found."); return; }
        BaseVectorCandidateConstraint constraint;
        try { constraint = Constraint(body.Filters ?? [], index); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException) { await Error(context, 400, "base.vector.filterUnsupported", "The vector filter is invalid or unsupported."); return; }
        BaseVectorConsistencyRequirement consistency;
        try { consistency = Consistency(body); }
        catch (FormatException) { await Error(context, 400, "base.vector.consistencyInvalid", "The vector consistency token is invalid."); return; }
        PrincipalContext principal = await context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>().CreateAsync(context, context.RequestAborted);
        OperationContext operation = context.RequestServices.GetRequiredService<IBaseHttpOperationContextFactory>().Create(context, principal, BaseOperationKind.VectorQuery, collectionId);
        OperationResult<BaseVectorRuntimeResult> result = await context.RequestServices.GetRequiredService<IBaseVectorRuntime>().ExecuteAsync(new BaseVectorRuntimeRequest { Collection = collection, Index = index, Vector = vector, Constraint = constraint, Take = body.Take, Consistency = consistency, Principal = principal, Operation = operation }, context.RequestAborted);
        if (!result.Status.IsSuccess() || result.Value is null) { await Error(context, Status(result), result.Error?.Code ?? "base.vector.providerResultInvalid", result.Error?.Message ?? "The vector request failed."); return; }
        BaseVectorRuntimeResult value = result.Value; bool includeMeasures = body.MeasureDisclosure == BaseVectorHttpMeasureDisclosure.Include; var response = new BaseVectorHttpQueryResponse { Matches = value.Matches.Select(item => new BaseVectorHttpMatch { Record = item.Record, Rank = item.Rank, Measure = includeMeasures ? new BaseVectorHttpMeasure { Function = item.Measure.Function switch { BaseVectorFunction.CosineSimilarity => "cosineSimilarity", BaseVectorFunction.DotProductSimilarity => "dotProductSimilarity", _ => "euclideanDistance" }, Value = item.Measure.Value, Direction = item.Measure.Direction == BaseVectorMeasureDirection.HigherIsNearer ? "higherIsNearer" : "lowerIsNearer", NormalizedRelevance = item.Measure.NormalizedRelevance } : null }).ToArray(), VectorIndexId = value.VectorIndexId, VectorIndexGeneration = value.VectorIndexGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture), ProviderId = value.ProviderId, Accuracy = value.Accuracy, ConsistencyToken = value.ConsistencyToken.Encode() };
        context.Response.StatusCode = 200; context.Response.ContentType = "application/json; charset=utf-8"; await JsonSerializer.SerializeAsync(context.Response.Body, response, BaseVectorHttpJsonContext.Default.BaseVectorHttpQueryResponse, context.RequestAborted);
    }

    private static async Task ListIndexes(HttpContext context)
    {
        OperationResult<BaseVectorIndexStatus[]> result = await context.RequestServices.GetRequiredService<IBaseVectorAdministration>().ListAsync(context.RequestAborted).ConfigureAwait(false);
        if (!result.Status.IsSuccess() || result.Value is null) { await Error(context, 424, result.Error?.Code ?? "base.vector.providerUnavailable", "Vector index state is unavailable."); return; }
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, result.Value, BaseVectorHttpJsonContext.Default.BaseVectorIndexStatusArray, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task GetDiagnostics(HttpContext context)
    {
        (string collectionId, string indexId) = RouteIds(context);
        OperationResult<BaseVectorIndexStatus> result = await context.RequestServices.GetRequiredService<IBaseVectorAdministration>().GetAsync(collectionId, indexId, context.RequestAborted).ConfigureAwait(false);
        if (!result.Status.IsSuccess() || result.Value is null) { await Error(context, result.Status == OperationStatus.NotFound ? 404 : 424, result.Error?.Code ?? "base.vector.providerUnavailable", "Vector index state is unavailable."); return; }
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, result.Value, BaseVectorHttpJsonContext.Default.BaseVectorIndexStatus, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task Rebuild(HttpContext context)
    {
        HPDBaseVectorHttpSnapshot options = context.RequestServices.GetRequiredService<HPDBaseVectorHttpSnapshot>();
        BaseVectorHttpRebuildRequest? body;
        try { await using var limited = new LimitedRequestBodyStream(context.Request.Body, options.MaxRequestBodyBytes); body = await JsonSerializer.DeserializeAsync(limited, BaseVectorHttpJsonContext.Default.BaseVectorHttpRebuildRequest, context.RequestAborted).ConfigureAwait(false); }
        catch (RequestBodyTooLargeException) { await Error(context, 413, "base.vector.limitExceeded", "The vector request body exceeds the configured limit."); return; }
        catch (JsonException) { await Error(context, 400, "base.vector.invalid", "The vector request body is invalid."); return; }
        if (body is null) { await Error(context, 400, "base.vector.invalid", "The vector request body is invalid."); return; }
        (string collectionId, string indexId) = RouteIds(context);
        BaseCollectionRegistry registry = context.RequestServices.GetRequiredService<BaseCollectionRegistry>();
        if (!registry.Collections.TryGetValue(collectionId, out CollectionDefinition? collection) || !(collection.VectorIndexes ?? []).Any(index => index.Id == indexId)) { await Error(context, 404, "base.vector.indexNotFound", "The vector index was not found."); return; }
        PrincipalContext principal = await context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>().CreateAsync(context, context.RequestAborted).ConfigureAwait(false);
        OperationContext operation = context.RequestServices.GetRequiredService<IBaseHttpOperationContextFactory>().Create(context, principal, BaseOperationKind.VectorRebuild, collectionId);
        OperationResult<BasePolicyEvaluation> allowed = await context.RequestServices.GetRequiredService<IBasePolicyOrchestrator>().EvaluateWriteAsync(new BasePolicyRequest { Principal = principal, Operation = operation, Collection = collection, ResourceKind = PolicyResourceKind.VectorIndex, VectorIndexId = indexId, VectorSpaceId = (collection.VectorIndexes ?? []).Single(index => index.Id == indexId).VectorSpaceId }, context.RequestAborted).ConfigureAwait(false);
        if (!allowed.Status.IsSuccess()) { await Error(context, 403, "base.vector.unauthorized", "The vector rebuild is not authorized."); return; }
        BaseResult<BaseVectorRebuildResult> result = await context.RequestServices.GetRequiredService<IHPDBaseAdministration>().RebuildVectorIndexAsync(new BaseVectorRebuildRequest { StoreId = body.StoreId, Principal = principal, CollectionId = collectionId, VectorIndexId = indexId, ExpectedGeneration = body.ExpectedGeneration, ExpectedPurgeGeneration = body.ExpectedPurgeGeneration, Confirmation = body.Confirmation }, context.RequestAborted).ConfigureAwait(false);
        if (!result.TryGetValue(out BaseVectorRebuildResult? rebuilt) || rebuilt is null) { BaseError? failure = (result as BaseFailure<BaseVectorRebuildResult>)?.Error; await Error(context, RebuildStatus(result.Status, failure?.Code), failure?.Code ?? "base.vector.providerUnavailable", "The vector rebuild failed."); return; }
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, rebuilt, BaseVectorHttpJsonContext.Default.BaseVectorRebuildResult, context.RequestAborted).ConfigureAwait(false);
    }

    private static (string CollectionId, string IndexId) RouteIds(HttpContext context) =>
        (Convert.ToString(context.Request.RouteValues["collectionId"], System.Globalization.CultureInfo.InvariantCulture) ?? "", Convert.ToString(context.Request.RouteValues["vectorIndexId"], System.Globalization.CultureInfo.InvariantCulture) ?? "");

    private static BaseVectorCandidateConstraint Constraint(BaseVectorHttpFilter[] filters, VectorIndexDefinition index)
    {
        if (filters.Length > 16) throw new ArgumentOutOfRangeException(nameof(filters));
        BaseVectorCandidateConstraint[] children = filters.Select(filter => { if (!index.FilterFieldIds.Contains(filter.FieldId, StringComparer.Ordinal)) throw new NotSupportedException(); BaseVectorFilterValue value = filter.Value.Kind switch { "null" when filter.Value.Text is null && filter.Value.Boolean is null && filter.Value.Integer is null => BaseVectorFilterValue.Null(), "string" when filter.Value.Text is not null => BaseVectorFilterValue.FromString(filter.Value.Text), "id" when filter.Value.Text is not null => BaseVectorFilterValue.FromId(filter.Value.Text), "boolean" when filter.Value.Boolean is not null => BaseVectorFilterValue.FromBoolean(filter.Value.Boolean.Value), "integer" when filter.Value.Integer is not null => BaseVectorFilterValue.FromInteger(filter.Value.Integer.Value), _ => throw new ArgumentException() }; return (BaseVectorCandidateConstraint)new BaseVectorCandidateConstraint.Equal(new BaseVectorFilterField(filter.FieldId, value.Kind), value); }).ToArray();
        return children.Length switch { 0 => new BaseVectorCandidateConstraint.True(), 1 => children[0], _ => new BaseVectorCandidateConstraint.And(children) };
    }
    private static BaseVectorConsistencyRequirement Consistency(BaseVectorHttpQueryRequest request) => request.Consistency switch
    {
        null or "current" when request.ConsistencyToken is null && request.MaximumAgeMilliseconds is null => new BaseVectorConsistencyRequirement.Current(),
        "available" when request.ConsistencyToken is null && request.MaximumAgeMilliseconds is null => new BaseVectorConsistencyRequirement.Available(),
        "atLeast" when request.ConsistencyToken is not null && request.MaximumAgeMilliseconds is null => new BaseVectorConsistencyRequirement.AtLeast(BaseVectorConsistencyToken.Parse(request.ConsistencyToken)),
        "boundedStaleness" when request.ConsistencyToken is null && request.MaximumAgeMilliseconds is >= 1 and <= 86_400_000 => new BaseVectorConsistencyRequirement.BoundedStaleness(TimeSpan.FromMilliseconds(request.MaximumAgeMilliseconds.Value)),
        _ => throw new FormatException()
    };
    private static int Status(OperationResult<BaseVectorRuntimeResult> result) => result.Error?.Code switch
    {
        "base.vector.indexNotFound" => 404,
        "base.vector.consistencyExpired" => 410,
        "base.vector.consistencyScopeMismatch" or "base.vector.snapshotChanged" => 409,
        "base.vector.indexUnavailable" or "base.vector.indexBuilding" or "base.vector.indexStale" or "base.vector.rebuildRequired" or "base.vector.consistencyUnavailable" or "base.vector.providerUnavailable" or "base.vector.capabilityUnavailable" or "base.vector.providerUnsupportedPlatform" or "base.vector.tokenProtectionRequired" => 424,
        "base.vector.filterUnsupported" or "base.vector.policyConstraintUnsupported" => 422,
        "base.vector.providerResultInvalid" => 502,
        "base.vector.hydrationFailed" or "base.vector.rebuildIndeterminate" or "base.vector.administrationFailed" => 500,
        "base.vector.timeout" => 504,
        "base.vector.cancelled" => 408,
        _ => result.Status == OperationStatus.Unsupported ? 422 : result.Status == OperationStatus.PolicyDenied ? 403 : 400,
    };
    private static int RebuildStatus(OperationStatus status, string? code) => code switch
    {
        "base.vector.timeout" => 504,
        "base.vector.cancelled" => 408,
        "base.vector.providerUnavailable" or "base.vector.indexUnavailable" or "base.vector.capabilityUnavailable" => 424,
        "base.vector.rebuildIndeterminate" or "base.vector.administrationFailed" => 500,
        _ => status == OperationStatus.Conflict ? 409 : status == OperationStatus.NotFound ? 404 : status == OperationStatus.PolicyDenied ? 403 : 400,
    };
    private static async Task Error(HttpContext context, int status, string code, string message) { context.Response.StatusCode = status; context.Response.ContentType = "application/json; charset=utf-8"; await JsonSerializer.SerializeAsync(context.Response.Body, new BaseVectorHttpError { Code = code, Message = message }, BaseVectorHttpJsonContext.Default.BaseVectorHttpError, context.RequestAborted); }

    private static IEndpointConventionBuilder WithVectorRequest(this IEndpointConventionBuilder builder, Type requestType)
    {
        builder.Add(endpoint => endpoint.Metadata.Add(new AcceptsMetadata(["application/json"], requestType, isOptional: false)));
        return builder;
    }

    private static IEndpointConventionBuilder WithVectorResponses(this IEndpointConventionBuilder builder, Type successType, int successStatus, params int[] errorStatuses)
    {
        builder.Add(endpoint =>
        {
            endpoint.Metadata.Add(new ProducesResponseTypeMetadata(successStatus, successType, ["application/json"]));
            foreach (int status in errorStatuses)
                endpoint.Metadata.Add(new ProducesResponseTypeMetadata(status, typeof(BaseVectorHttpError), ["application/json"]));
        });
        return builder;
    }
}

internal sealed class VectorModuleStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        if (app.ApplicationServices.GetServices<IBaseVectorProvider>().Count() != 1 || app.ApplicationServices.GetService<IBaseVectorRuntime>() is null)
            throw new InvalidOperationException("base.vector.providerUnavailable: mapped vector routes require one installed vector module and provider.");
        next(app);
    };
}
