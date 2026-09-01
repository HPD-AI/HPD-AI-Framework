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
        BaseStudioApplicationGraph graph = services.GetRequiredService<BaseStudioApplicationGraphProvider>().GetRequiredGraph();
        BaseStudioAuthenticationProvider authentication = services.GetRequiredService<BaseStudioAuthenticationProvider>();
        IBaseStudioBootstrapRuntime bootstrap = services.GetRequiredService<IBaseStudioBootstrapRuntime>();
        BaseStudioRuntimeCatalog runtime = services.GetRequiredService<BaseStudioRuntimeCatalog>();
        BaseStudioRuntimeLeaseRegistry runtimeLeases = services.GetRequiredService<BaseStudioRuntimeLeaseRegistry>();
        BaseStudioLateWorkRegistry lateWork = services.GetRequiredService<BaseStudioLateWorkRegistry>();
        BaseStudioCommandAuthorityRegistry commandAuthority = services.GetRequiredService<BaseStudioCommandAuthorityRegistry>();
        BaseStudioFrameworkEndpointSurfaceCatalog frameworkSurfaces = services.GetRequiredService<BaseStudioFrameworkEndpointSurfaceCatalog>();
        IBaseStudioResponseAuthorityValidator[] authorityValidators = services.GetServices<IBaseStudioResponseAuthorityValidator>().ToArray();
        BaseStudioShellContract shell = services.GetRequiredService<BaseStudioShellContract>();
        BaseStudioShellAssetGraph shellAssets = services.GetRequiredService<BaseStudioShellAssetGraph>();
        BaseStudioEditionAssetGraph assets = BaseStudioEditionAssetGraph.Create(
            services.GetRequiredService<BaseStudioEditionAssetCatalogProvider>().GetRequiredCatalog(), shell);
        var routeGroup = endpoints.MapGroup(routePrefix);

        BaseStudioAuthenticationEndpoints.Map(routeGroup, authentication, graph, lateWork, commandAuthority);

        routeGroup.MapGet("/control/shell", async context =>
        {
            ApplyStaticSecurityHeaders(context.Response); context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentType = "application/json; charset=utf-8";
            var payload = new ArrayBufferWriter<byte>();
            using var json = new Utf8JsonWriter(payload);
            json.WriteStartObject(); Hex(json, "shellContractChecksum", shell.Checksum);
            Hex(json, "editionAssetGraphChecksum", assets.Checksum);
            BaseStudioSha256 runtimeChecksum = graph.Modules.SelectMany(static module => module.Clients)
                .Where(static client => client.ClientId == "base.control-plane").Select(static client => client.StaticRuntimeAbiChecksum).Single();
            Hex(json, "runtimeClientChecksum", runtimeChecksum);
            json.WriteString("bootstrapRoute", routePrefix + "/control/bootstrap");
            json.WriteString("sessionRoute", routePrefix + authentication.Integration.Descriptor.SessionRoute);
            json.WriteString("loginRoute", routePrefix + authentication.Integration.Descriptor.LoginRoute);
            json.WriteString("logoutRoute", routePrefix + authentication.Integration.Descriptor.LogoutRoute);
            json.WritePropertyName("authentication"); json.WriteStartObject();
            json.WriteString("kind", authentication.Integration.Descriptor.Kind == BaseStudioAuthenticationKind.CookieBff ? "cookieBff" : "bearer");
            json.WriteString("authorizationRoute", routePrefix + "/control/authorize");
            if (authentication.Integration.Descriptor.Kind == BaseStudioAuthenticationKind.Bearer)
                json.WriteBoolean("refreshSupported", authentication.Integration.Descriptor.RefreshSupported);
            Hex(json, "descriptorChecksum", authentication.Integration.Descriptor.Checksum); json.WriteEndObject();
            json.WritePropertyName("modules"); json.WriteStartArray();
            foreach (BaseStudioEditionModuleAssetContribution module in assets.Modules)
            { json.WriteStartObject(); json.WriteString("moduleId", module.ModuleId); json.WriteNumber("moduleVersion", module.ModuleVersion);
              json.WriteString("entryModulePath", routePrefix + "/modules/" + module.ModuleId + "/" + module.ModuleVersion.ToString(CultureInfo.InvariantCulture) + "/" + module.Asset.EntryModulePath);
              Hex(json, "assetGraphChecksum", module.Asset.AssetGraphChecksum); json.WriteEndObject(); }
            json.WriteEndArray(); json.WriteEndObject(); json.Flush();
            context.Response.ContentLength = payload.WrittenCount;
            await context.Response.Body.WriteAsync(payload.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
        }).WithName("BaseStudioShellDescriptor").WithSummary("Get the authorization-neutral Studio host descriptor");

        routeGroup.MapGet("/assets/{**shellAssetPath}", async context =>
        {
            string? path = context.Request.RouteValues["shellAssetPath"] as string;
            if (path is null || !shellAssets.TryResolve("assets/" + path, out BaseStudioShellAsset asset))
            { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
            ApplyStaticSecurityHeaders(context.Response); context.Response.ContentType = asset.ContentType;
            byte[] content = asset.GetContent(); context.Response.ContentLength = content.LongLength;
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.ETag = "\"sha256-" + Convert.ToHexString(asset.Digest.ToArray()).ToLowerInvariant() + "\"";
            await context.Response.Body.WriteAsync(content, context.RequestAborted).ConfigureAwait(false);
        }).WithName("BaseStudioShellAsset").WithSummary("Get one revalidated Studio shell asset");

        routeGroup.MapPost("/control/bootstrap", async context =>
        {
            if (!await BaseStudioAuthenticationEndpoints.AuthorizeBootstrapAsync(context, authentication).ConfigureAwait(false))
                return;
            BaseStudioBootstrapRequest? request = await ReadBootstrapRequestAsync(context).ConfigureAwait(false);
            if (request is null)
                return;
            if (!BaseStudioSha256.FixedTimeEquals(request.ShellContractChecksum, shell.Checksum) ||
                !BaseStudioSha256.FixedTimeEquals(request.EditionAssetGraphChecksum, assets.Checksum))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
                return;
            }
            var invocation = new BaseStudioBootstrapInvocation(context, graph,
                (BaseStudioTransportAuthorization)context.Items[typeof(BaseStudioTransportAuthorization)]!, request);
            BaseStudioBootstrapSnapshot? snapshot = await bootstrap.CreateAsync(invocation, context.RequestAborted).ConfigureAwait(false);
            if (snapshot is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
                return;
            }
            byte[] payload = BaseStudioBootstrapJson.Encode(snapshot);
            runtimeLeases.Publish(invocation, snapshot);
            BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength = payload.Length;
            await context.Response.Body.WriteAsync(payload, context.RequestAborted).ConfigureAwait(false);
        }).WithName("BaseStudioBootstrap").WithSummary("Get the principal-filtered HPD Studio bootstrap");

        foreach (var route in runtime.Contributions.SelectMany(static value => value.Endpoints)
                     .GroupBy(static value => (value.Method, value.RelativeRoute))
                     .OrderBy(static value => value.Key.RelativeRoute, StringComparer.Ordinal).ThenBy(static value => value.Key.Method))
        {
            BaseStudioEndpointContract[] routeEndpoints = route.OrderBy(static value => value.EndpointId, StringComparer.Ordinal).ToArray();
            string relativeRoute = route.Key.RelativeRoute;
            routeGroup.MapPost(relativeRoute, async context =>
            {
                string? methodId = context.Request.Headers["X-HPD-Studio-Method"].SingleOrDefault();
                if (string.IsNullOrEmpty(methodId) || !runtime.TryGetProducer(methodId, out BaseStudioProducerBinding producer))
                { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
                BaseStudioTransportPurpose purpose = producer.Kind switch
                { BaseStudioMethodKind.Preview => BaseStudioTransportPurpose.CommandPreview, BaseStudioMethodKind.Execute => BaseStudioTransportPurpose.CommandExecution,
                  _ => BaseStudioTransportPurpose.Observation };
                if (!await BaseStudioAuthenticationEndpoints.AuthorizeAsync(context, authentication, purpose).ConfigureAwait(false)) return;
                string? snapshotChecksum = context.Request.Headers["X-HPD-Studio-Snapshot"].SingleOrDefault();
                BaseStudioTransportAuthorization authorization = (BaseStudioTransportAuthorization)context.Items[typeof(BaseStudioTransportAuthorization)]!;
                BaseStudioEndpointContract? boundEndpoint = routeEndpoints.SingleOrDefault(value => runtime.Contributions.SelectMany(static item => item.Methods)
                    .Any(method => StringComparer.Ordinal.Equals(method.RegisteredMethodId, methodId) && StringComparer.Ordinal.Equals(method.EndpointId, value.EndpointId)));
                if (!runtimeLeases.TryResolve(snapshotChecksum, authorization, graph, out BaseStudioBootstrapRequest bootstrapRequest,
                        out BaseStudioBootstrapSnapshot snapshot) || boundEndpoint is null ||
                    !snapshot.ContractMap.Methods.Any(value => StringComparer.Ordinal.Equals(value.RegisteredMethodId, methodId) &&
                        StringComparer.Ordinal.Equals(value.EndpointId, boundEndpoint.EndpointId)) ||
                    producer.Kind is BaseStudioMethodKind.Preview or BaseStudioMethodKind.Execute && snapshot.Mode == BaseStudioMode.Inspect)
                { context.Response.StatusCode = StatusCodes.Status404NotFound; BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
                foreach (IBaseStudioResponseAuthorityValidator validator in authorityValidators)
                    if (!await validator.IsCurrentAsync(snapshot.Authority, context.RequestAborted).ConfigureAwait(false))
                    { context.Response.StatusCode = StatusCodes.Status404NotFound; BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
                if (!StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json") &&
                    !StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json; charset=utf-8"))
                { context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType; return; }
                if (context.Request.ContentLength is null or < 2) { context.Response.StatusCode = StatusCodes.Status411LengthRequired; return; }
                if (context.Request.ContentLength > boundEndpoint.MaximumRequestBytes) { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return; }
                byte[] requestBytes;
                try { requestBytes = await ReadBoundedAsync(context.Request.Body, checked((int)boundEndpoint.MaximumRequestBytes), context.RequestAborted).ConfigureAwait(false); }
                catch (InvalidDataException) { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return; }
                BaseStudioCanonicalJson request;
                try { request = BaseStudioCanonicalJson.Create(requestBytes, checked((int)boundEndpoint.MaximumRequestBytes)); }
                catch (Exception exception) when (exception is JsonException or ArgumentException)
                { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
                try { BaseStudioL41JsonValidator.Require(request, boundEndpoint.RequestNodeId, snapshot.ContractMap.Types); }
                catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
                { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
                CommandExecutionIdentity? commandExecution = null;
                CommandPreviewInvocation? commandPreview = null;
                if (producer.Kind == BaseStudioMethodKind.Preview && !TryCaptureCommandPreviewInvocation(commandAuthority, request, snapshot, authorization.Session,
                    snapshot.ContractMap.Methods.Single(value => StringComparer.Ordinal.Equals(value.RegisteredMethodId, methodId)).OwningPageOrCommandId, out commandPreview))
                { context.Response.StatusCode = StatusCodes.Status400BadRequest; BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
                if (producer.Kind == BaseStudioMethodKind.Execute)
                {
                    BaseStudioMethodBinding methodBinding = snapshot.ContractMap.Methods.Single(value => StringComparer.Ordinal.Equals(value.RegisteredMethodId, methodId));
                    BaseStudioCommandRegistration? command = graph.Modules.Single(value => StringComparer.Ordinal.Equals(value.Identity.ModuleId, methodBinding.OwningModuleId))
                        .Commands.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.CommandId, methodBinding.OwningPageOrCommandId));
                    if (command is null || !TryAuthorizeCommandExecution(commandAuthority, request, command, snapshot, authorization.Session, out commandExecution))
                    { context.Response.StatusCode = StatusCodes.Status409Conflict; BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
                    if (commandAuthority.Outcomes.TryGetValue(commandExecution.Key, out CommandOutcome? historical))
                    {
                        BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
                        context.Response.Headers["X-HPD-Studio-Response-Authority"] = Convert.ToHexString(snapshot.Authority.Checksum.ToArray()).ToLowerInvariant();
                        context.Response.ContentType = "application/json; charset=utf-8"; context.Response.ContentLength = historical.Result.LongLength;
                        await context.Response.Body.WriteAsync(historical.Result, context.RequestAborted).ConfigureAwait(false); return;
                    }
                    if (commandExecution.ReceiptOnly)
                    { ApplyCommandFailure(context.Response, snapshot, StatusCodes.Status409Conflict, "base.studio.commandIndeterminate"); return; }
                }
                var bootstrapInvocation = new BaseStudioBootstrapInvocation(context, graph, authorization, bootstrapRequest);
                var producerInvocation = new BaseStudioProducerInvocation(bootstrapInvocation, snapshot.Authority, methodId, request);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                deadline.CancelAfter(boundEndpoint.Deadline);
                BaseStudioCanonicalJson? result;
                if (!lateWork.TryEnter(out BaseStudioLateWorkLease workLease))
                { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
                Task<BaseStudioCanonicalJson?> producerTask;
                try { producerTask = producer switch
                    {
                        BaseStudioViewProducerBinding view => view.Producer.ReadAsync(producerInvocation, deadline.Token).AsTask(),
                        BaseStudioResourceProducerBinding resolver => resolver.Producer.ResolveAsync(producerInvocation, deadline.Token).AsTask(),
                        BaseStudioLinkProducerBinding links => links.Producer.ResolveAsync(producerInvocation, deadline.Token).AsTask(),
                        BaseStudioCommandPreviewProducerBinding preview => preview.Producer.PreviewAsync(producerInvocation, deadline.Token).AsTask(),
                        BaseStudioCommandExecuteProducerBinding execute => execute.Producer.ExecuteAsync(producerInvocation, deadline.Token).AsTask(),
                        _ => Task.FromResult<BaseStudioCanonicalJson?>(null),
                    }; }
                catch (BaseStudioCommandFailedBeforeInfluenceException) when (commandExecution is not null)
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
                catch (BaseStudioCommandFailedBeforeInfluenceException) when (commandExecution is not null)
                { workLease.Dispose(); ReleaseBeforeInfluence(commandAuthority, commandExecution); ApplyCommandFailure(context.Response, snapshot, StatusCodes.Status409Conflict, "base.studio.failedBeforeInfluence"); return; }
                catch (BaseStudioCommandIndeterminateException) when (commandExecution is not null)
                { workLease.Dispose(); commandAuthority.Previews.TryRemove(commandExecution.PreviewKey, out _);
                  ApplyCommandFailure(context.Response, snapshot, StatusCodes.Status409Conflict, "base.studio.commandIndeterminate"); return; }
                catch when (commandExecution is not null)
                { workLease.Dispose(); commandAuthority.Previews.TryRemove(commandExecution.PreviewKey, out _);
                  ApplyCommandFailure(context.Response, snapshot, StatusCodes.Status409Conflict, "base.studio.commandIndeterminate"); return; }
                catch { workLease.Dispose(); throw; }
                if (result is null) { if (commandExecution is not null) ReleaseBeforeInfluence(commandAuthority, commandExecution);
                    context.Response.StatusCode = StatusCodes.Status404NotFound; BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
                byte[] bytes = result.ToArray();
                if (commandExecution is not null) commandAuthority.Previews.TryRemove(commandExecution.PreviewKey, out _);
                if (bytes.LongLength > boundEndpoint.MaximumResultBytes) throw new InvalidOperationException("base.studio.resultTooLarge");
                BaseStudioL41JsonValidator.Require(result, boundEndpoint.ResultNodeId, snapshot.ContractMap.Types);
                if (commandPreview is not null && !RegisterCommandPreview(commandAuthority, commandPreview, bytes))
                { context.Response.StatusCode = StatusCodes.Status502BadGateway; BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
                if (commandExecution is not null) commandAuthority.Outcomes.TryAdd(commandExecution.Key,
                    new CommandOutcome(commandExecution.CommandId, commandExecution.TargetChecksum, commandExecution.PreviewChecksum,
                        commandExecution.RequestChecksum, commandExecution.SessionChecksum, commandExecution.ProtectedScopeChecksum,
                        bytes.ToArray(), commandExecution.RetainThroughUtc));
                BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
                context.Response.Headers["X-HPD-Studio-Response-Authority"] = Convert.ToHexString(snapshot.Authority.Checksum.ToArray()).ToLowerInvariant();
                context.Response.ContentType = "application/json; charset=utf-8"; context.Response.ContentLength = bytes.LongLength;
                await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
            }).WithName("BaseStudioRuntime_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(relativeRoute))).ToLowerInvariant()[..16])
              .WithSummary("Invoke one bootstrap-disclosed Studio Runtime method");
        }

        routeGroup.MapMethods("/base/studio/framework-clients/{endpointSurfaceId}/{**relativePath}",
            ["GET", "POST", "PUT", "DELETE"], async context =>
        {
            string? surfaceId = context.Request.RouteValues["endpointSurfaceId"] as string;
            string? relativePath = context.Request.RouteValues["relativePath"] as string;
            string? operationId = context.Request.Headers["X-HPD-Studio-Operation"].SingleOrDefault();
            string? snapshotChecksum = context.Request.Headers["X-HPD-Studio-Snapshot"].SingleOrDefault();
            BaseStudioTransportMethod method = context.Request.Method switch
            { "GET" => BaseStudioTransportMethod.Get, "POST" => BaseStudioTransportMethod.Post,
              "PUT" => BaseStudioTransportMethod.Put, "DELETE" => BaseStudioTransportMethod.Delete,
              _ => 0 };
            if (string.IsNullOrEmpty(surfaceId) || string.IsNullOrEmpty(relativePath) || relativePath.Length > 2_048 ||
                relativePath.StartsWith('/') || relativePath.Split('/').Any(static segment => segment is "" or "." or "..") ||
                relativePath.Any(char.IsControl) || string.IsNullOrEmpty(operationId) ||
                !frameworkSurfaces.TryGet(surfaceId, out IBaseStudioFrameworkEndpointSurface surface) ||
                surface.Operations.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.OperationId, operationId)) is not { } operation ||
                operation.Method != method || !operation.Matches(relativePath))
            { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
            if (!await BaseStudioAuthenticationEndpoints.AuthorizeAsync(context, authentication, operation.Purpose).ConfigureAwait(false)) return;
            BaseStudioTransportAuthorization authorization = (BaseStudioTransportAuthorization)context.Items[typeof(BaseStudioTransportAuthorization)]!;
            if (!runtimeLeases.TryResolve(snapshotChecksum, authorization, graph, out _, out BaseStudioBootstrapSnapshot snapshot) ||
                snapshot.Clients.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.EndpointSurfaceId, surfaceId) &&
                    BaseStudioSha256.FixedTimeEquals(value.OperationInventoryChecksum, surface.OperationInventoryChecksum)) is not { } client ||
                operation.Purpose is BaseStudioTransportPurpose.CommandPreview or BaseStudioTransportPurpose.CommandExecution && snapshot.Mode == BaseStudioMode.Inspect)
            { context.Response.StatusCode = StatusCodes.Status404NotFound; BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return; }
            foreach (IBaseStudioResponseAuthorityValidator validator in authorityValidators)
                if (!await validator.IsCurrentAsync(snapshot.Authority, context.RequestAborted).ConfigureAwait(false))
                { ApplyFrameworkFailure(context.Response, StatusCodes.Status404NotFound, snapshot); return; }
            long requestMaximum = Math.Min(operation.MaximumRequestBytes, client.Limits.MaximumRequestBytes);
            byte[] body = [];
            bool hasBody = context.Request.ContentLength is > 0;
            if (hasBody || requestMaximum > 0 && method != BaseStudioTransportMethod.Get)
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
            var request = new BaseStudioFrameworkSurfaceRequest(operationId, operation.RequiredCapability, relativePath, query, method,
                context.Request.ContentType, body,
                headerBuilder.ToImmutable(), snapshot.Authority, context.User);
            if (!lateWork.TryEnter(out BaseStudioLateWorkLease lease)) { ApplyFrameworkFailure(context.Response, StatusCodes.Status503ServiceUnavailable, snapshot); return; }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(operation.Deadline < client.Limits.OperationDeadline ? operation.Deadline : client.Limits.OperationDeadline);
            Task<BaseStudioFrameworkSurfaceResponse?> task;
            try { task = surface.ExecuteAsync(request, deadline.Token).AsTask(); }
            catch { lease.Dispose(); throw; }
            BaseStudioFrameworkSurfaceResponse? response;
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
            BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
            context.Response.Headers["X-HPD-Studio-Response-Authority"] = Convert.ToHexString(snapshot.Authority.Checksum.ToArray()).ToLowerInvariant();
            foreach (var header in response.Headers) context.Response.Headers[header.Key] = header.Value;
            context.Response.StatusCode = response.StatusCode; context.Response.ContentType = response.ContentType;
            context.Response.ContentLength = responseBytes.LongLength; await context.Response.Body.WriteAsync(responseBytes, context.RequestAborted).ConfigureAwait(false);
        }).WithName("BaseStudioFrameworkClientBridge").WithSummary("Invoke one bootstrap-disclosed sealed framework client operation");

        routeGroup.MapGet("/modules/{moduleId}/{version:long}/{**assetPath}", async context =>
        {
            string? moduleId = context.Request.RouteValues["moduleId"] as string;
            string? assetPath = context.Request.RouteValues["assetPath"] as string;
            if (!long.TryParse(Convert.ToString(context.Request.RouteValues["version"], CultureInfo.InvariantCulture),
                    NumberStyles.None, CultureInfo.InvariantCulture, out long version) ||
                !assets.TryResolve(moduleId, version, assetPath, out BaseStudioResolvedAsset asset))
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
        }).WithName("BaseStudioModuleAsset").WithSummary("Get one manifest-verified Studio module asset");

        routeGroup.MapGet("/", context => WriteShellAsync(context, shellAssets, routePrefix));
        routeGroup.MapGet("/{**studioPath}", context => WriteRouteOrNotFoundAsync(context, graph, shellAssets, routePrefix));
        options.ConfigureRoutes?.Invoke(routeGroup);
        return routeGroup;
    }

    private static bool TryAuthorizeCommandExecution(BaseStudioCommandAuthorityRegistry registry, BaseStudioCanonicalJson request, BaseStudioCommandRegistration command,
        BaseStudioBootstrapSnapshot snapshot, BaseStudioSessionObservation session, out CommandExecutionIdentity execution)
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
                !BaseStudioSha256.FixedTimeEquals(ParseHex(responseAuthority), snapshot.Authority.Checksum)) return false;
            JsonElement target = root.GetProperty("target"); JsonElement preview = root.GetProperty("preview");
            if (target.ValueKind != JsonValueKind.Object || preview.ValueKind != JsonValueKind.Object ||
                target.TryGetProperty("authorityChecksum", out JsonElement targetChecksumElement) is false ||
                target.TryGetProperty("kind", out JsonElement kindElement) is false || preview.TryGetProperty("previewChecksum", out JsonElement previewElement) is false)
                return false;
            if (!TryDecodeResource(target, out BaseStudioResourceIdentity? targetResource) || targetResource is null ||
                !StringComparer.Ordinal.Equals(targetResource.ApplicationId, snapshot.ApplicationId)) return false;
            BaseStudioSha256 targetChecksum = ParseHex(targetChecksumElement.GetString()); BaseStudioSha256 previewChecksum = ParseHex(previewElement.GetString());
            if (!BaseStudioSha256.FixedTimeEquals(targetResource.AuthorityChecksum, targetChecksum)) return false;
            string? targetKind = kindElement.GetString();
            BaseStudioVisibleCommand? visible = snapshot.Commands.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.CommandId, command.CommandId));
            if (visible is null || !visible.AcceptedResources.Any(kind => StringComparer.Ordinal.Equals(BaseStudioResourceIdentity.Name(kind), targetKind))) return false;
            BaseStudioSha256 requestChecksum = BaseStudioSha256.FromDigest(System.Security.Cryptography.SHA256.HashData(request.ToArray()));
            string outcomeKey = session.PrincipalGeneration.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToHexString(session.SessionChecksum.ToArray()) + ":" + requestIdentity;
            if (registry.Outcomes.TryGetValue(outcomeKey, out CommandOutcome? resolved) && resolved.RetainThroughUtc > DateTimeOffset.UtcNow)
            {
                if (!StringComparer.Ordinal.Equals(resolved.CommandId, commandId) ||
                    !BaseStudioSha256.FixedTimeEquals(resolved.TargetChecksum, targetChecksum) ||
                    !BaseStudioSha256.FixedTimeEquals(resolved.PreviewChecksum, previewChecksum) ||
                    !BaseStudioSha256.FixedTimeEquals(resolved.RequestChecksum, requestChecksum) ||
                    !BaseStudioSha256.FixedTimeEquals(resolved.SessionChecksum, session.SessionChecksum) ||
                    !BaseStudioSha256.FixedTimeEquals(resolved.ProtectedScopeChecksum, session.ProtectedScopeChecksum)) return false;
                execution = new(outcomeKey, commandId, targetChecksum, previewChecksum, requestChecksum, session.SessionChecksum,
                    session.ProtectedScopeChecksum, string.Empty, null, resolved.RetainThroughUtc, true);
                return true;
            }
            if (registry.Executions.TryGetValue(outcomeKey, out CommandExecutionIdentity? active))
            {
                if (!StringComparer.Ordinal.Equals(active.CommandId, commandId) ||
                    !BaseStudioSha256.FixedTimeEquals(active.TargetChecksum, targetChecksum) ||
                    !BaseStudioSha256.FixedTimeEquals(active.PreviewChecksum, previewChecksum) ||
                    !BaseStudioSha256.FixedTimeEquals(active.RequestChecksum, requestChecksum) ||
                    !BaseStudioSha256.FixedTimeEquals(active.SessionChecksum, session.SessionChecksum) ||
                    !BaseStudioSha256.FixedTimeEquals(active.ProtectedScopeChecksum, session.ProtectedScopeChecksum)) return false;
                execution = active with { ReceiptOnly = true }; return true;
            }
            string previewKey = PreviewKey(session, commandId, previewChecksum);
            if (!registry.Previews.TryGetValue(previewKey, out CommandPreviewEvidence? capturedPreview) || capturedPreview.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
                !StringComparer.Ordinal.Equals(capturedPreview.PageId, pageId) ||
                !BaseStudioSha256.FixedTimeEquals(capturedPreview.TargetChecksum, targetResource.AuthorityChecksum) ||
                !preview.GetRawText().Equals(System.Text.Encoding.UTF8.GetString(capturedPreview.CanonicalBytes), StringComparison.Ordinal)) return false;
            JsonElement acknowledgements = root.GetProperty("acknowledgements");
            if (acknowledgements.ValueKind != JsonValueKind.Array || acknowledgements.GetArrayLength() != command.Acknowledgements.Length) return false;
            var supplied = new List<(string Purpose, string Impact)>();
            foreach (JsonElement evidence in acknowledgements.EnumerateArray())
            {
                if (evidence.ValueKind != JsonValueKind.Object || !evidence.EnumerateObject().Select(static p => p.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(new[] { "impactId", "previewChecksum", "purposeId" }) ||
                    evidence.GetProperty("purposeId").GetString() is not { } purpose || evidence.GetProperty("impactId").GetString() is not { } impact ||
                    !BaseStudioSha256.FixedTimeEquals(ParseHex(evidence.GetProperty("previewChecksum").GetString()), previewChecksum)) return false;
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
                { } required => protectedAuthority is not null && BaseStudioAuthenticationEndpoints.TryConsumeFreshAuthority(
                    registry, protectedAuthority, requestIdentity, commandId, targetResource, previewChecksum, session, required),
                null => protectedAuthority is null,
            };
            if (authorityAccepted) return true;
            registry.Executions.TryRemove(new KeyValuePair<string, CommandExecutionIdentity>(outcomeKey, execution)); return false;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or InvalidOperationException) { return false; }
    }

    private static BaseStudioSha256 ParseHex(string? value)
    {
        if (value is not { Length: 64 } || value.Any(static c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) throw new FormatException();
        return BaseStudioSha256.FromDigest(Convert.FromHexString(value));
    }

    private static bool TryCaptureCommandPreviewInvocation(BaseStudioCommandAuthorityRegistry registry, BaseStudioCanonicalJson request, BaseStudioBootstrapSnapshot snapshot,
        BaseStudioSessionObservation session, string registeredCommandId, out CommandPreviewInvocation invocation)
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
                !BaseStudioSha256.FixedTimeEquals(ParseHex(root.GetProperty("responseAuthorityChecksum").GetString()), snapshot.Authority.Checksum) ||
                !TryDecodeResource(root.GetProperty("target"), out BaseStudioResourceIdentity? target) || target is null ||
                !StringComparer.Ordinal.Equals(target.ApplicationId, snapshot.ApplicationId)) return false;
            invocation = new(commandId, pageId, target, session); return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException) { return false; }
    }

    private static bool RegisterCommandPreview(BaseStudioCommandAuthorityRegistry registry, CommandPreviewInvocation invocation, byte[] bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }); JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("previewChecksum", out JsonElement checksumElement) ||
                !root.TryGetProperty("expiresAtUtc", out JsonElement expiryElement) ||
                !DateTimeOffset.TryParseExact(expiryElement.GetString(), "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTimeOffset expiry) || expiry <= DateTimeOffset.UtcNow) return false;
            BaseStudioSha256 checksum = ParseHex(checksumElement.GetString()); string key = PreviewKey(invocation.Session, invocation.CommandId, checksum);
            foreach (var expired in registry.Previews.Where(static item => item.Value.ExpiresAtUtc <= DateTimeOffset.UtcNow)) registry.Previews.TryRemove(expired.Key, out _);
            if (registry.Previews.Count >= 4096) return false;
            var evidence = new CommandPreviewEvidence(invocation.PageId, invocation.Target.AuthorityChecksum, bytes.ToArray(), expiry);
            return registry.Previews.TryAdd(key, evidence) || registry.Previews.TryGetValue(key, out CommandPreviewEvidence? prior) &&
                StringComparer.Ordinal.Equals(prior.PageId, evidence.PageId) && BaseStudioSha256.FixedTimeEquals(prior.TargetChecksum, evidence.TargetChecksum) &&
                System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(prior.CanonicalBytes, evidence.CanonicalBytes) && prior.ExpiresAtUtc == evidence.ExpiresAtUtc;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException) { return false; }
    }

    private static string PreviewKey(BaseStudioSessionObservation session, string commandId, BaseStudioSha256 preview)
        => session.PrincipalGeneration.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToHexString(session.SessionChecksum.ToArray()) + ":" + commandId + ":" + Convert.ToHexString(preview.ToArray());

    internal static bool TryAuthorizeFreshAuthentication(BaseStudioCommandAuthorityRegistry registry, BaseStudioSessionObservation session, string commandId,
        BaseStudioResourceIdentity target, BaseStudioSha256 preview, out DateTimeOffset previewExpiresAtUtc)
    {
        previewExpiresAtUtc = default;
        if (!registry.Previews.TryGetValue(PreviewKey(session, commandId, preview), out CommandPreviewEvidence? evidence) ||
            evidence.ExpiresAtUtc <= DateTimeOffset.UtcNow || !BaseStudioSha256.FixedTimeEquals(evidence.TargetChecksum, target.AuthorityChecksum)) return false;
        previewExpiresAtUtc = evidence.ExpiresAtUtc; return true;
    }

    private static bool TryDecodeResource(JsonElement value, out BaseStudioResourceIdentity? resource)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value.GetRawText());
        string token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return BaseStudioResourceRouteToken.TryDecode(token, out resource) && resource is not null;
    }

    private static void ReleaseBeforeInfluence(BaseStudioCommandAuthorityRegistry registry, CommandExecutionIdentity execution)
    { registry.Executions.TryRemove(new KeyValuePair<string, CommandExecutionIdentity>(execution.Key, execution));
      BaseStudioAuthenticationEndpoints.RestoreFreshAuthorityBeforeInfluence(registry, execution.ProtectedAuthority); }

    private static async Task RetainCommandOutcomeAsync(BaseStudioCommandAuthorityRegistry registry, Task<BaseStudioCanonicalJson?> task, CommandExecutionIdentity execution,
        BaseStudioEndpointContract endpoint, ImmutableArray<BaseStudioNamedTypeContract> types)
    {
        try
        {
            BaseStudioCanonicalJson? result = await task.ConfigureAwait(false);
            if (result is null) { ReleaseBeforeInfluence(registry, execution); return; }
            registry.Previews.TryRemove(execution.PreviewKey, out _);
            byte[] bytes = result.ToArray();
            if (bytes.LongLength > endpoint.MaximumResultBytes) return;
            BaseStudioL41JsonValidator.Require(result, endpoint.ResultNodeId, types);
            registry.Outcomes.TryAdd(execution.Key, new CommandOutcome(execution.CommandId, execution.TargetChecksum, execution.PreviewChecksum,
                execution.RequestChecksum, execution.SessionChecksum, execution.ProtectedScopeChecksum, bytes, execution.RetainThroughUtc));
            registry.Previews.TryRemove(execution.PreviewKey, out _);
        }
        catch (BaseStudioCommandFailedBeforeInfluenceException) { ReleaseBeforeInfluence(registry, execution); }
        catch { registry.Previews.TryRemove(execution.PreviewKey, out _); }
    }

    private static void ApplyCommandFailure(HttpResponse response, BaseStudioBootstrapSnapshot snapshot, int statusCode, string code)
    {
        ApplyFrameworkFailure(response, statusCode, snapshot); response.ContentType = "application/json; charset=utf-8";
        response.Headers["X-HPD-Studio-Error"] = code;
    }

    private static void ReclaimExpired(BaseStudioCommandAuthorityRegistry registry)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var expired in registry.Outcomes.Where(item => item.Value.RetainThroughUtc <= now)) registry.Outcomes.TryRemove(expired.Key, out _);
        foreach (var expired in registry.Executions.Where(item => item.Value.RetainThroughUtc <= now)) registry.Executions.TryRemove(expired.Key, out _);
    }

    private static async Task<BaseStudioBootstrapRequest?> ReadBootstrapRequestAsync(HttpContext context)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json") &&
            !StringComparer.OrdinalIgnoreCase.Equals(context.Request.ContentType, "application/json; charset=utf-8"))
        { context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType; BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response); return null; }
        if (context.Request.ContentLength is null or < 2)
        {
            context.Response.StatusCode = StatusCodes.Status411LengthRequired;
            BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
            return null;
        }
        if (context.Request.ContentLength is > MaximumBootstrapRequestBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
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
            BaseStudioSha256 shell = ParseChecksum(root.GetProperty("shellContractChecksum"));
            BaseStudioSha256 assets = ParseChecksum(root.GetProperty("editionAssetGraphChecksum"));
            BaseStudioSha256 runtime = ParseChecksum(root.GetProperty("runtimeClientChecksum"));
            string locale = root.GetProperty("locale").GetString() ?? throw new JsonException();
            JsonElement capabilities = root.GetProperty("clientCapabilities");
            if (capabilities.ValueKind != JsonValueKind.Array) throw new JsonException();
            ImmutableArray<BaseStudioBrowserCapability> values = capabilities.EnumerateArray()
                .Select(static value => (BaseStudioBrowserCapability)value.GetByte()).ToImmutableArray();
            return BaseStudioBootstrapRequest.Create(shell, assets, runtime, locale, values);
        }
        catch (InvalidDataException)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
            return null;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(context.Response);
            return null;
        }
    }

    private static BaseStudioSha256 ParseChecksum(JsonElement value)
    {
        string text = value.GetString() ?? throw new JsonException();
        if (text.Length != 64 || !text.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new JsonException();
        return BaseStudioSha256.FromDigest(Convert.FromHexString(text));
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

    private static Task WriteRouteOrNotFoundAsync(HttpContext context, BaseStudioApplicationGraph graph,
        BaseStudioShellAssetGraph shellAssets, string routePrefix)
    {
        string path = context.Request.RouteValues["studioPath"] as string ?? string.Empty;
        if (!BaseStudioRouteMatcher.Matches(graph, path, context.Request.Query))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
        return WriteShellAsync(context, shellAssets, routePrefix);
    }

    private static Task WriteShellAsync(HttpContext context, BaseStudioShellAssetGraph shellAssets, string routePrefix)
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

    private static void Hex(Utf8JsonWriter json, string name, BaseStudioSha256 checksum)
        => json.WriteString(name, Convert.ToHexString(checksum.ToArray()).ToLowerInvariant());

    private static void ApplyFrameworkFailure(HttpResponse response, int statusCode, BaseStudioBootstrapSnapshot snapshot)
    {
        response.StatusCode = statusCode;
        BaseStudioAuthenticationEndpoints.ApplyProtectedResponseHeaders(response);
        response.Headers["X-HPD-Studio-Response-Authority"] =
            Convert.ToHexString(snapshot.Authority.Checksum.ToArray()).ToLowerInvariant();
    }
}

internal sealed record BaseStudioResolvedAsset(byte[] Content, string ContentType, string ETag);

internal sealed class BaseStudioEditionAssetGraph
{
    private readonly IReadOnlyDictionary<(string ModuleId, long Version, string Path), BaseStudioResolvedAsset> _assets;
    private BaseStudioEditionAssetGraph(BaseStudioSha256 checksum,
        IReadOnlyDictionary<(string ModuleId, long Version, string Path), BaseStudioResolvedAsset> assets)
    { Checksum = checksum; _assets = assets; }
    internal BaseStudioSha256 Checksum { get; }
    internal ImmutableArray<BaseStudioEditionModuleAssetContribution> Modules { get; private init; }

    internal static BaseStudioEditionAssetGraph Create(ImmutableArray<BaseStudioEditionModuleAssetContribution> catalog, BaseStudioShellContract shell)
    {
        var assets = new Dictionary<(string, long, string), BaseStudioResolvedAsset>();
        foreach (BaseStudioEditionModuleAssetContribution module in catalog)
        {
            if (!BaseStudioSha256.FixedTimeEquals(module.Asset.ShellContractChecksum, shell.Checksum))
                throw new InvalidOperationException("A Studio module targets another shell contract.");
            foreach (BaseStudioAssetEntry entry in module.Asset.Assets)
            {
                byte[] content = module.Asset.GetRequiredContent(entry.Path);
                if (content.LongLength != entry.Length || !BaseStudioSha256.FixedTimeEquals(BaseStudioSha256.Compute(content), entry.Digest))
                    throw new InvalidOperationException("A Studio module asset differs from its manifest.");
                assets.Add((module.ModuleId, module.ModuleVersion, entry.Path),
                    new(content, ContentType(entry.MediaType), $"\"sha256-{Convert.ToHexString(entry.Digest.ToArray()).ToLowerInvariant()}\""));
            }
        }
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.edition-assets.v1", writer =>
        {
            writer.Checksum(shell.Checksum); writer.Count(catalog.Length);
            foreach (BaseStudioEditionModuleAssetContribution module in catalog)
            { writer.String(module.ModuleId); writer.Int32(module.ModuleVersion); writer.Checksum(module.Asset.AssetGraphChecksum); }
        });
        return new(checksum, assets) { Modules = catalog };
    }

    internal bool TryResolve(string? module, long version, string? path, out BaseStudioResolvedAsset asset)
    {
        asset = null!;
        if (string.IsNullOrWhiteSpace(module) || version < 1 || string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal) || path.Contains('\\'))
            return false;
        if (!_assets.TryGetValue((module, version, path), out BaseStudioResolvedAsset? resolved))
            return false;
        asset = resolved;
        return true;
    }

    private static string ContentType(BaseStudioAssetMediaType type) => type switch
    {
        BaseStudioAssetMediaType.JavaScriptModule => "text/javascript; charset=utf-8",
        BaseStudioAssetMediaType.Css => "text/css; charset=utf-8",
        BaseStudioAssetMediaType.Svg => "image/svg+xml",
        BaseStudioAssetMediaType.Woff2 => "font/woff2",
        BaseStudioAssetMediaType.Png => "image/png",
        BaseStudioAssetMediaType.Json => "application/json; charset=utf-8",
        _ => throw new InvalidOperationException("A Studio asset media type is unsupported."),
    };
}

internal static class BaseStudioRouteMatcher
{
    internal static bool Matches(BaseStudioApplicationGraph graph, string path, IQueryCollection query)
    {
        string[] segments = string.IsNullOrEmpty(path) ? [] : path.Split('/', StringSplitOptions.None);
        return graph.Modules.SelectMany(static module => module.Pages).Any(page => Matches(page.Route, segments, query));
    }

    private static bool Matches(BaseStudioRouteTemplate route, string[] values, IQueryCollection query)
    {
        if (route.Segments.Length != values.Length || query.Keys.Any(key => route.Query.All(member => !StringComparer.Ordinal.Equals(member.Name, key))))
            return false;
        for (int index = 0; index < values.Length; index++)
        {
            BaseStudioRouteSegment segment = route.Segments[index];
            if (segment.Kind == BaseStudioRouteSegmentKind.Literal)
            { if (!StringComparer.Ordinal.Equals(segment.Value, values[index])) return false; }
            else if (!Valid(values[index], segment.Codec!.Value, [])) return false;
        }
        foreach (BaseStudioQueryParameter member in route.Query)
        {
            bool present = query.TryGetValue(member.Name, out var value);
            if (member.Required != present && member.Required || present && (value.Count != 1 || !Valid(value[0]!, member.Codec, member.RegisteredValues)))
                return false;
        }
        return true;
    }

    private static bool Valid(string value, BaseStudioRouteCodec codec, ImmutableArray<string> registered) => codec switch
    {
        BaseStudioRouteCodec.PositiveLong => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number > 0 && number.ToString(CultureInfo.InvariantCulture) == value,
        BaseStudioRouteCodec.NonnegativeLong => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number >= 0 && number.ToString(CultureInfo.InvariantCulture) == value,
        BaseStudioRouteCodec.Sha256 => value.Length == 64 && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f'),
        BaseStudioRouteCodec.RegisteredEnum or BaseStudioRouteCodec.SelectedTab => registered.Contains(value, StringComparer.Ordinal),
        _ => value.Length is >= 1 and <= 256 && value.All(static character => !char.IsControl(character) && character != '/'),
    };
}
