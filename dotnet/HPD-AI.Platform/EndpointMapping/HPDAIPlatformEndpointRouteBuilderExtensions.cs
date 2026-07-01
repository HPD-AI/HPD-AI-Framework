using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.AI.Platform;

public static class HPDAIPlatformEndpointRouteBuilderExtensions
{
    private const string ResourcePrefix = "HPD.AI.Platform.wwwroot.";
    private static readonly Assembly Assembly = typeof(HPDAIPlatformEndpointRouteBuilderExtensions).Assembly;
    private static readonly IReadOnlySet<string> ResourceNames = Assembly
        .GetManifestResourceNames()
        .Where(static name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        .ToHashSet(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".css"] = "text/css; charset=utf-8",
        [".html"] = "text/html; charset=utf-8",
        [".ico"] = "image/x-icon",
        [".js"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml",
        [".txt"] = "text/plain; charset=utf-8",
        [".webmanifest"] = "application/manifest+json; charset=utf-8",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2"
    };

    public static RouteGroupBuilder MapHPDAIPlatform(this IEndpointRouteBuilder endpoints)
        => endpoints.MapHPDAIPlatform(static _ => { });

    public static RouteGroupBuilder MapHPDAIPlatform(
        this IEndpointRouteBuilder endpoints,
        string routePrefix,
        string apiBasePath = "/api/hpd")
        => endpoints.MapHPDAIPlatform(options =>
        {
            options.RoutePrefix = routePrefix;
            options.ApiBasePath = apiBasePath;
        });

    public static RouteGroupBuilder MapHPDAIPlatform(
        this IEndpointRouteBuilder endpoints,
        Action<HPDAIPlatformEndpointOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = CreateEndpointOptions(endpoints);
        configure?.Invoke(options);

        var routeGroup = endpoints.MapGroup(NormalizeRoutePrefix(options.RoutePrefix));

        routeGroup.MapGet("/studio-config.js", () =>
            Results.Text(CreateConfigScript(options), "text/javascript; charset=utf-8"))
            .WithName("GetHPDAIPlatformConfig")
            .WithSummary("Get HPD AI Platform runtime configuration");

        routeGroup.MapGet("/", (HttpContext context, CancellationToken ct) =>
                WriteResourceAsync(context, "index.html", fallbackToIndex: false, ct))
            .WithName("GetHPDAIPlatformIndex")
            .WithSummary("Get HPD AI Platform");

        routeGroup.MapGet("/{**assetPath}", (HttpContext context, string? assetPath, CancellationToken ct) =>
                WriteResourceAsync(context, assetPath, fallbackToIndex: true, ct))
            .WithName("GetHPDAIPlatformAsset")
            .WithSummary("Get an HPD AI Platform static asset");

        options.ConfigureRoutes?.Invoke(routeGroup);

        return routeGroup;
    }

    private static HPDAIPlatformEndpointOptions CreateEndpointOptions(IEndpointRouteBuilder endpoints)
    {
        var registered = endpoints.ServiceProvider.GetService<IOptions<HPDAIPlatformOptions>>()?.Value;
        var options = new HPDAIPlatformEndpointOptions();
        if (registered == null)
            return options;

        foreach (var capability in registered.Capabilities)
        {
            options.Capabilities.Add(capability);
        }

        foreach (var module in registered.Modules)
        {
            options.Modules.Add(module);
        }

        return options;
    }

    private static async Task<IResult> WriteResourceAsync(
        HttpContext context,
        string? assetPath,
        bool fallbackToIndex,
        CancellationToken ct)
    {
        var normalizedPath = NormalizeAssetPath(assetPath);
        if (normalizedPath == null)
            return Results.BadRequest();

        var resourceName = ToResourceName(normalizedPath);
        if (!ResourceNames.Contains(resourceName))
        {
            if (!fallbackToIndex || Path.HasExtension(normalizedPath))
                return Results.NotFound();

            normalizedPath = "index.html";
            resourceName = ToResourceName(normalizedPath);
        }

        await using var stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return Results.NotFound();

        context.Response.ContentType = GetContentType(normalizedPath);
        context.Response.Headers.CacheControl = normalizedPath == "index.html"
            ? "no-cache"
            : "public,max-age=31536000,immutable";

        await stream.CopyToAsync(context.Response.Body, ct);
        return Results.Empty;
    }

    private static string CreateConfigScript(HPDAIPlatformEndpointOptions options)
    {
        var apiBasePath = JavaScriptEncode(NormalizePathBase(options.ApiBasePath));
        var routePrefix = JavaScriptEncode(NormalizeRoutePrefix(options.RoutePrefix));
        var productTitle = JavaScriptEncode(options.ProductTitle);
        var mode = JavaScriptEncode(options.Mode);
        var capabilities = JavaScriptArray(options.Capabilities);
        var modules = StudioModulesArray(options.Modules);

        return $$"""
            window.HPD_AI_PLATFORM_CONFIG = {
              apiBasePath: "{{apiBasePath}}",
              routePrefix: "{{routePrefix}}",
              productTitle: "{{productTitle}}",
              mode: "{{mode}}",
              capabilities: {{capabilities}},
              studioModules: {{modules}}
            };
            """;
    }

    private static string? NormalizeAssetPath(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return "index.html";

        var normalized = assetPath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.StartsWith("_", StringComparison.Ordinal))
        {
            return null;
        }

        return normalized;
    }

    private static string NormalizeRoutePrefix(string routePrefix)
    {
        if (string.IsNullOrWhiteSpace(routePrefix) || routePrefix == "/")
            return string.Empty;

        return "/" + routePrefix.Trim('/');
    }

    private static string NormalizePathBase(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return string.Empty;

        return "/" + path.Trim('/');
    }

    private static string ToResourceName(string assetPath)
        => ResourcePrefix + assetPath;

    private static string GetContentType(string assetPath)
        => ContentTypes.TryGetValue(Path.GetExtension(assetPath), out var contentType)
            ? contentType
            : "application/octet-stream";

    private static string JavaScriptArray(IEnumerable<string> values)
        => "[" + string.Join(",", values.Select(static value => $"\"{JavaScriptEncode(value)}\"")) + "]";

    private static string StudioModulesArray(IEnumerable<HPDAIPlatformModuleOptions> modules)
        => "[" + string.Join(",", modules.Select(static module =>
            $$"""{"id":"{{JavaScriptEncode(module.Id)}}","label":"{{JavaScriptEncode(module.Label)}}","title":"{{JavaScriptEncode(module.Title)}}","status":"{{JavaScriptEncode(module.Status)}}"}""")) + "]";

    private static string JavaScriptEncode(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
