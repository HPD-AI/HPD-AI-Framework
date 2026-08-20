using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

internal static class SubjectLifecycleEndpoints
{
    private const int MaximumBodyBytes = 16 * 1024;

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        BaseSubjectLifecycleRegistry? registry = endpoints.ServiceProvider.GetService<BaseSubjectLifecycleRegistry>();
        if (registry is null || registry.All.Count == 0)
            return;
        endpoints.MapPost("/subject-lifecycle/feed/read", (RequestDelegate)Read)
            .WithHPDBaseEndpoint("base.subjectLifecycle.feed.read", HPDBaseEndpointAudience.Application,
                HPDBaseEndpointOperation.SubjectLifecycleRead, HPDBaseCapabilities.SubjectLifecycleFeedRead)
            .WithName("base.subjectLifecycle.feed.read");
        endpoints.MapPost("/subject-lifecycle/feed/checkpoints", (RequestDelegate)Advance)
            .WithHPDBaseEndpoint("base.subjectLifecycle.feed.checkpoint", HPDBaseEndpointAudience.Application,
                HPDBaseEndpointOperation.SubjectLifecycleCheckpoint, HPDBaseCapabilities.SubjectLifecycleFeedCheckpoint)
            .WithName("base.subjectLifecycle.feed.checkpoint");
        if (registry.All.Any(static value => value.Definition.ReconciliationGrantId is not null))
            endpoints.MapPost("/subject-lifecycle/reconciliation/read", (RequestDelegate)Reconcile)
                .WithHPDBaseEndpoint("base.subjectLifecycle.reconciliation.read", HPDBaseEndpointAudience.Application,
                    HPDBaseEndpointOperation.SubjectLifecycleReconciliationRead, HPDBaseCapabilities.SubjectLifecycleReconcileRead)
                .WithName("base.subjectLifecycle.reconciliation.read");
    }

    private static async Task Read(HttpContext context)
    {
        EndpointState? state = await TryContext(context).ConfigureAwait(false);
        if (state is null) return;
        WireRequest? request = await ReadRequest(context, checkpointRequest: false).ConfigureAwait(false);
        if (request is null) return;
        BaseInstalledSubjectLifecycleConsumer? installed = state.Registry.All.SingleOrDefault(value =>
            string.Equals(value.Definition.Id, request.ConsumerId, StringComparison.Ordinal) && value.Definition.Version == request.ConsumerVersion);
        if (installed is null || installed.Definition.ContractId != request.ContractId || installed.Definition.ContractVersion != request.ContractVersion) { await Problem(context, OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized).ConfigureAwait(false); return; }
        if (!AudienceAllows(installed.Definition.Audience, state.Principal.AuthenticationState)) { await Problem(context, OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized).ConfigureAwait(false); return; }
        BaseSubjectLifecycleCursor? cursor;
        try { cursor = request.Cursor is null ? null : new BaseSubjectLifecycleCursor(Decode(request.Cursor, 8192)); }
        catch (FormatException) { await Problem(context, OperationStatus.ValidationFailed, BaseSubjectErrorCodes.CursorInvalid).ConfigureAwait(false); return; }
        BaseResult<BaseUntypedSubjectLifecyclePage> result = await state.Runtime.ReadUntypedAsync(state.Session(request.ProjectId), installed, cursor, request.Take, context.RequestAborted).ConfigureAwait(false);
        if (result is BaseFailure<BaseUntypedSubjectLifecyclePage> failure) { await Problem(context, failure.Status, failure.Error.Code).ConfigureAwait(false); return; }
        BaseUntypedSubjectLifecyclePage page = ((BaseSuccess<BaseUntypedSubjectLifecyclePage>)result).Value;
        context.Response.StatusCode = StatusCodes.Status200OK; context.Response.ContentType = "application/json; charset=utf-8";
        await using var writer = new Utf8JsonWriter(context.Response.BodyWriter);
        writer.WriteStartObject(); writer.WritePropertyName("facts"); writer.WriteStartArray();
        foreach (BaseSubjectLifecycleFact fact in page.Facts) WriteFact(writer, fact);
        writer.WriteEndArray();
        if (page.Next is null) writer.WriteNull("next"); else writer.WriteString("next", Encode(page.Next.ToArray()));
        writer.WriteString("checkpoint", Encode(page.Through.ToArray())); writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task Advance(HttpContext context)
    {
        EndpointState? state = await TryContext(context).ConfigureAwait(false);
        if (state is null) return;
        WireRequest? request = await ReadRequest(context, checkpointRequest: true).ConfigureAwait(false);
        if (request?.Checkpoint is null || request.Identity is null
            || !context.Request.Headers.TryGetValue(BaseHttpHeaders.IdempotencyKey, out var keys) || keys.Count != 1
            || !string.Equals(keys[0], request.Identity.IdempotencyKey, StringComparison.Ordinal))
        { await Problem(context, OperationStatus.ValidationFailed, BaseSubjectErrorCodes.LifecycleContractInvalid).ConfigureAwait(false); return; }
        BaseInstalledSubjectLifecycleConsumer? installed = state.Registry.All.SingleOrDefault(value =>
            string.Equals(value.Definition.Id, request.ConsumerId, StringComparison.Ordinal) && value.Definition.Version == request.ConsumerVersion);
        if (installed is null || installed.Definition.ContractId != request.ContractId || installed.Definition.ContractVersion != request.ContractVersion) { await Problem(context, OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized).ConfigureAwait(false); return; }
        if (!AudienceAllows(installed.Definition.Audience, state.Principal.AuthenticationState)) { await Problem(context, OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized).ConfigureAwait(false); return; }
        BaseSubjectLifecycleCheckpoint checkpoint;
        try { checkpoint = new BaseSubjectLifecycleCheckpoint(Decode(request.Checkpoint, 8192)); }
        catch (FormatException) { await Problem(context, OperationStatus.ValidationFailed, BaseSubjectErrorCodes.CursorInvalid).ConfigureAwait(false); return; }
        BaseMutationRequestIdentity identity;
        try
        {
            if (!string.Equals(request.Identity.Scope, $"subject-lifecycle:{installed.Definition.Id}", StringComparison.Ordinal)
                || !string.Equals(request.Identity.Operation, "subjectLifecycle.advance", StringComparison.Ordinal)
                || request.Identity.IdempotencyKey.Length != 64
                || request.Identity.IdempotencyKey.Any(static value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
                throw new ArgumentException();
            byte[] fingerprint = Convert.FromBase64String(request.Identity.Fingerprint);
            if (fingerprint.Length != BaseMutationRequestFingerprint.Length
                || !string.Equals(Convert.ToBase64String(fingerprint), request.Identity.Fingerprint, StringComparison.Ordinal))
                throw new ArgumentException();
            identity = BaseMutationRequestIdentity.Create(request.Identity.Scope, request.Identity.Operation,
                request.Identity.IdempotencyKey, BaseMutationRequestFingerprint.Create(fingerprint));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        { await Problem(context, OperationStatus.ValidationFailed, BaseSubjectErrorCodes.LifecycleContractInvalid).ConfigureAwait(false); return; }
        BaseResult<BaseSubjectLifecycleCheckpointResult> result = await state.Runtime.AdvanceUntypedAsync(state.Session(request.ProjectId), installed, checkpoint, identity, context.RequestAborted).ConfigureAwait(false);
        if (result is BaseFailure<BaseSubjectLifecycleCheckpointResult> failure) { await Problem(context, failure.Status, failure.Error.Code).ConfigureAwait(false); return; }
        BaseSubjectLifecycleCheckpointResult value = ((BaseSuccess<BaseSubjectLifecycleCheckpointResult>)result).Value;
        context.Response.StatusCode = StatusCodes.Status200OK; context.Response.ContentType = "application/json; charset=utf-8";
        await using var writer = new Utf8JsonWriter(context.Response.BodyWriter);
        writer.WriteStartObject(); writer.WriteString("checkpointGeneration", value.CheckpointGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteString("advancedAtUtc", value.AdvancedAtUtc); writer.WriteBoolean("duplicate", value.Duplicate); writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task Reconcile(HttpContext context)
    {
        EndpointState? state=await TryContext(context).ConfigureAwait(false);if(state is null)return;
        WireRequest? request=await ReadRequest(context,checkpointRequest:false,reconciliationRequest:true).ConfigureAwait(false);if(request is null)return;
        BaseInstalledSubjectLifecycleConsumer? installed=state.Registry.All.SingleOrDefault(value=>value.Definition.Id==request.ConsumerId&&value.Definition.Version==request.ConsumerVersion&&value.Definition.ContractId==request.ContractId&&value.Definition.ContractVersion==request.ContractVersion);
        if(installed is null||!AudienceAllows(installed.Definition.Audience,state.Principal.AuthenticationState)){await Problem(context,OperationStatus.PolicyDenied,BaseSubjectErrorCodes.LifecycleUnauthorized).ConfigureAwait(false);return;}
        BaseGeneratedSubjectRegistration? contract=state.Contracts.Find(request.ContractId,request.ContractVersion);
        if(contract is null){await Problem(context,OperationStatus.PolicyDenied,BaseSubjectErrorCodes.LifecycleUnauthorized).ConfigureAwait(false);return;}
        BaseSubjectId? after=null;try{if(request.AfterSubjectId is not null)after=BaseSubjectId.Create(request.AfterSubjectId,contract.Definition.SubjectIdKind,contract.Definition.MaximumSubjectIdUtf8Bytes);}catch(ArgumentException){await Problem(context,OperationStatus.ValidationFailed,BaseSubjectErrorCodes.LifecycleContractInvalid).ConfigureAwait(false);return;}
        BaseResult<BaseSubjectLifecycleProviderReconciliationPage> result=await state.Runtime.ReconcileUntypedAsync(state.Session(request.ProjectId),installed,after,request.Take,context.RequestAborted).ConfigureAwait(false);
        if(result is BaseFailure<BaseSubjectLifecycleProviderReconciliationPage> failure){await Problem(context,failure.Status,failure.Error.Code).ConfigureAwait(false);return;}
        BaseSubjectLifecycleProviderReconciliationPage page=((BaseSuccess<BaseSubjectLifecycleProviderReconciliationPage>)result).Value;
        context.Response.StatusCode=StatusCodes.Status200OK;context.Response.ContentType="application/json; charset=utf-8";await using var writer=new Utf8JsonWriter(context.Response.BodyWriter);writer.WriteStartObject();writer.WritePropertyName("subjects");writer.WriteStartArray();foreach(BaseCurrentSubjectLifecycle subject in page.Subjects){writer.WriteStartObject();writer.WriteString("subjectId",subject.SubjectId.Value);writer.WriteString("authorityEpoch",subject.AuthorityEpoch.ToBase64Url());writer.WriteString("incarnation",subject.Incarnation.ToBase64Url());writer.WriteString("state",State(subject.State));writer.WriteString("subjectSequence",subject.SubjectSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));writer.WriteEndObject();}writer.WriteEndArray();if(page.NextSubjectId is null)writer.WriteNull("nextSubjectId");else writer.WriteString("nextSubjectId",page.NextSubjectId.Value.Value);if(page.CapturedHighWater is null)writer.WriteNull("capturedHighWater");else{writer.WritePropertyName("capturedHighWater");WriteBoundary(writer,page.CapturedHighWater);}writer.WriteEndObject();await writer.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }

    private static async ValueTask<EndpointState?> TryContext(HttpContext context)
    {
        PrincipalContext principal = await context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>().CreateAsync(context, context.RequestAborted).ConfigureAwait(false);
        if (principal.AuthenticationState is not (PrincipalAuthenticationState.Service or PrincipalAuthenticationState.System))
        { await Problem(context, OperationStatus.PolicyDenied, BaseSubjectErrorCodes.LifecycleUnauthorized).ConfigureAwait(false); return null; }
        BaseSubjectLifecycleRegistry? registry = context.RequestServices.GetService<BaseSubjectLifecycleRegistry>();
        IBaseSubjectLifecycleRuntime? runtime = context.RequestServices.GetService<IBaseSubjectLifecycleRuntime>();
        if (registry is null || runtime is null)
        { await Problem(context, OperationStatus.CapabilityUnavailable, BaseSubjectErrorCodes.LifecycleProviderContractInvalid).ConfigureAwait(false); return null; }
        return new(registry, runtime, context.RequestServices.GetRequiredService<BaseSubjectContractRegistry>(), principal, context.RequestServices.GetRequiredService<IBaseSessionFactory>());
    }

    private static async ValueTask<WireRequest?> ReadRequest(HttpContext context, bool checkpointRequest, bool reconciliationRequest = false)
    {
        try
        {
            if (context.Request.ContentLength is > MaximumBodyBytes) throw new InvalidDataException();
            await using var bounded = new LimitedRequestBodyStream(context.Request.Body, MaximumBodyBytes);
            using JsonDocument document = await JsonDocument.ParseAsync(bounded, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 }, context.RequestAborted).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
            string[] allowed = checkpointRequest
                ? ["consumerId", "consumerVersion", "contractId", "contractVersion", "projectId", "checkpoint", "identity"]
                : reconciliationRequest
                    ? ["consumerId", "consumerVersion", "contractId", "contractVersion", "projectId", "take", "afterSubjectId"]
                    : ["consumerId", "consumerVersion", "contractId", "contractVersion", "projectId", "take", "cursor"];
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
                if (!names.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal))
                    throw new JsonException();
            if (!names.Contains("consumerId") || !names.Contains("consumerVersion") || !names.Contains("contractId") || !names.Contains("contractVersion")
                || checkpointRequest && (!names.Contains("checkpoint") || !names.Contains("identity")))
                throw new JsonException();
            string consumerId = root.GetProperty("consumerId").GetString() ?? throw new JsonException();
            int consumerVersion = root.GetProperty("consumerVersion").GetInt32();
            string contractId = root.GetProperty("contractId").GetString() ?? throw new JsonException();
            int contractVersion = root.GetProperty("contractVersion").GetInt32();
            int? take = root.TryGetProperty("take", out JsonElement takeValue) ? takeValue.GetInt32() : null;
            string? cursor = root.TryGetProperty("cursor", out JsonElement cursorValue) && cursorValue.ValueKind != JsonValueKind.Null ? cursorValue.GetString() : null;
            string? checkpoint = root.TryGetProperty("checkpoint", out JsonElement checkpointValue) && checkpointValue.ValueKind != JsonValueKind.Null ? checkpointValue.GetString() : null;
            string? projectId = root.TryGetProperty("projectId", out JsonElement projectValue) && projectValue.ValueKind != JsonValueKind.Null ? projectValue.GetString() : null;
            WireIdentity? identity = checkpointRequest ? ReadIdentity(root.GetProperty("identity")) : null;
            string? afterSubjectId=root.TryGetProperty("afterSubjectId",out JsonElement afterValue)&&afterValue.ValueKind!=JsonValueKind.Null?afterValue.GetString():null;
            if (consumerVersion < 1 || consumerId.Length is < 1 or > 128 || contractVersion<1 || contractId.Length is < 1 or > 128
                || projectId is { Length: < 1 or > 256 }
                || checkpointRequest && checkpoint is null) throw new JsonException();
            return new(consumerId, consumerVersion, contractId, contractVersion, projectId, take, cursor, checkpoint, afterSubjectId, identity);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or KeyNotFoundException)
        { await Problem(context, OperationStatus.ValidationFailed, BaseSubjectErrorCodes.LifecycleContractInvalid).ConfigureAwait(false); return null; }
    }

    private static WireIdentity ReadIdentity(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException();
        string[] allowed = ["scope", "operation", "idempotencyKey", "fingerprint"];
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
            if (!names.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new JsonException();
        if (names.Count != allowed.Length) throw new JsonException();
        return new(value.GetProperty("scope").GetString() ?? throw new JsonException(),
            value.GetProperty("operation").GetString() ?? throw new JsonException(),
            value.GetProperty("idempotencyKey").GetString() ?? throw new JsonException(),
            value.GetProperty("fingerprint").GetString() ?? throw new JsonException());
    }

    private static void WriteFact(Utf8JsonWriter writer, BaseSubjectLifecycleFact fact)
    {
        writer.WriteStartObject(); writer.WriteString("commitPosition", fact.CommitPosition.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)); writer.WriteString("contractId", fact.ContractId); writer.WriteNumber("contractVersion", fact.ContractVersion);
        writer.WriteString("subjectId", fact.SubjectId.Value); writer.WriteString("authorityEpoch", fact.AuthorityEpoch.ToBase64Url()); writer.WriteString("incarnation", fact.Incarnation.ToBase64Url());
        writer.WriteString("subjectSequence", fact.SubjectSequence.ToString(System.Globalization.CultureInfo.InvariantCulture)); writer.WriteString("contractStateGeneration", fact.ContractStateGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture)); writer.WriteString("deliveryEpoch", fact.DeliveryEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteString("kind", fact.Kind switch { BaseSubjectLifecycleFactKind.Created => "created", BaseSubjectLifecycleFactKind.Transitioned => "transitioned", _ => "retired" });
        if (fact.Created is { } created) writer.WriteString("currentState", State(created.CurrentState));
        if (fact.Transitioned is { } transitioned) { writer.WriteString("previousState", State(transitioned.PreviousState)); writer.WriteString("currentState", State(transitioned.CurrentState)); }
        if (fact.Retired is { } retired) writer.WriteString("previousState", State(retired.PreviousState));
        writer.WriteEndObject();
    }

    private static void WriteBoundary(Utf8JsonWriter writer, BaseSubjectLifecycleOrderingBoundary boundary)
    {
        writer.WriteStartObject();
        writer.WriteString("commitPosition", boundary.CommitPosition.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteString("subjectId", boundary.SubjectId.Value);
        writer.WriteString("authorityEpoch", boundary.AuthorityEpoch.ToBase64Url());
        writer.WriteString("incarnation", boundary.Incarnation.ToBase64Url());
        writer.WriteString("subjectSequence", boundary.SubjectSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static string State(BaseSubjectLifecycleState state) => state.ToString().ToLowerInvariant();
    internal static BaseClientNamedTypeDescriptor[] LifecycleTypes()
    {
        static BaseClientPropertyDescriptor Property(string name, string type, bool required, bool nullable = false) => new()
        {
            Name = name, WireName = name, TypeId = type, Required = required, Nullable = nullable,
            DisclosureShape = "none",
        };
        return
        [
            new() { Id = "base.subjectLifecycle.integer", Node = new() { Kind = "integer", Minimum = "1", Maximum = long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), Wire = "decimal-string" } },
            new() { Id = "base.subjectLifecycle.subjectId", Node = new() { Kind = "string", Format = "plain", MinLength = 1, MaxLength = 256 } },
            new() { Id = "base.subjectLifecycle.authorityEpoch", Node = new() { Kind = "subject-lifecycle-authority-epoch" } },
            new() { Id = "base.subjectLifecycle.incarnation", Node = new() { Kind = "subject-lifecycle-incarnation" } },
            new() { Id = "base.subjectLifecycle.cursor", Node = new() { Kind = "subject-lifecycle-cursor" } },
            new() { Id = "base.subjectLifecycle.checkpoint", Node = new() { Kind = "subject-lifecycle-checkpoint" } },
            new() { Id = "base.subjectLifecycle.state", Node = new() { Kind = "enum", Values = ["active", "inactive", "tombstoned", "retired"] } },
            new() { Id = "base.subjectLifecycle.kind", Node = new() { Kind = "enum", Values = ["created", "transitioned", "retired"] } },
            new() { Id = "base.subjectLifecycle.contractId", Node = new() { Kind = "string", Format = "plain", MinLength = 1, MaxLength = 128 } },
            new() { Id = "base.subjectLifecycle.contractVersion", Node = new() { Kind = "integer", Minimum = "1", Maximum = int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), Wire = "number" } },
            new() { Id = "base.subjectLifecycle.fact", Node = new() { Kind = "object", AdditionalProperties = false, Properties =
            [
                Property("commitPosition", "base.subjectLifecycle.integer", true), Property("contractId", "base.subjectLifecycle.contractId", true),
                Property("contractVersion", "base.subjectLifecycle.contractVersion", true), Property("subjectId", "base.subjectLifecycle.subjectId", true),
                Property("authorityEpoch", "base.subjectLifecycle.authorityEpoch", true), Property("incarnation", "base.subjectLifecycle.incarnation", true),
                Property("subjectSequence", "base.subjectLifecycle.integer", true), Property("contractStateGeneration", "base.subjectLifecycle.integer", true),
                Property("deliveryEpoch", "base.subjectLifecycle.integer", true), Property("kind", "base.subjectLifecycle.kind", true),
                Property("previousState", "base.subjectLifecycle.state", false), Property("currentState", "base.subjectLifecycle.state", false),
            ] } },
            new() { Id = "base.subjectLifecycle.facts", Node = new() { Kind = "array", ElementTypeId = "base.subjectLifecycle.fact", MinItems = 0, MaxItems = 256 } },
            new() { Id = "base.subjectLifecycle.page", Node = new() { Kind = "object", AdditionalProperties = false, Properties =
            [
                Property("facts", "base.subjectLifecycle.facts", true), Property("next", "base.subjectLifecycle.cursor", true, true),
                Property("checkpoint", "base.subjectLifecycle.checkpoint", true),
            ] } },
            new() { Id = "base.subjectLifecycle.current", Node = new() { Kind = "object", AdditionalProperties = false, Properties =
            [
                Property("subjectId", "base.subjectLifecycle.subjectId", true), Property("authorityEpoch", "base.subjectLifecycle.authorityEpoch", true),
                Property("incarnation", "base.subjectLifecycle.incarnation", true), Property("state", "base.subjectLifecycle.state", true),
                Property("subjectSequence", "base.subjectLifecycle.integer", true),
            ] } },
            new() { Id = "base.subjectLifecycle.currentItems", Node = new() { Kind = "array", ElementTypeId = "base.subjectLifecycle.current", MinItems = 0, MaxItems = 256 } },
            new() { Id = "base.subjectLifecycle.orderingBoundary", Node = new() { Kind = "object", AdditionalProperties = false, Properties =
            [
                Property("commitPosition", "base.subjectLifecycle.integer", true), Property("subjectId", "base.subjectLifecycle.subjectId", true),
                Property("authorityEpoch", "base.subjectLifecycle.authorityEpoch", true), Property("incarnation", "base.subjectLifecycle.incarnation", true),
                Property("subjectSequence", "base.subjectLifecycle.integer", true),
            ] } },
            new() { Id = "base.subjectLifecycle.reconciliation.page", Node = new() { Kind = "object", AdditionalProperties = false, Properties =
            [
                Property("subjects", "base.subjectLifecycle.currentItems", true), Property("nextSubjectId", "base.subjectLifecycle.subjectId", true, true),
                Property("capturedHighWater", "base.subjectLifecycle.orderingBoundary", true, true),
            ] } },
        ];
    }
    private static bool AudienceAllows(BaseSubjectLifecycleConsumerAudience audience, PrincipalAuthenticationState principal) => audience == BaseSubjectLifecycleConsumerAudience.System ? principal == PrincipalAuthenticationState.System : principal is PrincipalAuthenticationState.Service or PrincipalAuthenticationState.System;
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value, int maximum)
    {
        if (value.Length is < 1 or > 16_384) throw new FormatException();
        string text = value.Replace('-', '+').Replace('_', '/'); int remainder = text.Length % 4; if (remainder != 0) text = text.PadRight(text.Length + 4 - remainder, '=');
        byte[] bytes = Convert.FromBase64String(text); if (bytes.Length > maximum) throw new FormatException(); return bytes;
    }
    private static Task Problem(HttpContext context, OperationStatus status, string code) => Results.Problem(statusCode: code switch
    {
        BaseSubjectErrorCodes.CursorExpired or BaseSubjectErrorCodes.CursorOvertaken => StatusCodes.Status410Gone,
        BaseSubjectErrorCodes.LifecycleReconciliationUnavailable or BaseSubjectErrorCodes.LifecycleProviderContractInvalid => StatusCodes.Status424FailedDependency,
        BaseSubjectErrorCodes.LifecycleCapacityExceeded => StatusCodes.Status429TooManyRequests,
        BaseSubjectErrorCodes.LifecycleCommitIndeterminate => StatusCodes.Status500InternalServerError,
        _ => BaseHttpStatusCodeMapper.ToStatusCode(status),
    }, title: "BASE subject lifecycle operation failed.", detail: FailureMessage(code), extensions: new Dictionary<string, object?> { ["hpd.error.code"] = code }).ExecuteAsync(context);
    private static string FailureMessage(string code) => code switch
    {
        BaseSubjectErrorCodes.LifecycleContractInvalid => "The subject lifecycle contract is invalid.",
        BaseSubjectErrorCodes.LifecycleRegistrationConflict => "The subject lifecycle registration conflicts with the installed graph.",
        BaseSubjectErrorCodes.LifecycleUnauthorized => "The subject lifecycle operation is not authorized.",
        BaseSubjectErrorCodes.LifecycleTransitionInvalid => "The subject lifecycle transition is invalid.",
        BaseSubjectErrorCodes.SequenceExhausted => "The subject lifecycle sequence is exhausted.",
        BaseSubjectErrorCodes.LifetimeGenerationExhausted => "The subject lifetime generation is exhausted.",
        BaseSubjectErrorCodes.LifecycleIncarnationUnavailable => "A subject incarnation could not be allocated.",
        BaseSubjectErrorCodes.CursorInvalid => "The subject lifecycle cursor is invalid.",
        BaseSubjectErrorCodes.CursorExpired => "The subject lifecycle cursor has expired.",
        BaseSubjectErrorCodes.CursorScopeMismatch => "The subject lifecycle cursor is not valid for this scope.",
        BaseSubjectErrorCodes.ScopeAuthorityInvalid => "The subject lifecycle scope authority is invalid.",
        BaseSubjectErrorCodes.CursorOvertaken => "The subject lifecycle cursor is no longer retained.",
        BaseSubjectErrorCodes.LifecycleReconciliationUnavailable => "Subject lifecycle reconciliation is unavailable.",
        BaseSubjectErrorCodes.LifecycleProviderContractInvalid => "The provider cannot satisfy the subject lifecycle contract.",
        BaseSubjectErrorCodes.LifecycleCapacityExceeded => "Subject lifecycle capacity is unavailable.",
        BaseSubjectErrorCodes.Timeout => "The subject lifecycle operation timed out.",
        BaseSubjectErrorCodes.LifecycleCommitIndeterminate => "The subject lifecycle commit outcome is indeterminate.",
        BaseSubjectErrorCodes.MaintenanceRequired => "Subject lifecycle maintenance must complete before this operation.",
        BaseSubjectErrorCodes.ScopeProtectionRotationConflict => "The subject scope-protection rotation conflicts with current authority.",
        _ => "The subject lifecycle operation failed.",
    };
    private sealed record WireRequest(string ConsumerId, int ConsumerVersion, string ContractId, int ContractVersion, string? ProjectId, int? Take, string? Cursor, string? Checkpoint, string? AfterSubjectId, WireIdentity? Identity);
    private sealed record WireIdentity(string Scope, string Operation, string IdempotencyKey, string Fingerprint);
    private sealed record EndpointState(BaseSubjectLifecycleRegistry Registry, IBaseSubjectLifecycleRuntime Runtime, BaseSubjectContractRegistry Contracts, PrincipalContext Principal, IBaseSessionFactory Sessions)
    {
        internal BaseSession Session(string? projectId) => Sessions.For(Principal, options => options.ProjectId = projectId);
    }
}
