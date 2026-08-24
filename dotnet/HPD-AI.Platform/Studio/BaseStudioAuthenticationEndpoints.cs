using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using System.Buffers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.AI.Platform.Studio;

internal static class BaseStudioAuthenticationEndpoints
{
    internal static void Map(RouteGroupBuilder group, BaseStudioAuthenticationProvider provider, BaseStudioApplicationGraph graph,
        BaseStudioLateWorkRegistry lateWork, BaseStudioCommandAuthorityRegistry commandAuthority)
    {
        IBaseStudioAuthenticationIntegration integration = provider.Integration;
        BaseStudioAuthenticationDescriptor descriptor = integration.Descriptor;

        group.MapGet(descriptor.LoginRoute, async context =>
        {
            string? target = context.Request.Query.TryGetValue("return", out var value) && value.Count == 1 ? value[0] : null;
            BaseStudioAuthenticationResult<BaseStudioProtectedReturnTarget> protectedTarget =
                await integration.ProtectReturnTargetAsync(context, target, context.RequestAborted).ConfigureAwait(false);
            if (!protectedTarget.IsSuccess) { await Failure(context, protectedTarget.Failure!.Value).ConfigureAwait(false); return; }
            await integration.BeginSignInAsync(context, protectedTarget.Value!, context.RequestAborted).ConfigureAwait(false);
        }).WithName("BaseStudioAuthenticationLogin");

        group.MapGet(descriptor.CallbackRoute, async context =>
            await integration.CompleteCallbackAsync(context, context.RequestAborted).ConfigureAwait(false))
            .WithName("BaseStudioAuthenticationCallback");

        group.MapPost(descriptor.LogoutRoute, async context =>
        {
            BaseStudioAuthenticationResult<BaseStudioTransportAuthorization> authorization =
                await integration.AuthorizeRequestAsync(context, BaseStudioTransportPurpose.Bootstrap, context.RequestAborted).ConfigureAwait(false);
            if (!authorization.IsSuccess) { await Failure(context, authorization.Failure!.Value).ConfigureAwait(false); return; }
            await integration.BeginSignOutAsync(context, context.RequestAborted).ConfigureAwait(false);
        }).WithName("BaseStudioAuthenticationLogout");

        group.MapGet(descriptor.SessionRoute, async context =>
        {
            BaseStudioAuthenticationResult<BaseStudioSessionObservation> result =
                await integration.ObserveSessionAsync(context, context.RequestAborted).ConfigureAwait(false);
            if (!result.IsSuccess) { await Failure(context, result.Failure!.Value).ConfigureAwait(false); return; }
            context.Response.ContentType = "application/json; charset=utf-8";
            ApplyProtectedResponseHeaders(context.Response);
            var payload = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(payload);
            writer.WriteStartObject(); writer.WriteString("kind", "authenticated");
            writer.WriteString("principalGeneration", result.Value!.PrincipalGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("sessionChecksum", Convert.ToHexString(result.Value.SessionChecksum.ToArray()).ToLowerInvariant());
            writer.WriteString("audience", result.Value.Audience);
            writer.WriteString("protectedScopeChecksum", Convert.ToHexString(result.Value.ProtectedScopeChecksum.ToArray()).ToLowerInvariant());
            writer.WriteString("issuedAtUtc", BaseStudioResponseAuthority.CanonicalUtc(result.Value.IssuedAtUtc));
            writer.WriteString("expiresAtUtc", BaseStudioResponseAuthority.CanonicalUtc(result.Value.ExpiresAtUtc));
            writer.WriteString("descriptorChecksum", Convert.ToHexString(result.Value.DescriptorChecksum.ToArray()).ToLowerInvariant());
            writer.WriteEndObject(); writer.Flush(); context.Response.ContentLength = payload.WrittenCount;
            await context.Response.Body.WriteAsync(payload.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
        }).WithName("BaseStudioAuthenticationSession");

        group.MapPost("/control/authorize", async context =>
        {
            BaseStudioTransportPurpose? purpose = await ReadPurposeAsync(context).ConfigureAwait(false);
            if (purpose is null) return;
            BaseStudioAuthenticationResult<BaseStudioBrowserAuthorization> result =
                await integration.AcquireBrowserAuthorizationAsync(context, purpose.Value, context.RequestAborted).ConfigureAwait(false);
            if (!result.IsSuccess) { await Failure(context, result.Failure!.Value).ConfigureAwait(false); return; }
            if (!BaseStudioSha256.FixedTimeEquals(result.Value!.Authority.Session.DescriptorChecksum, descriptor.Checksum) ||
                result.Value.Authority.Purpose != purpose.Value)
            { await Failure(context, BaseStudioAuthenticationFailure.IntegrationUnavailable).ConfigureAwait(false); return; }
            ApplyProtectedResponseHeaders(context.Response); context.Response.ContentType = "application/json; charset=utf-8";
            var payload = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(payload); writer.WriteStartObject();
            writer.WriteString("headerName", result.Value.HeaderName); writer.WriteString("headerValue", result.Value.HeaderValue);
            writer.WriteString("authorizedThroughUtc", BaseStudioResponseAuthority.CanonicalUtc(result.Value.Authority.AuthorizedThroughUtc));
            writer.WriteString("descriptorChecksum", Convert.ToHexString(descriptor.Checksum.ToArray()).ToLowerInvariant());
            writer.WriteString("purpose", PurposeName(purpose.Value)); writer.WriteEndObject();
            writer.Flush(); context.Response.ContentLength = payload.WrittenCount;
            await context.Response.Body.WriteAsync(payload.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
        }).WithName("BaseStudioAuthenticationAuthorization");

        group.MapPost("/base/studio/auth/fresh", async context =>
        {
            if (!await AuthorizeAsync(context, provider, BaseStudioTransportPurpose.CommandExecution).ConfigureAwait(false)) return;
            if (!StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json") &&
                !StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json; charset=utf-8"))
            { context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType; return; }
            if (context.Request.ContentLength is null or < 2) { context.Response.StatusCode = StatusCodes.Status411LengthRequired; return; }
            if (context.Request.ContentLength > 16_384) { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return; }
            try
            {
                byte[] bytes = await ReadBoundedAsync(context.Request.Body, 16_384, context.RequestAborted).ConfigureAwait(false);
                using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 3 }); JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4 ||
                    !root.TryGetProperty("requestIdentity", out JsonElement identity) || identity.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("commandId", out JsonElement command) || command.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("targetToken", out JsonElement token) || token.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("previewChecksum", out JsonElement checksum) || checksum.ValueKind != JsonValueKind.String) throw new JsonException();
                string? requestIdentity = identity.GetString(); string? commandId = command.GetString(); string? digestText = checksum.GetString();
                if (!BaseStudioResourceRouteToken.TryDecode(token.GetString(), out BaseStudioResourceIdentity? target) || target is null ||
                    requestIdentity is not { Length: >= 1 and <= 128 } || commandId is not { Length: >= 1 and <= 128 } ||
                    digestText is not { Length: 64 } || commandId[0] is < 'a' or > 'z' || commandId.Any(static value =>
                        !char.IsAsciiLetterOrDigit(value) && value is not '.' and not '-')) throw new JsonException();
                byte[] digest = Convert.FromHexString(digestText);
                BaseStudioCommandRegistration? registered = graph.Modules.SelectMany(static module => module.Commands)
                    .SingleOrDefault(value => StringComparer.Ordinal.Equals(value.CommandId, commandId));
                BaseStudioModuleRegistration? owningModule = registered is null ? null : graph.Modules.Single(module => module.Commands.Contains(registered));
                if (registered is null || owningModule is null || !StringComparer.Ordinal.Equals(target.ApplicationId, graph.ApplicationId) || !owningModule.Pages.Where(page => page.Presentation.Sections.Any(section => section.CommandIds.Contains(commandId)))
                    .SelectMany(static page => page.AcceptedResources).Contains(target.Kind))
                { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
                BaseStudioTransportAuthorization transport = (BaseStudioTransportAuthorization)context.Items[typeof(BaseStudioTransportAuthorization)]!;
                DateTimeOffset issued = DateTimeOffset.UtcNow;
                BaseStudioSha256 previewChecksum = BaseStudioSha256.FromDigest(digest);
                if (!HPD.AI.Platform.HPDAIPlatformEndpointRouteBuilderExtensions.TryAuthorizeFreshAuthentication(
                    commandAuthority, transport.Session, commandId, target, previewChecksum, out DateTimeOffset previewExpiresAtUtc))
                { context.Response.StatusCode = StatusCodes.Status409Conflict; return; }
                BaseStudioFreshAuthenticationRequest request = new() { RequestIdentity = new(requestIdentity.AsSpan()), CommandId = new(commandId.AsSpan()),
                    Target = target, PreviewChecksum = previewChecksum, PrincipalGeneration = transport.Session.PrincipalGeneration,
                    SessionChecksum = transport.Session.SessionChecksum, ProtectedScopeChecksum = transport.Session.ProtectedScopeChecksum,
                    RequiredAssurance = registered.ActionClass >= BaseStudioActionClass.Destructive ? BaseStudioFreshAuthenticationClass.MultiFactor : BaseStudioFreshAuthenticationClass.Password,
                    MaximumAuthenticationAge = TimeSpan.FromMinutes(5), IssuedAtUtc = issued,
                    ExpiresAtUtc = new DateTimeOffset(Math.Min(previewExpiresAtUtc.UtcTicks, Math.Min(transport.AuthorizedThroughUtc.UtcTicks, issued.AddMinutes(5).UtcTicks)), TimeSpan.Zero) };
                BaseStudioFreshAuthenticationBinding expected = BaseStudioFreshAuthenticationBinding.Create(request,
                    descriptor.IntegrationId, descriptor.Checksum, issued);
                ReclaimFresh(commandAuthority);
                string flightKey = FreshFlightKey(request, descriptor);
                FreshAcquisitionState flight;
                lock (commandAuthority.FreshRegistryGate)
                {
                    if (!commandAuthority.FreshAcquisitions.TryGetValue(flightKey, out flight!))
                    {
                        if (commandAuthority.FreshAcquisitions.Count >= 1024) { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
                        flight = new FreshAcquisitionState(expected, request); commandAuthority.FreshAcquisitions[flightKey] = flight;
                    }
                }
                expected = flight.Binding;
                await flight.Gate.WaitAsync(context.RequestAborted).ConfigureAwait(false);
                bool capacitySlot = false;
                try
                {
                    BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>? result = flight.Result;
                    if (result is null)
                    {
                        int pending = Interlocked.Increment(ref commandAuthority.PendingFreshAcquisitions);
                        capacitySlot = true;
                        if (pending + ActiveFreshCount(commandAuthority) > 1024)
                        { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
                        flight.Operation ??= TryStartFreshOperation(
                            token => integration.AcquireFreshAuthenticationAsync(context, flight.Request, token), lateWork);
                        result = flight.Operation is null ? null :
                            await WaitFreshBoundedAsync(flight.Operation, context.RequestAborted).ConfigureAwait(false);
                        if (result is not null) flight.Result = result;
                    }
                    if (result is null) { context.Response.StatusCode = StatusCodes.Status504GatewayTimeout; return; }
                    if (!result.IsSuccess) { await Failure(context, result.Failure!.Value).ConfigureAwait(false); return; }
                    if (!ResultMatches(result.Value!, expected, descriptor)) { await Failure(context, BaseStudioAuthenticationFailure.IntegrationUnavailable).ConfigureAwait(false); return; }
                    await WriteFreshResultAsync(context, result.Value!, commandAuthority).ConfigureAwait(false);
                }
                finally
                {
                    if (capacitySlot) Interlocked.Decrement(ref commandAuthority.PendingFreshAcquisitions);
                    flight.Gate.Release();
                    if (flight.Operation is null || flight.Result is { IsSuccess: false } ||
                        flight.Result is { IsSuccess: true, Value: BaseStudioFreshAuthenticationResult.Unsupported })
                        commandAuthority.FreshAcquisitions.TryRemove(new KeyValuePair<string, FreshAcquisitionState>(flightKey, flight));
                }
            }
            catch (FormatException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
            catch (InvalidDataException) { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; }
            catch (JsonException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
        }).WithName("BaseStudioFreshAuthentication");

        group.MapPost("/base/studio/auth/fresh/complete", async context =>
        {
            if (!await AuthorizeAsync(context, provider, BaseStudioTransportPurpose.CommandExecution).ConfigureAwait(false)) return;
            try
            {
                byte[] bytes = await ReadExactJsonAsync(context, 8_192).ConfigureAwait(false);
                using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 2 });
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
                    !root.TryGetProperty("continuation", out JsonElement member) || member.ValueKind != JsonValueKind.String ||
                    member.GetString() is not { } token || !commandAuthority.Continuations.TryGetValue(token, out FreshChallengeState? challengeState))
                { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
                BaseStudioFreshAuthenticationContinuation continuation = challengeState.Continuation;
                BaseStudioTransportAuthorization transport = (BaseStudioTransportAuthorization)context.Items[typeof(BaseStudioTransportAuthorization)]!;
                if (continuation.Binding.ExpiresAtUtc <= DateTimeOffset.UtcNow || !SessionMatches(continuation.Binding, transport.Session))
                { commandAuthority.Continuations.TryRemove(token, out _); context.Response.StatusCode = StatusCodes.Status409Conflict; return; }
                await challengeState.CompletionGate.WaitAsync(context.RequestAborted).ConfigureAwait(false);
                BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>? result;
                try
                {
                    challengeState.CompletionOperation ??= TryStartFreshOperation(
                        token => integration.CompleteFreshAuthenticationAsync(context, continuation, token), lateWork);
                    result = challengeState.TerminalResult ?? (challengeState.CompletionOperation is null ? null :
                        await WaitFreshBoundedAsync(challengeState.CompletionOperation, context.RequestAborted).ConfigureAwait(false));
                    if (result is not null) AcceptCompletionResult(challengeState, result, descriptor);
                }
                finally { challengeState.CompletionGate.Release(); }
                if (result is null) { context.Response.StatusCode = StatusCodes.Status504GatewayTimeout; return; }
                if (!result.IsSuccess) { await Failure(context, result.Failure!.Value).ConfigureAwait(false); return; }
                if (!ResultMatches(result.Value!, continuation.Binding, descriptor)) { await Failure(context, BaseStudioAuthenticationFailure.IntegrationUnavailable).ConfigureAwait(false); return; }
                if (result.Value is BaseStudioFreshAuthenticationResult.Challenge pending && !PendingChallengeMatches(pending, challengeState))
                { await Failure(context, BaseStudioAuthenticationFailure.IntegrationUnavailable).ConfigureAwait(false); return; }
                await WriteFreshResultAsync(context, result.Value!, commandAuthority).ConfigureAwait(false);
            }
            catch (InvalidDataException) { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; }
            catch (JsonException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
        }).WithName("BaseStudioFreshAuthenticationComplete");
    }

    private static bool SessionMatches(BaseStudioFreshAuthenticationBinding binding, BaseStudioSessionObservation session)
        => binding.PrincipalGeneration == session.PrincipalGeneration && BaseStudioSha256.FixedTimeEquals(binding.SessionChecksum, session.SessionChecksum) &&
           BaseStudioSha256.FixedTimeEquals(binding.ProtectedScopeChecksum, session.ProtectedScopeChecksum);

    private static bool PendingChallengeMatches(BaseStudioFreshAuthenticationResult.Challenge pending, FreshChallengeState state)
        => StringComparer.Ordinal.Equals(pending.Continuation.ToString(), state.Continuation.ToString()) &&
           pending.Continuation.Binding.ExpiresAtUtc == state.Continuation.Binding.ExpiresAtUtc &&
           pending.BrowserAction.Kind == state.BrowserAction.Kind &&
           StringComparer.Ordinal.Equals(pending.BrowserAction.Target, state.BrowserAction.Target);

    internal static bool AcceptCompletionResult(FreshChallengeState state,
        BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult> result, BaseStudioAuthenticationDescriptor descriptor)
    {
        if (!result.IsSuccess) return false;
        if (result.Value is BaseStudioFreshAuthenticationResult.Satisfied or BaseStudioFreshAuthenticationResult.Unsupported)
        { state.TerminalResult = result; return true; }
        if (result.Value is not BaseStudioFreshAuthenticationResult.Challenge pending ||
            !ResultMatches(pending, state.Continuation.Binding, descriptor) || !PendingChallengeMatches(pending, state)) return false;
        // Completed pending observations are non-terminal. Only an incomplete task remains retained.
        state.CompletionOperation = null;
        return true;
    }

    private static bool ResultMatches(BaseStudioFreshAuthenticationResult result, BaseStudioFreshAuthenticationBinding expected,
        BaseStudioAuthenticationDescriptor descriptor)
    {
        BaseStudioFreshAuthenticationBinding? actual = result switch
        { BaseStudioFreshAuthenticationResult.Satisfied value => value.Authority.Binding,
          BaseStudioFreshAuthenticationResult.Challenge value => value.Continuation.Binding, _ => null };
        return actual is null || BaseStudioSha256.FixedTimeEquals(actual.Checksum, expected.Checksum) &&
            BaseStudioSha256.FixedTimeEquals(actual.IntegrationChecksum, descriptor.Checksum) && StringComparer.Ordinal.Equals(actual.IntegrationId, descriptor.IntegrationId);
    }

    private static async Task WriteFreshResultAsync(HttpContext context, BaseStudioFreshAuthenticationResult result, BaseStudioCommandAuthorityRegistry registry)
    {
        ApplyProtectedResponseHeaders(context.Response); context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(payload); writer.WriteStartObject();
        switch (result)
        {
            case BaseStudioFreshAuthenticationResult.Satisfied satisfied:
                RegisterFreshAuthority(registry, satisfied.Authority);
                writer.WriteString("authority", satisfied.Authority.ToString());
                writer.WriteString("expiresAtUtc", BaseStudioResponseAuthority.CanonicalUtc(satisfied.Authority.Binding.ExpiresAtUtc));
                writer.WriteString("kind", "satisfied"); break;
            case BaseStudioFreshAuthenticationResult.Challenge challenge:
                string token = challenge.Continuation.ToString();
                if (!ChallengeTargetMatches(context, challenge.BrowserAction.Target, token))
                    throw new InvalidOperationException("base.studio.freshAuthenticationTargetInvalid");
                foreach (var expired in registry.Continuations.Where(static item => item.Value.Continuation.Binding.ExpiresAtUtc <= DateTimeOffset.UtcNow)) registry.Continuations.TryRemove(expired.Key, out _);
                var challengeState = new FreshChallengeState(challenge.Continuation, challenge.BrowserAction);
                if (!registry.Continuations.TryAdd(token, challengeState) &&
                    (!registry.Continuations.TryGetValue(token, out FreshChallengeState? existing) ||
                     !BaseStudioSha256.FixedTimeEquals(existing.Continuation.Binding.Checksum, challenge.Continuation.Binding.Checksum) ||
                     existing.BrowserAction.Kind != challenge.BrowserAction.Kind || !StringComparer.Ordinal.Equals(existing.BrowserAction.Target, challenge.BrowserAction.Target)))
                    throw new InvalidOperationException("base.studio.freshAuthenticationTokenReused");
                writer.WritePropertyName("browserAction"); writer.WriteStartObject();
                writer.WriteString("kind", challenge.BrowserAction.Kind switch { BaseStudioFreshAuthenticationBrowserActionKind.Redirect => "redirect",
                    BaseStudioFreshAuthenticationBrowserActionKind.WebAuthn => "webAuthn", _ => "externalIdp" });
                writer.WriteString("target", challenge.BrowserAction.Target); writer.WriteEndObject();
                writer.WriteString("continuation", token);
                writer.WriteString("expiresAtUtc", BaseStudioResponseAuthority.CanonicalUtc(challenge.Continuation.Binding.ExpiresAtUtc));
                writer.WriteString("kind", "challenge"); break;
            case BaseStudioFreshAuthenticationResult.Unsupported: writer.WriteString("kind", "unsupported"); break;
        }
        writer.WriteEndObject(); writer.Flush(); context.Response.ContentLength = payload.WrittenCount;
        await context.Response.Body.WriteAsync(payload.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
    }

    internal static bool ChallengeTargetMatches(HttpContext context, string target, string continuation)
    {
        if (!Uri.TryCreate(target, UriKind.RelativeOrAbsolute, out Uri? value)) return false;
        Uri absolute;
        if (value.IsAbsoluteUri)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(value.Scheme, context.Request.Scheme) ||
                !StringComparer.OrdinalIgnoreCase.Equals(value.Authority, context.Request.Host.Value)) return false;
            absolute = value;
        }
        else if (!Uri.TryCreate($"{context.Request.Scheme}://{context.Request.Host}{(target.StartsWith('/') ? string.Empty : "/")}{target}", UriKind.Absolute, out absolute!)) return false;
        if (!StringComparer.Ordinal.Equals(absolute.AbsolutePath, "/base/studio/auth/fresh/callback") ||
            !string.IsNullOrEmpty(absolute.UserInfo) || !string.IsNullOrEmpty(absolute.Fragment)) return false;
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(absolute.Query);
        return query.Count == 1 && query.TryGetValue("continuation", out var values) && values.Count == 1 &&
            StringComparer.Ordinal.Equals(values[0], continuation);
    }

    internal static bool TryConsumeFreshAuthority(BaseStudioCommandAuthorityRegistry registry, string protectedValue, string requestIdentity, string commandId,
        BaseStudioResourceIdentity target, BaseStudioSha256 previewChecksum, BaseStudioSessionObservation session,
        BaseStudioFreshAuthenticationClass requiredClass)
    {
        if (!registry.Authorities.TryGetValue(protectedValue, out FreshAuthorityState? state)) return false;
        BaseStudioFreshAuthenticationBinding binding = state.Authority.Binding;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now > binding.ExpiresAtUtc || now - state.Authority.AuthenticatedAtUtc > binding.MaximumAuthenticationAge ||
            state.Authority.AchievedAssurance < requiredClass || !SessionMatches(binding, session) ||
            !StringComparer.Ordinal.Equals(binding.RequestIdentity, requestIdentity) || !StringComparer.Ordinal.Equals(binding.CommandId, commandId) ||
            !BaseStudioSha256.FixedTimeEquals(binding.Target.AuthorityChecksum, target.AuthorityChecksum) ||
            !BaseStudioSha256.FixedTimeEquals(binding.PreviewChecksum, previewChecksum)) return false;
        return Interlocked.CompareExchange(ref state.Consumed, 1, 0) == 0;
    }

    internal static void RegisterFreshAuthority(BaseStudioCommandAuthorityRegistry registry, BaseStudioFreshAuthenticationAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        foreach (var expired in registry.Authorities.Where(static item => item.Value.Authority.Binding.ExpiresAtUtc <= DateTimeOffset.UtcNow)) registry.Authorities.TryRemove(expired.Key, out _);
        if (registry.Authorities.Count >= 1024) throw new InvalidOperationException("base.studio.freshAuthenticationCapacityExceeded");
        if (!registry.Authorities.TryAdd(authority.ToString(), new FreshAuthorityState(authority)) &&
            (!registry.Authorities.TryGetValue(authority.ToString(), out FreshAuthorityState? existing) ||
             !BaseStudioSha256.FixedTimeEquals(existing.Authority.Binding.Checksum, authority.Binding.Checksum)))
            throw new InvalidOperationException("base.studio.freshAuthenticationTokenReused");
    }

    private static void ReclaimFresh(BaseStudioCommandAuthorityRegistry registry)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var expired in registry.Authorities.Where(item => item.Value.Authority.Binding.ExpiresAtUtc <= now)) registry.Authorities.TryRemove(expired.Key, out _);
        foreach (var expired in registry.Continuations.Where(item => item.Value.Continuation.Binding.ExpiresAtUtc <= now)) registry.Continuations.TryRemove(expired.Key, out _);
        foreach (var expired in registry.FreshAcquisitions.Where(item => item.Value.ExpiresAtUtc <= now)) registry.FreshAcquisitions.TryRemove(expired.Key, out _);
    }

    private static int ActiveFreshCount(BaseStudioCommandAuthorityRegistry registry)
    {
        HashSet<string> terminalAuthorities = registry.Continuations.Values.Select(static state => state.TerminalResult?.Value)
            .OfType<BaseStudioFreshAuthenticationResult.Satisfied>().Select(static result => result.Authority.ToString()).ToHashSet(StringComparer.Ordinal);
        return registry.Continuations.Count + registry.Authorities.Keys.Count(key => !terminalAuthorities.Contains(key));
    }

    private static string FreshFlightKey(BaseStudioFreshAuthenticationRequest request, BaseStudioAuthenticationDescriptor descriptor)
    {
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.fresh-authentication-flight.v1", writer =>
        { writer.String(request.RequestIdentity); writer.String(request.CommandId); writer.Checksum(request.Target.AuthorityChecksum);
          writer.Checksum(request.PreviewChecksum); writer.Int64(request.PrincipalGeneration); writer.Checksum(request.SessionChecksum);
          writer.Checksum(request.ProtectedScopeChecksum); writer.Enum(request.RequiredAssurance); writer.Int64(request.MaximumAuthenticationAge.Ticks);
          writer.String(descriptor.IntegrationId); writer.Checksum(descriptor.Checksum); });
        return Convert.ToHexString(checksum.ToArray());
    }

    internal static void RestoreFreshAuthorityBeforeInfluence(BaseStudioCommandAuthorityRegistry registry, string? protectedValue)
    {
        if (protectedValue is not null && registry.Authorities.TryGetValue(protectedValue, out FreshAuthorityState? state))
            Interlocked.CompareExchange(ref state.Consumed, 0, 1);
    }


    private static async Task<byte[]> ReadExactJsonAsync(HttpContext context, int maximum)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json") &&
            !StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json; charset=utf-8")) throw new JsonException();
        if (context.Request.ContentLength is null or < 2) throw new JsonException();
        if (context.Request.ContentLength > maximum) throw new InvalidDataException();
        return await ReadBoundedAsync(context.Request.Body, maximum, context.RequestAborted).ConfigureAwait(false);
    }

    private static Task<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>>? TryStartFreshOperation(
        Func<CancellationToken, ValueTask<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>>> operation,
        BaseStudioLateWorkRegistry lateWork)
    {
        if (!lateWork.TryEnter(out BaseStudioLateWorkLease lease)) return null;
        var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>> task;
        try { task = operation(deadline.Token).AsTask(); }
        catch { deadline.Dispose(); lease.Dispose(); throw; }
        _ = task.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), deadline,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        lease.Retain(task);
        return task;
    }

    private static async ValueTask<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>?> WaitFreshBoundedAsync(
        Task<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>> task, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try { return await task.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    private static async ValueTask<BaseStudioTransportPurpose?> ReadPurposeAsync(HttpContext context)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json") &&
            !StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json; charset=utf-8"))
        { context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType; return null; }
        if (context.Request.ContentLength is null or < 2) { context.Response.StatusCode = StatusCodes.Status411LengthRequired; return null; }
        if (context.Request.ContentLength > 256) { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return null; }
        try
        {
            byte[] bytes = await ReadBoundedAsync(context.Request.Body, 256, context.RequestAborted).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 2 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty("purpose", out JsonElement member) || member.ValueKind != JsonValueKind.String) throw new JsonException();
            return member.GetString() switch
            { "bootstrap" => BaseStudioTransportPurpose.Bootstrap, "observation" => BaseStudioTransportPurpose.Observation,
              "commandPreview" => BaseStudioTransportPurpose.CommandPreview, "commandExecution" => BaseStudioTransportPurpose.CommandExecution,
              "receiptResolution" => BaseStudioTransportPurpose.ReceiptResolution, "artifactStaging" => BaseStudioTransportPurpose.ArtifactStaging,
              _ => throw new JsonException() };
        }
        catch (InvalidDataException) { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return null; }
        catch (JsonException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return null; }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(Math.Min(maximumBytes, 256));
        byte[] buffer = new byte[Math.Min(maximumBytes + 1, 257)];
        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > maximumBytes) throw new InvalidDataException("The request exceeds its declared bound.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return output.ToArray();
    }

    private static string PurposeName(BaseStudioTransportPurpose purpose) => purpose switch
    { BaseStudioTransportPurpose.Bootstrap => "bootstrap", BaseStudioTransportPurpose.Observation => "observation",
      BaseStudioTransportPurpose.CommandPreview => "commandPreview", BaseStudioTransportPurpose.CommandExecution => "commandExecution",
      BaseStudioTransportPurpose.ReceiptResolution => "receiptResolution", BaseStudioTransportPurpose.ArtifactStaging => "artifactStaging",
      _ => throw new ArgumentOutOfRangeException(nameof(purpose)) };

    internal static async Task<bool> AuthorizeBootstrapAsync(
        HttpContext context,
        BaseStudioAuthenticationProvider provider)
    {
        BaseStudioAuthenticationResult<BaseStudioTransportAuthorization> result =
            await provider.Integration.AuthorizeRequestAsync(
                context, BaseStudioTransportPurpose.Bootstrap, context.RequestAborted).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            context.Items[typeof(BaseStudioTransportAuthorization)] = result.Value!;
            return true;
        }
        await Failure(context, result.Failure!.Value).ConfigureAwait(false);
        return false;
    }

    internal static async Task<bool> AuthorizeAsync(HttpContext context,
        BaseStudioAuthenticationProvider provider, BaseStudioTransportPurpose purpose)
    {
        BaseStudioAuthenticationResult<BaseStudioTransportAuthorization> result =
            await provider.Integration.AuthorizeRequestAsync(context, purpose, context.RequestAborted).ConfigureAwait(false);
        if (result.IsSuccess)
        { context.Items[typeof(BaseStudioTransportAuthorization)] = result.Value!; return true; }
        await Failure(context, result.Failure!.Value).ConfigureAwait(false); return false;
    }

    private static async Task Failure(HttpContext context, BaseStudioAuthenticationFailure failure)
    {
        ApplyProtectedResponseHeaders(context.Response);
        context.Response.StatusCode = failure is BaseStudioAuthenticationFailure.AuthenticationRequired or BaseStudioAuthenticationFailure.SessionExpired
            ? StatusCodes.Status401Unauthorized : failure is BaseStudioAuthenticationFailure.OriginRejected or BaseStudioAuthenticationFailure.AntiForgeryInvalid
                ? StatusCodes.Status403Forbidden : StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, new BaseStudioAuthenticationFailureEnvelope(failure.ToString()),
            BaseStudioAuthenticationJsonContext.Default.BaseStudioAuthenticationFailureEnvelope, context.RequestAborted).ConfigureAwait(false);
    }

    internal static void ApplyProtectedResponseHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, private";
        response.Headers.Pragma = "no-cache";
        response.Headers["X-Content-Type-Options"] = "nosniff";
    }
}

internal sealed record BaseStudioAuthenticationFailureEnvelope(string Code);

[JsonSerializable(typeof(BaseStudioAuthenticationFailureEnvelope))]
internal sealed partial class BaseStudioAuthenticationJsonContext : JsonSerializerContext;
