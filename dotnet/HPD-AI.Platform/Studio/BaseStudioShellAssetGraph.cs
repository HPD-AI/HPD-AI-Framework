using System.Collections.Immutable;
using System.Reflection;
using System.Text;

namespace HPD.AI.Platform.Studio;

/// <summary>Owns the immutable, authorization-neutral HPD Studio shell assets embedded in the platform edition.</summary>
public sealed class BaseStudioShellAssetGraph
{
    private const string ResourcePrefix = "HPD.AI.Platform.StudioShell.";
    private const string BaseMarker = "__HPD_STUDIO_BASE__";
    private readonly ImmutableDictionary<string, BaseStudioShellAsset> _assets;
    private readonly string _indexTemplate;

    /// <summary>Initializes and validates the embedded shell graph.</summary>
    public BaseStudioShellAssetGraph(BaseStudioShellContract shellContract)
    {
        ArgumentNullException.ThrowIfNull(shellContract);
        Assembly assembly = typeof(BaseStudioShellAssetGraph).Assembly;
        _indexTemplate = Encoding.UTF8.GetString(ReadRequired(assembly, ResourcePrefix + "index.html"));
        if (_indexTemplate.Count(static value => value == '<') < 2 ||
            _indexTemplate.Split(BaseMarker, StringSplitOptions.None).Length != 2 ||
            Count(_indexTemplate, "./assets/hpd-studio-shell.js") != 1 ||
            Count(_indexTemplate, "./assets/hpd-studio-shell.css") != 1 ||
            _indexTemplate.Contains("studio-config", StringComparison.OrdinalIgnoreCase) ||
            _indexTemplate.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            _indexTemplate.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
            _indexTemplate.Contains("<script>", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The embedded Studio entry document is invalid.");

        BaseStudioShellAsset[] assets =
        [
            Create(assembly, "assets/hpd-studio-shell.css", "text/css; charset=utf-8"),
            Create(assembly, "assets/hpd-studio-shell.js", "text/javascript; charset=utf-8"),
        ];
        _assets = assets.ToImmutableDictionary(static value => value.Path, StringComparer.Ordinal);
        EntryTemplateDigest = BaseStudioSha256.Compute(Encoding.UTF8.GetBytes(_indexTemplate));
        Checksum = StudioCanonicalEncoding.Hash("base.studio.shell-assets.v1", writer =>
        {
            writer.Checksum(shellContract.Checksum);
            writer.Checksum(EntryTemplateDigest);
            writer.Count(assets.Length);
            foreach (BaseStudioShellAsset asset in assets)
            {
                writer.String(asset.Path);
                writer.Int64(asset.Length);
                writer.Checksum(asset.Digest);
            }
        });
    }

    /// <summary>Gets the immutable shell asset-graph checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Gets the checksum of the immutable entry-document template before route-prefix binding.</summary>
    public BaseStudioSha256 EntryTemplateDigest { get; }

    /// <summary>Creates the entry document with one validated host-owned Studio root.</summary>
    public byte[] CreateEntryDocument(string routePrefix)
    {
        string normalized = NormalizePrefix(routePrefix);
        string document = _indexTemplate.Replace(BaseMarker, normalized, StringComparison.Ordinal);
        foreach (BaseStudioShellAsset asset in _assets.Values)
            document = document.Replace("./" + asset.Path,
                "./" + asset.Path + "?v=" + Convert.ToHexString(asset.Digest.ToArray()).ToLowerInvariant(), StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(document);
    }

    /// <summary>Resolves one exact public shell asset.</summary>
    public bool TryResolve(string path, out BaseStudioShellAsset asset)
        => _assets.TryGetValue(path, out asset!);

    private static BaseStudioShellAsset Create(Assembly assembly, string path, string contentType)
    {
        byte[] content = ReadRequired(assembly, ResourcePrefix + path.Replace('/', '.'));
        return new(path, contentType, content, BaseStudioSha256.Compute(content));
    }

    private static byte[] ReadRequired(Assembly assembly, string name)
    {
        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded Studio shell resource '{name}' is absent.");
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }

    private static string NormalizePrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('/') || value.Contains("..", StringComparison.Ordinal) ||
            value.Contains('\\') || value.Any(char.IsControl))
            throw new ArgumentException("The Studio route prefix is invalid.", nameof(value));
        return "/" + value.Trim('/') ;
    }

    private static int Count(string value, string term)
    {
        int count = 0, offset = 0;
        while ((offset = value.IndexOf(term, offset, StringComparison.Ordinal)) >= 0) { count++; offset += term.Length; }
        return count;
    }
}

/// <summary>Contains one immutable content-addressed public shell asset.</summary>
public sealed class BaseStudioShellAsset
{
    private readonly byte[] _content;
    internal BaseStudioShellAsset(string path, string contentType, byte[] content, BaseStudioSha256 digest)
    { Path = path; ContentType = contentType; _content = content.ToArray(); Digest = BaseStudioSha256.FromDigest(digest.ToArray()); }
    /// <summary>Gets the normalized shell-relative path.</summary>
    public string Path { get; }
    /// <summary>Gets the fixed safe media type.</summary>
    public string ContentType { get; }
    /// <summary>Gets the exact content length.</summary>
    public long Length => _content.LongLength;
    /// <summary>Gets the content digest.</summary>
    public BaseStudioSha256 Digest { get; }
    /// <summary>Returns a defensive copy of the embedded content.</summary>
    public byte[] GetContent() => _content.ToArray();
}
