using HPD.Agent;
using HPD.Agent.AspNetCore;
using HPD.Agent.Planning;
using HPD.Agent.Secrets;
using HPDOS.Core.Auth;
using HPDOS.Harneses;
using HPDOS.Core.Auth.Providers;
using HPDOS.Core.Shell;
using HPDOS.Core.Shell.ExternalApps;
using HPDOS.Shell.Shell;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

namespace HPDOS.Shell.Server;

public static class KestrelHostBuilder
{
    public static WebApplication Build(int port, string[] args)
    {
        // WebRootPath must be set via WebApplicationOptions at construction time —
        // builder.WebHost.UseWebRoot() throws NotSupportedException on CreateSlimBuilder.
        // StaticWebAssetsLoader is NOT used (requires Microsoft.NET.Sdk.Web, incompatible
        // with the MAUI SDK). wwwroot/ is a flat Bun build output; PhysicalFileProvider suffices.
        // On macCatalyst, BundleResource items land in Contents/Resources/ while
        // AppContext.BaseDirectory points at Contents/MonoBundle/. Check both.
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!Directory.Exists(wwwroot) || !Directory.EnumerateFiles(wwwroot).Any())
        {
            var resources = Path.Combine(AppContext.BaseDirectory, "..", "Resources", "wwwroot");
            if (Directory.Exists(resources))
                wwwroot = resources;
        }
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            WebRootPath = wwwroot,
        });

        // Logging levels are driven by appsettings.json (Logging section).
        // HPD.Agent logs at Debug; ASP.NET/Kestrel/Microsoft stay at Warning to avoid noise.
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

        // Load appsettings.json from the binary's output directory.
        // CreateSlimBuilder doesn't automatically add appsettings.json like CreateDefaultBuilder does.
        var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(appsettingsPath))
            builder.Configuration.AddJsonFile(appsettingsPath, optional: true, reloadOnChange: false);

        // Auth mode: enabled explicitly via config OR when the server is running in remote/serve
        // mode (i.e. it's reachable over a network and needs to know who's talking to it).
        var authEnabled = builder.Configuration.GetValue<bool>("Auth:Enabled") || ShellConfig.IsServeMode;
        var jwtSecret   = builder.Configuration.GetValue<string>("Auth:JwtSecret") ?? "";
        ShellConfig.AuthEnabled = authEnabled;

        // CORS — allow the Bun dev server origin in local (non-serve) mode.
        // In production Kestrel serves the SPA itself so there's no cross-origin request.
        // In serve mode the server is public; only same-origin requests are expected.
        if (!authEnabled)
        {
            builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
                p.WithOrigins("http://localhost:5174", "http://localhost:5173")
                 .AllowAnyMethod()
                 .AllowAnyHeader()));
        }

        // Host filtering.
        // Local mode: only localhost — DNS rebinding defence, no external access.
        // Remote/serve mode: allow any host — the server is public and sits behind a
        // reverse proxy that handles TLS and its own host validation.
        builder.Services.AddHostFiltering(options =>
        {
            options.AllowedHosts = authEnabled ? ["*"] : ["localhost", "127.0.0.1", "[::1]"];
            options.AllowEmptyHosts = false;
        });
        // Provider credential store — persists LLM API keys/OAuth tokens to HpdosDataPaths.Root/providers.json.
        // Resolution order: providers.json (with OAuth refresh) → env vars → appsettings.json
        builder.Services.AddSingleton<AuthStorage>();
        builder.Services.AddSingleton<AuthManager>();
        builder.Services.AddSingleton<UserPreferencesStore>();
        builder.Services.AddSingleton<ExternalAppLauncher>();

        // Register HPDOS-local types with the Minimal API JSON serializer.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>, HpdosJsonOptionsSetup>());

        // Lazy boxes — filled after app.Build() so ConfigureAgent can reference the built singletons.
        var secretResolverBox = new Lazy<HPD.Agent.Secrets.ISecretResolver>[1];
        var serviceProviderBox = new IServiceProvider?[1];

        builder.Services.AddHPDAgent(options =>
        {
            options.SessionStorePath = HpdosDataPaths.Sessions;
            options.PersistAfterTurn = true;
            options.DefaultAgentConfig = new HPD.Agent.AgentConfig
            {
                Name = "HPDOS Agent",
                Provider = new HPD.Agent.ProviderConfig
                {
                    ProviderKey = "openrouter",
                    ModelName = "google/gemini-3.1-flash-lite-preview",
                },


            };
            options.ConfigureAgent = agentBuilder =>
            {
                var resolver = secretResolverBox[0]?.Value;
                if (resolver != null)
                    agentBuilder.WithSecretResolver(resolver);

                var sp = serviceProviderBox[0];
                if (sp != null)
                    agentBuilder.WithServiceProvider(sp);

                agentBuilder.WithHarness<MathHarness>();
                agentBuilder.WithHarness<PingHarness>();
                agentBuilder.WithHarness<CodingHarness>();
                agentBuilder.WithPermissions();
                agentBuilder.WithPlanMode();
                agentBuilder.WithLogging();
                // Optional: Enable iteration limits with user permission requests.
                // When enabled, after reaching MaxAgenticIterations (default 10), the agent asks for permission to continue.
                // Uncomment the line below to enable:
                // agentBuilder.WithMiddleware(new ContinuationPermissionMiddleware(maxIterations: 15, extensionAmount: 3));
            };
        });

        var app = builder.Build();

        serviceProviderBox[0] = app.Services;

        // Build and cache the chained secret resolver now that the DI container is ready.
        secretResolverBox[0] = new Lazy<HPD.Agent.Secrets.ISecretResolver>(() =>
            new HPD.Agent.Secrets.ChainedSecretResolver(
                new AuthStorageSecretResolver(
                    app.Services.GetRequiredService<AuthStorage>(),
                    app.Services.GetRequiredService<AuthManager>()),
                new HPD.Agent.Secrets.EnvironmentSecretResolver(),
                new HPD.Agent.Secrets.ConfigurationSecretResolver(app.Configuration)));

        // CORS must run before UseHostFiltering so the header is present even on error responses.
        // Without this, a 500 from any endpoint causes the browser to report a CORS error instead.
        if (!authEnabled)
            app.UseCors();

        app.UseHostFiltering();              // rejects bad Host before routing runs

        // Reconstruct the correct public URL when running behind a reverse proxy
        // (Caddy, Nginx, load balancer). No-op when running locally with no proxy.
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedHost
                             | ForwardedHeaders.XForwardedProto
        });

        // CSP: block external resources; 'unsafe-inline' required for Svelte's scoped style injection.
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; " +
                "connect-src 'self' http://127.0.0.1:* ws://127.0.0.1:*; " +
                "frame-src http://127.0.0.1:*; " +
                "font-src 'self'; " +
                "frame-ancestors 'none'";
            await next();
        });

        // Agent API — anonymous (auth removed)
        var agentApi = app.MapHPDAgentApi();

        // External app launch endpoints
        app.MapPost("/api/apps/{appId}/launch",
            (string appId, ExternalAppLaunchRequest req, ExternalAppLauncher launcher) =>
                LaunchExternalAppAsync(appId, req, launcher));

        app.MapDelete("/api/apps/{appId}",
            (string appId, ExternalAppLauncher launcher) =>
            {
                launcher.Stop(appId);
                return TypedResults.Ok();
            });

        // Health check — used by remote clients to verify the server is reachable before saving.
        app.MapGet("/api/status", Ok<StatusResponse> () => TypedResults.Ok(new StatusResponse("ok")));

        // Remote server config — read and write the remote URL preference.
        // Persistence is delegated to ShellConfig.SaveRemoteUrl (wired by MauiProgram
        // to Preferences API) so this code has no MAUI dependency.
        var remoteGet = app.MapGet("/api/config/remote", Ok<RemoteConfigResponse> () =>
            TypedResults.Ok(new RemoteConfigResponse(ShellConfig.RemoteServerUrl)));

        var remotePost = app.MapPost("/api/config/remote", Ok (RemoteConfigRequest req) =>
        {
            ShellConfig.RemoteServerUrl = string.IsNullOrWhiteSpace(req.Url) ? null : req.Url;
            ShellConfig.SaveRemoteUrl?.Invoke(ShellConfig.RemoteServerUrl);
            return TypedResults.Ok();
        });

        // Auth endpoints removed
        if (false)
        {
            // remoteGet.RequireAuthorization();
            // remotePost.RequireAuthorization();
        }

        // Holds in-progress NeedsUserInput continuations keyed by "{providerId}:{methodIndex}".
        // Required for OAuth manual-code flows (e.g. Anthropic) where the codeVerifier lives in
        // the closure from StartFlow — re-running StartFlow would generate a new verifier and
        // reopen the browser. The entry is consumed on /login/complete and evicted after 10 minutes.
        var pendingFlows = new System.Collections.Concurrent.ConcurrentDictionary<string, (AuthFlowResult.NeedsUserInput Flow, DateTime Expires)>();

        // Provider credential management — list, check status, logout, and start login flows.
        // In auth-enabled mode all provider endpoints require a valid JWT.
        var providerList = app.MapGet("/api/providers", (AuthManager auth) =>
            GetProvidersAsync(auth));

        var providerStatus = app.MapGet("/api/providers/{id}", (string id, AuthManager auth) =>
            GetProviderByIdAsync(id, auth));

        var providerMethods = app.MapGet("/api/providers/{id}/methods",
            (string id, AuthManager auth) =>
                GetProviderMethodsAsync(id, auth));

        var providerLogout = app.MapDelete("/api/providers/{id}", (string id, AuthManager auth) =>
            LogoutProviderAsync(id, auth));

        // Remove a specific stored entry by ID.
        var providerEntryDelete = app.MapDelete("/api/providers/{id}/entries/{entryId}",
            (string id, string entryId, AuthManager auth) =>
                RemoveProviderEntryAsync(id, entryId, auth));

        // Promote a stored entry to active.
        var providerSetActive = app.MapPut("/api/providers/{id}/active",
            (string id, SetActiveRequest req, AuthManager auth) =>
                SetActiveAsync(id, req, auth));

        // Login — starts an auth flow. Returns either a success (entry stored), a pending action
        // (device code: show code + URL to user), or a needs-input (manual code: return input URL).
        // The method index to use is passed as ?method=N (defaults to 0 = recommended method).
        var providerLogin = app.MapPost("/api/providers/{id}/login",
            (string id, int? method, AuthManager auth) =>
                ProviderLoginAsync(id, method, auth, pendingFlows));

        // Complete a NeedsUserInput flow (e.g. paste Anthropic auth code or an API key).
        var providerLoginComplete = app.MapPost("/api/providers/{id}/login/complete",
            (string id, int? method, ProviderLoginCompleteRequest req, AuthManager auth) =>
                ProviderLoginCompleteAsync(id, method, req, auth, pendingFlows));

        var providerModels = app.MapGet("/api/providers/{id}/models",
            (string id, bool? live, string? filter, AuthManager auth, AuthStorage storage) =>
                GetProviderModelsAsync(id, live, filter, auth, storage));

        var defaultsGet = app.MapGet("/api/defaults",
            (UserPreferencesStore prefs) =>
                GetDefaultsAsync(prefs));

        var defaultsPatch = app.MapPatch("/api/defaults",
            (DefaultsRequest req, UserPreferencesStore prefs) =>
                SetDefaultsAsync(req, prefs));

        // Auth requirements removed

        app.UseStaticFiles();                // serves wwwroot/ via PhysicalFileProvider
        app.MapFallbackToFile("index.html"); // SPA fallback

        app.Urls.Add($"http://localhost:{port}");
        return app;
    }

    private static async Task<Results<Ok<List<HPDOS.Core.Auth.AuthSummary>>, ValidationProblem>> GetProvidersAsync(AuthManager auth)
    {
        try
        {
            return TypedResults.Ok(await auth.GetAuthSummaryAsync());
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["GetProvidersError"] = [ex.Message] });
        }
    }

    private static Task<Results<Ok<List<AuthMethodInfoResponse>>, NotFound<ErrorResponse>, ValidationProblem>> GetProviderMethodsAsync(string id, AuthManager auth)
    {
        try
        {
            var provider = auth.GetProvider(id);
            if (provider is null) return Task.FromResult<Results<Ok<List<AuthMethodInfoResponse>>, NotFound<ErrorResponse>, ValidationProblem>>(
                TypedResults.NotFound(new ErrorResponse($"Unknown provider: {id}")));
            var methods = provider.Methods.Select((m, i) => new AuthMethodInfoResponse(i, m.Label, m.Description, m.IsRecommended)).ToList();
            return Task.FromResult<Results<Ok<List<AuthMethodInfoResponse>>, NotFound<ErrorResponse>, ValidationProblem>>(
                TypedResults.Ok(methods));
        }
        catch (Exception ex)
        {
            return Task.FromResult<Results<Ok<List<AuthMethodInfoResponse>>, NotFound<ErrorResponse>, ValidationProblem>>(
                TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["GetProviderMethodsError"] = [ex.Message] }));
        }
    }

    private static async Task<Results<Ok<HPDOS.Core.Auth.AuthSummary>, NotFound, ValidationProblem>> GetProviderByIdAsync(string id, AuthManager auth)
    {
        try
        {
            var summaries = await auth.GetAuthSummaryAsync();
            var summary = summaries.FirstOrDefault(s => s.ProviderId.Equals(id, StringComparison.OrdinalIgnoreCase));
            return summary is null ? TypedResults.NotFound() : TypedResults.Ok(summary);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["GetProviderError"] = [ex.Message] });
        }
    }

    private static async Task<Results<Ok, NotFound, ValidationProblem>> LogoutProviderAsync(string id, AuthManager auth)
    {
        try
        {
            var removed = await auth.Storage.RemoveAsync(id);
            return removed ? TypedResults.Ok() : TypedResults.NotFound();
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["LogoutError"] = [ex.Message] });
        }
    }

    private static async Task<Results<Ok, NotFound, ValidationProblem>> RemoveProviderEntryAsync(string id, string entryId, AuthManager auth)
    {
        try
        {
            var removed = await auth.Storage.RemoveEntryAsync(id, entryId);
            return removed ? TypedResults.Ok() : TypedResults.NotFound();
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["RemoveEntryError"] = [ex.Message] });
        }
    }

    private static async Task<Results<Ok, NotFound, ValidationProblem>> SetActiveAsync(string id, SetActiveRequest req, AuthManager auth)
    {
        try
        {
            var ok = await auth.Storage.SetActiveAsync(id, req.EntryId);
            return ok ? TypedResults.Ok() : TypedResults.NotFound();
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["SetActiveError"] = [ex.Message] });
        }
    }

    private static async Task<Results<Ok<AuthStoreResponse>, Ok<PendingActionResponse>, Ok<NeedsInputResponse>, BadRequest<ErrorResponse>, NotFound<ErrorResponse>>> ProviderLoginAsync(
        string id, int? method, AuthManager auth, System.Collections.Concurrent.ConcurrentDictionary<string, (AuthFlowResult.NeedsUserInput Flow, DateTime Expires)> pendingFlows)
    {
        try
        {
            var provider = auth.GetProvider(id);
            if (provider is null) return TypedResults.NotFound(new ErrorResponse($"Unknown provider: {id}"));

            var methodIndex = method ?? 0;
            if (methodIndex < 0 || methodIndex >= provider.Methods.Count)
                return TypedResults.BadRequest(new ErrorResponse($"Method index {methodIndex} out of range"));

            var authMethod = provider.Methods[methodIndex];
            var result = await authMethod.StartFlow(CancellationToken.None);

            var flowKey = $"{id.ToLowerInvariant()}:{methodIndex}";

            // Evict any expired pending flows on each login start.
            var now = DateTime.UtcNow;
            foreach (var k in pendingFlows.Keys.ToArray())
                if (pendingFlows.TryGetValue(k, out var v) && v.Expires < now)
                    pendingFlows.TryRemove(k, out _);

            return result switch
            {
                AuthFlowResult.Success s => await KestrelHostBuilderHelpers.StoreAndOkAsync(auth, id,
                    s.Entry with { MethodLabel = authMethod.Label }),
                AuthFlowResult.PendingUserAction p => TypedResults.Ok(
                    new PendingActionResponse("pending", p.Message, p.Url, p.UserCode)),
                AuthFlowResult.NeedsUserInput n => KestrelHostBuilderHelpers.StoreAndReturnNeedsInput(pendingFlows, flowKey, n),
                AuthFlowResult.Cancelled => TypedResults.BadRequest(new ErrorResponse("Cancelled")),
                AuthFlowResult.Failed f => TypedResults.BadRequest(new ErrorResponse(f.Error)),
                _ => throw new InvalidOperationException("Unexpected auth flow result")
            };
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    private static async Task<Results<Ok<AuthStoreResponse>, BadRequest<ErrorResponse>, NotFound<ErrorResponse>>> ProviderLoginCompleteAsync(
        string id, int? method, ProviderLoginCompleteRequest req, AuthManager auth, System.Collections.Concurrent.ConcurrentDictionary<string, (AuthFlowResult.NeedsUserInput Flow, DateTime Expires)> pendingFlows)
    {
        try
        {
            var provider = auth.GetProvider(id);
            if (provider is null) return TypedResults.NotFound(new ErrorResponse($"Unknown provider: {id}"));

            var methodIndex = method ?? 0;
            if (methodIndex < 0 || methodIndex >= provider.Methods.Count)
                return TypedResults.BadRequest(new ErrorResponse($"Method index {methodIndex} out of range"));

            var flowKey = $"{id.ToLowerInvariant()}:{methodIndex}";

            // Use stored continuation if present (OAuth manual-code flows carry a codeVerifier in
            // the closure — re-running StartFlow would generate a new verifier and reopen the browser).
            AuthFlowResult.NeedsUserInput needsInput;
            if (pendingFlows.TryRemove(flowKey, out var pending) && pending.Expires >= DateTime.UtcNow)
            {
                needsInput = pending.Flow;
            }
            else
            {
                // No stored state (e.g. API key flows are stateless) — re-run StartFlow.
                var authMethod = provider.Methods[methodIndex];
                var startResult = await authMethod.StartFlow(CancellationToken.None);
                if (startResult is not AuthFlowResult.NeedsUserInput ni)
                    return TypedResults.BadRequest(new ErrorResponse("Flow does not require input at this stage"));
                needsInput = ni;
            }

            var methodLabel = provider.Methods[methodIndex].Label;
            var result = await needsInput.CompleteWithInput(req.Input, CancellationToken.None);
            return result switch
            {
                AuthFlowResult.Success s => await KestrelHostBuilderHelpers.StoreAndOkAsync(auth, id,
                    s.Entry with { MethodLabel = methodLabel }),
                AuthFlowResult.Cancelled => TypedResults.BadRequest(new ErrorResponse("Cancelled")),
                AuthFlowResult.Failed f => TypedResults.BadRequest(new ErrorResponse(f.Error)),
                _ => throw new InvalidOperationException("Unexpected auth flow result")
            };
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    private static async Task<Results<Ok<ModelInfo[]>, NotFound, ValidationProblem>> GetProviderModelsAsync(
        string id, bool? live, string? filter, AuthManager auth, AuthStorage storage)
    {
        try
        {
            var provider = auth.GetProvider(id);
            if (provider is null) return TypedResults.NotFound();

            // live=true: fetch full model list from provider API
            if (live == true && provider is ILiveModelProvider liveProvider)
            {
                var entry = await storage.GetAsync(id);
                var liveModels = await liveProvider.FetchModelsAsync(entry);
                // Always exclude models without tool call support
                var toolModels = liveModels.Where(m => m.SupportsTools);
                // filter=free: also restrict to free models
                if (string.Equals(filter, "free", StringComparison.OrdinalIgnoreCase))
                    return TypedResults.Ok(toolModels.Where(m => m.IsFree).ToArray());
                return TypedResults.Ok(toolModels.ToArray());
            }

            // Default: return curated static list from IModelProvider
            var models = provider is IModelProvider mp ? mp.GetModels().ToArray() : Array.Empty<ModelInfo>();
            return TypedResults.Ok(models);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["GetProviderModelsError"] = [ex.Message] });
        }
    }

    private static async Task<Results<Ok<DefaultsResponse>, ValidationProblem>> GetDefaultsAsync(UserPreferencesStore prefs)
    {
        try
        {
            var p = await prefs.GetAsync();
            return TypedResults.Ok(new DefaultsResponse(
                p.DefaultProvider ?? "openrouter",
                p.DefaultModel ?? "google/gemini-3.1-flash-lite-preview"));
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["GetDefaultsError"] = [ex.Message] });
        }
    }

    private static async Task<Results<Ok, ValidationProblem>> SetDefaultsAsync(DefaultsRequest req, UserPreferencesStore prefs)
    {
        try
        {
            await prefs.SetAsync(new UserPreferences
            {
                DefaultProvider = req.ProviderKey,
                DefaultModel = req.ModelId
            });
            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["SetDefaultsError"] = [ex.Message] });
        }
    }

    private static async Task<Results<Ok<ExternalAppLaunchResponse>, ValidationProblem>> LaunchExternalAppAsync(
        string appId, ExternalAppLaunchRequest req, ExternalAppLauncher launcher)
    {
        try
        {
            var url = await launcher.LaunchAsync(
                appId,
                req.Executable,
                port => [.. req.Args.Select(a => a.Replace("{port}", port.ToString()))],
                port => req.UrlTemplate.Replace("{port}", port.ToString()),
                req.TimeoutSeconds.HasValue ? TimeSpan.FromSeconds(req.TimeoutSeconds.Value) : null);

            return TypedResults.Ok(new ExternalAppLaunchResponse(url));
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["LaunchError"] = [ex.Message] });
        }
    }
}

// Request types
internal record RemoteConfigRequest(string? Url);
internal record ExternalAppLaunchRequest(string Executable, string[] Args, string UrlTemplate, int? TimeoutSeconds);
internal record ProviderLoginCompleteRequest(string Input);
internal record DefaultsRequest(string? ProviderKey, string? ModelId);
internal record SetActiveRequest(string EntryId);

// Response types (named so they can be registered with the source-gen JSON context)
internal record StatusResponse(string Status);
internal record ErrorResponse(string Error);
internal record RemoteConfigResponse(string? RemoteServerUrl);
internal record AuthStoreResponse(string Status, string Source);
internal record NeedsInputResponse(string Status, string Prompt, string? InputLabel);
internal record PendingActionResponse(string Status, string? Message, string? Url, string? UserCode);
internal record AuthMethodInfoResponse(int Index, string Label, string? Description, bool IsRecommended);
internal record DefaultsResponse(string ProviderKey, string ModelId);
internal record ExternalAppLaunchResponse(string Url);

/// <summary>
/// Registers HPDOS-local request/response types with the Minimal API JSON serializer.
/// Inserted into the TypeInfoResolverChain alongside the agent framework contexts.
/// </summary>
internal class HpdosJsonOptionsSetup : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options) =>
        options.SerializerOptions.TypeInfoResolverChain.Add(HpdosJsonContext.Default);
}

[JsonSerializable(typeof(ExternalAppLaunchRequest))]
[JsonSerializable(typeof(ExternalAppLaunchResponse))]
[JsonSerializable(typeof(SetActiveRequest))]
[JsonSerializable(typeof(RemoteConfigRequest))]
[JsonSerializable(typeof(ProviderLoginCompleteRequest))]
[JsonSerializable(typeof(DefaultsRequest))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(RemoteConfigResponse))]
[JsonSerializable(typeof(AuthStoreResponse))]
[JsonSerializable(typeof(NeedsInputResponse))]
[JsonSerializable(typeof(PendingActionResponse))]
[JsonSerializable(typeof(AuthMethodInfoResponse))]
[JsonSerializable(typeof(List<AuthMethodInfoResponse>))]
[JsonSerializable(typeof(DefaultsResponse))]
[JsonSerializable(typeof(HPDOS.Core.Auth.AuthSummary))]
[JsonSerializable(typeof(List<HPDOS.Core.Auth.AuthSummary>))]
[JsonSerializable(typeof(HPDOS.Core.Auth.StoredEntryInfo))]
[JsonSerializable(typeof(List<HPDOS.Core.Auth.StoredEntryInfo>))]
[JsonSerializable(typeof(HPDOS.Core.Auth.ModelInfo))]
[JsonSerializable(typeof(HPDOS.Core.Auth.ModelInfo[]))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Http.HttpValidationProblemDetails))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class HpdosJsonContext : JsonSerializerContext { }

internal static class KestrelHostBuilderHelpers
{
    public static async Task<Ok<AuthStoreResponse>> StoreAndOkAsync(AuthManager auth, string providerId, AuthEntry entry)
    {
        await auth.Storage.SetAsync(providerId, entry);
        return TypedResults.Ok(new AuthStoreResponse("ok", entry is OAuthEntry ? "oauth" : "api"));
    }

    public static Ok<NeedsInputResponse> StoreAndReturnNeedsInput(
        System.Collections.Concurrent.ConcurrentDictionary<string, (AuthFlowResult.NeedsUserInput, DateTime)> pendingFlows,
        string flowKey,
        AuthFlowResult.NeedsUserInput flow)
    {
        pendingFlows[flowKey] = (flow, DateTime.UtcNow.AddMinutes(10));
        return TypedResults.Ok(new NeedsInputResponse("needs_input", flow.Prompt, flow.InputLabel));
    }
}
