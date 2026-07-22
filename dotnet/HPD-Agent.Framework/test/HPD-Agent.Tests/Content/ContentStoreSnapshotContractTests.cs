using System.Text;
using FluentAssertions;
using Xunit;

namespace HPD.Agent.Tests.Content;

public sealed class ContentStoreSnapshotContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteAndOpen_ReturnOneExactSnapshot(bool local)
    {
        await using var fixture = CreateFixture(local);
        var scope = ContentScope.Create("tenant-a");
        var stored = await fixture.Store.WriteTextAsync(
            scope,
            "first",
            new ContentMetadata { Name = "guide.md", ContentType = "text/markdown" });

        await using var opened = await fixture.Store.OpenReadAsync(stored.Address);

        opened.Should().NotBeNull();
        opened!.Info.Should().BeEquivalentTo(stored);
        opened.Info.Address.Scope.Should().Be(scope);
        opened.Info.Address.Version.Should().NotBeNullOrWhiteSpace();
        opened.Info.Address.Sha256.Should().HaveLength(64);
        (await ReadTextAsync(opened.Content)).Should().Be("first");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactOldAddress_ConflictsAfterReplacement(bool local)
    {
        await using var fixture = CreateFixture(local);
        var scope = ContentScope.Create("tenant-a");
        var first = await fixture.Store.WriteTextAsync(
            scope,
            "first",
            new ContentMetadata { Name = "guide.md", ContentType = "text/plain" });
        var replacement = await fixture.Store.WriteTextAsync(
            scope,
            "second",
            new ContentMetadata { Name = "guide.md", ContentType = "text/plain" },
            new ContentWriteOptions
            {
                Mode = ContentWriteMode.ReplaceById,
                ContentId = first.Address.ContentId,
                IfMatchVersion = first.Address.Version
            });

        var oldOpen = async () => await fixture.Store.OpenReadAsync(first.Address);
        await oldOpen.Should().ThrowAsync<ContentConflictException>();

        await using var current = await fixture.Store.OpenReadAsync(replacement.Address);
        current.Should().NotBeNull();
        (await ReadTextAsync(current!.Content)).Should().Be("second");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HashConstraint_IsEnforced(bool local)
    {
        await using var fixture = CreateFixture(local);
        var stored = await fixture.Store.WriteTextAsync(
            ContentScope.Global,
            "content",
            new ContentMetadata { ContentType = "text/plain" });
        var incorrect = stored.Address with { Sha256 = new string('0', 64) };

        var open = async () => await fixture.Store.OpenReadAsync(incorrect);

        await open.Should().ThrowAsync<ContentConflictException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Query_IsRestrictedToOneExplicitScope(bool local)
    {
        await using var fixture = CreateFixture(local);
        var firstScope = ContentScope.Create("tenant-a");
        var secondScope = ContentScope.Create("tenant-b");
        await fixture.Store.WriteTextAsync(firstScope, "a", new ContentMetadata { ContentType = "text/plain" });
        await fixture.Store.WriteTextAsync(secondScope, "b", new ContentMetadata { ContentType = "text/plain" });

        var first = await fixture.Store.QueryAsync(firstScope);
        var second = await fixture.Store.QueryAsync(secondScope);

        first.Should().ContainSingle().Which.Address.Scope.Should().Be(firstScope);
        second.Should().ContainSingle().Which.Address.Scope.Should().Be(secondScope);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_UsesAddressVersionAndIsIdempotent(bool local)
    {
        await using var fixture = CreateFixture(local);
        var stored = await fixture.Store.WriteTextAsync(
            ContentScope.Global,
            "content",
            new ContentMetadata { ContentType = "text/plain" });

        await fixture.Store.DeleteAsync(stored.Address);
        await fixture.Store.DeleteAsync(stored.Address);

        (await fixture.Store.OpenReadAsync(stored.Address)).Should().BeNull();
    }

    [Fact]
    public async Task LocalStore_RejectsLossyOrEscapingScopeSegments()
    {
        await using var fixture = CreateFixture(local: true);

        var write = async () => await fixture.Store.WriteTextAsync(
            ContentScope.Create("../tenant"),
            "content",
            new ContentMetadata { ContentType = "text/plain" });

        await write.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LocalStore_OpenSnapshotRemainsCoherentAcrossInstanceReplacement()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-content-{Guid.NewGuid():N}");
        try
        {
            var firstStore = new LocalFileContentStore(path);
            var secondStore = new LocalFileContentStore(path);
            var scope = ContentScope.Create("shared");
            var first = await firstStore.WriteTextAsync(
                scope, "first", new ContentMetadata { Name = "current", ContentType = "text/plain" });
            await using var leased = await firstStore.OpenReadAsync(first.Address);
            leased.Should().NotBeNull();

            var replacement = await secondStore.WriteTextAsync(
                scope, "second", new ContentMetadata { Name = "current", ContentType = "text/plain" },
                new ContentWriteOptions
                {
                    Mode = ContentWriteMode.ReplaceById,
                    ContentId = first.Address.ContentId,
                    IfMatchVersion = first.Address.Version
                });

            leased!.Info.Address.Should().Be(first.Address);
            (await ReadTextAsync(leased.Content)).Should().Be("first");
            await using var current = await firstStore.OpenReadAsync(replacement.Address);
            (await ReadTextAsync(current!.Content)).Should().Be("second");
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task LocalStore_TwoInstancesCannotBothReplaceOneExpectedVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-content-{Guid.NewGuid():N}");
        try
        {
            var firstStore = new LocalFileContentStore(path);
            var secondStore = new LocalFileContentStore(path);
            var scope = ContentScope.Create("shared");
            var initial = await firstStore.WriteTextAsync(
                scope, "initial", new ContentMetadata { Name = "current", ContentType = "text/plain" });
            var options = new ContentWriteOptions
            {
                Mode = ContentWriteMode.ReplaceById,
                ContentId = initial.Address.ContentId,
                IfMatchVersion = initial.Address.Version
            };

            var attempts = await Task.WhenAll(
                CaptureAsync(() => firstStore.WriteTextAsync(scope, "one",
                    new ContentMetadata { Name = "current", ContentType = "text/plain" }, options).AsTask()),
                CaptureAsync(() => secondStore.WriteTextAsync(scope, "two",
                    new ContentMetadata { Name = "current", ContentType = "text/plain" }, options).AsTask()));

            attempts.Count(result => result is null).Should().Be(1);
            attempts.Count(result => result is ContentConflictException).Should().Be(1);
            (await firstStore.QueryAsync(scope)).Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

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

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception exception) { return exception; }
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
