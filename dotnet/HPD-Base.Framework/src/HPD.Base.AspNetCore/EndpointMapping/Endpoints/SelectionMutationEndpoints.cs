using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

/// <summary>Defines the closed merge-patch HTTP input for one installed selection profile.</summary>
public sealed record BaseMergePatchSelectionHttpRequest
{
    /// <summary>Gets the bounded query.</summary>
    [JsonPropertyName("query")] public required BaseSelectionMutationHttpQuery Query { get; init; }
    /// <summary>Gets the fixed patch.</summary>
    [JsonPropertyName("patch")] public required RecordPatchRequest Patch { get; init; }
    /// <summary>Gets the previous-state requirements.</summary>
    [JsonPropertyName("previousState")] public required BasePreviousStateRequirement PreviousState { get; init; }
    /// <summary>Gets the optional identified-request contract.</summary>
    [JsonPropertyName("requestIdentity")] public BaseMutationRequestIdentity? RequestIdentity { get; init; }
    /// <summary>Gets the optional caller observation timeout in ticks.</summary>
    [JsonPropertyName("callerWaitTimeoutTicks")] public long? CallerWaitTimeoutTicks { get; init; }
}

/// <summary>Defines the closed delete HTTP input for one installed selection profile.</summary>
public sealed record BaseDeleteSelectionHttpRequest
{
    /// <summary>Gets the bounded query.</summary>
    [JsonPropertyName("query")] public required BaseSelectionMutationHttpQuery Query { get; init; }
    /// <summary>Gets the previous-state requirements.</summary>
    [JsonPropertyName("previousState")] public required BasePreviousStateRequirement PreviousState { get; init; }
    /// <summary>Gets the optional identified-request contract.</summary>
    [JsonPropertyName("requestIdentity")] public BaseMutationRequestIdentity? RequestIdentity { get; init; }
    /// <summary>Gets the optional caller observation timeout in ticks.</summary>
    [JsonPropertyName("callerWaitTimeoutTicks")] public long? CallerWaitTimeoutTicks { get; init; }
}

/// <summary>Defines the query subset accepted by selection-mutation HTTP profiles.</summary>
public sealed record BaseSelectionMutationHttpQuery
{
    /// <summary>Gets the optional closed filter.</summary>
    [JsonPropertyName("filter")] public FilterExpression? Filter { get; init; }
    /// <summary>Gets the required total order.</summary>
    [JsonPropertyName("sort")] public required QuerySort[] Sort { get; init; }
    /// <summary>Gets the positive selected-record bound.</summary>
    [JsonPropertyName("take")] public required int Take { get; init; }
}

/// <summary>Returns the bounded HTTP result of a selection mutation.</summary>
public sealed record BaseSelectionMutationHttpResult
{
    /// <summary>Gets the selected count.</summary>
    [JsonPropertyName("selectedCount")] public required int SelectedCount { get; init; }
    /// <summary>Gets the mutated count.</summary>
    [JsonPropertyName("mutatedCount")] public required int MutatedCount { get; init; }
    /// <summary>Gets the canonical batch outcome.</summary>
    [JsonPropertyName("outcome")] public required BaseRecordBatchOutcome Outcome { get; init; }
    /// <summary>Gets the request disposition.</summary>
    [JsonPropertyName("requestDisposition")] public required BaseMutationRequestDisposition RequestDisposition { get; init; }
}

internal static class SelectionMutationEndpoints
{
    internal static void Map(IEndpointRouteBuilder endpoints, HPDBaseEndpointAudience audience,
        Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor>? convention = null)
    {
        BaseSelectionProfileRegistry? registry = endpoints.ServiceProvider.GetService<BaseSelectionProfileRegistry>();
        if (registry is null)
            return;
        foreach (BaseSelectionOperationProfile profile in registry.All
            .Where(profile => profile.HttpProjection is { } projection && ToAudience(projection.Audience) == audience)
            .OrderBy(static profile => profile.Id, StringComparer.Ordinal))
        {
            BaseSelectionOperationProfile captured = profile;
            string endpointId = $"base.selection-mutations.{profile.Id}.execute";
            endpoints.MapPost($"/selection-mutations/{profile.HttpProjection!.RouteName}/execute",
                    (RequestDelegate)(context => Execute(context, captured)))
                .WithHPDBaseEndpoint(endpointId, audience, HPDBaseEndpointOperation.SelectionMutation,
                    profile.RequiredGrantId, convention)
                .WithName(endpointId);
        }
    }

    private static async Task Execute(HttpContext context, BaseSelectionOperationProfile profile)
    {
        int maximumBody = profile.HttpProjection!.MaximumRequestBodyBytes;
        if (context.Request.ContentLength is { } contentLength && contentLength > maximumBody)
        {
            await Problem(context, OperationStatus.ValidationFailed, BaseSelectionErrorCodes.LimitExceeded);
            return;
        }
        object? body;
        try
        {
            await using var bounded = new LimitedRequestBodyStream(context.Request.Body, maximumBody);
            body = profile.MutationKind == BaseSelectionMutationKind.MergePatch
                ? await JsonSerializer.DeserializeAsync(bounded,
                    HPDBaseAspNetCoreJsonSerializerContext.Default.BaseMergePatchSelectionHttpRequest, context.RequestAborted).ConfigureAwait(false)
                : await JsonSerializer.DeserializeAsync(bounded,
                    HPDBaseAspNetCoreJsonSerializerContext.Default.BaseDeleteSelectionHttpRequest, context.RequestAborted).ConfigureAwait(false);
        }
        catch (InvalidDataException) { await Problem(context, OperationStatus.ValidationFailed, BaseSelectionErrorCodes.LimitExceeded); return; }
        catch (JsonException) { await Problem(context, OperationStatus.ValidationFailed, BaseSelectionErrorCodes.ContractInvalid); return; }
        if (body is null) { await Problem(context, OperationStatus.ValidationFailed, BaseSelectionErrorCodes.ContractInvalid); return; }
        IBaseHttpPrincipalContextFactory principals = context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>();
        PrincipalContext principal = await principals.CreateAsync(context, context.RequestAborted).ConfigureAwait(false);
        BaseSession session = context.RequestServices.GetRequiredService<IBaseSessionFactory>().For(principal);
        BaseCollectionRegistry collections = context.RequestServices.GetRequiredService<BaseCollectionRegistry>();
        if (!collections.Collections.TryGetValue(profile.CollectionId, out CollectionDefinition? collection))
        { await Problem(context, OperationStatus.NotFound, BaseSelectionErrorCodes.ProfileNotFound); return; }
        BaseSelectionMutationHttpQuery queryDto;
        RecordPatchRequest? patch;
        BasePreviousStateRequirement previous;
        BaseMutationRequestIdentity? identity;
        long? timeout;
        if (body is BaseMergePatchSelectionHttpRequest merge)
        { queryDto = merge.Query; patch = merge.Patch; previous = merge.PreviousState; identity = merge.RequestIdentity; timeout = merge.CallerWaitTimeoutTicks; }
        else
        { var delete = (BaseDeleteSelectionHttpRequest)body; queryDto = delete.Query; patch = null; previous = delete.PreviousState; identity = delete.RequestIdentity; timeout = delete.CallerWaitTimeoutTicks; }
        RecordQuery query = new() { Filter = queryDto.Filter, Sort = queryDto.Sort, Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = queryDto.Take } };
        BaseResult<BaseSelectionMutationResult> result = await context.RequestServices.GetRequiredService<IBaseSelectionMutationRuntime>()
            .ExecuteAsync(session, collection, profile, query, patch, previous, identity,
                timeout is null ? null : new BaseSelectionMutationExecutionOptions { CallerWaitTimeout = TimeSpan.FromTicks(timeout.Value) }, context.RequestAborted)
            .ConfigureAwait(false);
        if (result is BaseFailure<BaseSelectionMutationResult> failure) { await Problem(context, failure.Status, failure.Error.Code); return; }
        BaseSelectionMutationResult value = ((BaseSuccess<BaseSelectionMutationResult>)result).Value;
        await Results.Json(new BaseSelectionMutationHttpResult { SelectedCount = value.SelectedCount, MutatedCount = value.MutatedCount, Outcome = value.Outcome, RequestDisposition = value.RequestDisposition },
            HPDBaseAspNetCoreJsonSerializerContext.Default.BaseSelectionMutationHttpResult).ExecuteAsync(context);
    }

    private static HPDBaseEndpointAudience ToAudience(BaseSelectionEndpointAudience audience) => audience == BaseSelectionEndpointAudience.Application
        ? HPDBaseEndpointAudience.Application : HPDBaseEndpointAudience.ControlPlane;
    private static Task Problem(HttpContext context, OperationStatus status, string code) => Results.Problem(
        statusCode: BaseHttpStatusCodeMapper.ToStatusCode(status), title: "BASE selection mutation failed.",
        extensions: new Dictionary<string, object?> { ["hpd.error.code"] = code }).ExecuteAsync(context);
}
