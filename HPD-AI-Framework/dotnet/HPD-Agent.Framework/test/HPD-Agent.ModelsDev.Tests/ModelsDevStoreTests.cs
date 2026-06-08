using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace HPD.Agent.ModelsDev.Tests;

public sealed class ModelsDevStoreTests
{
    [Fact]
    public async Task GetDatabaseAsync_deserializes_models_dev_api_shape()
    {
        using var http = new HttpClient(new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent(SampleJson),
                Headers = { ETag = new("\"v1\"") }
            }));

        var store = new ModelsDevStore(http, new ModelsDevOptions { UseDiskCache = false });

        var database = await store.GetDatabaseAsync();

        database.Providers.Should().ContainKey("openai");
        database.Providers["openai"].Models["gpt-4o"].Name.Should().Be("GPT-4o");
        database.Providers["openai"].Models["gpt-4o"].ToolCall.Should().BeTrue();
        database.Providers["openai"].Models["gpt-4o"].Cost!.Input.Should().Be(2.5m);
    }

    [Fact]
    public async Task GetDatabaseAsync_uses_fresh_disk_cache_without_http_call()
    {
        var cachePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await WriteCacheAsync(cachePath, DateTimeOffset.UtcNow, "cached");

        using var http = new HttpClient(new ThrowingHandler());
        var store = new ModelsDevStore(http, new ModelsDevOptions { CachePath = cachePath });

        var database = await store.GetDatabaseAsync();

        database.Providers.Should().ContainKey("openai");
    }

    [Fact]
    public async Task GetDatabaseAsync_uses_stale_cache_when_refresh_fails()
    {
        var cachePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await WriteCacheAsync(cachePath, DateTimeOffset.UtcNow.AddDays(-3), "stale");

        using var http = new HttpClient(new ThrowingHandler());
        var store = new ModelsDevStore(http, new ModelsDevOptions
        {
            CachePath = cachePath,
            RefreshInterval = TimeSpan.FromHours(1)
        });

        var database = await store.GetDatabaseAsync();

        database.Providers.Should().ContainKey("openai");
    }

    [Fact]
    public async Task GetDatabaseAsync_sends_etag_and_keeps_cache_on_not_modified()
    {
        var cachePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await WriteCacheAsync(cachePath, DateTimeOffset.UtcNow.AddDays(-3), "\"v1\"");
        var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.NotModified));

        using var http = new HttpClient(handler);
        var store = new ModelsDevStore(http, new ModelsDevOptions
        {
            CachePath = cachePath,
            RefreshInterval = TimeSpan.FromHours(1)
        });

        var database = await store.GetDatabaseAsync();

        database.Providers.Should().ContainKey("openai");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.IfNoneMatch.Should().Contain(h => h.Tag == "\"v1\"");
    }

    [Fact]
    public async Task ResolveModelAliasAsync_returns_pinned_latest_model()
    {
        var store = ModelsDevStore.FromDatabase(new ModelsDevDatabase
        {
            Providers = new Dictionary<string, ModelsDevProvider>(StringComparer.OrdinalIgnoreCase)
            {
                ["anthropic"] = new()
                {
                    Models = new Dictionary<string, ModelsDevModel>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["claude-sonnet-4-5"] = new() { Name = "Claude Sonnet 4.5 (latest)" },
                        ["claude-sonnet-4-5-20250929"] = new() { Name = "Claude Sonnet 4.5" }
                    }
                }
            }
        });

        var resolved = await store.ResolveModelAliasAsync("anthropic", "claude-sonnet-4-5");

        resolved.Should().Be("claude-sonnet-4-5-20250929");
    }

    private static async Task WriteCacheAsync(string cachePath, DateTimeOffset lastRefresh, string etag)
    {
        var store = ModelsDevStore.FromDatabase(SampleDatabase());
        var database = await store.GetDatabaseAsync();
        var cached = new ModelsDevCachedData
        {
            Database = database,
            LastRefresh = lastRefresh,
            ETag = etag
        };

        await using var stream = File.Create(cachePath);
        await JsonSerializer.SerializeAsync(
            stream,
            cached,
            ModelsDevJsonContext.Default.ModelsDevCachedData);
    }

    private static ModelsDevDatabase SampleDatabase()
        => new()
        {
            Providers = new Dictionary<string, ModelsDevProvider>(StringComparer.OrdinalIgnoreCase)
            {
                ["openai"] = new()
                {
                    Models = new Dictionary<string, ModelsDevModel>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["gpt-4o"] = new()
                        {
                            Name = "GPT-4o",
                            ToolCall = true,
                            Modalities = new ModelsDevModalities { Output = ["text"] }
                        }
                    }
                }
            }
        };

    private static StringContent JsonContent(string json)
        => new(json, Encoding.UTF8, "application/json");

    private const string SampleJson = """
        {
          "openai": {
            "models": {
              "gpt-4o": {
                "name": "GPT-4o",
                "family": "gpt",
                "tool_call": true,
                "cost": {
                  "input": 2.5,
                  "output": 10
                },
                "limit": {
                  "context": 128000,
                  "output": 16384
                },
                "modalities": {
                  "input": ["text", "image"],
                  "output": ["text"]
                }
              }
            }
          }
        }
        """;

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new HttpRequestException("offline");
    }
}
