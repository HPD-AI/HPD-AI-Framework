using System.Text;
using FluentAssertions;
using Xunit;

namespace HPD.Agent.Tests.Content;

/// <summary>
/// Behavioral contract shared by the built-in content stores. Snapshot-specific
/// guarantees live in <see cref="ContentStoreSnapshotContractTests"/>.
/// </summary>
public sealed class ContentStoreBehaviorContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_PreservesMetadataAndCreatesDistinctRecords(bool local)
    {
        await using var fixture = CreateFixture(local);
        var scope = ContentScope.Create("tenant-a");
        var metadata = new ContentMetadata
        {
            Name = "guide.md",
            ContentType = "text/markdown",
            Description = "Operator guide",
            Origin = ContentSource.System,
            OriginalSource = "package://guide",
            Tags = new Dictionary<string, string> { ["kind"] = "skill", ["team"] = "data" }
        };

        var first = await fixture.Store.WriteTextAsync(scope, "same", metadata);
        var second = await fixture.Store.WriteTextAsync(scope, "same", metadata);

        first.Address.ContentId.Should().NotBe(second.Address.ContentId);
        first.Name.Should().Be(metadata.Name);
        first.ContentType.Should().Be(metadata.ContentType);
        first.Description.Should().Be(metadata.Description);
        first.Origin.Should().Be(ContentSource.System);
        first.OriginalSource.Should().Be(metadata.OriginalSource);
        first.Tags.Should().BeEquivalentTo(metadata.Tags);
        first.SizeBytes.Should().Be(Encoding.UTF8.GetByteCount("same"));
        first.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        (await fixture.Store.QueryAsync(scope)).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Query_CombinesMetadataFiltersAndAppliesLimit(bool local)
    {
        await using var fixture = CreateFixture(local);
        var scope = ContentScope.Create("tenant-a");
        await fixture.Store.WriteTextAsync(scope, "one", Metadata("guide", "text/plain", "data", "published"));
        await fixture.Store.WriteTextAsync(scope, "two", Metadata("guide", "text/plain", "support", "published"));
        await fixture.Store.WriteTextAsync(scope, "three", Metadata("guide", "application/json", "data", "published"));
        await fixture.Store.WriteTextAsync(scope, "four", Metadata("other", "text/plain", "data", "draft"));

        var results = await fixture.Store.QueryAsync(scope, new ContentQuery
        {
            Name = "guide",
            ContentType = "text/plain",
            Tags = new Dictionary<string, string> { ["team"] = "data", ["state"] = "published" },
            CreatedAfter = DateTime.UtcNow.AddMinutes(-1),
            Limit = 1
        });

        results.Should().ContainSingle();
        results[0].Tags!["team"].Should().Be("data");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_WithUniqueNamePolicyRejectsDuplicateInSameScope(bool local)
    {
        await using var fixture = CreateFixture(local);
        var scope = ContentScope.Create("tenant-a");
        var metadata = Metadata("guide", "text/plain", "data", "published");
        var options = new ContentWriteOptions { FailIfNameExists = true };
        await fixture.Store.WriteTextAsync(scope, "first", metadata, options);

        var duplicate = async () => await fixture.Store.WriteTextAsync(scope, "second", metadata, options);

        await duplicate.Should().ThrowAsync<ContentConflictException>();
        (await fixture.Store.QueryAsync(scope)).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplaceByNameAndAppend_KeepIdentityAndPublishNewSnapshots(bool local)
    {
        await using var fixture = CreateFixture(local);
        var scope = ContentScope.Create("tenant-a");
        var metadata = Metadata("guide", "text/plain", "data", "published");
        var created = await fixture.Store.WriteTextAsync(scope, "first", metadata);
        var replaced = await fixture.Store.WriteTextAsync(scope, "second", metadata,
            new ContentWriteOptions
            {
                Mode = ContentWriteMode.ReplaceByName,
                IfMatchVersion = created.Address.Version
            });
        var appended = await fixture.Store.WriteTextAsync(scope, "+third", metadata,
            new ContentWriteOptions
            {
                Mode = ContentWriteMode.Append,
                ContentId = replaced.Address.ContentId,
                IfMatchVersion = replaced.Address.Version
            });

        replaced.Address.ContentId.Should().Be(created.Address.ContentId);
        appended.Address.ContentId.Should().Be(created.Address.ContentId);
        appended.Address.Version.Should().NotBe(replaced.Address.Version);
        await using var opened = await fixture.Store.OpenReadAsync(appended.Address);
        (await ReadTextAsync(opened!.Content)).Should().Be("second+third");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Append_PreservesAlreadyOpenedSnapshotAndCreatesMissingTarget(bool local)
    {
        await using var fixture = CreateFixture(local);
        var scope = ContentScope.Create("tenant-a");
        var metadata = Metadata("guide", "text/plain", "data", "published");
        var created = await fixture.Store.WriteTextAsync(scope, "first", metadata);
        await using var oldSnapshot = await fixture.Store.OpenReadAsync(created.Address);

        var appended = await fixture.Store.WriteTextAsync(scope, "+second", metadata,
            new ContentWriteOptions
            {
                Mode = ContentWriteMode.Append,
                ContentId = created.Address.ContentId,
                IfMatchVersion = created.Address.Version
            });
        var createdByAppend = await fixture.Store.WriteTextAsync(
            scope,
            "new",
            Metadata("new-guide", "text/plain", "data", "published"),
            new ContentWriteOptions { Mode = ContentWriteMode.Append });

        (await ReadTextAsync(oldSnapshot!.Content)).Should().Be("first");
        await using var current = await fixture.Store.OpenReadAsync(appended.Address);
        (await ReadTextAsync(current!.Content)).Should().Be("first+second");
        createdByAppend.Address.ContentId.Should().NotBe(created.Address.ContentId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatAndDelete_EnforceAddressAndDoNotAffectOtherContent(bool local)
    {
        await using var fixture = CreateFixture(local);
        var scope = ContentScope.Create("tenant-a");
        var otherScope = ContentScope.Create("tenant-b");
        var first = await fixture.Store.WriteTextAsync(scope, "first", new ContentMetadata());
        var second = await fixture.Store.WriteTextAsync(scope, "second", new ContentMetadata());
        var wrongScope = first.Address with { Scope = otherScope };
        var wrongHash = first.Address with { Sha256 = new string('0', 64) };

        (await fixture.Store.StatAsync(wrongScope)).Should().BeNull();
        (await fixture.Store.StatAsync(new ContentAddress(scope, "missing"))).Should().BeNull();
        var constrainedStat = async () => await fixture.Store.StatAsync(wrongHash);
        await constrainedStat.Should().ThrowAsync<ContentConflictException>();

        await fixture.Store.DeleteAsync(first.Address);
        (await fixture.Store.StatAsync(first.Address)).Should().BeNull();
        (await fixture.Store.StatAsync(second.Address)).Should().NotBeNull();
    }

    [Fact]
    public async Task LocalStore_NewInstanceDiscoversPersistedMetadataAndBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-content-{Guid.NewGuid():N}");
        try
        {
            var scope = ContentScope.Create("persistent");
            var firstStore = new LocalFileContentStore(path);
            var stored = await firstStore.WriteTextAsync(
                scope, "persisted", Metadata("guide", "text/plain", "data", "published"));

            var restartedStore = new LocalFileContentStore(path);
            var listed = await restartedStore.QueryAsync(scope, new ContentQuery { Name = "guide" });
            listed.Should().ContainSingle().Which.Should().BeEquivalentTo(stored);
            await using var opened = await restartedStore.OpenReadAsync(stored.Address);
            (await ReadTextAsync(opened!.Content)).Should().Be("persisted");
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    private static ContentMetadata Metadata(string name, string contentType, string team, string state) => new()
    {
        Name = name,
        ContentType = contentType,
        Tags = new Dictionary<string, string> { ["team"] = team, ["state"] = state }
    };

    private static StoreFixture CreateFixture(bool local)
    {
        if (!local)
            return new StoreFixture(new InMemoryContentStore(), null);

        var path = Path.Combine(Path.GetTempPath(), $"hpd-content-{Guid.NewGuid():N}");
        return new StoreFixture(new LocalFileContentStore(path), path);
    }

    private static async Task<string> ReadTextAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private sealed class StoreFixture(IContentStore store, string? path) : IAsyncDisposable
    {
        public IContentStore Store { get; } = store;

        public ValueTask DisposeAsync()
        {
            if (path is not null && Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
