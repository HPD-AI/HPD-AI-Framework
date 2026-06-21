using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using HPD.Agent.Bots.Cards;
using HPD.Agent.Bots.Contracts;
using HPD.Agent.Bots.WhatsApp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Bots.Tests.Unit.WhatsAppBot;

public class WhatsAppBotTests
{
    [Fact]
    public void WhatsAppThreadId_FormatAndParse_RoundTrips()
    {
        var threadId = WhatsAppThreadId.Format("phone123", "15551234567");

        var parsed = WhatsAppThreadId.Parse(threadId);

        threadId.Should().Be("whatsapp:phone123:15551234567");
        parsed.PhoneNumberId.Should().Be("phone123");
        parsed.UserWaId.Should().Be("15551234567");
        parsed.IsDM.Should().BeTrue();
        parsed.ChannelId.Should().Be("whatsapp:phone123");
        WhatsAppThreadId.ChannelIdFromThreadId(threadId).Should().Be("whatsapp:phone123");
    }

    [Fact]
    public void WhatsAppCardConverter_RendersUpToThreeButtonsAsInteractive()
    {
        var card = new CardElement(
            Title: "Deploy {{emoji:rocket}}",
            Subtitle: "Ready",
            Children:
            [
                new CardActions(
                [
                    new CardButton("approve", "Approve", Value: "yes"),
                    new CardButton("deny", "Deny", Value: "no")
                ])
            ]);

        var result = WhatsAppCardConverter.ToWhatsApp(card);

        var interactive = result.Should().BeOfType<WhatsAppCardResult.Interactive>().Subject.Message;
        interactive.Header.Should().Be("Deploy 🚀");
        interactive.Buttons.Should().HaveCount(2);
        interactive.Buttons[0].Title.Should().Be("Approve");
        WhatsAppCardConverter.DecodeCallbackData(interactive.Buttons[0].Id)
            .Should().Be(("approve", "yes"));
    }

    [Fact]
    public void WhatsAppCardConverter_FallsBackToTextWhenTooManyButtons()
    {
        var card = new CardElement(
            Title: "Pick",
            ImageUrl: "https://example.com/root.png",
            Children:
            [
                new CardImage("https://example.com/child.png", AltText: "Preview"),
                new CardActions(
                [
                    new CardButton("a", "A"),
                    new CardButton("b", "B"),
                    new CardButton("c", "C"),
                    new CardButton("d", "D")
                ])
            ]);

        var result = WhatsAppCardConverter.ToWhatsApp(card);

        result.Should().BeOfType<WhatsAppCardResult.Text>()
            .Which.Body.Should().Contain("A | B | C | D")
            .And.Contain("https://example.com/root.png")
            .And.Contain("Preview: https://example.com/child.png");
    }

    [Fact]
    public void WhatsAppCardConverter_TruncatesButtonHeaderAndBody()
    {
        var card = new CardElement(
            Title: new string('h', 80),
            Children:
            [
                new CardText(new string('b', 1100)),
                new CardActions([new CardButton("approve", new string('x', 40))])
            ]);

        var result = WhatsAppCardConverter.ToWhatsApp(card);

        var interactive = result.Should().BeOfType<WhatsAppCardResult.Interactive>().Subject.Message;
        interactive.Header.Should().HaveLength(60).And.EndWith("...");
        interactive.Body.Should().HaveLength(1024).And.EndWith("...");
        interactive.Buttons[0].Title.Should().HaveLength(20).And.EndWith("...");
    }

    [Fact]
    public void WhatsAppFormatConverter_EscapesMarkdownCharactersAndConvertsEmoji()
    {
        new WhatsAppFormatConverter().RenderPlain("ship_this *now* {{emoji:rocket}}")
            .Should().Be(@"ship\_this \*now\* 🚀");
    }

    [Fact]
    public void WhatsAppFormatConverter_RendersTablesAsCodeBlocks()
    {
        var rendered = new WhatsAppFormatConverter().RenderTable(
            ["Name", "Status"],
            [["Deploy", "Ready"]]);

        rendered.Should().StartWith("```");
        rendered.Should().Contain("| Name   | Status |");
        rendered.Should().Contain("| Deploy | Ready  |");
        rendered.Should().EndWith("```");
    }

    [Fact]
    public void WhatsAppBot_VerifySignature_AcceptsValidSignature()
    {
        var bot = CreateBot(appSecret: "secret");
        var body = Encoding.UTF8.GetBytes("""{"hello":"world"}""");
        var signature = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), body)).ToLowerInvariant();

        bot.VerifySignature(body, signature).Should().BeTrue();
        bot.VerifySignature(body, "sha256=bad").Should().BeFalse();
    }

    [Fact]
    public async Task HandleWebhookAsync_GetChallenge_ReturnsChallenge()
    {
        var bot = CreateBot(verifyToken: "verify");
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.QueryString = new QueryString("?hub.mode=subscribe&hub.verify_token=verify&hub.challenge=abc123");
        ctx.Response.Body = new MemoryStream();
        ctx.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

        var result = await bot.HandleWebhookAsync(ctx);

        await result.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Body.Position = 0;
        var response = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        response.Should().Be("abc123");
    }

    [Fact]
    public async Task HandleWebhookAsync_TextMessage_WithValidSignature_Acks()
    {
        var bot = CreateBot(appSecret: "secret");
        var body = TextWebhook();
        var ctx = CreateSignedContext(body, "secret");

        var result = await bot.HandleWebhookAsync(ctx);

        await result.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task HandleWebhookAsync_TextMessage_WithInvalidSignature_ReturnsUnauthorized()
    {
        var bot = CreateBot(appSecret: "secret");
        var ctx = CreateSignedContext(TextWebhook(), "wrong");

        var result = await bot.HandleWebhookAsync(ctx);

        await result.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task HandleWebhookAsync_StatusOnlyWebhook_AcksAndIgnores()
    {
        var bot = CreateBot(appSecret: "secret");
        var ctx = CreateSignedContext(StatusWebhook(), "secret");

        var result = await bot.HandleWebhookAsync(ctx);

        await result.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task HandleWebhookAsync_InteractiveButton_DecodesCallback()
    {
        var bot = CreateBot(appSecret: "secret");
        WhatsAppButtonClickEvent? received = null;
        bot.OnButtonClick += evt => received = evt;

        var body = InteractiveWebhook(WhatsAppCardConverter.EncodeCallbackData("approve", "yes"));
        var ctx = CreateSignedContext(body, "secret");

        var result = await bot.HandleWebhookAsync(ctx);
        await result.ExecuteAsync(ctx);

        received.Should().NotBeNull();
        received!.ActionId.Should().Be("approve");
        received.Value.Should().Be("yes");
        received.ThreadId.Should().Be("whatsapp:phone123:15551234567");
        received.User.FullName.Should().Be("Ada");
    }

    [Fact]
    public async Task HandleWebhookAsync_Reaction_RaisesReactionEvent()
    {
        var bot = CreateBot(appSecret: "secret");
        WhatsAppReactionEvent? received = null;
        bot.OnReaction += evt => received = evt;

        var ctx = CreateSignedContext(ReactionWebhook("👍"), "secret");

        var result = await bot.HandleWebhookAsync(ctx);
        await result.ExecuteAsync(ctx);

        received.Should().NotBeNull();
        received!.MessageId.Should().Be("wamid.original");
        received.Emoji.Should().Be("👍");
        received.Added.Should().BeTrue();
    }

    [Fact]
    public async Task HandleWebhookAsync_BatchedMessages_ProcessesEveryMessageInEnvelope()
    {
        var bot = CreateBot(appSecret: "secret");
        WhatsAppButtonClickEvent? receivedButton = null;
        WhatsAppReactionEvent? receivedReaction = null;
        bot.OnButtonClick += evt => receivedButton = evt;
        bot.OnReaction += evt => receivedReaction = evt;

        var body = BatchedWebhook(
            WhatsAppCardConverter.EncodeCallbackData("approve", "yes"),
            "🔥");
        var ctx = CreateSignedContext(body, "secret");

        var result = await bot.HandleWebhookAsync(ctx);
        await result.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        receivedButton.Should().NotBeNull();
        receivedButton!.ActionId.Should().Be("approve");
        receivedReaction.Should().NotBeNull();
        receivedReaction!.Emoji.Should().Be("🔥");
    }

    [Fact]
    public async Task HandleWebhookAsync_ReactionWithEmptyEmoji_RaisesRemovalEvent()
    {
        var bot = CreateBot(appSecret: "secret");
        WhatsAppReactionEvent? received = null;
        bot.OnReaction += evt => received = evt;

        var ctx = CreateSignedContext(ReactionWebhook(string.Empty), "secret");

        var result = await bot.HandleWebhookAsync(ctx);
        await result.ExecuteAsync(ctx);

        received.Should().NotBeNull();
        received!.Added.Should().BeFalse();
        received.Emoji.Should().BeEmpty();
    }

    [Fact]
    public void WhatsAppBot_ParseMessage_ExtractsMediaAndLocation()
    {
        var bot = CreateBot();
        var message = new WhatsAppInboundMessage(
            From: "15551234567",
            Id: "wamid.1",
            Timestamp: "1700000000",
            Type: "location",
            Location: new WhatsAppLocationContent(1.25, 2.5, "HQ", "Main St"));

        var parsed = bot.ParseMessage(new WhatsAppRawMessage("phone123", message, new WhatsAppContact(new WhatsAppProfile("Ada"), "15551234567")));

        parsed.Text.Should().Be("[HQ: Main St]");
        parsed.Attachments.Should().ContainSingle().Which.Kind.Should().Be("location");
        parsed.Author.FullName.Should().Be("Ada");
    }

    [Fact]
    public async Task WhatsAppBot_DownloadAttachmentsAsync_UsesTypedDataContent()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri == "https://graph.facebook.com/v25.0/media-image")
                return JsonResponse("""{"url":"https://media.example/image","mime_type":"image/png","id":"media-image"}""");

            if (request.RequestUri!.AbsoluteUri == "https://media.example/image")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3])
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var bot = CreateBot(api: CreateApiClient(handler));
        var attachments = new[]
        {
            new WhatsAppAttachment("image", "media-image", "image/png", FileName: "photo.png")
        };

        var downloaded = await bot.DownloadAttachmentsAsync(attachments);

        downloaded.Should().ContainSingle()
            .Which.Should().BeOfType<ImageContent>()
            .Which.Name.Should().Be("photo.png");
        handler.Requests.Select(r => r.RequestUri!.AbsoluteUri).Should().Equal(
            "https://graph.facebook.com/v25.0/media-image",
            "https://media.example/image");
        handler.Requests.Select(r => r.Authorization?.Parameter).Should().OnlyContain(token => token == "token");
    }

    [Fact]
    public async Task WhatsAppBot_DownloadAttachmentsAsync_ConvertsLocationToGeoJsonDataContent()
    {
        var bot = CreateBot(api: CreateApiClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        var attachments = new[]
        {
            new WhatsAppAttachment(
                "location",
                "1.25,2.5",
                "application/geo+json",
                Raw: new WhatsAppLocationAttachment(1.25, 2.5, "HQ", "Main St"))
        };

        var downloaded = await bot.DownloadAttachmentsAsync(attachments);

        var content = downloaded.Should().ContainSingle().Subject;
        content.Should().BeOfType<DataContent>();
        content.MediaType.Should().Be("application/geo+json");
        content.Name.Should().Be("location.geojson");
    }

    [Fact]
    public async Task WhatsAppApiClient_MarkReadAsync_CanIncludeTypingIndicator()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"success":true}"""));
        var api = CreateApiClient(handler);

        await api.MarkReadAsync("wamid.1", showTypingIndicator: true);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Put);
        request.RequestUri!.AbsoluteUri.Should().Be("https://graph.facebook.com/v25.0/phone123/messages");
        request.Body.Should().Contain("\"status\":\"read\"");
        request.Body.Should().Contain("\"message_id\":\"wamid.1\"");
        request.Body.Should().Contain("\"typing_indicator\":{\"type\":\"text\"}");
    }

    [Fact]
    public async Task WhatsAppApiClient_SendTextAsync_PostsGraphMessagePayload()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"messages":[{"id":"wamid.sent"}]}"""));
        var api = CreateApiClient(handler);

        var sent = await api.SendTextAsync("15551234567", "hello");

        sent.Messages.Should().ContainSingle().Which.Id.Should().Be("wamid.sent");
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Body.Should().Contain("\"to\":\"15551234567\"");
        request.Body.Should().Contain("\"type\":\"text\"");
        request.Body.Should().Contain("\"body\":\"hello\"");
    }

    [Fact]
    public async Task WhatsAppApiClient_UsesConfiguredGraphApiBaseUrl()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{"messages":[{"id":"wamid.sent"}]}"""));
        var api = CreateApiClient(handler, cfg =>
        {
            cfg.ApiUrl = "https://graph.example.test/";
            cfg.ApiVersion = "/v99.0/";
        });

        await api.SendTextAsync("15551234567", "hello");

        handler.Requests.Should().ContainSingle()
            .Which.RequestUri!.AbsoluteUri.Should().Be("https://graph.example.test/v99.0/phone123/messages");
    }

    [Fact]
    public async Task WhatsAppBot_UnsupportedOperationsAndPlatformHelpers_MatchCloudApiLimits()
    {
        var bot = CreateBot();
        var threadId = WhatsAppThreadId.Format("phone123", "15551234567");

        Action edit = () => bot.EditMessageAsync(threadId, "wamid.1", "updated");
        Action delete = () => bot.DeleteMessageAsync(threadId, "wamid.1");
        edit.Should().Throw<NotSupportedException>();
        delete.Should().Throw<NotSupportedException>();

        (await bot.OpenDmAsync("15551234567")).Should().Be(threadId);
        HPD.Agent.Bots.WhatsApp.WhatsAppBot.IsDm(threadId).Should().BeTrue();
        HPD.Agent.Bots.WhatsApp.WhatsAppBot.ChannelIdFromThreadId(threadId).Should().Be("whatsapp:phone123");
        (await bot.FetchMessagesAsync(threadId)).Messages.Should().BeEmpty();

        var thread = await bot.FetchThreadAsync(threadId);
        thread.ChannelId.Should().Be("whatsapp:phone123");
        thread.IsDM.Should().BeTrue();

        var channel = await bot.FetchChannelInfoAsync("whatsapp:phone123");
        channel.Id.Should().Be("whatsapp:phone123");
    }

    [Fact]
    public void WhatsAppBot_SplitMessage_PrefersParagraphBoundaries()
    {
        var chunks = HPD.Agent.Bots.WhatsApp.WhatsAppBot.SplitMessage(new string('a', 4090) + "\n\n" + "tail-value");

        chunks.Should().HaveCount(2);
        chunks[0].Should().HaveLength(4090);
        chunks[1].Should().Be("tail-value");
    }

    private static HPD.Agent.Bots.WhatsApp.WhatsAppBot CreateBot(
        string appSecret = "secret",
        string verifyToken = "verify",
        WhatsAppApiClient? api = null)
        => new(Options.Create(new WhatsAppBotConfig
        {
            AccessToken = "token",
            AppSecret = appSecret,
            PhoneNumberId = "phone123",
            VerifyToken = verifyToken,
        }), api: api);

    private static WhatsAppApiClient CreateApiClient(
        HttpMessageHandler handler,
        Action<WhatsAppBotConfig>? configure = null)
    {
        var config = new WhatsAppBotConfig
        {
            AccessToken = "token",
            AppSecret = "secret",
            PhoneNumberId = "phone123",
            VerifyToken = "verify",
        };
        configure?.Invoke(config);
        return new WhatsAppApiClient(new TestHttpClientFactory(handler), Options.Create(config));
    }

    private static DefaultHttpContext CreateSignedContext(string body, string appSecret)
    {
        var ctx = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Request.Method = "POST";
        ctx.Request.Body = new MemoryStream(bytes);
        ctx.Request.ContentLength = bytes.Length;
        ctx.Response.Body = new MemoryStream();
        ctx.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        ctx.Request.Headers["x-hub-signature-256"] = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), bytes)).ToLowerInvariant();
        return ctx;
    }

    private static string TextWebhook() => """
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "waba",
            "changes": [{
              "field": "messages",
              "value": {
                "messaging_product": "whatsapp",
                "metadata": { "display_phone_number": "15550000000", "phone_number_id": "phone123" },
                "contacts": [{ "profile": { "name": "Ada" }, "wa_id": "15551234567" }],
                "messages": [{
                  "from": "15551234567",
                  "id": "wamid.1",
                  "timestamp": "1700000000",
                  "type": "text",
                  "text": { "body": "hello" }
                }]
              }
            }]
          }]
        }
        """;

    private static string InteractiveWebhook(string callbackData) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "waba",
            "changes": [{
              "field": "messages",
              "value": {
                "messaging_product": "whatsapp",
                "metadata": { "display_phone_number": "15550000000", "phone_number_id": "phone123" },
                "contacts": [{ "profile": { "name": "Ada" }, "wa_id": "15551234567" }],
                "messages": [{
                  "from": "15551234567",
                  "id": "wamid.2",
                  "timestamp": "1700000000",
                  "type": "interactive",
                  "interactive": {
                    "type": "button_reply",
                    "button_reply": { "id": {{JsonString(callbackData)}}, "title": "Approve" }
                  }
                }]
              }
            }]
          }]
        }
        """;

    private static string ReactionWebhook(string emoji) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "waba",
            "changes": [{
              "field": "messages",
              "value": {
                "messaging_product": "whatsapp",
                "metadata": { "display_phone_number": "15550000000", "phone_number_id": "phone123" },
                "contacts": [{ "profile": { "name": "Ada" }, "wa_id": "15551234567" }],
                "messages": [{
                  "from": "15551234567",
                  "id": "wamid.3",
                  "timestamp": "1700000000",
                  "type": "reaction",
                  "reaction": { "message_id": "wamid.original", "emoji": {{JsonString(emoji)}} }
                }]
              }
            }]
          }]
        }
        """;

    private static string BatchedWebhook(string callbackData, string emoji) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "waba",
            "changes": [{
              "field": "messages",
              "value": {
                "messaging_product": "whatsapp",
                "metadata": { "display_phone_number": "15550000000", "phone_number_id": "phone123" },
                "contacts": [{ "profile": { "name": "Ada" }, "wa_id": "15551234567" }],
                "messages": [{
                  "from": "15551234567",
                  "id": "wamid.1",
                  "timestamp": "1700000000",
                  "type": "text",
                  "text": { "body": "hello" }
                }, {
                  "from": "15551234567",
                  "id": "wamid.2",
                  "timestamp": "1700000001",
                  "type": "interactive",
                  "interactive": {
                    "type": "button_reply",
                    "button_reply": { "id": {{JsonString(callbackData)}}, "title": "Approve" }
                  }
                }, {
                  "from": "15551234567",
                  "id": "wamid.3",
                  "timestamp": "1700000002",
                  "type": "reaction",
                  "reaction": { "message_id": "wamid.original", "emoji": {{JsonString(emoji)}} }
                }]
              }
            }]
          }]
        }
        """;

    private static string StatusWebhook() => """
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "waba",
            "changes": [{
              "field": "messages",
              "value": {
                "messaging_product": "whatsapp",
                "metadata": { "display_phone_number": "15550000000", "phone_number_id": "phone123" },
                "statuses": [{
                  "id": "wamid.sent",
                  "status": "delivered",
                  "timestamp": "1700000000"
                }]
              }
            }]
          }]
        }
        """;

    private static string JsonString(string value)
        => $"\"{System.Text.Json.JsonEncodedText.Encode(value)}\"";

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            name.Should().Be("whatsapp");
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body, request.Headers.Authorization));
            return handler(request);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri RequestUri,
        string Body,
        System.Net.Http.Headers.AuthenticationHeaderValue? Authorization);
}
