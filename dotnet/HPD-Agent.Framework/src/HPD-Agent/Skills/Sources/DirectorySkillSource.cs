using System.Text.Json;
using System.Threading.Channels;

namespace HPD.Agent;

/// <summary>Controls how missing resource and script descriptions are handled during directory import.</summary>
public enum SkillDirectoryImportMode
{
    /// <summary>Requires explicit descriptions in <c>skill.json</c>.</summary>
    Strict,
    /// <summary>Derives descriptions from relative filenames when metadata is absent.</summary>
    Compatibility
}

/// <summary>Imports Agent Skills-compatible directories as harness-bound runtime skills.</summary>
public sealed class DirectorySkillSource : IWatchableSkillSource
{
    private readonly string _root;
    private readonly SkillDirectoryImportMode _mode;
    private readonly IContentStore _snapshots;
    private readonly ContentScope _scope;

    /// <summary>Initializes a directory source rooted beneath one approved path.</summary>
    public DirectorySkillSource(
        string root,
        SkillDirectoryImportMode mode = SkillDirectoryImportMode.Strict,
        IContentStore? snapshotStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        if (!Directory.Exists(_root))
            throw new DirectoryNotFoundException($"Skill source root '{_root}' does not exist.");
        RejectSymbolicLink(_root);
        _mode = mode;
        _snapshots = snapshotStore ?? new InMemoryContentStore();
        _scope = ContentScope.Create("directory-skills-" + Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Skill>> GetSkillsAsync(
        SkillSourceContext context,
        CancellationToken cancellationToken)
    {
        var roots = File.Exists(Path.Combine(_root, "SKILL.md"))
            ? new[] { _root }
            : Directory.EnumerateDirectories(_root)
                .Where(directory => File.Exists(Path.Combine(directory, "SKILL.md")))
                .OrderBy(directory => directory, StringComparer.Ordinal)
                .ToArray();
        var skills = new List<Skill>(roots.Length);
        foreach (var skillRoot in roots)
            skills.Add(await ReadSkillAsync(skillRoot, cancellationToken).ConfigureAwait(false));
        return skills;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SkillSourceChange> WatchAsync(
        SkillSourceContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var changes = Channel.CreateUnbounded<SkillSourceChange>();
        using var watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        void Changed(object? _, FileSystemEventArgs args) => changes.Writer.TryWrite(
            new SkillSourceChange(null, SkillSourceChangeKind.Reset, DateTimeOffset.UtcNow));
        void Renamed(object? _, RenamedEventArgs args) => changes.Writer.TryWrite(
            new SkillSourceChange(null, SkillSourceChangeKind.Reset, DateTimeOffset.UtcNow));
        void Error(object? _, ErrorEventArgs args) => changes.Writer.TryWrite(
            new SkillSourceChange(null, SkillSourceChangeKind.Reset, DateTimeOffset.UtcNow));
        watcher.Created += Changed;
        watcher.Changed += Changed;
        watcher.Deleted += Changed;
        watcher.Renamed += Renamed;
        watcher.Error += Error;
        await foreach (var change in changes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return change;
    }

    private async ValueTask<Skill> ReadSkillAsync(string skillRoot, CancellationToken cancellationToken)
    {
        EnsureSafeDescendant(skillRoot);
        var skillPath = Path.Combine(skillRoot, "SKILL.md");
        EnsureSafeDescendant(skillPath);
        var document = await File.ReadAllTextAsync(skillPath, cancellationToken).ConfigureAwait(false);
        var (frontmatter, instructions) = ParseSkillDocument(document, skillPath);
        var name = Required(frontmatter, "name", skillPath);
        var description = Required(frontmatter, "description", skillPath);
        var id = frontmatter.GetValueOrDefault("id") ?? name;
        var version = frontmatter.GetValueOrDefault("version") ?? "directory";
        var metadata = ReadMetadata(skillRoot);
        var capabilities = new List<SkillCapability>();

        var referencesRoot = Path.Combine(skillRoot, "references");
        if (Directory.Exists(referencesRoot))
        {
            foreach (var path in EnumerateSafeFiles(referencesRoot))
            {
                var relative = Path.GetRelativePath(skillRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                var capabilityName = ToCapabilityName("read_" + Path.GetFileNameWithoutExtension(path));
                var resourceDescription = metadata.ResourceDescriptions.GetValueOrDefault(relative) ??
                    DeriveOrThrow(relative, "resource");
                await using var stream = File.OpenRead(path);
                var address = await StoreSnapshotAsync(relative, GuessContentType(path), stream, cancellationToken).ConfigureAwait(false);
                capabilities.Add(new ContentStoreSkillResource(
                    capabilityName,
                    resourceDescription,
                    new ContentStoreSkillContentReference(address),
                    _snapshots));
            }
        }

        var scriptsRoot = Path.Combine(skillRoot, "scripts");
        if (Directory.Exists(scriptsRoot))
        {
            foreach (var path in EnumerateSafeFiles(scriptsRoot))
            {
                var relative = Path.GetRelativePath(skillRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                metadata.Scripts.TryGetValue(relative, out var scriptMetadata);
                var scriptDescription = scriptMetadata?.Description ?? DeriveOrThrow(relative, "script");
                var runtime = scriptMetadata?.Runtime ?? RuntimeFromExtension(path);
                await using var stream = File.OpenRead(path);
                var address = await StoreSnapshotAsync(relative, "application/octet-stream", stream, cancellationToken).ConfigureAwait(false);
                capabilities.Add(new SkillScript(ToCapabilityName(Path.GetFileNameWithoutExtension(path)), scriptDescription)
                {
                    Reference = new ContentStoreScriptReference(address, runtime),
                    RequiresPermission = scriptMetadata?.RequiresPermission ?? true,
                    InputContract = SkillScriptInput.FromCanonicalSchema(
                        scriptMetadata is null
                            ? throw new InvalidDataException($"Script '{relative}' requires an explicit parameters schema.")
                            : ResolveParametersSchema(skillRoot, relative, scriptMetadata.ParametersSchema)),
                    ContentStore = _snapshots
                });
            }
        }

        return Skill.Create(
            id: id + "@" + version,
            name: name,
            description: description,
            instructions: SkillInstructions.FromText(instructions),
            capabilities: capabilities,
            provenance: new SkillProvenance(
                "directory",
                id,
                version,
                Scope: _scope.Value));
    }

    private DirectoryMetadata ReadMetadata(string skillRoot)
    {
        var path = Path.Combine(skillRoot, "skill.json");
        if (!File.Exists(path))
            return new DirectoryMetadata();
        EnsureSafeDescendant(path);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new DirectoryMetadata();
        if (document.RootElement.TryGetProperty("resources", out var resources))
        {
            foreach (var property in resources.EnumerateObject())
                result.ResourceDescriptions[property.Name] = property.Value.GetString() ?? string.Empty;
        }
        if (document.RootElement.TryGetProperty("scripts", out var scripts))
        {
            foreach (var property in scripts.EnumerateObject())
            {
                var value = property.Value;
                result.Scripts[property.Name] = new DirectoryScriptMetadata(
                    value.GetProperty("description").GetString() ?? string.Empty,
                    value.GetProperty("runtime").GetString() ?? string.Empty,
                    value.TryGetProperty("requiresPermission", out var permission) ? permission.GetBoolean() : true,
                    value.GetProperty("parameters").Clone());
            }
        }
        return result;
    }

    private JsonElement ResolveParametersSchema(
        string skillRoot,
        string scriptPath,
        JsonElement declaration)
    {
        if (declaration.ValueKind != JsonValueKind.Object ||
            !declaration.TryGetProperty("$hpdContract", out var reference))
            return declaration;
        if (declaration.EnumerateObject().Count() != 1 ||
            reference.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(reference.GetString()))
            throw new InvalidDataException(
                $"Script '{scriptPath}' has an invalid $hpdContract declaration.");

        var relativePath = reference.GetString()!;
        if (Path.IsPathRooted(relativePath) ||
            Uri.TryCreate(relativePath, UriKind.Absolute, out _))
            throw new InvalidDataException(
                $"Script '{scriptPath}' $hpdContract must reference a local path beneath its skill root.");
        var contractPath = Path.GetFullPath(Path.Combine(skillRoot, relativePath));
        var skillPrefix = skillRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? skillRoot
            : skillRoot + Path.DirectorySeparatorChar;
        if (!contractPath.StartsWith(skillPrefix, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Script '{scriptPath}' $hpdContract path escapes its skill root.");
        EnsureSafeDescendant(contractPath);
        if (!File.Exists(contractPath))
            throw new InvalidDataException(
                $"Script '{scriptPath}' $hpdContract file is unavailable.");

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(contractPath));
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Script '{scriptPath}' $hpdContract file is not valid JSON.",
                exception);
        }
    }

    private static (Dictionary<string, string> Frontmatter, string Instructions) ParseSkillDocument(
        string content,
        string path)
    {
        content = content.TrimStart('\uFEFF');
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
            throw new InvalidDataException($"Skill document '{path}' must begin with YAML frontmatter.");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 1;
        for (; index < lines.Length && lines[index].Trim() != "---"; index++)
        {
            var line = lines[index];
            var separator = line.IndexOf(':');
            if (separator <= 0)
                throw new InvalidDataException($"Invalid frontmatter line in '{path}': {line}");
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value is "|" or "|-" or "|+" or ">" or ">-" or ">+")
            {
                var folded = value[0] == '>';
                var scalarLines = new List<string>();
                while (++index < lines.Length && lines[index].Trim() != "---" &&
                       (string.IsNullOrWhiteSpace(lines[index]) || char.IsWhiteSpace(lines[index][0])))
                    scalarLines.Add(lines[index]);
                index--;
                var indentation = scalarLines
                    .Where(static scalarLine => !string.IsNullOrWhiteSpace(scalarLine))
                    .Select(static scalarLine => scalarLine.TakeWhile(char.IsWhiteSpace).Count())
                    .DefaultIfEmpty(0)
                    .Min();
                var normalized = scalarLines
                    .Select(scalarLine => scalarLine.Length >= indentation
                        ? scalarLine[indentation..].TrimEnd('\r')
                        : string.Empty)
                    .ToArray();
                value = folded
                    ? FoldYamlScalar(normalized)
                    : string.Join('\n', normalized).TrimEnd();
            }
            else
            {
                value = value.Trim('"', '\'');
            }
            values[key] = value;
        }
        if (index >= lines.Length)
            throw new InvalidDataException($"Skill document '{path}' has unterminated frontmatter.");
        var instructions = string.Join('\n', lines[(index + 1)..]).Trim();
        if (string.IsNullOrWhiteSpace(instructions))
            throw new InvalidDataException($"Skill document '{path}' has no instructions.");
        return (values, instructions);
    }

    private static string FoldYamlScalar(IReadOnlyList<string> lines)
    {
        var result = new System.Text.StringBuilder();
        var afterBlank = false;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                result.AppendLine();
                afterBlank = true;
                continue;
            }
            if (result.Length > 0 && !afterBlank)
                result.Append(' ');
            result.Append(line);
            afterBlank = false;
        }
        return result.ToString().TrimEnd();
    }

    private async ValueTask<ContentAddress> StoreSnapshotAsync(
        string name,
        string contentType,
        Stream content,
        CancellationToken cancellationToken)
        => (await _snapshots.WriteAsync(
            _scope,
            content,
            new ContentMetadata { Name = name, ContentType = contentType, Origin = ContentSource.System },
            new ContentWriteOptions { Mode = ContentWriteMode.Create },
            cancellationToken).ConfigureAwait(false)).Address;

    private IEnumerable<string> EnumerateSafeFiles(string directory)
    {
        EnsureSafeDescendant(directory);
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            EnsureSafeDescendant(current);
            var files = Directory.EnumerateFiles(current)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            foreach (var path in files)
            {
                EnsureSafeDescendant(path);
                yield return path;
            }

            var children = Directory.EnumerateDirectories(current)
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .ToArray();
            foreach (var child in children)
            {
                // Validate the directory before recursion so enumeration never follows
                // a linked directory outside the approved package root.
                EnsureSafeDescendant(child);
                pending.Push(child);
            }
        }
    }

    private void EnsureSafeDescendant(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? _root : _root + Path.DirectorySeparatorChar;
        if (fullPath != _root && !fullPath.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException($"Skill path '{path}' escapes the configured root.");
        var cursor = fullPath;
        while (cursor.Length >= _root.Length)
        {
            if (File.Exists(cursor) || Directory.Exists(cursor))
                RejectSymbolicLink(cursor);
            if (cursor == _root)
                break;
            cursor = Path.GetDirectoryName(cursor) ?? _root;
        }
    }

    private static void RejectSymbolicLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Symbolic links are not allowed in skill packages: '{path}'.");
    }

    private string DeriveOrThrow(string relativePath, string kind)
    {
        if (_mode == SkillDirectoryImportMode.Strict)
            throw new InvalidDataException(
                $"Strict skill import requires a description for {kind} '{relativePath}' in skill.json.");
        return kind == "resource"
            ? $"Reads the packaged {Path.GetFileName(relativePath)} resource."
            : $"Runs the packaged {Path.GetFileName(relativePath)} script and returns its result.";
    }

    private static string Required(Dictionary<string, string> values, string key, string path)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Skill document '{path}' requires '{key}' frontmatter.");

    private static string ToCapabilityName(string value)
        => string.Concat(value.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_'));

    private static string RuntimeFromExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".py" => "python",
        ".js" or ".mjs" => "javascript",
        ".sh" => "shell",
        ".ps1" => "powershell",
        ".wasm" => "wasm",
        _ => throw new InvalidDataException($"No script runtime metadata was supplied for '{path}'.")
    };

    private static string GuessContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".md" => "text/markdown",
        ".json" => "application/json",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };

    private sealed class DirectoryMetadata
    {
        public Dictionary<string, string> ResourceDescriptions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, DirectoryScriptMetadata> Scripts { get; } = new(StringComparer.Ordinal);
    }

    private sealed record DirectoryScriptMetadata(
        string Description,
        string Runtime,
        bool RequiresPermission,
        JsonElement ParametersSchema);
}
