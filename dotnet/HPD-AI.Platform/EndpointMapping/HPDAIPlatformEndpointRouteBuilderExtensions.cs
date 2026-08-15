using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.AI.Platform;

public static class HPDAIPlatformEndpointRouteBuilderExtensions
{
    private const string ResourcePrefix = "HPD.AI.Platform.wwwroot.";
    private const int MaximumAssetCount = 256;
    private const int MaximumAssetPathLength = 256;
    private const long MaximumAssetBytes = 8 * 1024 * 1024;
    private const long MaximumAssetGraphBytes = 32 * 1024 * 1024;
    private const int MaximumRuntimeEntries = 64;
    private const string ShellContractMarker = "hpd-shell-contract-v1:";
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
    private static readonly IReadOnlyDictionary<string, EmbeddedAsset> Assets = BuildAssets();
    private static readonly string AssetIdentity = ComputeAssetIdentity(Assets.Values);
    private static readonly string ShellContractIdentity = ResolveShellContractIdentity(Assets.Values);

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

        string routePrefix = NormalizeRoutePrefix(options.RoutePrefix);
        RuntimeConfiguration runtime = ValidateOptions(options, routePrefix);
        string configurationScript = CreateConfigScript(runtime);
        IReadOnlySet<string> spaRoutes = runtime.SpaRoutes.ToHashSet(StringComparer.Ordinal);
        var routeGroup = endpoints.MapGroup(routePrefix);

        routeGroup.MapGet("/studio-config.js", (RequestDelegate)(context =>
            WriteGeneratedAsync(context, configurationScript, "text/javascript; charset=utf-8")))
            .WithName("GetHPDAIPlatformConfig")
            .WithSummary("Get HPD AI Platform runtime configuration");

        routeGroup.MapGet("/", (RequestDelegate)(context =>
                WriteIndexOrRedirectAsync(context, routePrefix)))
            .WithName("GetHPDAIPlatformIndex")
            .WithSummary("Get HPD AI Platform");

        routeGroup.MapGet("/{**assetPath}", (RequestDelegate)(context =>
                WriteAssetOrRouteAsync(context, context.Request.RouteValues["assetPath"] as string,
                    spaRoutes, context.RequestAborted)))
            .WithName("GetHPDAIPlatformAsset")
            .WithSummary("Get an HPD AI Platform static asset");

        options.ConfigureRoutes?.Invoke(routeGroup);

        return routeGroup;
    }

    private static Task WriteIndexOrRedirectAsync(HttpContext context, string routePrefix)
    {
        if (context.Request.Path.Equals(routePrefix))
        {
            context.Response.Redirect(routePrefix + "/" + context.Request.QueryString, permanent: false);
            return Task.CompletedTask;
        }

        return WriteResourceAsync(context, "index.html", context.RequestAborted);
    }

    private static HPDAIPlatformEndpointOptions CreateEndpointOptions(IEndpointRouteBuilder endpoints)
    {
        var registered = endpoints.ServiceProvider.GetService<IOptions<HPDAIPlatformOptions>>()?.Value;
        var options = new HPDAIPlatformEndpointOptions();
        if (registered == null)
            return options;

        foreach (var capability in MaterializeBounded(registered.Capabilities, MaximumRuntimeEntries, "capability"))
        {
            options.Capabilities.Add(capability);
        }

        foreach (var module in MaterializeBounded(registered.Modules, MaximumRuntimeEntries, "module"))
        {
            options.Modules.Add(module);
        }

        return options;
    }

    private static async Task WriteAssetOrRouteAsync(
        HttpContext context,
        string? assetPath,
        IReadOnlySet<string> spaRoutes,
        CancellationToken ct)
    {
        var normalizedPath = NormalizeAssetPath(assetPath);
        if (normalizedPath == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!normalizedPath.StartsWith("assets/", StringComparison.Ordinal) || !Assets.ContainsKey(normalizedPath))
        {
            if (spaRoutes.Contains("/" + normalizedPath.TrimEnd('/')))
                await WriteResourceAsync(context, "index.html", ct).ConfigureAwait(false);
            else
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await WriteResourceAsync(context, normalizedPath, ct).ConfigureAwait(false);
    }

    private static async Task WriteResourceAsync(
        HttpContext context,
        string assetPath,
        CancellationToken ct)
    {
        if (!Assets.TryGetValue(assetPath, out EmbeddedAsset? asset))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await using var stream = Assembly.GetManifestResourceStream(asset.ResourceName);
        if (stream == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ApplySecurityHeaders(context.Response);
        context.Response.ContentType = asset.ContentType;
        context.Response.ContentLength = asset.Length;
        context.Response.Headers.ETag = asset.ETag;
        context.Response.Headers["HPD-Studio-Asset-Identity"] = AssetIdentity;
        context.Response.Headers.CacheControl = assetPath == "index.html"
            ? "no-store"
            : IsHashedAsset(assetPath)
                ? "public,max-age=31536000,immutable"
                : "no-cache";

        if (context.Request.Headers.IfNoneMatch.Any(value => StringComparer.Ordinal.Equals(value, asset.ETag)))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            context.Response.ContentLength = null;
            return;
        }

        await stream.CopyToAsync(context.Response.Body, ct);
    }

    private static Task WriteGeneratedAsync(HttpContext context, string value, string contentType)
    {
        ApplySecurityHeaders(context.Response);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["HPD-Studio-Asset-Identity"] = AssetIdentity;
        context.Response.ContentType = contentType;
        return context.Response.WriteAsync(value, context.RequestAborted);
    }

    private static string CreateConfigScript(RuntimeConfiguration options)
    {
        var apiBasePath = JavaScriptEncode(options.ApiBasePath);
        var encodedRoutePrefix = JavaScriptEncode(options.RoutePrefix);
        var productTitle = JavaScriptEncode(options.ProductTitle);
        var mode = JavaScriptEncode(options.Mode);
        var capabilities = JavaScriptArray(options.Capabilities);
        var modules = StudioModulesArray(options.Modules);

        return $$"""
            globalThis.HPD_STUDIO_CONFIG = {
              apiBasePath: "{{apiBasePath}}",
              routePrefix: "{{encodedRoutePrefix}}",
              productTitle: "{{productTitle}}",
              mode: "{{mode}}",
              assetContractVersion: "1",
              assetIdentity: "{{AssetIdentity}}",
              shellContractIdentity: "{{ShellContractIdentity}}",
              capabilities: {{capabilities}},
              studioModules: {{modules}}
            };
            """;
    }

    private static string? NormalizeAssetPath(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || assetPath.Contains('\\'))
            return null;

        var normalized = assetPath.TrimStart('/');
        if (normalized.Length > 256 || normalized.Any(static character => character < 0x21 || character > 0x7e) ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Contains("//", StringComparison.Ordinal) ||
            normalized.StartsWith("_", StringComparison.Ordinal))
        {
            return null;
        }

        return normalized;
    }

    private static string NormalizeRoutePrefix(string routePrefix)
    {
        if (string.IsNullOrWhiteSpace(routePrefix) || routePrefix == "/")
            throw new InvalidOperationException("The Studio route prefix must identify a non-root absolute path.");

        if (!routePrefix.StartsWith("/", StringComparison.Ordinal))
            throw new InvalidOperationException("The Studio route prefix must be absolute.");

        string normalized = "/" + routePrefix.Trim('/');
        if (normalized.Length > 128 || normalized.Any(static character => character < 0x21 || character > 0x7e) ||
            normalized.Contains("//", StringComparison.Ordinal) || normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Contains('\\'))
            throw new InvalidOperationException("The Studio route prefix is invalid.");
        return normalized;
    }

    private static string NormalizePathBase(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            throw new InvalidOperationException("The Studio API path base must identify a non-root absolute path.");

        if (!path.StartsWith("/", StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal) || path.Length > 256 ||
            path.Any(static character => character < 0x21 || character > 0x7e) ||
            path.Contains("//", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal) ||
            path.Contains('\\') || path.Contains('?') || path.Contains('#') ||
            path.Contains("%2f", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("%5c", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Studio API path base is invalid.");

        return path;
    }

    private static string ToResourceName(string assetPath)
        => ResourcePrefix + assetPath;

    private static string GetContentType(string assetPath)
        => ContentTypes.TryGetValue(Path.GetExtension(assetPath), out var contentType)
            ? contentType
            : "application/octet-stream";

    private static RuntimeConfiguration ValidateOptions(HPDAIPlatformEndpointOptions options, string routePrefix)
    {
        string apiBasePath = NormalizePathBase(options.ApiBasePath);
        if (!IsValidNfcText(options.ProductTitle, 256))
            throw new InvalidOperationException("The Studio product title is invalid.");
        if (options.Mode is not ("development" or "read-only"))
            throw new InvalidOperationException("The Studio mode is invalid.");
        string[] capabilities = MaterializeBounded(options.Capabilities, MaximumRuntimeEntries, "capability");
        if (capabilities.Any(static value => value is null ||
                !Regex.IsMatch(value, "^[a-z][a-z0-9.-]{0,127}$", RegexOptions.CultureInvariant)) ||
            capabilities.Distinct(StringComparer.Ordinal).Count() != capabilities.Length)
            throw new InvalidOperationException("The Studio capability catalog is invalid.");
        HPDAIPlatformModuleOptions[] modules = MaterializeBounded(options.Modules, MaximumRuntimeEntries, "module");
        var moduleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (HPDAIPlatformModuleOptions module in modules)
        {
            string? moduleId = module?.Id;
            if (module is null || moduleId is null || !Regex.IsMatch(moduleId, "^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant) ||
                !moduleIds.Add(moduleId) || !IsValidNfcText(module.Label, 128) ||
                !IsValidNfcText(module.Title, 256) || module.Status != "active")
                throw new InvalidOperationException("The Studio module catalog is invalid.");
        }
        string[] routes = MaterializeBounded(options.SpaRoutes, MaximumRuntimeEntries, "SPA route");
        foreach (string route in routes)
        {
            if (!route.StartsWith("/", StringComparison.Ordinal) || route.Length > 256 ||
                route.Any(static character => character < 0x21 || character > 0x7e) ||
                route.Contains("..", StringComparison.Ordinal) || Path.HasExtension(route))
                throw new InvalidOperationException("A Studio SPA route is invalid.");
        }
        return new(apiBasePath, routePrefix, options.ProductTitle, options.Mode, capabilities, modules, routes);
    }

    private static bool IsValidNfcText(string? value, int maximumBytes)
    {
        if (string.IsNullOrEmpty(value) || value.Any(char.IsControl)) return false;
        try
        {
            return value.IsNormalized(NormalizationForm.FormC) &&
                new UTF8Encoding(false, true).GetByteCount(value) <= maximumBytes;
        }
        catch (ArgumentException) { return false; }
    }

    private static T[] MaterializeBounded<T>(IEnumerable<T> values, int maximum, string kind)
    {
        ArgumentNullException.ThrowIfNull(values);
        var materialized = new List<T>(maximum);
        using IEnumerator<T> enumerator = values.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (materialized.Count == maximum)
                throw new InvalidOperationException($"The Studio {kind} catalog exceeds its bound.");
            materialized.Add(enumerator.Current);
        }
        return [.. materialized];
    }

    private static IReadOnlyDictionary<string, EmbeddedAsset> BuildAssets()
    {
        if (ResourceNames.Count is 0 or > MaximumAssetCount)
            throw new InvalidOperationException("The embedded Studio asset count is invalid.");
        var assets = new Dictionary<string, EmbeddedAsset>(StringComparer.Ordinal);
        long total = 0;
        foreach (string resourceName in ResourceNames.Order(StringComparer.Ordinal))
        {
            string path = resourceName[ResourcePrefix.Length..];
            ValidateEmbeddedAssetPath(path);
            string contentType = GetContentType(path);
            if (contentType == "application/octet-stream" || path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The embedded Studio asset type is invalid.");
            using Stream stream = Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("An embedded Studio asset is unavailable.");
            if (stream.Length < 0 || stream.Length > MaximumAssetBytes)
                throw new InvalidOperationException("An embedded Studio asset is too large.");
            total = checked(total + stream.Length);
            if (total > MaximumAssetGraphBytes)
                throw new InvalidOperationException("The embedded Studio asset graph is too large.");
            string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            assets.Add(path, new(resourceName, contentType, stream.Length, $"\"sha256-{hash}\"", hash));
        }
        if (!assets.ContainsKey("index.html") || !assets.Keys.Any(static path => path.StartsWith("assets/", StringComparison.Ordinal)))
            throw new InvalidOperationException("The embedded Studio asset graph is incomplete.");
        return assets;
    }

    internal static void ValidateEmbeddedAssetPath(string path)
    {
        if (path.Length is < 1 or > MaximumAssetPathLength ||
            path.Any(static character => character < 0x21 || character > 0x7e) ||
            path.Contains('\\') || path.Contains("//", StringComparison.Ordinal) ||
            path.Contains("..", StringComparison.Ordinal) || path.StartsWith('/') ||
            path.Split('/').Any(static segment => segment.Length == 0 || segment[0] is '.' or '_') ||
            (path != "index.html" && !path.StartsWith("assets/", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The embedded Studio asset path is invalid.");
        }
    }

    internal static void ValidateEmbeddedAssetGraph(IEnumerable<(string Path, long Length)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        int count = 0;
        long total = 0;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string path, long length) in entries)
        {
            if (++count > MaximumAssetCount)
                throw new InvalidOperationException("The embedded Studio asset count is invalid.");
            ValidateEmbeddedAssetPath(path);
            if (length < 0 || length > MaximumAssetBytes)
                throw new InvalidOperationException("An embedded Studio asset is too large.");
            total = checked(total + length);
            if (total > MaximumAssetGraphBytes)
                throw new InvalidOperationException("The embedded Studio asset graph is too large.");
            if (!paths.Add(path))
                throw new InvalidOperationException("The embedded Studio asset path is duplicated.");
        }
        if (count == 0)
            throw new InvalidOperationException("The embedded Studio asset count is invalid.");
    }

    private static string ResolveShellContractIdentity(IEnumerable<EmbeddedAsset> assets)
    {
        EmbeddedAsset script = assets.SingleOrDefault(static asset =>
            asset.ResourceName[ResourcePrefix.Length..].StartsWith("assets/", StringComparison.Ordinal) &&
            asset.ResourceName.EndsWith(".js", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The embedded Studio shell script is missing.");
        using Stream stream = Assembly.GetManifestResourceStream(script.ResourceName)
            ?? throw new InvalidOperationException("The embedded Studio shell script is unavailable.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        string content = reader.ReadToEnd();
        MatchCollection matches = Regex.Matches(content, ShellContractMarker + "[0-9a-f]{64}", RegexOptions.CultureInvariant);
        if (matches.Count != 1)
            throw new InvalidOperationException("The embedded Studio shell identity is invalid.");
        return matches[0].Value[ShellContractMarker.Length..];
    }

    private static string ComputeAssetIdentity(IEnumerable<EmbeddedAsset> assets)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "hpd-studio-assets-v1");
        foreach (EmbeddedAsset asset in assets.OrderBy(static asset => asset.ResourceName, StringComparer.Ordinal))
        {
            Append(hash, asset.ResourceName[ResourcePrefix.Length..]);
            Append(hash, asset.ContentType);
            Append(hash, asset.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, asset.Hash);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool IsHashedAsset(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int separator = name.IndexOf('-');
        return path.StartsWith("assets/", StringComparison.Ordinal) && separator > 0 &&
            name[(separator + 1)..].Length >= 8 &&
            name[(separator + 1)..].All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }

    private static void ApplySecurityHeaders(HttpResponse response)
    {
        response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";
        response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.XFrameOptions = "DENY";
    }

    private sealed record EmbeddedAsset(string ResourceName, string ContentType, long Length, string ETag, string Hash);

    private sealed record RuntimeConfiguration(
        string ApiBasePath,
        string RoutePrefix,
        string ProductTitle,
        string Mode,
        string[] Capabilities,
        HPDAIPlatformModuleOptions[] Modules,
        string[] SpaRoutes);

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
