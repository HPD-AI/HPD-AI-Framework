using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Descriptors;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Results;
using HPD.Base.Runtime.Serialization;
using HPD.Base.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace HPD.Base.AspNetCore.Results;

internal sealed class BaseHttpResultMapper : IBaseHttpResultMapper
{
    private readonly BaseProblemDetailsFactory _problemDetailsFactory;
    private readonly IBaseJsonOptionsProvider _jsonOptionsProvider;

    public BaseHttpResultMapper(
        BaseProblemDetailsFactory problemDetailsFactory,
        IBaseJsonOptionsProvider jsonOptionsProvider)
    {
        _problemDetailsFactory = problemDetailsFactory;
        _jsonOptionsProvider = jsonOptionsProvider;
    }

    public IResult ToHttpResult<T>(
        OperationResult<T> result,
        HttpContext httpContext,
        HPDBaseHttpResultMappingContext mappingContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ApplyCommonHeaders(result.Status, result.Revision, result.Events, result.Diagnostics, httpContext, mappingContext);

        if (!result.IsSuccess())
            return Problem(result.Status, result.Error, result.Warnings, result.Diagnostics, httpContext, mappingContext);

        if (result.Status == OperationStatus.NoContent)
            return TypedResults.NoContent();

        ApplyValueHeaders(result.Value, httpContext);
        var statusCode = BaseHttpStatusCodeMapper.ToStatusCode(result.Status);
        var jsonTypeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)_jsonOptionsProvider.Options.GetTypeInfo(typeof(T));
        return TypedResults.Json(result.Value, jsonTypeInfo, statusCode: statusCode);
    }

    public IResult ToHttpResult(
        OperationResult result,
        HttpContext httpContext,
        HPDBaseHttpResultMappingContext mappingContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ApplyCommonHeaders(result.Status, result.Revision, result.Events, result.Diagnostics, httpContext, mappingContext);

        if (!result.IsSuccess())
            return Problem(result.Status, result.Error, result.Warnings, result.Diagnostics, httpContext, mappingContext);

        return result.Status == OperationStatus.NoContent
            ? TypedResults.NoContent()
            : Microsoft.AspNetCore.Http.Results.StatusCode(BaseHttpStatusCodeMapper.ToStatusCode(result.Status));
    }

    private IResult Problem(
        OperationStatus status,
        BaseError? error,
        OperationWarning[]? warnings,
        OperationDiagnostics? diagnostics,
        HttpContext httpContext,
        HPDBaseHttpResultMappingContext mappingContext)
    {
        var problem = _problemDetailsFactory.Create(
            status,
            error,
            warnings,
            diagnostics,
            httpContext.Request.Path,
            includeDiagnostics: mappingContext.IsAdmin || diagnostics?.SafeData is not null);

        return TypedResults.Problem(problem);
    }

    private static void ApplyCommonHeaders(
        OperationStatus status,
        RevisionInfo? revision,
        EventReference[]? events,
        OperationDiagnostics? diagnostics,
        HttpContext httpContext,
        HPDBaseHttpResultMappingContext mappingContext)
    {
        if (!string.IsNullOrWhiteSpace(revision?.ETag))
            httpContext.Response.Headers.ETag = revision.ETag;
        if (!string.IsNullOrWhiteSpace(revision?.Revision))
            httpContext.Response.Headers[Http.BaseHttpHeaders.Revision] = revision.Revision;
        if (revision?.LastModified is not null)
            httpContext.Response.Headers.LastModified = revision.LastModified.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (status == OperationStatus.Created && !string.IsNullOrWhiteSpace(mappingContext.Location))
            httpContext.Response.Headers.Location = mappingContext.Location;
        var correlationId = diagnostics?.CorrelationId ?? mappingContext.CorrelationId;
        if (!string.IsNullOrWhiteSpace(correlationId))
            httpContext.Response.Headers[Http.BaseHttpHeaders.CorrelationId] = correlationId;
        if (events is { Length: > 0 })
            httpContext.Response.Headers[Http.BaseHttpHeaders.EventIds] = string.Join(",", events.Select(static e => e.EventId));
        if (mappingContext.RetryAfter is { } retryAfter)
            httpContext.Response.Headers[Http.BaseHttpHeaders.RetryAfter] = Math.Max(0, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (mappingContext.PreferenceApplied is { Length: > 0 })
            httpContext.Response.Headers[Http.BaseHttpHeaders.PreferenceApplied] = string.Join(", ", mappingContext.PreferenceApplied);
    }

    private static void ApplyValueHeaders<T>(T? value, HttpContext httpContext)
    {
        if (value is RecordEnvelope envelope)
        {
            if (!string.IsNullOrWhiteSpace(envelope.Metadata.ETag))
                httpContext.Response.Headers.ETag = envelope.Metadata.ETag;
            if (envelope.Metadata.Revision is { } revision)
                httpContext.Response.Headers[Http.BaseHttpHeaders.Revision] = revision.Value;
            if (envelope.Metadata.UpdatedAt is not null)
                httpContext.Response.Headers.LastModified = envelope.Metadata.UpdatedAt.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (value is BaseManifest manifest)
        {
            ApplyEtag(manifest.ETag, httpContext);
        }
        else if (value is ExpandedBaseManifest expandedManifest)
        {
            ApplyEtag(expandedManifest.ETag ?? expandedManifest.Manifest.ETag, httpContext);
        }
        else if (value is SchemaMetadata schema)
        {
            ApplyEtag(schema.ETag, httpContext);
            if (schema.RefreshedAt is not null)
                httpContext.Response.Headers.LastModified = schema.RefreshedAt.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyEtag(string? etag, HttpContext httpContext)
    {
        if (!string.IsNullOrWhiteSpace(etag))
            httpContext.Response.Headers.ETag = etag;
    }
}
