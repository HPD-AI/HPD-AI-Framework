// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json;
using HPD.Agent;

namespace HPD.Agent.ToolHarness.Coding;

/// <summary>
/// Per-run multi-root filesystem workspace selected by the host application.
/// </summary>
public sealed record AgentWorkspace(
    string DefaultRootId,
    string DefaultRootPath,
    IReadOnlyList<AgentWorkspaceRoot> Roots)
{
    public const string ContextKey = "workspace";

    public int Version => 1;

    private readonly IReadOnlyDictionary<string, AgentWorkspaceRoot> _rootsById =
        Roots.ToDictionary(root => root.Id, StringComparer.Ordinal);

    private readonly IReadOnlyList<AgentWorkspaceRoot> _rootsByPathDescending =
        Roots.OrderByDescending(root => root.Path.Length).ToArray();

    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new AgentWorkspaceException(AgentWorkspaceErrorKind.InvalidWorkspaceShape, "Path is required.");

        var trimmed = path.Trim();
        var fullPath = ResolvePathUnchecked(trimmed);

        if (!IsAllowedPath(fullPath))
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.PathOutsideWorkspace,
                "Path is outside the configured workspace.");

        return fullPath;
    }

    public string ResolveDirectory(string? path)
        => string.IsNullOrWhiteSpace(path) ? DefaultRootPath : ResolvePath(path);

    public AgentWorkspaceRoot GetOwningRoot(string fullPath)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        foreach (var root in _rootsByPathDescending)
        {
            if (IsPathUnderDirectory(root.Path, normalizedPath))
                return root;
        }

        throw new AgentWorkspaceException(
            AgentWorkspaceErrorKind.PathOutsideWorkspace,
            "Path is outside the configured workspace.");
    }

    public bool IsAllowedPath(string fullPath)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        return _rootsByPathDescending.Any(root => IsPathUnderDirectory(root.Path, normalizedPath));
    }

    public void ThrowIfPathIsOutsideWorkspace(string fullPath)
    {
        if (!IsAllowedPath(fullPath))
        {
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.PathOutsideWorkspace,
                "Path is outside the configured workspace.");
        }
    }

    public static AgentWorkspace From(AgentRunConfig runConfig)
    {
        ArgumentNullException.ThrowIfNull(runConfig);

        if (TryFrom(runConfig, out var workspace, out var error))
            return workspace;

        throw new AgentWorkspaceException(AgentWorkspaceErrorKind.InvalidWorkspaceShape, error ?? "Invalid workspace.");
    }

    public static bool TryFrom(AgentRunConfig runConfig, out AgentWorkspace workspace, out string? error)
    {
        workspace = null!;
        error = null;

        if (runConfig.ContextOverrides is null ||
            !runConfig.ContextOverrides.TryGetValue(ContextKey, out var raw) ||
            raw is null)
        {
            error = "Workspace is required. Select a workspace before using workspace tools.";
            return false;
        }

        try
        {
            workspace = raw switch
            {
                AgentWorkspace typed => Normalize(typed),
                JsonElement element => ParseJsonElement(element),
                IReadOnlyDictionary<string, object?> dictionary => ParseDictionary(dictionary),
                _ => throw new AgentWorkspaceException(
                    AgentWorkspaceErrorKind.InvalidWorkspaceShape,
                    "Workspace must be an object.")
            };
            return true;
        }
        catch (AgentWorkspaceException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool IsPathUnderDirectory(string rootDirectory, string path)
    {
        var fullRoot = NormalizePlatformPathAlias(Path.GetFullPath(rootDirectory))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = NormalizePlatformPathAlias(Path.GetFullPath(path));

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return fullPath.Equals(fullRoot, comparison) ||
            fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison) ||
            (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar &&
                fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, comparison));
    }

    private static string NormalizePlatformPathAlias(string path)
    {
        if (OperatingSystem.IsMacOS() &&
            path.StartsWith("/var/", StringComparison.Ordinal))
        {
            return "/private" + path;
        }

        return path;
    }

    private string ResolvePathUnchecked(string path)
    {
        if (TryParseRootQualifiedPath(path, out var rootId, out var relativePath))
        {
            if (!_rootsById.TryGetValue(rootId, out var root))
            {
                throw new AgentWorkspaceException(
                    AgentWorkspaceErrorKind.UnknownRootId,
                    $"Workspace root '{rootId}' is not configured.");
            }

            var fullPath = Path.GetFullPath(relativePath.Length == 0
                ? root.Path
                : Path.Combine(root.Path, relativePath));
            if (!IsPathUnderDirectory(root.Path, fullPath))
            {
                throw new AgentWorkspaceException(
                    AgentWorkspaceErrorKind.PathOutsideWorkspace,
                    "Path is outside the configured workspace root.");
            }

            return fullPath;
        }

        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(DefaultRootPath, path));
    }

    private static bool TryParseRootQualifiedPath(
        string path,
        out string rootId,
        out string relativePath)
    {
        rootId = string.Empty;
        relativePath = string.Empty;

        if (!path.StartsWith('@') || path.Length == 1)
            return false;

        var separatorIndex = path.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], 1);
        if (separatorIndex < 0)
        {
            rootId = path[1..];
            return rootId.Length > 0;
        }

        rootId = path[1..separatorIndex];
        relativePath = path[(separatorIndex + 1)..];
        return rootId.Length > 0;
    }

    private static AgentWorkspace ParseJsonElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.InvalidWorkspaceShape,
                "Workspace must be an object.");
        }

        var version = RequireInt32(element, "version");
        if (version != 1)
        {
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.UnsupportedVersion,
                $"Workspace version '{version}' is not supported.");
        }

        var defaultRootId = RequireString(element, "defaultRootId");
        if (!element.TryGetProperty("roots", out var rootsElement) ||
            rootsElement.ValueKind != JsonValueKind.Array ||
            rootsElement.GetArrayLength() == 0)
        {
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.MissingRoots,
                "Workspace roots are required.");
        }

        var roots = new List<AgentWorkspaceRoot>();
        foreach (var rootElement in rootsElement.EnumerateArray())
        {
            if (rootElement.ValueKind != JsonValueKind.Object)
            {
                throw new AgentWorkspaceException(
                    AgentWorkspaceErrorKind.InvalidWorkspaceShape,
                    "Workspace root must be an object.");
            }

            roots.Add(new AgentWorkspaceRoot(
                RequireString(rootElement, "id"),
                RequireString(rootElement, "path"),
                TryGetString(rootElement, "label")));
        }

        return Create(defaultRootId, roots);
    }

    private static AgentWorkspace ParseDictionary(IReadOnlyDictionary<string, object?> dictionary)
    {
        var version = Convert.ToInt32(RequireDictionaryValue(dictionary, "version"), System.Globalization.CultureInfo.InvariantCulture);
        if (version != 1)
        {
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.UnsupportedVersion,
                $"Workspace version '{version}' is not supported.");
        }

        var defaultRootId = RequireDictionaryValue(dictionary, "defaultRootId") as string
            ?? throw new AgentWorkspaceException(AgentWorkspaceErrorKind.MissingDefaultRoot, "Workspace defaultRootId is required.");

        if (RequireDictionaryValue(dictionary, "roots") is not IEnumerable<object?> rawRoots)
        {
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.MissingRoots,
                "Workspace roots are required.");
        }

        var roots = new List<AgentWorkspaceRoot>();
        foreach (var rawRoot in rawRoots)
        {
            if (rawRoot is JsonElement rootElement)
            {
                roots.Add(new AgentWorkspaceRoot(
                    RequireString(rootElement, "id"),
                    RequireString(rootElement, "path"),
                    TryGetString(rootElement, "label")));
                continue;
            }

            if (rawRoot is not IReadOnlyDictionary<string, object?> rootDictionary)
            {
                throw new AgentWorkspaceException(
                    AgentWorkspaceErrorKind.InvalidWorkspaceShape,
                    "Workspace root must be an object.");
            }

            roots.Add(new AgentWorkspaceRoot(
                RequireDictionaryValue(rootDictionary, "id") as string
                    ?? throw new AgentWorkspaceException(AgentWorkspaceErrorKind.InvalidWorkspaceShape, "Workspace root id is required."),
                RequireDictionaryValue(rootDictionary, "path") as string
                    ?? throw new AgentWorkspaceException(AgentWorkspaceErrorKind.InvalidWorkspaceShape, "Workspace root path is required."),
                rootDictionary.TryGetValue("label", out var label) ? label as string : null));
        }

        return Create(defaultRootId, roots);
    }

    private static AgentWorkspace Normalize(AgentWorkspace workspace)
        => Create(workspace.DefaultRootId, workspace.Roots);

    private static AgentWorkspace Create(string defaultRootId, IReadOnlyList<AgentWorkspaceRoot> roots)
    {
        if (string.IsNullOrWhiteSpace(defaultRootId))
        {
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.MissingDefaultRoot,
                "Workspace defaultRootId is required.");
        }

        if (roots.Count == 0)
        {
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.MissingRoots,
                "Workspace roots are required.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(PathComparer);
        var normalizedRoots = new List<AgentWorkspaceRoot>(roots.Count);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root.Id))
            {
                throw new AgentWorkspaceException(
                    AgentWorkspaceErrorKind.InvalidWorkspaceShape,
                    "Workspace root id is required.");
            }

            if (!ids.Add(root.Id))
            {
                throw new AgentWorkspaceException(
                    AgentWorkspaceErrorKind.DuplicateRootId,
                    $"Workspace root id '{root.Id}' is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(root.Path))
            {
                throw new AgentWorkspaceException(
                    AgentWorkspaceErrorKind.InvalidWorkspaceShape,
                    "Workspace root path is required.");
            }

            var fullPath = Path.GetFullPath(root.Path);
            if (!Directory.Exists(fullPath))
            {
                throw new AgentWorkspaceException(
                    AgentWorkspaceErrorKind.RootPathDoesNotExist,
                    $"Workspace root path does not exist: {fullPath}");
            }

            if (!paths.Add(fullPath))
            {
                throw new AgentWorkspaceException(
                    AgentWorkspaceErrorKind.DuplicateRootPath,
                    $"Workspace root path is duplicated: {fullPath}");
            }

            normalizedRoots.Add(root with { Path = fullPath });
        }

        var defaultRoot = normalizedRoots.FirstOrDefault(root => root.Id == defaultRootId);
        if (defaultRoot is null)
        {
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.MissingDefaultRoot,
                $"Workspace defaultRootId '{defaultRootId}' does not match a configured root.");
        }

        return new AgentWorkspace(defaultRootId, defaultRoot.Path, normalizedRoots);
    }

    private static IEqualityComparer<string> PathComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static object? RequireDictionaryValue(IReadOnlyDictionary<string, object?> dictionary, string name)
    {
        if (!dictionary.TryGetValue(name, out var value) || value is null)
        {
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.InvalidWorkspaceShape,
                $"Workspace '{name}' is required.");
        }

        return value;
    }

    private static int RequireInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || !property.TryGetInt32(out var value))
        {
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.InvalidWorkspaceShape,
                $"Workspace '{name}' is required.");
        }

        return value;
    }

    private static string RequireString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new AgentWorkspaceException(
                AgentWorkspaceErrorKind.InvalidWorkspaceShape,
                $"Workspace '{name}' is required.");
        }

        return property.GetString()!;
    }

    private static string? TryGetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

public sealed record AgentWorkspaceRoot(
    string Id,
    string Path,
    string? Label = null);

public sealed class AgentWorkspaceException : Exception
{
    public AgentWorkspaceException(AgentWorkspaceErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public AgentWorkspaceErrorKind Kind { get; }
}

public enum AgentWorkspaceErrorKind
{
    MissingWorkspace,
    InvalidWorkspaceShape,
    UnsupportedVersion,
    MissingDefaultRoot,
    MissingRoots,
    DuplicateRootId,
    DuplicateRootPath,
    RootPathDoesNotExist,
    UnknownRootId,
    PathOutsideWorkspace
}
