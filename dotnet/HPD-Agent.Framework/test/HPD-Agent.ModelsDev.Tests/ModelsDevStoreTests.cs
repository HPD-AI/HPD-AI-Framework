using System.Net;
using System.Text;
using FluentAssertions;

namespace HPD.Agent.ModelsDev.Tests;

public sealed class ModelsDevStoreTests
{
    [Fact]
    public async Task Snapshot_preserves_provenance_and_current_cost_shape()
    {
        using var http = new HttpClient(new SequenceHandler(Response(HttpStatusCode.OK, SampleJson, "\"v1\"")));
        var snapshot = await new ModelsDevStore(http, new ModelsDevOptions { UseDiskCache = false }).GetSnapshotAsync();

        snapshot.Origin.Should().Be(ModelsDevCatalogOrigin.Network);
        snapshot.ETag.Should().Be("\"v1\"");
        snapshot.ContentDigest.Should().HaveLength(64);
        var cost = snapshot.Database.Providers["openai"].Models["gpt-4o"].Cost!;
        cost.Reasoning.Should().Be(15m);
        cost.InputAudio.Should().Be(20m);
        cost.Tiers.Should().ContainSingle().Which.Tier.Size.Should().Be(200_000);
    }

    [Fact]
    public async Task Missing_cost_tiers_are_normalized_to_an_empty_collection()
    {
        const string json = """
        {"deepseek":{"models":{"deepseek-v4":{"cost":{"input":1,"output":2}}}}}
        """;
        using var http = new HttpClient(new SequenceHandler(Response(HttpStatusCode.OK, json)));

        var snapshot = await new ModelsDevStore(http, new ModelsDevOptions { UseDiskCache = false }).GetSnapshotAsync();

        snapshot.Database.Providers["deepseek"].Models["deepseek-v4"].Cost!.Tiers.Should().BeEmpty();
    }

    [Fact]
    public async Task Fresh_cache_is_reused_without_http_call()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using (var http = new HttpClient(new SequenceHandler(Response(HttpStatusCode.OK, SampleJson))))
                await new ModelsDevStore(http, new ModelsDevOptions { CachePath = directory }).GetSnapshotAsync();
            using var offline = new HttpClient(new ThrowingHandler());

            var snapshot = await new ModelsDevStore(offline, new ModelsDevOptions { CachePath = directory }).GetSnapshotAsync();

            snapshot.Origin.Should().Be(ModelsDevCatalogOrigin.FreshCache);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Cache_only_returns_stale_snapshot_without_network()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using (var http = new HttpClient(new SequenceHandler(Response(HttpStatusCode.OK, SampleJson))))
                await new ModelsDevStore(http, new ModelsDevOptions { CachePath = directory }).GetSnapshotAsync();
            using var offline = new HttpClient(new ThrowingHandler());
            var store = new ModelsDevStore(offline, new ModelsDevOptions { CachePath = directory, RefreshInterval = TimeSpan.Zero });

            (await store.GetSnapshotAsync(ModelsDevRefreshMode.CacheOnly)).Origin.Should().Be(ModelsDevCatalogOrigin.StaleCache);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Forced_refresh_uses_etag_on_not_modified()
    {
        var handler = new SequenceHandler(Response(HttpStatusCode.OK, SampleJson, "\"v1\""), new(HttpStatusCode.NotModified));
        using var http = new HttpClient(handler);
        var store = new ModelsDevStore(http, new ModelsDevOptions { UseDiskCache = false });
        var first = await store.GetSnapshotAsync();
        var second = await store.GetSnapshotAsync(ModelsDevRefreshMode.Force);

        handler.Requests[1].Headers.IfNoneMatch.Should().Contain(item => item.Tag == "\"v1\"");
        second.ContentDigest.Should().Be(first.ContentDigest);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(1, -10)]
    public async Task Negative_rates_are_rejected(decimal input, decimal output)
    {
        var json = "{\"openai\":{\"models\":{\"bad\":{\"cost\":{\"input\":INPUT,\"output\":OUTPUT}}}}}"
            .Replace("INPUT", input.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("OUTPUT", output.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        using var http = new HttpClient(new SequenceHandler(Response(HttpStatusCode.OK, json)));
        var store = new ModelsDevStore(http, new ModelsDevOptions { UseDiskCache = false, MaxTransientRetries = 0 });

        Func<Task> action = () => store.GetSnapshotAsync().AsTask();
        await action.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"tier\":null,\"input\":3,\"output\":4}")]
    public async Task Null_tier_entries_and_selectors_are_rejected_as_invalid_catalog_data(string tier)
    {
        var json = "{\"openai\":{\"models\":{\"bad\":{\"cost\":{\"input\":1,\"output\":2,\"tiers\":["
            + tier
            + "]}}}}}";
        using var http = new HttpClient(new SequenceHandler(Response(HttpStatusCode.OK, json)));
        var store = new ModelsDevStore(http, new ModelsDevOptions { UseDiskCache = false, MaxTransientRetries = 0 });

        Func<Task> action = () => store.GetSnapshotAsync().AsTask();

        await action.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string? json = null, string? etag = null)
    {
        var response = new HttpResponseMessage(status);
        if (json is not null) response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (etag is not null) response.Headers.ETag = new(etag);
        return response;
    }

    private const string SampleJson = """
    {"openai":{"models":{"gpt-4o":{"name":"GPT-4o","cost":{"input":2.5,"output":10,"reasoning":15,"cache_read":1.25,"cache_write":3,"input_audio":20,"output_audio":40,"tiers":[{"tier":{"type":"context","size":200000},"input":5,"output":20}]}}}}}
    """;

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("offline");
    }
}
