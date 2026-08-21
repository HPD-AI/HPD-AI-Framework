using System.Collections.Immutable;
using System.Text.Json;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base;

/// <summary>Maps bounded policy-safe lexical query endpoints.</summary>
public static class HPDBaseTextAspNetCoreExtensions
{
    /// <summary>Maps lexical query into an already secured Application group.</summary>
    public static RouteGroupBuilder MapHPDBaseTextApplicationApi(this RouteGroupBuilder group) { MapQuery(group, HPDBaseEndpointAudience.Application); return group; }

    /// <summary>Maps lexical query and bounded index administration into a secured ControlPlane group.</summary>
    public static RouteGroupBuilder MapHPDBaseTextControlPlaneApi(this RouteGroupBuilder group, Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor> convention)
    {
        ArgumentNullException.ThrowIfNull(group); ArgumentNullException.ThrowIfNull(convention);
        MapQuery(group, HPDBaseEndpointAudience.ControlPlane, convention);
        group.MapGet("/text/indexes", (RequestDelegate)ListIndexes).WithHPDBaseEndpoint("hpd.base.text.metadata.list", HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.TextMetadataRead, HPDBaseCapabilities.TextIndexRead, convention).WithName("hpd.base.text.metadata.list");
        group.MapGet("/text/indexes/{collectionId}/{textIndexId}/diagnostics", (RequestDelegate)GetDiagnostics).WithHPDBaseEndpoint("hpd.base.text.diagnostics.read", HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.DiagnosticsRead, HPDBaseCapabilities.TextDiagnosticsRead, convention).WithName("hpd.base.text.diagnostics.read");
        group.MapPost("/text/indexes/{collectionId}/{textIndexId}/rebuild", (RequestDelegate)Rebuild).WithHPDBaseEndpoint("hpd.base.text.rebuild", HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.TextRebuild, HPDBaseCapabilities.TextRebuild, convention).WithName("hpd.base.text.rebuild");
        return group;
    }
    private static void MapQuery(RouteGroupBuilder group, HPDBaseEndpointAudience audience, Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor>? convention = null)
    {
        group.MapPost("/text/{collectionId}/{textIndexId}/query", (RequestDelegate)Query)
            .WithHPDBaseEndpoint("hpd.base.text.query", audience, HPDBaseEndpointOperation.TextQuery, HPDBaseCapabilities.TextQuery, convention)
            .WithHPDBaseOpenApi("hpd.base.text.query").WithName("hpd.base.text.query")
            .Add(endpoint => { endpoint.Metadata.Add(new AcceptsMetadata(["application/json"], typeof(BaseTextHttpQueryRequest), false)); endpoint.Metadata.Add(new ProducesResponseTypeMetadata(200, typeof(BaseTextHttpResult), ["application/json"])); });
    }

    private static async Task ListIndexes(HttpContext context)
    {
        PrincipalContext principal = await context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>().CreateAsync(context, context.RequestAborted).ConfigureAwait(false);
        BaseCollectionRegistry registry = context.RequestServices.GetRequiredService<BaseCollectionRegistry>(); IBasePolicyOrchestrator policy = context.RequestServices.GetRequiredService<IBasePolicyOrchestrator>(); IBaseHttpOperationContextFactory operations = context.RequestServices.GetRequiredService<IBaseHttpOperationContextFactory>();
        foreach (CollectionDefinition collection in registry.Collections.Values.OrderBy(static value => value.Id, StringComparer.Ordinal)) foreach (BaseTextIndexDefinition index in collection.TextIndexes ?? [])
        {
            OperationContext operation = operations.Create(context, principal, BaseOperationKind.TextIndexRead, collection.Id); OperationResult<BasePolicyEvaluation> admitted = await policy.EvaluateReadAsync(new() { Principal = principal, Operation = operation, Collection = collection, ResourceKind = PolicyResourceKind.TextIndex, TextIndexId = index.Id }, context.RequestAborted).ConfigureAwait(false);
            if (!admitted.Status.IsSuccess() || !BaseSystemCollectionGate.HasExactTextGrant(admitted, BaseTextGrants.IndexRead, principal, operation, collection.Id, index.Id)) { await Error(context, 403, BaseTextErrorCodes.Unauthorized); return; }
        }
        OperationResult<BaseTextIndexStatus[]> result = await context.RequestServices.GetRequiredService<IBaseTextAdministration>().ListAsync(context.RequestAborted).ConfigureAwait(false);
        if (!result.Status.IsSuccess() || result.Value is null) { await Error(context, Status(result.Status, result.Error?.Code), result.Error?.Code ?? BaseTextErrorCodes.IndexUnavailable); return; }
        context.Response.ContentType = "application/json; charset=utf-8"; await JsonSerializer.SerializeAsync(context.Response.Body, result.Value, BaseTextHttpJsonContext.Default.BaseTextIndexStatusArray, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task GetDiagnostics(HttpContext context)
    {
        (string collectionId, string indexId) = RouteIds(context); BaseCollectionRegistry registry = context.RequestServices.GetRequiredService<BaseCollectionRegistry>();
        if (!registry.Collections.TryGetValue(collectionId, out CollectionDefinition? collection) || (collection.TextIndexes ?? []).All(value => value.Id != indexId)) { await Error(context, 403, BaseTextErrorCodes.Unauthorized); return; }
        PrincipalContext principal = await context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>().CreateAsync(context, context.RequestAborted).ConfigureAwait(false); OperationContext operation = context.RequestServices.GetRequiredService<IBaseHttpOperationContextFactory>().Create(context, principal, BaseOperationKind.TextDiagnosticsRead, collectionId); OperationResult<BasePolicyEvaluation> admitted = await context.RequestServices.GetRequiredService<IBasePolicyOrchestrator>().EvaluateReadAsync(new() { Principal = principal, Operation = operation, Collection = collection, ResourceKind = PolicyResourceKind.TextIndex, TextIndexId = indexId }, context.RequestAborted).ConfigureAwait(false);
        if (!admitted.Status.IsSuccess() || !BaseSystemCollectionGate.HasExactTextGrant(admitted, BaseTextGrants.DiagnosticsRead, principal, operation, collectionId, indexId)) { await Error(context, 403, BaseTextErrorCodes.Unauthorized); return; }
        OperationResult<BaseTextIndexStatus> result = await context.RequestServices.GetRequiredService<IBaseTextAdministration>().GetAsync(collectionId, indexId, context.RequestAborted).ConfigureAwait(false);
        if (!result.Status.IsSuccess() || result.Value is null) { await Error(context, Status(result.Status, result.Error?.Code), result.Error?.Code ?? BaseTextErrorCodes.IndexUnavailable); return; }
        context.Response.ContentType = "application/json; charset=utf-8"; await JsonSerializer.SerializeAsync(context.Response.Body, result.Value, BaseTextHttpJsonContext.Default.BaseTextIndexStatus, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task Rebuild(HttpContext context)
    {
        BaseTextHttpRebuildRequest? body; try { body = JsonSerializer.Deserialize(await ReadStrictBodyAsync(context.Request, 16 * 1024, context.RequestAborted).ConfigureAwait(false), BaseTextHttpJsonContext.Default.BaseTextHttpRebuildRequest); } catch (InvalidDataException) { await Error(context, 413, BaseTextErrorCodes.BudgetExceeded); return; } catch (JsonException) { await Error(context, 400, BaseTextErrorCodes.QueryInvalid); return; }
        if (body is null) { await Error(context, 400, BaseTextErrorCodes.QueryInvalid); return; }
        (string collectionId, string indexId) = RouteIds(context); BaseCollectionRegistry registry = context.RequestServices.GetRequiredService<BaseCollectionRegistry>();
        if (!registry.Collections.TryGetValue(collectionId, out CollectionDefinition? collection) || (collection.TextIndexes ?? []).All(value => value.Id != indexId)) { await Error(context, 403, BaseTextErrorCodes.Unauthorized); return; }
        PrincipalContext principal = await context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>().CreateAsync(context, context.RequestAborted).ConfigureAwait(false); OperationContext operation = context.RequestServices.GetRequiredService<IBaseHttpOperationContextFactory>().Create(context, principal, BaseOperationKind.TextRebuild, collectionId);
        OperationResult<BasePolicyEvaluation> authorized = await context.RequestServices.GetRequiredService<IBasePolicyOrchestrator>().EvaluateWriteAsync(new BasePolicyRequest { Principal = principal, Operation = operation, Collection = collection, ResourceKind = PolicyResourceKind.TextIndex, TextIndexId = indexId }, context.RequestAborted).ConfigureAwait(false);
        if (!authorized.Status.IsSuccess() || !BaseSystemCollectionGate.HasExactTextGrant(authorized, BaseTextGrants.Rebuild, principal, operation, collectionId, indexId)) { await Error(context, 403, BaseTextErrorCodes.Unauthorized); return; }
        BaseMutationRequestFingerprint fingerprint; try { fingerprint = BaseMutationRequestFingerprint.Create(Convert.FromBase64String(body.Fingerprint)); } catch (Exception exception) when (exception is FormatException or ArgumentException) { await Error(context, 400, BaseTextErrorCodes.QueryInvalid); return; }
        BaseMutationRequestIdentity identity; try { identity = BaseMutationRequestIdentity.Create(body.Scope, body.Operation, body.IdempotencyKey, fingerprint); } catch (ArgumentException) { await Error(context, 400, BaseTextErrorCodes.QueryInvalid); return; }
        OperationResult<BaseTextRebuildResult> result = await context.RequestServices.GetRequiredService<IBaseTextAdministration>().RebuildAsync(new() { CollectionId = collectionId, TextIndexId = indexId, ExpectedGeneration = body.ExpectedGeneration, Identity = identity }, context.RequestAborted).ConfigureAwait(false);
        if (!result.Status.IsSuccess() || result.Value is null) { await Error(context, Status(result.Status, result.Error?.Code), result.Error?.Code ?? BaseTextErrorCodes.CommitIndeterminate); return; }
        context.Response.ContentType = "application/json; charset=utf-8"; await JsonSerializer.SerializeAsync(context.Response.Body, result.Value, BaseTextHttpJsonContext.Default.BaseTextRebuildResult, context.RequestAborted).ConfigureAwait(false);
    }

    private static (string CollectionId, string IndexId) RouteIds(HttpContext context) => (Convert.ToString(context.Request.RouteValues["collectionId"], System.Globalization.CultureInfo.InvariantCulture) ?? "", Convert.ToString(context.Request.RouteValues["textIndexId"], System.Globalization.CultureInfo.InvariantCulture) ?? "");

    private static async Task Query(HttpContext context)
    {
        BaseTextHttpQueryRequest? body;
        try { body = JsonSerializer.Deserialize(await ReadStrictBodyAsync(context.Request, checked((int)BaseTextPlatform.DefaultLimits.MaximumQueryBytes + 16 * 1024), context.RequestAborted).ConfigureAwait(false), BaseTextHttpJsonContext.Default.BaseTextHttpQueryRequest); }
        catch (InvalidDataException) { await Error(context, 413, BaseTextErrorCodes.BudgetExceeded); return; }
        catch (JsonException) { await Error(context, 400, BaseTextErrorCodes.QueryInvalid); return; }
        string collectionId = Convert.ToString(context.Request.RouteValues["collectionId"], System.Globalization.CultureInfo.InvariantCulture) ?? "";
        string indexId = Convert.ToString(context.Request.RouteValues["textIndexId"], System.Globalization.CultureInfo.InvariantCulture) ?? "";
        BaseCollectionRegistry registry = context.RequestServices.GetRequiredService<BaseCollectionRegistry>();
        if (body is null || body.IndexId != indexId) { await Error(context, 400, BaseTextErrorCodes.QueryInvalid); return; }
        if (!registry.Collections.TryGetValue(collectionId, out CollectionDefinition? collection) || (collection.TextIndexes ?? []).SingleOrDefault(value => value.Id == indexId) is not { } index) { await Error(context, 403, BaseTextErrorCodes.Unauthorized); return; }
        try
        {
            BaseTextQuery query = QueryNode(body.Query); BaseTextCandidateConstraint filter = body.Filter is null ? new BaseTextCandidateConstraint.True() : Filter(body.Filter, index);
            BaseTextConsistencyRequirement consistency = body.Consistency switch
            {
                "current" when body.ConsistencyToken is null && body.MaximumAgeMilliseconds is null => new BaseTextConsistencyRequirement.Current(),
                "available" when body.ConsistencyToken is null && body.MaximumAgeMilliseconds is null => new BaseTextConsistencyRequirement.Available(),
                "atLeast" when body.ConsistencyToken is not null && body.MaximumAgeMilliseconds is null => new BaseTextConsistencyRequirement.AtLeast(BaseTextConsistencyToken.Parse(body.ConsistencyToken)),
                "boundedStaleness" when body.ConsistencyToken is null && body.MaximumAgeMilliseconds is >= 1 and <= 2_592_000_000 => new BaseTextConsistencyRequirement.BoundedStaleness(TimeSpan.FromMilliseconds(body.MaximumAgeMilliseconds.Value)),
                _ => throw new FormatException(),
            };
            BaseTextCursor? cursor = body.Cursor is null ? null : BaseTextCursor.Parse(body.Cursor);
            PrincipalContext principal = await context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>().CreateAsync(context, context.RequestAborted).ConfigureAwait(false);
            OperationContext operation = context.RequestServices.GetRequiredService<IBaseHttpOperationContextFactory>().Create(context, principal, BaseOperationKind.TextQuery, collectionId);
            OperationResult<BaseTextRuntimeResult> result = await context.RequestServices.GetRequiredService<IBaseTextRuntime>().ExecuteAsync(new() { Collection = collection, Index = index, Query = query, Constraint = filter, Take = body.Take, After = cursor, Consistency = consistency, Principal = principal, Operation = operation }, context.RequestAborted).ConfigureAwait(false);
            if (!result.Status.IsSuccess() || result.Value is null) { await Error(context, Status(result.Status, result.Error?.Code), result.Error?.Code ?? BaseTextErrorCodes.ProviderContractInvalid); return; }
            BaseTextRuntimeResult value = result.Value;
            var response = new BaseTextHttpResult { Matches = value.Matches.Select(static match => new BaseTextHttpMatch { Record = match.Record, Revision = match.Revision.Value, ScoreUnits = match.Score.Units.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToArray(), Next = value.Next?.Encode(), ConsistencyToken = value.Consistency.Encode() };
            context.Response.ContentType = "application/json; charset=utf-8"; await JsonSerializer.SerializeAsync(context.Response.Body, response, BaseTextHttpJsonContext.Default.BaseTextHttpResult, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException or OverflowException) { await Error(context, 400, BaseTextErrorCodes.QueryInvalid); }
    }

    private static BaseTextQuery QueryNode(BaseTextHttpQueryNode value) => value.Kind switch
    {
        "term" when Only(value, value: true) => BaseTextQuery.Token(value.Value!),
        "prefix" when Only(value, value: true) => BaseTextQuery.StartsWith(value.Value!),
        "phrase" when Only(value, terms: true) => BaseTextQuery.ExactPhrase(value.Terms!),
        "field" when Only(value, field: true, child: true) => BaseTextQuery.InField(value.Field!, QueryNode(value.Child!)),
        "and" when Only(value, children: true) => BaseTextQuery.All(value.Children!.Select(QueryNode).ToArray()),
        "or" when Only(value, children: true) => BaseTextQuery.Any(value.Children!.Select(QueryNode).ToArray()),
        "not" when Only(value, child: true) => BaseTextQuery.Exclude(QueryNode(value.Child!)),
        _ => throw new ArgumentException(),
    };
    private static bool Only(BaseTextHttpQueryNode node, bool value = false, bool terms = false, bool field = false, bool child = false, bool children = false) =>
        (value == (node.Value is not null)) && (terms == (node.Terms is not null)) && (field == (node.Field is not null)) && (child == (node.Child is not null)) && (children == (node.Children is not null));
    private static BaseTextCandidateConstraint Filter(BaseTextHttpFilter value, BaseTextIndexDefinition index)
    {
        if (value.Kind is "and" or "or" && value.Children is { Length: > 0 } children && value.Field is null && value.Value is null && value.Values is null)
        { BaseTextCandidateConstraint[] lowered = children.Select(child => Filter(child, index)).ToArray(); return value.Kind == "and" ? new BaseTextCandidateConstraint.And([.. lowered]) : new BaseTextCandidateConstraint.Or([.. lowered]); }
        BaseTextIndexFilterFieldDefinition field = index.FilterFields.Single(item => item.StableFieldId == value.Field); var handle = new BaseTextFilterField(field.StableFieldId, field.ValueKind);
        return value.Kind switch { "missing" when value.Value is null && value.Values is null && value.Children is null => new BaseTextCandidateConstraint.IsMissing(handle), "null" when value.Value is null && value.Values is null && value.Children is null => new BaseTextCandidateConstraint.IsNull(handle), "equal" when value.Value is not null && value.Values is null && value.Children is null => new BaseTextCandidateConstraint.Equal(handle, FilterValue(value.Value, field.ValueKind)), "in" when value.Value is null && value.Values is { Length: > 0 } values && value.Children is null => new BaseTextCandidateConstraint.In(handle, values.Select(item => FilterValue(item, field.ValueKind)).ToImmutableArray()), _ => throw new ArgumentException() };
    }
    private static BaseTextFilterValue FilterValue(BaseTextHttpFilterValue value, BaseTextFilterValueKind expected) => (value.Kind, expected) switch { ("string", BaseTextFilterValueKind.String) when value.Text is not null && value.Boolean is null && value.Integer is null => BaseTextFilterValue.FromString(value.Text), ("id", BaseTextFilterValueKind.Id) when value.Text is not null && value.Boolean is null && value.Integer is null => BaseTextFilterValue.FromId(value.Text), ("boolean", BaseTextFilterValueKind.Boolean) when value.Text is null && value.Boolean is not null && value.Integer is null => BaseTextFilterValue.FromBoolean(value.Boolean.Value), ("integer", BaseTextFilterValueKind.Integer) when value.Text is null && value.Boolean is null && value.Integer is not null => BaseTextFilterValue.FromInteger(value.Integer.Value), _ => throw new ArgumentException() };
    private static int Status(OperationStatus status, string? code) => code switch
    {
        BaseTextErrorCodes.BudgetExceeded => 413,
        BaseTextErrorCodes.IndexUnavailable or BaseTextErrorCodes.ConsistencyUnavailable or BaseTextErrorCodes.RebuildRequired or BaseTextErrorCodes.DerivedProjectionGap or BaseTextErrorCodes.DerivedProjectionCorrupt => 503,
        BaseTextErrorCodes.Timeout => 504,
        BaseTextErrorCodes.ProviderContractInvalid or BaseTextErrorCodes.CompletenessEvidenceInvalid or BaseTextErrorCodes.HistoryOvertaken => 424,
        _ => status switch { OperationStatus.Unauthorized => 401, OperationStatus.PolicyDenied => 403, OperationStatus.NotFound => 404, OperationStatus.Conflict => 409, OperationStatus.Unsupported => 422, OperationStatus.CapabilityUnavailable => 424, OperationStatus.StoreError => 503, _ => 400 },
    };
    private static async Task Error(HttpContext context, int status, string code) { context.Response.StatusCode = status; context.Response.ContentType = "application/json; charset=utf-8"; await JsonSerializer.SerializeAsync(context.Response.Body, new BaseTextHttpError { Code = code, Message = SafeMessage(code) }, BaseTextHttpJsonContext.Default.BaseTextHttpError, context.RequestAborted).ConfigureAwait(false); }
    private static async ValueTask<byte[]> ReadStrictBodyAsync(HttpRequest request, int maximumBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength is > 0 && request.ContentLength > maximumBytes) throw new InvalidDataException(BaseTextErrorCodes.BudgetExceeded);
        using var stream = new MemoryStream(Math.Min(maximumBytes, request.ContentLength is > 0 ? checked((int)request.ContentLength.Value) : 4096));
        byte[] buffer = new byte[8192]; int read; long total = 0;
        while ((read = await request.Body.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0) { total = checked(total + read); if (total > maximumBytes) throw new InvalidDataException(BaseTextErrorCodes.BudgetExceeded); stream.Write(buffer, 0, read); }
        byte[] payload = stream.ToArray(); ValidateNoDuplicateProperties(payload); return payload;
    }
    private static void ValidateNoDuplicateProperties(ReadOnlySpan<byte> payload)
    {
        var reader = new Utf8JsonReader(payload, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 });
        var objects = new Stack<HashSet<string>?>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.StartArray) objects.Push(null);
            else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) { if (objects.Count == 0) throw new JsonException(); objects.Pop(); }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (objects.Count == 0 || objects.Peek() is not { } names || !names.Add(reader.GetString()!)) throw new JsonException("Duplicate JSON property.");
            }
        }
        if (objects.Count != 0) throw new JsonException();
    }
    private static string SafeMessage(string code) => code switch
    {
        BaseTextErrorCodes.ContractInvalid => "The text search contract is invalid.", BaseTextErrorCodes.QueryInvalid => "The text search query is invalid.", BaseTextErrorCodes.Unauthorized => "The text search operation is not authorized.", BaseTextErrorCodes.IndexNotFound => "The text search index was not found.", BaseTextErrorCodes.CapabilityUnavailable => "The required text search capability is unavailable.", BaseTextErrorCodes.PolicyConstraintUnsupported => "The effective text search policy cannot be enforced.", BaseTextErrorCodes.IndexUnavailable => "The text search index is unavailable.", BaseTextErrorCodes.GenerationChanged => "The text search generation changed.", BaseTextErrorCodes.InMemoryGenerationChanged => "The in-memory text search generation changed.", BaseTextErrorCodes.SnapshotChanged => "The authoritative text search snapshot changed.", BaseTextErrorCodes.CursorInvalid => "The text search cursor is invalid.", BaseTextErrorCodes.CursorExpired => "The text search cursor has expired.", BaseTextErrorCodes.CursorScopeMismatch => "The text search cursor is not valid for this request.", BaseTextErrorCodes.ConsistencyUnavailable => "The requested text search consistency is unavailable.", BaseTextErrorCodes.HistoryOvertaken => "Required text search history is no longer retained.", BaseTextErrorCodes.DerivedProjectionGap => "The derived text search projection requires rebuilding.", BaseTextErrorCodes.DerivedProjectionCorrupt => "The derived text search projection is unhealthy.", BaseTextErrorCodes.BudgetExceeded => "The text search operation exceeds its configured limits.", BaseTextErrorCodes.ProviderContractInvalid => "The text search provider returned invalid evidence.", BaseTextErrorCodes.CompletenessEvidenceInvalid => "The text search provider returned invalid completeness evidence.", BaseTextErrorCodes.Timeout => "The text search operation timed out.", BaseTextErrorCodes.RebuildRequired => "The text search index requires rebuilding.", BaseTextErrorCodes.CommitIndeterminate => "The text search maintenance outcome is indeterminate.", _ => "The text search could not be completed.",
    };
}
