using System.Collections.Immutable;
using System.Buffers;
using System.Globalization;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.AI.Platform;

/// <summary>Maps the immutable HPD Studio application graph.</summary>
public static class HPDAIPlatformEndpointRouteBuilderExtensions
{
    private const int MaximumBootstrapRequestBytes = 65_536;

    /// <summary>Maps Studio at its default route prefix.</summary>
    public static RouteGroupBuilder MapHPDAIPlatform(this IEndpointRouteBuilder endpoints)
        => endpoints.MapHPDAIPlatform(static _ => { });

    /// <summary>Maps Studio using host-owned endpoint placement.</summary>
    public static RouteGroupBuilder MapHPDAIPlatform(
        this IEndpointRouteBuilder endpoints,
        Action<HPDAIPlatformEndpointOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = new HPDAIPlatformEndpointOptions();
        configure?.Invoke(options);
        string routePrefix = NormalizeRoutePrefix(options.RoutePrefix);

        IServiceProvider services = endpoints.ServiceProvider;
        StudioApplicationGraph graph = services.GetRequiredService<StudioApplicationGraphProvider>().GetRequiredGraph();
        StudioAuthenticationProvider authentication = services.GetRequiredService<StudioAuthenticationProvider>();
        IStudioBootstrapRuntime bootstrap = services.GetRequiredService<IStudioBootstrapRuntime>();
        StudioRuntimeCatalog runtime = services.GetRequiredService<StudioRuntimeCatalog>();
        StudioRuntimeLeaseRegistry runtimeLeases = services.GetRequiredService<StudioRuntimeLeaseRegistry>();
        StudioLateWorkRegistry lateWork = services.GetRequiredService<StudioLateWorkRegistry>();
        StudioCommandAuthorityRegistry commandAuthority = services.GetRequiredService<StudioCommandAuthorityRegistry>();
        StudioFrameworkEndpointSurfaceCatalog frameworkSurfaces = services.GetRequiredService<StudioFrameworkEndpointSurfaceCatalog>();
        IStudioResponseAuthorityValidator[] authorityValidators = services.GetServices<IStudioResponseAuthorityValidator>().ToArray();
        StudioShellContract shell = services.GetRequiredService<StudioShellContract>();
        StudioShellAssetGraph shellAssets = services.GetRequiredService<StudioShellAssetGraph>();
        StudioEditionAssetGraph assets = StudioEditionAssetGraph.Create(
            services.GetRequiredService<StudioEditionAssetCatalogProvider>().GetRequiredCatalog(), shell);
        var routeGroup = endpoints.MapGroup(routePrefix);

        StudioAuthenticationEndpoints.Map(routeGroup, authentication, graph, lateWork, commandAuthority);

        routeGroup.MapGet("/control/shell", async context =>
        {
            ApplyStaticSecurityHeaders(context.Response); context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentType = "application/json; charset=utf-8";
            var payload = new ArrayBufferWriter<byte>();
            using var json = new Utf8JsonWriter(payload);
            json.WriteStartObject(); Hex(json, "shellContractChecksum", shell.Checksum);
            Hex(json, "editionAssetGraphChecksum", assets.Checksum);
            StudioSha256 runtimeChecksum = graph.Modules.SelectMany(static module => module.Clients)
                .Where(static client => client.ClientId == "base.control-plane").Select(static client => client.StaticRuntimeAbiChecksum).Single();
            Hex(json, "runtimeClientChecksum", runtimeChecksum);
            json.WriteString("bootstrapRoute", routePrefix + "/control/bootstrap");
            json.WriteString("sessionRoute", routePrefix + authentication.Integration.Descriptor.SessionRoute);
            json.WriteString("loginRoute", routePrefix + authentication.Integration.Descriptor.LoginRoute);
            json.WriteString("logoutRoute", routePrefix + authentication.Integration.Descriptor.LogoutRoute);
            json.WritePropertyName("authentication"); json.WriteStartObject();
            json.WriteString("kind", authentication.Integration.Descriptor.Kind == StudioAuthenticationKind.CookieBff ? "cookieBff" : "bearer");
            json.WriteString("authorizationRoute", routePrefix + "/control/authorize");
            if (authentication.Integration.Descriptor.Kind == StudioAuthenticationKind.Bearer)
                json.WriteBoolean("refreshSupported", authentication.Integration.Descriptor.RefreshSupported);
            Hex(json, "descriptorChecksum", authentication.Integration.Descriptor.Checksum); json.WriteEndObject();
            json.WritePropertyName("modules"); json.WriteStartArray();
            foreach (StudioEditionModuleAssetContribution module in assets.Modules)
            { json.WriteStartObject(); json.WriteString("moduleId", module.ModuleId); json.WriteNumber("moduleVersion", module.ModuleVersion);
              json.WriteString("entryModulePath", routePrefix + "/modules/" + module.ModuleId + "/" + module.ModuleVersion.ToString(CultureInfo.InvariantCulture) + "/" + module.Asset.EntryModulePath);
              Hex(json, "assetGraphChecksum", module.Asset.AssetGraphChecksum); json.WriteEndObject(); }
            json.WriteEndArray(); json.WriteEndObject(); json.Flush();
            context.Response.ContentLength = payload.WrittenCount;
            await context.Response.Body.WriteAsync(payload.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
        }).WithName("StudioShellDescriptor").WithSummary("Get the authorization-neutral Studio host descriptor");

        routeGroup.MapGet("/assets/{**shellAssetPath}", async context =>
        {
            string? path = context.Request.RouteValues["shellAssetPath"] as string;
            if (path is null || !shellAssets.TryResolve("assets/" + path, out StudioShellAsset asset))
            { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
            ApplyStaticSecurityHeaders(context.Response); context.Response.ContentType = asset.ContentType;
            byte[] content = asset.GetContent(); context.Response.ContentLength = content.LongLength;
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.ETag = "\"sha256-" + Convert.ToHexString(asset.Digest.ToArray()).ToLowerInvariant() + "\"";
            await context.Response.Body.WriteAsync(content, context.RequestAborted).ConfigureAwait(false);
        }).WithName("StudioShellAsset").WithSummary("Get one revalidated Studio shell asset");

        routeGroup.MapPost("/control/bootstrap", async context =>
        {
            if (!await StudioAuthenticationEndpoints.AuthorizeBootstrapAsync(context, authentication).ConfigureAwait(false))
                return;
            StudioBootstrapRequest? request = await ReadBootstrapRequestAsync(context).ConfigureAwait(false);
            if (request is null)
                return;
            if (!StudioSha256.FixedTimeEquals(request.ShellContractChecksum, shell.Checksum) ||
                !StudioSha256.FixedTimeEquals(request.EditionAssetGraphChecksum, assets.Checksum))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
                return;
            }
            var invocation = new StudioBootstrapInvocation(context, graph,
                (StudioTransportAuthorization)context.Items[typeof(StudioTransportAuthorization)]!, request);
            StudioBootstrapSnapshot? snapshot = await bootstrap.CreateAsync(invocation, context.RequestAborted).ConfigureAwait(false);
            if (snapshot is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
                return;
            }
            byte[] payload = StudioBootstrapJson.Encode(snapshot);
            runtimeLeases.Publish(invocation, snapshot);
            StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength = payload.Length;
            await context.Response.Body.WriteAsync(payload, context.RequestAborted).ConfigureAwait(false);
        }).WithName("StudioBootstrap").WithSummary("Get the principal-filtered HPD Studio bootstrap");

        foreach (var route in runtime.Contributions.SelectMany(static value => value.Endpoints)
                     .Where(static value => value.Method == StudioTransportMethod.Post)
                     .GroupBy(static value => (value.Method, value.RelativeRoute))
                     .OrderBy(static value => value.Key.RelativeRoute, StringComparer.Ordinal).ThenBy(static value => value.Key.Method))
        {
            StudioEndpointContract[] routeEndpoints = route.OrderBy(static value => value.EndpointId, StringComparer.Ordinal).ToArray();
            string relativeRoute = route.Key.RelativeRoute;
            routeGroup.MapPost(relativeRoute, async context =>
            {
                string? methodId = context.Request.Headers["X-HPD-Studio-Method"].SingleOrDefault();
                if (string.IsNullOrEmpty(methodId) || !runtime.TryGetProducer(methodId, out StudioProducerBinding producer))
                { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
                StudioTransportPurpose purpose = producer.Kind switch
                { StudioMethodKind.Preview => StudioTransportPurpose.CommandPreview, StudioMethodKind.Execute => StudioTransportPurpose.CommandExecution,
                  _ => StudioTransportPurpose.Observation };
                if (!await StudioAuthenticationEndpoints.AuthorizeAsync(context, authentication, purpose).ConfigureAwait(false)) return;
                string? snapshotChecksum = context.Request.Headers["X-HPD-Studio-Snapshot"].SingleOrDefault();
                StudioTransportAuthorization authorization = (StudioTransportAuthorization)context.Items[typeof(StudioTransportAuthorization)]!;
                StudioEndpointContract? boundEndpoint = routeEndpoints.SingleOrDefault(value => runtime.Contributions.SelectMany(static item => item.Methods)
                    .Any(method => StringComparer.Ordinal.Equals(method.RegisteredMethodId, methodId) && StringComparer.Ordinal.Equals(method.EndpointId, value.EndpointId)));
                if (!runtimeLeases.TryResolve(snapshotChecksum, authorization, graph, out StudioBootstrapRequest bootstrapRequest,
                        out StudioBootstrapSnapshot snapshot) || boundEndpoint is null ||
                    !snapshot.ContractMap.Methods.Any(value => StringComparer.Ordinal.Equals(value.RegisteredMethodId, methodId) &&
                        StringComparer.Ordinal.Equals(value.EndpointId, boundEndpoint.EndpointId)) ||
                    producer.Kind is StudioMethodKind.Preview or StudioMethodKind.Execute && snapshot.Mode == StudioMode.Inspect)
                { context.Response.StatusCode = StatusCodes.Status404NotFound; StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
                foreach (IStudioResponseAuthorityValidator validator in authorityValidators)
                    if (!await validator.IsCurrentAsync(snapshot.Authority, context.RequestAborted).ConfigureAwait(false))
                    { context.Response.StatusCode = StatusCodes.Status404NotFound; StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
                if (!StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json") &&
                    !StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json; charset=utf-8"))
                { context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType; return; }
                if (context.Request.ContentLength is null or < 2) { context.Response.StatusCode = StatusCodes.Status411LengthRequired; return; }
                if (context.Request.ContentLength > boundEndpoint.MaximumRequestBytes) { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return; }
                byte[] requestBytes;
                try { requestBytes = await ReadBoundedAsync(context.Request.Body, checked((int)boundEndpoint.MaximumRequestBytes), context.RequestAborted).ConfigureAwait(false); }
                catch (InvalidDataException) { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return; }
                StudioCanonicalJson request;
                try { request = StudioCanonicalJson.Create(requestBytes, checked((int)boundEndpoint.MaximumRequestBytes)); }
                catch (Exception exception) when (exception is JsonException or ArgumentException)
                { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
                try { StudioL41JsonValidator.Require(request, boundEndpoint.RequestNodeId, snapshot.ContractMap.Types); }
                catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
                { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
                CommandExecutionIdentity? commandExecution = null;
                CommandPreviewInvocation? commandPreview = null;
                if (producer.Kind == StudioMethodKind.Preview && !TryCaptureCommandPreviewInvocation(commandAuthority, request, snapshot, authorization.Session,
                    snapshot.ContractMap.Methods.Single(value => StringComparer.Ordinal.Equals(value.RegisteredMethodId, methodId)).OwningPageOrCommandId, out commandPreview))
                { context.Response.StatusCode = StatusCodes.Status400BadRequest; StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
                if (producer.Kind == StudioMethodKind.Execute)
                {
                    StudioMethodBinding methodBinding = snapshot.ContractMap.Methods.Single(value => StringComparer.Ordinal.Equals(value.RegisteredMethodId, methodId));
                    StudioCommandRegistration? command = graph.Modules.Single(value => StringComparer.Ordinal.Equals(value.Identity.ModuleId, methodBinding.OwningModuleId))
                        .Commands.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.CommandId, methodBinding.OwningPageOrCommandId));
                    if (command is null || !TryAuthorizeCommandExecution(commandAuthority, request, command, snapshot, authorization.Session, out commandExecution))
                    { context.Response.StatusCode = StatusCodes.Status409Conflict; StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
                    if (commandAuthority.Outcomes.TryGetValue(commandExecution.Key, out CommandOutcome? historical))
                    {
                        StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
                        context.Response.Headers["X-HPD-Studio-Response-Authority"] = Convert.ToHexString(snapshot.Authority.Checksum.ToArray()).ToLowerInvariant();
                        context.Response.ContentType = "application/json; charset=utf-8"; context.Response.ContentLength = historical.Result.LongLength;
                        await context.Response.Body.WriteAsync(historical.Result, context.RequestAborted).ConfigureAwait(false); return;
                    }
                    if (commandExecution.ReceiptOnly)
                    { ApplyCommandFailure(context.Response, snapshot, StatusCodes.Status409Conflict, "base.studio.commandIndeterminate"); return; }
                }
                var bootstrapInvocation = new StudioBootstrapInvocation(context, graph, authorization, bootstrapRequest);
                var producerInvocation = new StudioProducerInvocation(bootstrapInvocation, snapshot.Authority, methodId, request);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                deadline.CancelAfter(boundEndpoint.Deadline);
                StudioCanonicalJson? result;
                if (!lateWork.TryEnter(out StudioLateWorkLease workLease))
                { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
                Task<StudioCanonicalJson?> producerTask;
                try { producerTask = producer switch
                    {
                        StudioViewProducerBinding view => view.Producer.ReadAsync(producerInvocation, deadline.Token).AsTask(),
                        StudioResourceProducerBinding resolver => resolver.Producer.ResolveAsync(producerInvocation, deadline.Token).AsTask(),
                        StudioLinkProducerBinding links => links.Producer.ResolveAsync(producerInvocation, deadline.Token).AsTask(),
                        StudioCommandPreviewProducerBinding preview => preview.Producer.PreviewAsync(producerInvocation, deadline.Token).AsTask(),
                        StudioCommandExecuteProducerBinding execute => execute.Producer.ExecuteAsync(producerInvocation, deadline.Token).AsTask(),
                        _ => Task.FromResult<StudioCanonicalJson?>(null),
                    }; }
                catch (StudioCommandFailedBeforeInfluenceException) when (commandExecution is not null)
                { workLease.Dispose(); ReleaseBeforeInfluence(commandAuthority, commandExecution); ApplyCommandFailure(context.Response, snapshot, StatusCodes.Status409Conflict, "base.studio.failedBeforeInfluence"); return; }
                catch when (commandExecution is not null)
                { workLease.Dispose(); commandAuthority.Previews.TryRemove(commandExecution.PreviewKey, out _);
                  ApplyCommandFailure(context.Response, snapshot, StatusCodes.Status409Conflict, "base.studio.commandIndeterminate"); return; }
                catch { workLease.Dispose(); throw; }
                try { result = await producerTask.WaitAsync(deadline.Token).ConfigureAwait(false); workLease.Dispose(); }
                catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
                { if (commandExecution is not null) { commandAuthority.Previews.TryRemove(commandExecution.PreviewKey, out _); _ = RetainCommandOutcomeAsync(commandAuthority, producerTask, commandExecution, boundEndpoint, snapshot.ContractMap.Types); }
                  workLease.Retain(producerTask); ApplyCommandFailure(context.Response, snapshot, StatusCodes.Status504GatewayTimeout, "base.studio.commandIndeterminate"); return; }
                catch (OperationCanceledException)
                { if (commandExecution is not null) { commandAuthority.Previews.TryRemove(commandExecution.PreviewKey, out _); _ = RetainCommandOutcomeAsync(commandAuthority, producerTask, commandExecution, boundEndpoint, snapshot.ContractMap.Types); }
                  workLease.Retain(producerTask); throw; }
                catch (StudioCommandFailedBeforeInfluenceException) when (commandExecution is not null)
                { workLease.Dispose(); ReleaseBeforeInfluence(commandAuthority, commandExecution); ApplyCommandFailure(context.Response, snapshot, StatusCodes.Status409Conflict, "base.studio.failedBeforeInfluence"); return; }
                catch (StudioCommandIndeterminateException) when (commandExecution is not null)
                { workLease.Dispose(); commandAuthority.Previews.TryRemove(commandExecution.PreviewKey, out _);
                  ApplyCommandFailure(context.Response, snapshot, StatusCodes.Status409Conflict, "base.studio.commandIndeterminate"); return; }
                catch when (commandExecution is not null)
                { workLease.Dispose(); commandAuthority.Previews.TryRemove(commandExecution.PreviewKey, out _);
                  ApplyCommandFailure(context.Response, snapshot, StatusCodes.Status409Conflict, "base.studio.commandIndeterminate"); return; }
                catch { workLease.Dispose(); throw; }
                if (result is null) { if (commandExecution is not null) ReleaseBeforeInfluence(commandAuthority, commandExecution);
                    context.Response.StatusCode = StatusCodes.Status404NotFound; StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
                byte[] bytes = result.ToArray();
                if (commandExecution is not null) commandAuthority.Previews.TryRemove(commandExecution.PreviewKey, out _);
                if (bytes.LongLength > boundEndpoint.MaximumResultBytes) throw new InvalidOperationException("base.studio.resultTooLarge");
                StudioL41JsonValidator.Require(result, boundEndpoint.ResultNodeId, snapshot.ContractMap.Types);
                if (commandPreview is not null && !RegisterCommandPreview(commandAuthority, commandPreview, bytes))
                { context.Response.StatusCode = StatusCodes.Status502BadGateway; StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
                if (commandExecution is not null) commandAuthority.Outcomes.TryAdd(commandExecution.Key,
                    new CommandOutcome(commandExecution.CommandId, commandExecution.TargetChecksum, commandExecution.PreviewChecksum,
                        commandExecution.RequestChecksum, commandExecution.SessionChecksum, commandExecution.ProtectedScopeChecksum,
                        bytes.ToArray(), commandExecution.RetainThroughUtc));
                StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
                context.Response.Headers["X-HPD-Studio-Response-Authority"] = Convert.ToHexString(snapshot.Authority.Checksum.ToArray()).ToLowerInvariant();
                context.Response.ContentType = "application/json; charset=utf-8"; context.Response.ContentLength = bytes.LongLength;
                await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
            }).WithName("StudioRuntime_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(relativeRoute))).ToLowerInvariant()[..16])
              .WithSummary("Invoke one bootstrap-disclosed Studio Runtime method");
        }

        foreach (var route in runtime.Contributions.SelectMany(static value => value.Endpoints)
                     .Where(static value => value.Method == StudioTransportMethod.Get &&
                         value.Transport == StudioTransportKind.SameOriginHttp)
                     .GroupBy(static value => value.RelativeRoute)
                     .OrderBy(static value => value.Key, StringComparer.Ordinal))
        {
            string relativeRoute = route.Key;
            StudioEndpointContract[] routeEndpoints = route.OrderBy(static value => value.EndpointId, StringComparer.Ordinal).ToArray();
            routeGroup.MapGet(relativeRoute, async context =>
            {
                string? methodId = context.Request.Headers["X-HPD-Studio-Method"].SingleOrDefault();
                if (string.IsNullOrEmpty(methodId) || !runtime.TryGetProducer(methodId, out StudioProducerBinding producer) ||
                    producer is not StudioSubscriptionProducerBinding subscription)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                if (!await StudioAuthenticationEndpoints.AuthorizeAsync(
                        context, authentication, StudioTransportPurpose.Observation).ConfigureAwait(false))
                    return;
                string? snapshotChecksum = context.Request.Headers["X-HPD-Studio-Snapshot"].SingleOrDefault();
                StudioTransportAuthorization authorization =
                    (StudioTransportAuthorization)context.Items[typeof(StudioTransportAuthorization)]!;
                StudioEndpointContract? boundEndpoint = routeEndpoints.SingleOrDefault(value =>
                    runtime.Contributions.SelectMany(static item => item.Methods).Any(method =>
                        StringComparer.Ordinal.Equals(method.RegisteredMethodId, methodId) &&
                        StringComparer.Ordinal.Equals(method.EndpointId, value.EndpointId)));
                if (!runtimeLeases.TryResolve(snapshotChecksum, authorization, graph, out StudioBootstrapRequest bootstrapRequest,
                        out StudioBootstrapSnapshot snapshot) || boundEndpoint is null ||
                    !snapshot.ContractMap.Methods.Any(value =>
                        StringComparer.Ordinal.Equals(value.RegisteredMethodId, methodId) &&
                        StringComparer.Ordinal.Equals(value.EndpointId, boundEndpoint.EndpointId)))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
                    return;
                }
                foreach (IStudioResponseAuthorityValidator validator in authorityValidators)
                    if (!await validator.IsCurrentAsync(snapshot.Authority, context.RequestAborted).ConfigureAwait(false))
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
                        return;
                    }
                string? encodedRequest = context.Request.Query["request"].SingleOrDefault();
                byte[] requestBytes;
                try
                {
                    requestBytes = string.IsNullOrEmpty(encodedRequest)
                        ? throw new FormatException()
                        : Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(encodedRequest);
                    if (requestBytes.LongLength > boundEndpoint.MaximumRequestBytes) throw new InvalidDataException();
                }
                catch (Exception exception) when (exception is FormatException or InvalidDataException or ArgumentException)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                StudioCanonicalJson request;
                try
                {
                    request = StudioCanonicalJson.Create(requestBytes, checked((int)boundEndpoint.MaximumRequestBytes));
                    StudioL41JsonValidator.Require(request, boundEndpoint.RequestNodeId, snapshot.ContractMap.Types);
                }
                catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                var bootstrapInvocation = new StudioBootstrapInvocation(context, graph, authorization, bootstrapRequest);
                var producerInvocation = new StudioProducerInvocation(bootstrapInvocation, snapshot.Authority, methodId, request);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                deadline.CancelAfter(boundEndpoint.Deadline);
                StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/event-stream";
                context.Response.Headers.CacheControl = "no-cache";
                context.Response.Headers["X-Accel-Buffering"] = "no";
                context.Response.Headers["X-HPD-Studio-Response-Authority"] =
                    Convert.ToHexString(snapshot.Authority.Checksum.ToArray()).ToLowerInvariant();
                await context.Response.WriteAsync(": connected\n\n", deadline.Token).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(deadline.Token).ConfigureAwait(false);
                try
                {
                    await foreach (StudioCanonicalJson result in subscription.Producer.StreamAsync(
                                       producerInvocation, deadline.Token).ConfigureAwait(false))
                    {
                        StudioL41JsonValidator.Require(result, boundEndpoint.ResultNodeId, snapshot.ContractMap.Types);
                        byte[] bytes = result.ToArray();
                        if (bytes.LongLength > boundEndpoint.MaximumResultBytes)
                            throw new InvalidOperationException("base.studio.resultTooLarge");
                        await context.Response.WriteAsync("event: invalidate\n", deadline.Token).ConfigureAwait(false);
                        await context.Response.WriteAsync("data: ", deadline.Token).ConfigureAwait(false);
                        await context.Response.Body.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
                        await context.Response.WriteAsync("\n\n", deadline.Token).ConfigureAwait(false);
                        await context.Response.Body.FlushAsync(deadline.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    // A completed subscription or disconnected client closes the stream normally.
                }
            }).WithName("StudioRuntimeSubscription_" +
                        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes(relativeRoute))).ToLowerInvariant()[..16])
                .WithSummary("Observe one bootstrap-disclosed Studio runtime subscription");
        }

        routeGroup.MapMethods("/base/studio/framework-clients/{endpointSurfaceId}/{**relativePath}",
            ["GET", "POST", "PUT", "DELETE"], async context =>
        {
            string? surfaceId = context.Request.RouteValues["endpointSurfaceId"] as string;
            string? relativePath = context.Request.RouteValues["relativePath"] as string;
            string? operationId = context.Request.Headers["X-HPD-Studio-Operation"].SingleOrDefault();
            string? snapshotChecksum = context.Request.Headers["X-HPD-Studio-Snapshot"].SingleOrDefault();
            StudioTransportMethod method = context.Request.Method switch
            { "GET" => StudioTransportMethod.Get, "POST" => StudioTransportMethod.Post,
              "PUT" => StudioTransportMethod.Put, "DELETE" => StudioTransportMethod.Delete,
              _ => 0 };
            if (string.IsNullOrEmpty(surfaceId) || string.IsNullOrEmpty(relativePath) || relativePath.Length > 2_048 ||
                relativePath.StartsWith('/') || relativePath.Split('/').Any(static segment => segment is "" or "." or "..") ||
                relativePath.Any(char.IsControl) || string.IsNullOrEmpty(operationId) ||
                !frameworkSurfaces.TryGet(surfaceId, out IStudioFrameworkEndpointSurface surface) ||
                surface.Operations.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.OperationId, operationId)) is not { } operation ||
                operation.Method != method || !operation.Matches(relativePath))
            { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
            if (!await StudioAuthenticationEndpoints.AuthorizeAsync(context, authentication, operation.Purpose).ConfigureAwait(false)) return;
            StudioTransportAuthorization authorization = (StudioTransportAuthorization)context.Items[typeof(StudioTransportAuthorization)]!;
            if (!runtimeLeases.TryResolve(snapshotChecksum, authorization, graph, out _, out StudioBootstrapSnapshot snapshot) ||
                snapshot.Clients.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.EndpointSurfaceId, surfaceId) &&
                    StudioSha256.FixedTimeEquals(value.OperationInventoryChecksum, surface.OperationInventoryChecksum)) is not { } client ||
                operation.Purpose is StudioTransportPurpose.CommandPreview or StudioTransportPurpose.CommandExecution && snapshot.Mode == StudioMode.Inspect)
            { context.Response.StatusCode = StatusCodes.Status404NotFound; StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
            foreach (IStudioResponseAuthorityValidator validator in authorityValidators)
                if (!await validator.IsCurrentAsync(snapshot.Authority, context.RequestAborted).ConfigureAwait(false))
                { ApplyFrameworkFailure(context.Response, StatusCodes.Status404NotFound, snapshot); return; }
            long requestMaximum = Math.Min(operation.MaximumRequestBytes, client.Limits.MaximumRequestBytes);
            byte[] body = [];
            bool hasBody = context.Request.ContentLength is > 0;
            if (hasBody || requestMaximum > 0 && method != StudioTransportMethod.Get)
            {
                if (context.Request.ContentType is null || !operation.RequestMediaTypes.Contains(context.Request.ContentType, StringComparer.OrdinalIgnoreCase))
                { ApplyFrameworkFailure(context.Response, StatusCodes.Status415UnsupportedMediaType, snapshot); return; }
                if (context.Request.ContentLength is null) { ApplyFrameworkFailure(context.Response, StatusCodes.Status411LengthRequired, snapshot); return; }
                if (context.Request.ContentLength > requestMaximum) { ApplyFrameworkFailure(context.Response, StatusCodes.Status413PayloadTooLarge, snapshot); return; }
                try { body = await ReadBoundedAsync(context.Request.Body, checked((int)requestMaximum), context.RequestAborted).ConfigureAwait(false); }
                catch (InvalidDataException) { ApplyFrameworkFailure(context.Response, StatusCodes.Status413PayloadTooLarge, snapshot); return; }
            }
            string query = context.Request.QueryString.Value ?? string.Empty;
            if (query.Length > 4_096 || query.Any(char.IsControl)) { ApplyFrameworkFailure(context.Response, StatusCodes.Status404NotFound, snapshot); return; }
            var headerBuilder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in operation.RequestHeaderNames)
            {
                Microsoft.Extensions.Primitives.StringValues supplied = context.Request.Headers[name];
                if (supplied.Count > 1 || supplied.Count == 1 && (supplied[0]!.Length > 4_096 || supplied[0]!.Any(char.IsControl)))
                { ApplyFrameworkFailure(context.Response, StatusCodes.Status400BadRequest, snapshot); return; }
                if (supplied.Count == 1) headerBuilder.Add(name, supplied[0]!);
            }
            var request = new StudioFrameworkSurfaceRequest(operationId, operation.RequiredCapability, relativePath, query, method,
                context.Request.ContentType, body,
                headerBuilder.ToImmutable(), snapshot.Authority, context.User);
            if (!lateWork.TryEnter(out StudioLateWorkLease lease)) { ApplyFrameworkFailure(context.Response, StatusCodes.Status503ServiceUnavailable, snapshot); return; }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(operation.Deadline < client.Limits.OperationDeadline ? operation.Deadline : client.Limits.OperationDeadline);
            Task<StudioFrameworkSurfaceResponse?> task;
            try { task = surface.ExecuteAsync(request, deadline.Token).AsTask(); }
            catch { lease.Dispose(); throw; }
            StudioFrameworkSurfaceResponse? response;
            try { response = await task.WaitAsync(deadline.Token).ConfigureAwait(false); lease.Dispose(); }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            { lease.Retain(task); ApplyFrameworkFailure(context.Response, StatusCodes.Status504GatewayTimeout, snapshot); return; }
            catch (OperationCanceledException) { lease.Retain(task); throw; }
            catch { lease.Dispose(); throw; }
            if (response is null) { ApplyFrameworkFailure(context.Response, StatusCodes.Status404NotFound, snapshot); return; }
            byte[] responseBytes = response.GetBody(); long responseMaximum = Math.Min(operation.MaximumResponseBytes, client.Limits.MaximumResponseBytes);
            if (responseBytes.LongLength > responseMaximum) throw new InvalidOperationException("base.studio.frameworkResponseTooLarge");
            if (response.Headers.Keys.Any(name => !operation.ResponseHeaderNames.Contains(name, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("base.studio.frameworkResponseHeaderUnexpected");
            if (!operation.ResponseMediaTypes.Contains(response.ContentType, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("base.studio.frameworkResponseMediaTypeUnexpected");
            StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
            context.Response.Headers["X-HPD-Studio-Response-Authority"] = Convert.ToHexString(snapshot.Authority.Checksum.ToArray()).ToLowerInvariant();
            foreach (var header in response.Headers) context.Response.Headers[header.Key] = header.Value;
            context.Response.StatusCode = response.StatusCode; context.Response.ContentType = response.ContentType;
            context.Response.ContentLength = responseBytes.LongLength; await context.Response.Body.WriteAsync(responseBytes, context.RequestAborted).ConfigureAwait(false);
        }).WithName("StudioFrameworkClientBridge").WithSummary("Invoke one bootstrap-disclosed sealed framework client operation");

        routeGroup.MapGet("/modules/{moduleId}/{version:long}/{**assetPath}", async context =>
        {
            string? moduleId = context.Request.RouteValues["moduleId"] as string;
            string? assetPath = context.Request.RouteValues["assetPath"] as string;
            if (!long.TryParse(Convert.ToString(context.Request.RouteValues["version"], CultureInfo.InvariantCulture),
                    NumberStyles.None, CultureInfo.InvariantCulture, out long version) ||
                !assets.TryResolve(moduleId, version, assetPath, out StudioResolvedAsset asset))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            ApplyStaticSecurityHeaders(context.Response);
            context.Response.ContentType = asset.ContentType;
            context.Response.ContentLength = asset.Content.LongLength;
            context.Response.Headers.ETag = asset.ETag;
            context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            if (context.Request.Headers.IfNoneMatch.Any(value => StringComparer.Ordinal.Equals(value, asset.ETag)))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                context.Response.ContentLength = null;
                return;
            }
            await context.Response.Body.WriteAsync(asset.Content, context.RequestAborted).ConfigureAwait(false);
        }).WithName("StudioModuleAsset").WithSummary("Get one manifest-verified Studio module asset");

        routeGroup.MapGet("/", context => WriteShellAsync(context, shellAssets, routePrefix));
        routeGroup.MapGet("/{**studioPath}", context => WriteRouteOrNotFoundAsync(context, graph, shellAssets, routePrefix));
        options.ConfigureRoutes?.Invoke(routeGroup);
        return routeGroup;
    }

    private static bool TryAuthorizeCommandExecution(StudioCommandAuthorityRegistry registry, StudioCanonicalJson request, StudioCommandRegistration command,
        StudioBootstrapSnapshot snapshot, StudioSessionObservation session, out CommandExecutionIdentity execution)
    {
        execution = null!;
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            string[] exact = ["acknowledgements", "commandId", "freshAuthentication", "pageId", "preview", "requestIdentity", "responseAuthorityChecksum", "target"];
            if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Select(static p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(exact)) return false;
            if (root.GetProperty("commandId").GetString() is not { } commandId || !StringComparer.Ordinal.Equals(commandId, command.CommandId) ||
                root.GetProperty("pageId").GetString() is not { } pageId ||
                root.GetProperty("requestIdentity").GetString() is not { Length: >= 1 and <= 128 } requestIdentity ||
                root.GetProperty("responseAuthorityChecksum").GetString() is not { } responseAuthority ||
                !StudioSha256.FixedTimeEquals(ParseHex(responseAuthority), snapshot.Authority.Checksum)) return false;
            JsonElement target = root.GetProperty("target"); JsonElement preview = root.GetProperty("preview");
            if (target.ValueKind != JsonValueKind.Object || preview.ValueKind != JsonValueKind.Object ||
                target.TryGetProperty("authorityChecksum", out JsonElement targetChecksumElement) is false ||
                target.TryGetProperty("kind", out JsonElement kindElement) is false || preview.TryGetProperty("previewChecksum", out JsonElement previewElement) is false)
                return false;
            if (!TryDecodeResource(target, out StudioResourceIdentity? targetResource) || targetResource is null ||
                !StringComparer.Ordinal.Equals(targetResource.ApplicationId, snapshot.ApplicationId)) return false;
            StudioSha256 targetChecksum = ParseHex(targetChecksumElement.GetString()); StudioSha256 previewChecksum = ParseHex(previewElement.GetString());
            if (!StudioSha256.FixedTimeEquals(targetResource.AuthorityChecksum, targetChecksum)) return false;
            string? targetKind = kindElement.GetString();
            StudioVisibleCommand? visible = snapshot.Commands.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.CommandId, command.CommandId));
            if (visible is null || !visible.AcceptedResources.Any(kind => StringComparer.Ordinal.Equals(StudioResourceIdentity.Name(kind), targetKind))) return false;
            StudioSha256 requestChecksum = StudioSha256.FromDigest(System.Security.Cryptography.SHA256.HashData(request.ToArray()));
            string outcomeKey = session.PrincipalGeneration.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToHexString(session.SessionChecksum.ToArray()) + ":" + requestIdentity;
            if (registry.Outcomes.TryGetValue(outcomeKey, out CommandOutcome? resolved) && resolved.RetainThroughUtc > DateTimeOffset.UtcNow)
            {
                if (!StringComparer.Ordinal.Equals(resolved.CommandId, commandId) ||
                    !StudioSha256.FixedTimeEquals(resolved.TargetChecksum, targetChecksum) ||
                    !StudioSha256.FixedTimeEquals(resolved.PreviewChecksum, previewChecksum) ||
                    !StudioSha256.FixedTimeEquals(resolved.RequestChecksum, requestChecksum) ||
                    !StudioSha256.FixedTimeEquals(resolved.SessionChecksum, session.SessionChecksum) ||
                    !StudioSha256.FixedTimeEquals(resolved.ProtectedScopeChecksum, session.ProtectedScopeChecksum)) return false;
                execution = new(outcomeKey, commandId, targetChecksum, previewChecksum, requestChecksum, session.SessionChecksum,
                    session.ProtectedScopeChecksum, string.Empty, null, resolved.RetainThroughUtc, true);
                return true;
            }
            if (registry.Executions.TryGetValue(outcomeKey, out CommandExecutionIdentity? active))
            {
                if (!StringComparer.Ordinal.Equals(active.CommandId, commandId) ||
                    !StudioSha256.FixedTimeEquals(active.TargetChecksum, targetChecksum) ||
                    !StudioSha256.FixedTimeEquals(active.PreviewChecksum, previewChecksum) ||
                    !StudioSha256.FixedTimeEquals(active.RequestChecksum, requestChecksum) ||
                    !StudioSha256.FixedTimeEquals(active.SessionChecksum, session.SessionChecksum) ||
                    !StudioSha256.FixedTimeEquals(active.ProtectedScopeChecksum, session.ProtectedScopeChecksum)) return false;
                execution = active with { ReceiptOnly = true }; return true;
            }
            string previewKey = PreviewKey(session, commandId, previewChecksum);
            if (!registry.Previews.TryGetValue(previewKey, out CommandPreviewEvidence? capturedPreview) || capturedPreview.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
                !StringComparer.Ordinal.Equals(capturedPreview.PageId, pageId) ||
                !StudioSha256.FixedTimeEquals(capturedPreview.TargetChecksum, targetResource.AuthorityChecksum) ||
                !preview.GetRawText().Equals(System.Text.Encoding.UTF8.GetString(capturedPreview.CanonicalBytes), StringComparison.Ordinal)) return false;
            JsonElement acknowledgements = root.GetProperty("acknowledgements");
            if (acknowledgements.ValueKind != JsonValueKind.Array || acknowledgements.GetArrayLength() != command.Acknowledgements.Length) return false;
            var supplied = new List<(string Purpose, string Impact)>();
            foreach (JsonElement evidence in acknowledgements.EnumerateArray())
            {
                if (evidence.ValueKind != JsonValueKind.Object || !evidence.EnumerateObject().Select(static p => p.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(new[] { "impactId", "previewChecksum", "purposeId" }) ||
                    evidence.GetProperty("purposeId").GetString() is not { } purpose || evidence.GetProperty("impactId").GetString() is not { } impact ||
                    !StudioSha256.FixedTimeEquals(ParseHex(evidence.GetProperty("previewChecksum").GetString()), previewChecksum)) return false;
                supplied.Add((purpose, impact));
            }
            if (!supplied.OrderBy(static value => value.Purpose, StringComparer.Ordinal).SequenceEqual(command.Acknowledgements.Select(static value => (value.PurposeId, value.ImpactId)))) return false;
            string? protectedAuthority = root.GetProperty("freshAuthentication").ValueKind == JsonValueKind.String ? root.GetProperty("freshAuthentication").GetString() : null;
            ReclaimExpired(registry);
            execution = new(outcomeKey, commandId, targetChecksum, previewChecksum, requestChecksum, session.SessionChecksum,
                session.ProtectedScopeChecksum, previewKey, protectedAuthority, DateTimeOffset.UtcNow.AddHours(24));
            if (registry.Executions.Count >= 4096 || !registry.Executions.TryAdd(outcomeKey, execution)) return false;
            bool authorityAccepted = command.FreshAuthentication switch
            {
                { } required => protectedAuthority is not null && StudioAuthenticationEndpoints.TryConsumeFreshAuthority(
                    registry, protectedAuthority, requestIdentity, commandId, targetResource, previewChecksum, session, required),
                null => protectedAuthority is null,
            };
            if (authorityAccepted) return true;
            registry.Executions.TryRemove(new KeyValuePair<string, CommandExecutionIdentity>(outcomeKey, execution)); return false;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or InvalidOperationException) { return false; }
    }

    private static StudioSha256 ParseHex(string? value)
    {
        if (value is not { Length: 64 } || value.Any(static c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) throw new FormatException();
        return StudioSha256.FromDigest(Convert.FromHexString(value));
    }

    private static bool TryCaptureCommandPreviewInvocation(StudioCommandAuthorityRegistry registry, StudioCanonicalJson request, StudioBootstrapSnapshot snapshot,
        StudioSessionObservation session, string registeredCommandId, out CommandPreviewInvocation invocation)
    {
        invocation = null!;
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray(), new JsonDocumentOptions { MaxDepth = 16 }); JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Select(static p => p.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "commandId", "input", "pageId", "responseAuthorityChecksum", "target" }) ||
                root.GetProperty("commandId").GetString() is not { } commandId ||
                !StringComparer.Ordinal.Equals(commandId, registeredCommandId) || root.GetProperty("pageId").GetString() is not { } pageId ||
                snapshot.Commands.SingleOrDefault(command => StringComparer.Ordinal.Equals(command.CommandId, commandId)) is not { } visible ||
                !visible.OwningPageIds.Contains(pageId, StringComparer.Ordinal) ||
                !StudioSha256.FixedTimeEquals(ParseHex(root.GetProperty("responseAuthorityChecksum").GetString()), snapshot.Authority.Checksum) ||
                !TryDecodeResource(root.GetProperty("target"), out StudioResourceIdentity? target) || target is null ||
                !StringComparer.Ordinal.Equals(target.ApplicationId, snapshot.ApplicationId)) return false;
            invocation = new(commandId, pageId, target, session); return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException) { return false; }
    }

    private static bool RegisterCommandPreview(StudioCommandAuthorityRegistry registry, CommandPreviewInvocation invocation, byte[] bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }); JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("previewChecksum", out JsonElement checksumElement) ||
                !root.TryGetProperty("expiresAtUtc", out JsonElement expiryElement) ||
                !DateTimeOffset.TryParseExact(expiryElement.GetString(), "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTimeOffset expiry) || expiry <= DateTimeOffset.UtcNow) return false;
            StudioSha256 checksum = ParseHex(checksumElement.GetString()); string key = PreviewKey(invocation.Session, invocation.CommandId, checksum);
            foreach (var expired in registry.Previews.Where(static item => item.Value.ExpiresAtUtc <= DateTimeOffset.UtcNow)) registry.Previews.TryRemove(expired.Key, out _);
            if (registry.Previews.Count >= 4096) return false;
            var evidence = new CommandPreviewEvidence(invocation.PageId, invocation.Target.AuthorityChecksum, bytes.ToArray(), expiry);
            return registry.Previews.TryAdd(key, evidence) || registry.Previews.TryGetValue(key, out CommandPreviewEvidence? prior) &&
                StringComparer.Ordinal.Equals(prior.PageId, evidence.PageId) && StudioSha256.FixedTimeEquals(prior.TargetChecksum, evidence.TargetChecksum) &&
                System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(prior.CanonicalBytes, evidence.CanonicalBytes) && prior.ExpiresAtUtc == evidence.ExpiresAtUtc;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException) { return false; }
    }

    private static string PreviewKey(StudioSessionObservation session, string commandId, StudioSha256 preview)
        => session.PrincipalGeneration.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToHexString(session.SessionChecksum.ToArray()) + ":" + commandId + ":" + Convert.ToHexString(preview.ToArray());

    internal static bool TryAuthorizeFreshAuthentication(StudioCommandAuthorityRegistry registry, StudioSessionObservation session, string commandId,
        StudioResourceIdentity target, StudioSha256 preview, out DateTimeOffset previewExpiresAtUtc)
    {
        previewExpiresAtUtc = default;
        if (!registry.Previews.TryGetValue(PreviewKey(session, commandId, preview), out CommandPreviewEvidence? evidence) ||
            evidence.ExpiresAtUtc <= DateTimeOffset.UtcNow || !StudioSha256.FixedTimeEquals(evidence.TargetChecksum, target.AuthorityChecksum)) return false;
        previewExpiresAtUtc = evidence.ExpiresAtUtc; return true;
    }

    private static bool TryDecodeResource(JsonElement value, out StudioResourceIdentity? resource)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value.GetRawText());
        string token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return StudioResourceRouteToken.TryDecode(token, out resource) && resource is not null;
    }

    private static void ReleaseBeforeInfluence(StudioCommandAuthorityRegistry registry, CommandExecutionIdentity execution)
    { registry.Executions.TryRemove(new KeyValuePair<string, CommandExecutionIdentity>(execution.Key, execution));
      StudioAuthenticationEndpoints.RestoreFreshAuthorityBeforeInfluence(registry, execution.ProtectedAuthority); }

    private static async Task RetainCommandOutcomeAsync(StudioCommandAuthorityRegistry registry, Task<StudioCanonicalJson?> task, CommandExecutionIdentity execution,
        StudioEndpointContract endpoint, ImmutableArray<StudioNamedTypeContract> types)
    {
        try
        {
            StudioCanonicalJson? result = await task.ConfigureAwait(false);
            if (result is null) { ReleaseBeforeInfluence(registry, execution); return; }
            registry.Previews.TryRemove(execution.PreviewKey, out _);
            byte[] bytes = result.ToArray();
            if (bytes.LongLength > endpoint.MaximumResultBytes) return;
            StudioL41JsonValidator.Require(result, endpoint.ResultNodeId, types);
            registry.Outcomes.TryAdd(execution.Key, new CommandOutcome(execution.CommandId, execution.TargetChecksum, execution.PreviewChecksum,
                execution.RequestChecksum, execution.SessionChecksum, execution.ProtectedScopeChecksum, bytes, execution.RetainThroughUtc));
            registry.Previews.TryRemove(execution.PreviewKey, out _);
        }
        catch (StudioCommandFailedBeforeInfluenceException) { ReleaseBeforeInfluence(registry, execution); }
        catch { registry.Previews.TryRemove(execution.PreviewKey, out _); }
    }

    private static void ApplyCommandFailure(HttpResponse response, StudioBootstrapSnapshot snapshot, int statusCode, string code)
    {
        ApplyFrameworkFailure(response, statusCode, snapshot); response.ContentType = "application/json; charset=utf-8";
        response.Headers["X-HPD-Studio-Error"] = code;
    }

    private static void ReclaimExpired(StudioCommandAuthorityRegistry registry)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var expired in registry.Outcomes.Where(item => item.Value.RetainThroughUtc <= now)) registry.Outcomes.TryRemove(expired.Key, out _);
        foreach (var expired in registry.Executions.Where(item => item.Value.RetainThroughUtc <= now)) registry.Executions.TryRemove(expired.Key, out _);
    }

    private static async Task<StudioBootstrapRequest?> ReadBootstrapRequestAsync(HttpContext context)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json") &&
            !StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json; charset=utf-8"))
        { context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType; StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return null; }
        if (context.Request.ContentLength is null or < 2)
        {
            context.Response.StatusCode = StatusCodes.Status411LengthRequired;
            StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
            return null;
        }
        if (context.Request.ContentLength is > MaximumBootstrapRequestBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
            return null;
        }
        try
        {
            byte[] body = await ReadBoundedAsync(context.Request.Body, MaximumBootstrapRequestBytes, context.RequestAborted).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 4 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Select(static x => x.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(new[] { "clientCapabilities", "editionAssetGraphChecksum", "locale", "runtimeClientChecksum", "shellContractChecksum" }) is false)
                throw new JsonException();
            StudioSha256 shell = ParseChecksum(root.GetProperty("shellContractChecksum"));
            StudioSha256 assets = ParseChecksum(root.GetProperty("editionAssetGraphChecksum"));
            StudioSha256 runtime = ParseChecksum(root.GetProperty("runtimeClientChecksum"));
            string locale = root.GetProperty("locale").GetString() ?? throw new JsonException();
            JsonElement capabilities = root.GetProperty("clientCapabilities");
            if (capabilities.ValueKind != JsonValueKind.Array) throw new JsonException();
            ImmutableArray<StudioBrowserCapability> values = capabilities.EnumerateArray()
                .Select(static value => (StudioBrowserCapability)value.GetByte()).ToImmutableArray();
            return StudioBootstrapRequest.Create(shell, assets, runtime, locale, values);
        }
        catch (InvalidDataException)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
            return null;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
            return null;
        }
    }

    private static StudioSha256 ParseChecksum(JsonElement value)
    {
        string text = value.GetString() ?? throw new JsonException();
        if (text.Length != 64 || !text.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new JsonException();
        return StudioSha256.FromDigest(Convert.FromHexString(text));
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var destination = new MemoryStream(Math.Min(maximumBytes, 16_384));
        byte[] buffer = new byte[8_192]; int total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break; total = checked(total + read);
            if (total > maximumBytes) throw new InvalidDataException("Studio request exceeds its registered bound.");
            destination.Write(buffer, 0, read);
        }
        return destination.ToArray();
    }

    private static Task WriteRouteOrNotFoundAsync(HttpContext context, StudioApplicationGraph graph,
        StudioShellAssetGraph shellAssets, string routePrefix)
    {
        string path = context.Request.RouteValues["studioPath"] as string ?? string.Empty;
        if (!StudioRouteMatcher.Matches(graph, path, context.Request.Query))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
        return WriteShellAsync(context, shellAssets, routePrefix);
    }

    private static Task WriteShellAsync(HttpContext context, StudioShellAssetGraph shellAssets, string routePrefix)
    {
        ApplyStaticSecurityHeaders(context.Response);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "text/html; charset=utf-8";
        byte[] document = shellAssets.CreateEntryDocument(routePrefix);
        context.Response.ContentLength = document.LongLength;
        return context.Response.Body.WriteAsync(document, context.RequestAborted).AsTask();
    }

    private static string NormalizeRoutePrefix(string routePrefix)
    {
        if (string.IsNullOrWhiteSpace(routePrefix) || routePrefix == "/" || !routePrefix.StartsWith('/'))
            throw new InvalidOperationException("The Studio route prefix must identify a non-root absolute path.");
        string normalized = "/" + routePrefix.Trim('/');
        if (normalized.Length > 128 || normalized.Contains("//", StringComparison.Ordinal) || normalized.Contains("..", StringComparison.Ordinal) || normalized.Contains('\\') || normalized.Any(char.IsControl))
            throw new InvalidOperationException("The Studio route prefix is invalid.");
        return normalized;
    }

    private static void ApplyStaticSecurityHeaders(HttpResponse response)
    {
        response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; object-src 'none'";
        response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
    }

    private static void Hex(Utf8JsonWriter json, string name, StudioSha256 checksum)
        => json.WriteString(name, Convert.ToHexString(checksum.ToArray()).ToLowerInvariant());

    private static void ApplyFrameworkFailure(HttpResponse response, int statusCode, StudioBootstrapSnapshot snapshot)
    {
        response.StatusCode = statusCode;
        StudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(response);
        response.Headers["X-HPD-Studio-Response-Authority"] =
            Convert.ToHexString(snapshot.Authority.Checksum.ToArray()).ToLowerInvariant();
    }
}

internal sealed record StudioResolvedAsset(byte[] Content, string ContentType, string ETag);

internal sealed class StudioEditionAssetGraph
{
    private readonly IReadOnlyDictionary<(string ModuleId, long Version, string Path), StudioResolvedAsset> _assets;
    private StudioEditionAssetGraph(StudioSha256 checksum,
        IReadOnlyDictionary<(string ModuleId, long Version, string Path), StudioResolvedAsset> assets)
    { Checksum = checksum; _assets = assets; }
    internal StudioSha256 Checksum { get; }
    internal ImmutableArray<StudioEditionModuleAssetContribution> Modules { get; private init; }

    internal static StudioEditionAssetGraph Create(ImmutableArray<StudioEditionModuleAssetContribution> catalog, StudioShellContract shell)
    {
        var assets = new Dictionary<(string, long, string), StudioResolvedAsset>();
        foreach (StudioEditionModuleAssetContribution module in catalog)
        {
            if (!StudioSha256.FixedTimeEquals(module.Asset.ShellContractChecksum, shell.Checksum))
                throw new InvalidOperationException("A Studio module targets another shell contract.");
            foreach (StudioAssetEntry entry in module.Asset.Assets)
            {
                byte[] content = module.Asset.GetRequiredContent(entry.Path);
                if (content.LongLength != entry.Length || !StudioSha256.FixedTimeEquals(StudioSha256.Compute(content), entry.Digest))
                    throw new InvalidOperationException("A Studio module asset differs from its manifest.");
                assets.Add((module.ModuleId, module.ModuleVersion, entry.Path),
                    new(content, ContentType(entry.MediaType), $"\"sha256-{Convert.ToHexString(entry.Digest.ToArray()).ToLowerInvariant()}\""));
            }
        }
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.edition-assets.v1", writer =>
        {
            writer.Checksum(shell.Checksum); writer.Count(catalog.Length);
            foreach (StudioEditionModuleAssetContribution module in catalog)
            { writer.String(module.ModuleId); writer.Int32(module.ModuleVersion); writer.Checksum(module.Asset.AssetGraphChecksum); }
        });
        return new(checksum, assets) { Modules = catalog };
    }

    internal bool TryResolve(string? module, long version, string? path, out StudioResolvedAsset asset)
    {
        asset = null!;
        if (string.IsNullOrWhiteSpace(module) || version < 1 || string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal) || path.Contains('\\'))
            return false;
        if (!_assets.TryGetValue((module, version, path), out StudioResolvedAsset? resolved))
            return false;
        asset = resolved;
        return true;
    }

    private static string ContentType(StudioAssetMediaType type) => type switch
    {
        StudioAssetMediaType.JavaScriptModule => "text/javascript; charset=utf-8",
        StudioAssetMediaType.Css => "text/css; charset=utf-8",
        StudioAssetMediaType.Svg => "image/svg+xml",
        StudioAssetMediaType.Woff2 => "font/woff2",
        StudioAssetMediaType.Png => "image/png",
        StudioAssetMediaType.Json => "application/json; charset=utf-8",
        _ => throw new InvalidOperationException("A Studio asset media type is unsupported."),
    };
}

internal static class StudioRouteMatcher
{
    internal static bool Matches(StudioApplicationGraph graph, string path, IQueryCollection query)
    {
        string[] segments = string.IsNullOrEmpty(path) ? [] : path.Split('/', StringSplitOptions.None);
        return graph.Modules.SelectMany(static module => module.Pages).Any(page => Matches(page.Route, segments, query));
    }

    private static bool Matches(StudioRouteTemplate route, string[] values, IQueryCollection query)
    {
        if (route.Segments.Length != values.Length || query.Keys.Any(key => route.Query.All(member => !StringComparer.Ordinal.Equals(member.Name, key))))
            return false;
        for (int index = 0; index < values.Length; index++)
        {
            StudioRouteSegment segment = route.Segments[index];
            if (segment.Kind == StudioRouteSegmentKind.Literal)
            { if (!StringComparer.Ordinal.Equals(segment.Value, values[index])) return false; }
            else if (!Valid(values[index], segment.Codec!.Value, [])) return false;
        }
        foreach (StudioQueryParameter member in route.Query)
        {
            bool present = query.TryGetValue(member.Name, out var value);
            if (member.Required != present && member.Required || present && (value.Count != 1 || !Valid(value[0]!, member.Codec, member.RegisteredValues)))
                return false;
        }
        return true;
    }

    private static bool Valid(string value, StudioRouteCodec codec, ImmutableArray<string> registered) => codec switch
    {
        StudioRouteCodec.PositiveLong => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number > 0 && number.ToString(CultureInfo.InvariantCulture) == value,
        StudioRouteCodec.NonnegativeLong => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number >= 0 && number.ToString(CultureInfo.InvariantCulture) == value,
        StudioRouteCodec.Sha256 => value.Length == 64 && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f'),
        StudioRouteCodec.StudioResourceIdentity => StudioResourceRouteToken.TryDecode(value, out _),
        StudioRouteCodec.RegisteredEnum or StudioRouteCodec.SelectedTab => registered.Contains(value, StringComparer.Ordinal),
        _ => value.Length is >= 1 and <= 256 && value.All(static character => !char.IsControl(character) && character != '/'),
    };
}
