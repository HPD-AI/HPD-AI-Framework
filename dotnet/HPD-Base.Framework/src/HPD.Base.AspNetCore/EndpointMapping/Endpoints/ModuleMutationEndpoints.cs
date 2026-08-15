using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

internal static class ModuleMutationEndpoints
{
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor>? convention = null)
    {
        BaseModuleMutationRegistry? registry = endpoints.ServiceProvider.GetService<BaseModuleMutationRegistry>();
        if (registry is null) return;
        foreach (IBaseModuleMutationRegistration registration in registry.Registrations.OrderBy(static value => value.Id, StringComparer.Ordinal))
        {
            IBaseModuleMutationRegistration captured = registration;
            string endpointId = $"base.module-mutations.{registration.Id}.execute";
            endpoints.MapPost($"/module-mutations/v1/{registration.Id}:execute", (RequestDelegate)(context => Execute(context, captured)))
                .WithHPDBaseEndpoint(endpointId, HPDBaseEndpointAudience.ControlPlane,
                    HPDBaseEndpointOperation.ModuleMutation, registration.GrantId, convention)
                .WithName(endpointId);
        }
    }

    private static async Task Execute(HttpContext context, IBaseModuleMutationRegistration registration)
    {
        BaseModuleMutationRegistry registry = context.RequestServices.GetRequiredService<BaseModuleMutationRegistry>();
        BaseRegisteredModuleMutationDefinition? definition = registry.Find(registration.Id, registration.Version);
        if (definition is null) { await Problem(context, OperationStatus.NotFound, BaseModuleMutationErrorCodes.NotInstalled); return; }
        int maximum = checked((int)Math.Min(definition.Limits.MaximumRequestBytes, int.MaxValue));
        if (context.Request.ContentLength is { } length && length > maximum)
        { await Problem(context, OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.LimitExceeded); return; }
        byte[] body;
        try
        {
            await using var bounded = new LimitedRequestBodyStream(context.Request.Body, maximum);
            using var buffer = new MemoryStream(Math.Min(maximum, 16 * 1024));
            await bounded.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
            body = buffer.ToArray();
            using JsonDocument document = JsonDocument.Parse(body, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        { await Problem(context, OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.Invalid); return; }

        IBaseHttpPrincipalContextFactory principals = context.RequestServices.GetRequiredService<IBaseHttpPrincipalContextFactory>();
        PrincipalContext principal = await principals.CreateAsync(context, context.RequestAborted).ConfigureAwait(false);
        if (registration.Audience == BaseModuleMutationAudience.System
            && principal.AuthenticationState != PrincipalAuthenticationState.System
            || registration.Audience == BaseModuleMutationAudience.Service
            && principal.AuthenticationState is not (PrincipalAuthenticationState.Service or PrincipalAuthenticationState.System))
        { await Problem(context, OperationStatus.NotFound, BaseModuleMutationErrorCodes.NotInstalled); return; }
        if (!context.Request.Headers.TryGetValue(BaseHttpHeaders.IdempotencyKey, out var keys) || keys.Count != 1)
        { await Problem(context, OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.Invalid); return; }
        BaseMutationRequestIdentity identity;
        try { identity = registration.CreateRequestIdentity(body, keys[0]!, principal); }
        catch { await Problem(context, OperationStatus.ValidationFailed, BaseModuleMutationErrorCodes.Invalid); return; }
        BaseSession session = context.RequestServices.GetRequiredService<IBaseSessionFactory>().For(principal);
        BaseResult<BaseUntypedModuleMutationExecutionResult> result = await registration.ExecuteAsync(
            session, body, identity, null, context.RequestAborted).ConfigureAwait(false);
        if (result is BaseFailure<BaseUntypedModuleMutationExecutionResult> failure)
        { await Problem(context, failure.Status, failure.Error.Code); return; }
        BaseUntypedModuleMutationExecutionResult value = ((BaseSuccess<BaseUntypedModuleMutationExecutionResult>)result).Value;
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        await using var writer = new Utf8JsonWriter(context.Response.BodyWriter);
        writer.WriteStartObject();
        writer.WriteString("disposition", value.Disposition == BaseMutationRequestDisposition.Duplicate ? "duplicate" : "new");
        writer.WriteString("outcome", value.Outcome == BaseModuleMutationOutcome.Duplicate ? "duplicate" : "committed");
        writer.WritePropertyName("result");
        writer.WriteRawValue(value.CanonicalResultJson, skipInputValidation: false);
        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }

    private static Task Problem(HttpContext context, OperationStatus status, string code) => Results.Problem(
        statusCode: BaseHttpStatusCodeMapper.ToStatusCode(status), title: "BASE module mutation failed.",
        extensions: new Dictionary<string, object?> { ["hpd.error.code"] = code }).ExecuteAsync(context);
}
