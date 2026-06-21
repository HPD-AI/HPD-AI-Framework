using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Bots.Cards;
using HPD.Agent.Bots.Discord;
using HPD.Agent.Bots.Discord.Gateway;
using HPD.Agent.Bots.Discord.Payloads;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace HPD.Agent.Bots.Tests.Unit;

public class DiscordBotTests
{
    [Fact]
    public void DiscordThreadId_Format_DefaultThreadId_ProducesTrailingColon()
    {
        var key = DiscordThreadId.Format("guild123", "channel456");

        key.Should().Be("discord:guild123:channel456:");
    }

    [Fact]
    public void DiscordThreadId_RoundTripsGuildChannel()
    {
        var key = DiscordThreadId.Format("guild123", "channel456", "");

        var parsed = DiscordThreadId.Parse(key);

        key.Should().Be("discord:guild123:channel456:");
        parsed.GuildId.Should().Be("guild123");
        parsed.ChannelId.Should().Be("channel456");
        parsed.ThreadId.Should().BeEmpty();
        parsed.IsDM.Should().BeFalse();
        parsed.IsThread.Should().BeFalse();
        parsed.PostChannelId.Should().Be("channel456");
    }

    [Fact]
    public void DiscordThreadId_RoundTripsThread()
    {
        var key = DiscordThreadId.Format("guild123", "channel456", "thread789");

        var parsed = DiscordThreadId.Parse(key);

        parsed.IsThread.Should().BeTrue();
        parsed.GuildId.Should().Be("guild123");
        parsed.ChannelId.Should().Be("channel456");
        parsed.ThreadId.Should().Be("thread789");
        parsed.PostChannelId.Should().Be("thread789");
    }

    [Fact]
    public void DiscordThreadId_RoundTripsDm()
    {
        var key = DiscordThreadId.Format("@me", "dm456");

        var parsed = DiscordThreadId.Parse(key);

        key.Should().Be("discord:@me:dm456:");
        parsed.IsDM.Should().BeTrue();
        parsed.IsThread.Should().BeFalse();
        parsed.PostChannelId.Should().Be("dm456");
    }

    [Theory]
    [InlineData("")]
    [InlineData("discord:guild123")]
    [InlineData("slack:C123:1234.5")]
    public void DiscordThreadId_Parse_MalformedValue_ThrowsFormatException(string value)
    {
        var act = () => DiscordThreadId.Parse(value);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void DiscordCardRenderer_RendersEmbedFieldsAndButtons()
    {
        var card = new CardElement(
            Title: "Deploy",
            Subtitle: "Ready to ship",
            Children:
            [
                new CardText("Review the changes."),
                new CardFields([new CardField("Status", "Green")]),
                new CardActions(
                [
                    new CardButton("approve", "Approve", Style: "primary"),
                    new CardButton("docs", "Docs", Url: "https://example.com")
                ])
            ]);

        var (embed, rows) = new DiscordCardRenderer().Render(card);

        embed.Title.Should().Be("Deploy");
        embed.Description.Should().Contain("Ready to ship");
        embed.Description.Should().Contain("Review the changes.");
        embed.Fields.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new DiscordEmbedField("Status", "Green", true));
        rows.Should().ContainSingle();
        rows[0].Components.Should().HaveCount(2);
        rows[0].Components[0].Style.Should().Be(1);
        rows[0].Components[0].CustomId.Should().Be("approve");
        rows[0].Components[1].Style.Should().Be(5);
        rows[0].Components[1].Url.Should().Be("https://example.com");
    }

    [Fact]
    public async Task HandleWebhookAsync_PingWithGatewayToken_RoutesIntegerTypeToPing()
    {
        var bot = CreateBot();
        var ctx = CreateContext("""{"id":"i1","application_id":"app1","type":1,"token":"tok","version":1}""");
        ctx.Request.Headers["X-Discord-Gateway-Token"] = "bot-token";

        var result = await bot.HandleWebhookAsync(ctx);

        await result.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        doc.RootElement.GetProperty("type").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task HandleWebhookAsync_InvalidGatewayToken_ReturnsUnauthorized()
    {
        var bot = CreateBot();
        var ctx = CreateContext("""{"type":1}""");
        ctx.Request.Headers["X-Discord-Gateway-Token"] = "wrong";

        var result = await bot.HandleWebhookAsync(ctx);

        await result.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task HandleWebhookAsync_PingWithValidDiscordSignature_RoutesToPing()
    {
        var body = """{"id":"i1","application_id":"app1","type":1,"token":"tok","version":1}""";
        var timestamp = "1700000000";
        var seed = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        var publicKey = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(seed, 0, publicKey, 0);

        var bot = CreateBot(Convert.ToHexString(publicKey).ToLowerInvariant());
        var ctx = CreateContext(body);
        ctx.Request.Headers["X-Signature-Timestamp"] = timestamp;
        ctx.Request.Headers["X-Signature-Ed25519"] = SignDiscordMessage(seed, timestamp, Encoding.UTF8.GetBytes(body));

        var result = await bot.HandleWebhookAsync(ctx);

        await result.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        doc.RootElement.GetProperty("type").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task HandleWebhookAsync_SlashCommand_ReturnsDeferredResponse()
    {
        var bot = CreateBot();
        var ctx = CreateContext("""
            {
              "id":"i1",
              "application_id":"app1",
              "type":2,
              "token":"tok",
              "version":1,
              "data":{"name":"ask","options":[{"name":"prompt","type":3,"value":"hello"}]},
              "channel_id":"channel1",
              "user":{"id":"u1","username":"user","global_name":"User"}
            }
            """);
        ctx.Request.Headers["X-Discord-Gateway-Token"] = "bot-token";

        var result = await bot.HandleWebhookAsync(ctx);

        await result.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        doc.RootElement.GetProperty("type").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task HandleWebhookAsync_PingWithInvalidDiscordSignature_ReturnsUnauthorized()
    {
        var bodyBytes = Encoding.UTF8.GetBytes("""{"type":1}""");
        var timestamp = "1700000000";
        var seed = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        var publicKey = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(seed, 0, publicKey, 0);

        var bot = CreateBot(Convert.ToHexString(publicKey).ToLowerInvariant());
        var ctx = CreateContext(Encoding.UTF8.GetString(bodyBytes));
        ctx.Request.Headers["X-Signature-Timestamp"] = timestamp;
        ctx.Request.Headers["X-Signature-Ed25519"] = SignDiscordMessage(seed, timestamp, bodyBytes);
        bodyBytes[0] = (byte)' ';
        ctx.Request.Body = new MemoryStream(bodyBytes);

        var result = await bot.HandleWebhookAsync(ctx);

        await result.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task DiscordApiClient_FetchChannelAsync_GetsChannelWithBotAuth()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"id":"c1","type":0,"name":"general","parent_id":"p1"}"""));
        var client = CreateApiClient(handler);

        var channel = await client.FetchChannelAsync("c1", CancellationToken.None);

        channel.Should().BeEquivalentTo(new DiscordChannelInfo("c1", 0, "general", "p1"));
        handler.Requests.Should().ContainSingle()
            .Which.Headers.Authorization?.ToString().Should().Be("Bot bot-token");
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v10/channels/c1");
    }

    [Fact]
    public async Task DiscordApiClient_FetchMessagesAsync_ParsesPageAndCursorIds()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""
            [
              {"id":"m2","channel_id":"c1","author":{"id":"u1","username":"Ada","global_name":"Ada","bot":false},"content":"second"},
              {"id":"m1","channel_id":"c1","author":{"id":"u1","username":"Ada","global_name":"Ada","bot":false},"content":"first"}
            ]
            """));
        var client = CreateApiClient(handler);

        var page = await client.FetchMessagesAsync("c1", 200, "m3", CancellationToken.None);

        page.Items.Should().HaveCount(2);
        page.After.Should().Be("m2");
        page.Before.Should().Be("m1");
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v10/channels/c1/messages?limit=100&before=m3");
    }

    [Fact]
    public async Task DiscordApiClient_FetchMessagesAsync_UsesAfterWhenBeforeIsAbsent()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("[]"));
        var client = CreateApiClient(handler);

        await client.FetchMessagesAsync("c1", 50, before: null, after: "m1", CancellationToken.None);

        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v10/channels/c1/messages?limit=50&after=m1");
    }

    [Fact]
    public async Task DiscordApiClient_CreateThreadAsync_WhenThreadAlreadyExists_ReturnsStarterMessageId()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"code":160004,"message":"A thread has already been created for this message"}""",
                Encoding.UTF8,
                "application/json"),
        });
        var client = CreateApiClient(handler);

        var threadId = await client.CreateThreadAsync("c1", "m1", "Reply", CancellationToken.None);

        threadId.Should().Be("m1");
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v10/channels/c1/messages/m1/threads");
        handler.RequestBodies[0].Should().Contain("auto_archive_duration");
    }

    [Fact]
    public async Task DiscordApiClient_ListThreadsAsync_MergesActiveAndArchivedThreads()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery == "/api/v10/guilds/g1/threads/active")
            {
                return JsonResponse("""
                    {"threads":[
                      {"id":"t1","parent_id":"c1","name":"active"},
                      {"id":"other","parent_id":"c2","name":"skip"}
                    ]}
                    """);
            }

            return JsonResponse("""{"threads":[{"id":"t2","parent_id":"c1","name":"archived"}]}""");
        });
        var client = CreateApiClient(handler);

        var page = await client.ListThreadsAsync("g1", "c1", 50, CancellationToken.None);

        page.Items.Should().BeEquivalentTo(
        [
            new DiscordThreadSummary("t1", "c1", "active"),
            new DiscordThreadSummary("t2", "c1", "archived"),
        ]);
        handler.Requests.Select(r => r.RequestUri!.PathAndQuery).Should().Equal(
            "/api/v10/guilds/g1/threads/active",
            "/api/v10/channels/c1/threads/archived/public?limit=50");
    }

    [Fact]
    public async Task DiscordApiClient_FetchUserAsync_ParsesUserProfile()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"id":"u1","username":"Ada","global_name":"Ada Lovelace","bot":false}"""));
        var client = CreateApiClient(handler);

        var user = await client.FetchUserAsync("u1", CancellationToken.None);

        user.Should().BeEquivalentTo(new DiscordUserProfile("u1", "Ada", "Ada Lovelace", false));
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v10/users/u1");
    }

    [Fact]
    public async Task DiscordApiClient_PostMessageWithFilesAsync_SendsMultipartPayloadJsonAndFile()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"id":"m1"}"""));
        var client = CreateApiClient(handler);
        await using var file = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        var messageId = await client.PostMessageWithFilesAsync(
            "c1",
            new DiscordMessagePayload(Content: "with file"),
            [new DiscordFileUpload("hello.txt", file, "text/plain")],
            CancellationToken.None);

        messageId.Should().Be("m1");
        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.RequestUri!.PathAndQuery.Should().Be("/api/v10/channels/c1/messages");
        request.Content.Should().BeOfType<MultipartFormDataContent>();
        handler.RequestBodies.Should().ContainSingle();
        var body = handler.RequestBodies[0];
        body.Should().Contain("payload_json");
        body.Should().Contain("files[0]");
        body.Should().Contain("hello.txt");
    }

    [Fact]
    public async Task DiscordApiClient_SendGatewayEventAsync_ForwardsHeadersAndJsonBody()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var client = CreateApiClient(handler);
        var body = Encoding.UTF8.GetBytes("""{"id":"m1"}""");

        await client.SendGatewayEventAsync("https://example.test/webhooks/discord", "GATEWAY_MESSAGE_CREATE", body, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.RequestUri!.ToString().Should().Be("https://example.test/webhooks/discord");
        request.Headers.GetValues("X-Discord-Gateway-Token").Should().ContainSingle("bot-token");
        request.Headers.GetValues("X-Discord-Gateway-Event").Should().ContainSingle("GATEWAY_MESSAGE_CREATE");
        handler.RequestBodies.Should().ContainSingle("""{"id":"m1"}""");
    }

    [Fact]
    public async Task DiscordGatewayClient_GetGatewayUriAsync_UsesBotEndpointAndCachesResult()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"url":"wss://gateway.discord.gg"}"""));
        var client = new DiscordGatewayClient(Options.Create(new DiscordBotConfig
        {
            ApplicationId = "app1",
            BotToken = "bot-token",
            GatewayToken = "gateway-token",
            PublicKey = new string('a', 64),
        }), new TestHttpClientFactory(handler));

        var first = await client.GetGatewayUriAsync(CancellationToken.None);
        var second = await client.GetGatewayUriAsync(CancellationToken.None);

        first.Should().Be(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"));
        second.Should().Be(first);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://discord.com/api/v10/gateway/bot");
        handler.Requests[0].Headers.Authorization?.ToString().Should().Be("Bot gateway-token");
    }

    [Fact]
    public async Task HandleWebhookAsync_GatewayReactionInThread_FetchesParentAndNormalizesEmoji()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"id":"thread1","type":11,"name":"thread","parent_id":"parent1"}"""));
        var api = CreateApiClient(handler);
        var bot = CreateBot(api: api);
        DiscordReactionEvent? received = null;
        bot.OnReaction += evt => received = evt;

        var ctx = CreateContext("""
            {
              "user_id":"u1",
              "channel_id":"thread1",
              "message_id":"m1",
              "guild_id":"g1",
              "channel_type":11,
              "emoji":{"name":"👍"},
              "user":{"id":"u1","username":"Ada","global_name":"Ada","bot":false}
            }
            """);
        ctx.Request.Headers["X-Discord-Gateway-Token"] = "bot-token";
        ctx.Request.Headers["X-Discord-Gateway-Event"] = "GATEWAY_MESSAGE_REACTION_ADD";

        var result = await bot.HandleWebhookAsync(ctx);

        await result.ExecuteAsync(ctx);
        received.Should().NotBeNull();
        received!.ThreadId.Should().Be("discord:g1:parent1:thread1");
        received.Emoji.Should().Be("thumbs_up");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v10/channels/thread1");
    }

    private static DiscordBot CreateBot(string? publicKey = null, DiscordApiClient? api = null)
        => new(Options.Create(new DiscordBotConfig
        {
            ApplicationId = "app1",
            BotToken = "bot-token",
            PublicKey = publicKey ?? new string('a', 64),
        }),
        api: api);

    private static DiscordApiClient CreateApiClient(FakeHttpMessageHandler handler)
        => new(Options.Create(new DiscordBotConfig
        {
            ApplicationId = "app1",
            BotToken = "bot-token",
            PublicKey = new string('a', 64),
        }), new TestHttpClientFactory(handler));

    private static string SignDiscordMessage(byte[] seed, string timestamp, byte[] bodyBytes)
    {
        var publicKey = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(seed, 0, publicKey, 0);

        var timestampBytes = Encoding.UTF8.GetBytes(timestamp);
        var message = new byte[timestampBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(timestampBytes, 0, message, 0, timestampBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, message, timestampBytes.Length, bodyBytes.Length);

        var signature = new byte[Ed25519.SignatureSize];
        Ed25519.Sign(seed, 0, publicKey, 0, message, 0, message.Length, signature, 0);
        return Convert.ToHexString(signature).ToLowerInvariant();
    }

    private static DefaultHttpContext CreateContext(string json)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        ctx.RequestServices = new ServiceCollection()
            .AddOptions()
            .AddLogging()
            .BuildServiceProvider();
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            _ = name;
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return handler(request);
        }
    }
}
