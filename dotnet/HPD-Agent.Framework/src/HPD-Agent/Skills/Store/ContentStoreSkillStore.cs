using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace HPD.Agent;

/// <summary>Publishes reconstructable immutable skill package versions through an <see cref="IContentStore"/>.</summary>
public sealed class ContentStoreSkillStore : IWatchableSkillStore, IContentBackedSkillStore
{
    private const string PublicationKind = "skill-publication";
    private const string ManifestKind = "skill-version-manifest";
    private readonly ContentScope _scope;
    private readonly ConcurrentDictionary<string, PublishedEntry> _published = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, Channel<SkillStoreChange>> _watchers = new();
    private readonly SemaphoreSlim _mutationLock = new(1, 1);

    /// <summary>Initializes a package store in one explicit content scope.</summary>
    public ContentStoreSkillStore(IContentStore contentStore, ContentScope scope)
    {
        ContentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Value);
        _scope = scope;
    }

    /// <inheritdoc />
    public IContentStore ContentStore { get; }

    /// <summary>Raised when malformed persisted publication state is ignored during reconstruction.</summary>
    public event Action<SkillStoreDiagnostic>? Diagnostic;

    /// <inheritdoc />
    public async ValueTask<StoredSkill> InstallAsync(SkillPackage package, CancellationToken cancellationToken = default)
    {
        ValidatePackage(package);
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshPublishedCoreAsync(cancellationToken).ConfigureAwait(false);
            if (_published.ContainsKey(package.Manifest.Id))
                throw new InvalidOperationException($"Skill '{package.Manifest.Id}' is already installed.");

            var stored = await StorePackageAsync(package, cancellationToken).ConfigureAwait(false);
            var manifestAddress = await StoreVersionManifestAsync(stored, cancellationToken).ConfigureAwait(false);
            var publication = new SkillPackagePublicationRecord
            {
                SkillId = stored.Manifest.Id,
                Version = stored.Manifest.Version,
                VersionManifest = manifestAddress,
                Generation = 1,
                PublishedAt = DateTimeOffset.UtcNow
            };
            var publicationAddress = await WritePublicationAsync(publication, null, cancellationToken).ConfigureAwait(false);
            _published[stored.Manifest.Id] = new(stored, publicationAddress, publication.Generation);
            Publish(stored.Manifest.Id, stored.Manifest.Version, SkillSourceChangeKind.Added);
            return stored;
        }
        finally { _mutationLock.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<StoredSkill> UpdateAsync(string skillId, SkillPackage package, string? expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        ValidatePackage(package);
        if (!string.Equals(skillId, package.Manifest.Id, StringComparison.Ordinal))
            throw new ArgumentException("The package manifest ID must match the updated skill ID.", nameof(package));
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshPublishedCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!_published.TryGetValue(skillId, out var current))
                throw new KeyNotFoundException($"Skill '{skillId}' is not installed.");
            EnsureVersion(current.Skill, expectedVersion);
            if (string.Equals(current.Skill.Manifest.Version, package.Manifest.Version, StringComparison.Ordinal))
                throw new ContentConflictException(
                    $"Skill '{skillId}' package version '{package.Manifest.Version}' is already published. Updates require a new immutable version.",
                    skillId,
                    package.Manifest.Version,
                    current.Skill.Manifest.Version);

            var stored = await StorePackageAsync(package, cancellationToken).ConfigureAwait(false);
            var manifestAddress = await StoreVersionManifestAsync(stored, cancellationToken).ConfigureAwait(false);
            var publication = new SkillPackagePublicationRecord
            {
                SkillId = skillId,
                Version = stored.Manifest.Version,
                VersionManifest = manifestAddress,
                Generation = current.Generation + 1,
                PublishedAt = DateTimeOffset.UtcNow
            };
            var address = await WritePublicationAsync(publication, current.PublicationAddress, cancellationToken).ConfigureAwait(false);
            _published[skillId] = new(stored, address, publication.Generation);
            Publish(skillId, stored.Manifest.Version, SkillSourceChangeKind.Updated);
            return stored;
        }
        finally { _mutationLock.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(string skillId, string? expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshPublishedCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!_published.TryGetValue(skillId, out var current)) return;
            EnsureVersion(current.Skill, expectedVersion);
            await ContentStore.DeleteAsync(current.PublicationAddress, cancellationToken).ConfigureAwait(false);
            _published.TryRemove(skillId, out _);
            Publish(skillId, current.Skill.Manifest.Version, SkillSourceChangeKind.Deleted);
        }
        finally { _mutationLock.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<StoredSkill?> GetAsync(string skillId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        await RefreshPublishedAsync(cancellationToken).ConfigureAwait(false);
        return _published.TryGetValue(skillId, out var entry) ? entry.Skill : null;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<StoredSkill>> ListAsync(SkillQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await RefreshPublishedAsync(cancellationToken).ConfigureAwait(false);
        return _published.Values.Select(entry => entry.Skill)
            .Where(skill => query.Matches(skill.Manifest))
            .OrderBy(skill => skill.Manifest.Id, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SkillStoreChange> WatchAsync(SkillQuery query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<SkillStoreChange>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _watchers[id] = channel;
        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                // Tag membership can change during an update, so tag-filtered subscribers
                // receive candidate invalidations and reconcile through ListAsync.
                if (query.MayMatchId(change.SkillId))
                    yield return change;
        }
        finally
        {
            _watchers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }

    private async ValueTask RefreshPublishedAsync(CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await RefreshPublishedCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _mutationLock.Release(); }
    }

    private async ValueTask RefreshPublishedCoreAsync(CancellationToken cancellationToken)
    {
        var publications = await ContentStore.QueryAsync(_scope,
            new ContentQuery { Tags = new Dictionary<string, string> { ["kind"] = PublicationKind } },
            cancellationToken).ConfigureAwait(false);
        var reconstructed = new Dictionary<string, PublishedEntry>(StringComparer.Ordinal);
        foreach (var info in publications)
        {
            try
            {
                var publication = await ReadJsonAsync(info.Address, SkillStoreJsonContext.Default.SkillPackagePublicationRecord, cancellationToken).ConfigureAwait(false);
                var manifest = await ReadJsonAsync(publication.VersionManifest, SkillStoreJsonContext.Default.SkillPackageVersionManifest, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(publication.SkillId, manifest.Manifest.Id, StringComparison.Ordinal) ||
                    !string.Equals(publication.Version, manifest.Manifest.Version, StringComparison.Ordinal))
                    throw new InvalidDataException("Publication identity does not match its version manifest.");
                foreach (var script in manifest.Scripts)
                {
                    var contract = SkillScriptInput.FromCanonicalSchema(script.ParametersSchema);
                    if (!string.Equals(
                        contract.CanonicalSchemaFingerprint,
                        script.SchemaFingerprint,
                        StringComparison.Ordinal))
                        throw new InvalidDataException($"Stored script '{script.Name}' has an invalid input-contract fingerprint.");
                    if (script.Timeout <= TimeSpan.Zero || script.MaximumOutputBytes <= 0)
                        throw new InvalidDataException($"Stored script '{script.Name}' has invalid execution limits.");
                }
                reconstructed[publication.SkillId] = new(new StoredSkill
                {
                    Manifest = manifest.Manifest,
                    Instructions = manifest.Instructions,
                    Resources = manifest.Resources,
                    Scripts = manifest.Scripts
                }, info.Address, publication.Generation);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                ReportDiagnostic(new SkillStoreDiagnostic(
                    "InvalidPublication",
                    "A malformed or incomplete skill publication was ignored; other installed skills remain available.",
                    DateTimeOffset.UtcNow));
            }
        }
        _published.Clear();
        foreach (var entry in reconstructed)
            _published[entry.Key] = entry.Value;
    }

    private async ValueTask<StoredSkill> StorePackageAsync(SkillPackage package, CancellationToken cancellationToken)
    {
        var prefix = $"{package.Manifest.Id}@{package.Manifest.Version}";
        var instructions = await StoreAsync(prefix + "/instructions", "text/markdown", package.Instructions, null, cancellationToken).ConfigureAwait(false);
        var instructionInfo = await ContentStore.StatAsync(instructions, cancellationToken).ConfigureAwait(false);
        if (instructionInfo is null || instructionInfo.SizeBytes == 0)
            throw new InvalidDataException($"Skill '{package.Manifest.Id}' instructions cannot be empty.");
        var resources = new List<StoredSkillResource>();
        foreach (var resource in package.Resources)
            resources.Add(new(resource.Name, resource.Description, resource.ContentType,
                await StoreAsync(prefix + "/resources/" + resource.Name, resource.ContentType, resource.Content, null, cancellationToken).ConfigureAwait(false)));
        var scripts = new List<StoredSkillScript>();
        foreach (var script in package.Scripts)
        {
            var input = SkillScriptInput.FromCanonicalSchema(script.ParametersSchema);
            scripts.Add(new(
                script.Name,
                script.Description,
                script.Runtime,
                script.RequiresPermission,
                script.Timeout,
                script.MaximumOutputBytes,
                input.JsonSchema.Clone(),
                input.CanonicalSchemaFingerprint,
                await StoreAsync(prefix + "/scripts/" + script.Name, "application/octet-stream", script.Content, null, cancellationToken).ConfigureAwait(false)));
        }
        return new StoredSkill { Manifest = package.Manifest, Instructions = instructions, Resources = resources, Scripts = scripts };
    }

    private async ValueTask<ContentAddress> StoreVersionManifestAsync(StoredSkill stored, CancellationToken cancellationToken)
    {
        var manifest = new SkillPackageVersionManifest
        {
            Manifest = stored.Manifest,
            Instructions = stored.Instructions,
            Resources = stored.Resources,
            Scripts = stored.Scripts,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, SkillStoreJsonContext.Default.SkillPackageVersionManifest);
        using var stream = new MemoryStream(bytes, writable: false);
        return await StoreAsync($"{stored.Manifest.Id}@{stored.Manifest.Version}/manifest", "application/json", stream,
            new Dictionary<string, string> { ["kind"] = ManifestKind }, cancellationToken, failIfNameExists: true).ConfigureAwait(false);
    }

    private async ValueTask<ContentAddress> WritePublicationAsync(SkillPackagePublicationRecord publication, ContentAddress? current, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(publication, SkillStoreJsonContext.Default.SkillPackagePublicationRecord);
        using var stream = new MemoryStream(bytes, writable: false);
        var metadata = new ContentMetadata
        {
            Name = "skill-current:" + publication.SkillId,
            ContentType = "application/json",
            Origin = ContentSource.System,
            Tags = new Dictionary<string, string> { ["kind"] = PublicationKind, ["skill-id"] = publication.SkillId }
        };
        var options = current is null
            ? new ContentWriteOptions { Mode = ContentWriteMode.Create, FailIfNameExists = true }
            : new ContentWriteOptions { Mode = ContentWriteMode.ReplaceById, ContentId = current.Value.ContentId, IfMatchVersion = current.Value.Version };
        return (await ContentStore.WriteAsync(_scope, stream, metadata, options, cancellationToken).ConfigureAwait(false)).Address;
    }

    private async ValueTask<ContentAddress> StoreAsync(string name, string contentType, Stream stream,
        IReadOnlyDictionary<string, string>? tags, CancellationToken cancellationToken, bool failIfNameExists = false)
        => (await ContentStore.WriteAsync(_scope, stream,
            new ContentMetadata { Name = name, ContentType = contentType, Origin = ContentSource.System, Tags = tags },
            new ContentWriteOptions { Mode = ContentWriteMode.Create, FailIfNameExists = failIfNameExists },
            cancellationToken).ConfigureAwait(false)).Address;

    private async ValueTask<T> ReadJsonAsync<T>(ContentAddress address, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        await using var read = await ContentStore.OpenReadAsync(address, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Skill store content '{address.ContentId}' is unavailable.");
        return await JsonSerializer.DeserializeAsync(read.Content, typeInfo, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Skill store content '{address.ContentId}' is invalid.");
    }

    private static void ValidatePackage(SkillPackage package)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(package.Manifest); ArgumentNullException.ThrowIfNull(package.Instructions);
        ArgumentException.ThrowIfNullOrWhiteSpace(package.Manifest.Id); ArgumentException.ThrowIfNullOrWhiteSpace(package.Manifest.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(package.Manifest.Description); ArgumentException.ThrowIfNullOrWhiteSpace(package.Manifest.Version);
        if (!package.Instructions.CanRead)
            throw new ArgumentException("Skill instructions must be readable.", nameof(package));
        var capabilityNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in package.Resources)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource.Name); ArgumentException.ThrowIfNullOrWhiteSpace(resource.Description); ArgumentNullException.ThrowIfNull(resource.Content);
            if (!resource.Content.CanRead) throw new ArgumentException($"Skill resource '{resource.Name}' content must be readable.", nameof(package));
            if (!capabilityNames.Add(resource.Name)) throw new ArgumentException($"Duplicate packaged capability name '{resource.Name}'.", nameof(package));
        }
        foreach (var script in package.Scripts)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(script.Name); ArgumentException.ThrowIfNullOrWhiteSpace(script.Description); ArgumentException.ThrowIfNullOrWhiteSpace(script.Runtime); ArgumentNullException.ThrowIfNull(script.Content);
            if (!script.Content.CanRead) throw new ArgumentException($"Skill script '{script.Name}' content must be readable.", nameof(package));
            if (script.Timeout <= TimeSpan.Zero) throw new ArgumentException($"Skill script '{script.Name}' must have a positive timeout.", nameof(package));
            if (script.MaximumOutputBytes <= 0) throw new ArgumentException($"Skill script '{script.Name}' must have a positive output limit.", nameof(package));
            _ = SkillScriptInput.FromCanonicalSchema(script.ParametersSchema);
            if (!capabilityNames.Add(script.Name)) throw new ArgumentException($"Duplicate packaged capability name '{script.Name}'.", nameof(package));
        }
    }

    private static void EnsureVersion(StoredSkill current, string? expectedVersion)
    {
        if (expectedVersion is not null && !string.Equals(current.Manifest.Version, expectedVersion, StringComparison.Ordinal))
            throw new ContentConflictException($"Skill '{current.Manifest.Id}' version does not match.", current.Manifest.Id, expectedVersion, current.Manifest.Version);
    }

    private void Publish(string skillId, string? version, SkillSourceChangeKind kind)
    {
        var change = new SkillStoreChange(skillId, version, kind, DateTimeOffset.UtcNow);
        foreach (var watcher in _watchers.Values) watcher.Writer.TryWrite(change);
    }

    private void ReportDiagnostic(SkillStoreDiagnostic diagnostic)
    {
        try { Diagnostic?.Invoke(diagnostic); }
        catch { /* Diagnostics must never make store reconstruction fail. */ }
    }

    private sealed record PublishedEntry(StoredSkill Skill, ContentAddress PublicationAddress, long Generation);
}
