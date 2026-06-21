using System.Text;
using FluentAssertions;
using HPD.Agent.Bots.Cards;
using HPD.Agent.Bots.Contracts;
using HPD.Agent.Bots.Streaming;
using HPD.Agent.Bots.Telegram;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using ChatType = Telegram.Bot.Types.Enums.ChatType;
using TelegramBotAdapter = HPD.Agent.Bots.Telegram.TelegramBot;

namespace HPD.Agent.Bots.Tests.Unit.TelegramBot;

public class TelegramBotTests
{
    [Fact]
    public void TelegramThreadId_FormatThread_WithoutMessageThreadId_CollapsesToChatId()
    {
        var key = TelegramThreadId.FormatThread(123, null);

        key.Should().Be("telegram:123");
        TelegramThreadId.ParseFlexible(key).Should().Be(new TelegramThreadId("123", null));
    }

    [Fact]
    public void TelegramThreadId_FormatThread_WithMessageThreadId_RoundTrips()
    {
        var key = TelegramThreadId.FormatThread("-100123", "99");

        var parsed = TelegramThreadId.ParseFlexible(key);

        key.Should().Be("telegram:-100123:99");
        parsed.ChatId.Should().Be("-100123");
        parsed.MessageThreadId.Should().Be("99");
        parsed.IsDM.Should().BeFalse();
        parsed.ChannelId.Should().Be("telegram:-100123");
    }

    [Fact]
    public void TelegramCardConverter_RendersCallbackAndUrlButtons()
    {
        var card = new CardElement(
            Children:
            [
                new CardActions(
                [
                    new CardButton("approve", "Approve", Value: "yes"),
                    new CardButton("docs", "Docs", Url: "https://example.com")
                ])
            ]);

        var keyboard = TelegramCardConverter.ToInlineKeyboard(card);
        var buttons = keyboard!.InlineKeyboard.Single().ToArray();

        buttons.Should().HaveCount(2);
        buttons[0].Text.Should().Be("Approve");
        TelegramCardConverter.DecodeCallbackData(buttons[0].CallbackData)
            .Should().Be(("approve", "yes"));
        buttons[1].Text.Should().Be("Docs");
        buttons[1].Url.Should().Be("https://example.com");
    }

    [Fact]
    public void TelegramCardConverter_ConvertsKnownEmojiPlaceholdersInButtonLabels()
    {
        var card = new CardElement(
            Children:
            [
                new CardActions(
                [
                    new CardButton("ship", "Ship {{emoji:rocket}}")
                ])
            ]);

        var keyboard = TelegramCardConverter.ToInlineKeyboard(card);

        keyboard!.InlineKeyboard.Single().Single().Text.Should().Be("Ship 🚀");
    }

    [Fact]
    public void TelegramCardConverter_RejectsCallbackPayloadsOverTelegramLimit()
    {
        var act = () => TelegramCardConverter.EncodeCallbackData(new string('a', 70), null);

        act.Should().Throw<BotValidationException>()
            .WithMessage("*64 bytes*");
    }

    [Fact]
    public void TelegramFormatConverter_RenderCardFallback_ConvertsEmojiPlaceholders()
    {
        var card = new CardElement(
            Title: "Deploy {{emoji:rocket}}",
            Subtitle: "Ready {{emoji:thumbs_up}}");

        var rendered = new TelegramFormatConverter().RenderCardFallback(card);

        rendered.Should().Contain("Deploy 🚀");
        rendered.Should().Contain("Ready 👍");
    }

    [Fact]
    public void TelegramMarkdownV2_EscapesSpecialCharacters()
    {
        var escaped = TelegramMarkdownV2.EscapeText("hello_world! [x](y)");

        escaped.Should().Be("""hello\_world\! \[x\]\(y\)""");
    }

    [Fact]
    public void TelegramMarkdownV2_Truncate_DoesNotLeaveOrphanEscape()
    {
        var truncated = TelegramMarkdownV2.Truncate(@"abc\defghij", 10, TelegramRenderMode.MarkdownV2);

        truncated.Should().NotEndWith(@"\");
        truncated.Should().EndWith(@"\.\.\.");
    }

    [Fact]
    public void TelegramBot_SplitMessage_PrefersParagraphBoundaries()
    {
        var chunks = TelegramBotAdapter.SplitMessage(new string('a', 4090) + "\n\n" + "tail-value");

        chunks.Should().HaveCount(2);
        chunks[0].Should().HaveLength(4090);
        chunks[1].Should().Be("tail-value");
    }

    [Fact]
    public async Task HandleWebhookAsync_MessageWithValidSecret_RoutesAndCachesMessage()
    {
        var bot = CreateBot(secretToken: "secret");
        var ctx = CreateContext(
            """
            {
              "update_id": 1,
              "message": {
                "message_id": 42,
                "date": 1700000000,
                "chat": { "id": 123, "type": "private", "first_name": "E" },
                "from": { "id": 7, "is_bot": false, "first_name": "E" },
                "text": "hello"
              }
            }
            """,
            secretToken: "secret");

        var result = await bot.HandleWebhookAsync(ctx);

        await result.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(200);

        var cached = await bot.FetchMessagesAsync("telegram:123", new TelegramFetchOptions());
        cached.Messages.Should().ContainSingle();
        cached.Messages[0].Id.Should().Be("123:42");
        cached.Messages[0].Text.Should().Be("hello");
        cached.Messages[0].Author.UserId.Should().Be("7");
    }

    [Fact]
    public async Task HandleWebhookAsync_MessageWithInvalidSecret_ReturnsUnauthorized()
    {
        var bot = CreateBot(secretToken: "secret");
        var ctx = CreateContext("""{"update_id":1,"message":{}}""", secretToken: "wrong");

        var result = await bot.HandleWebhookAsync(ctx);

        await result.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task HandleWebhookAsync_EditedMessage_UpsertsCachedMessage()
    {
        var bot = CreateBot();

        await ExecuteWebhookAsync(bot, MessageUpdate(messageId: 42, text: "before"));
        await ExecuteWebhookAsync(bot,
            """
            {
              "update_id": 2,
              "edited_message": {
                "message_id": 42,
                "date": 1700000000,
                "edit_date": 1700000100,
                "chat": { "id": 123, "type": "private", "first_name": "E" },
                "from": { "id": 7, "is_bot": false, "first_name": "E" },
                "text": "after"
              }
            }
            """);

        var cached = await bot.FetchMessagesAsync("telegram:123", new TelegramFetchOptions());

        cached.Messages.Should().ContainSingle();
        cached.Messages[0].Text.Should().Be("after");
        cached.Messages[0].Edited.Should().BeTrue();
    }

    [Fact]
    public async Task HandleWebhookAsync_CallbackQuery_DecodesPayloadAndRaisesButtonEvent()
    {
        var fakeBot = new FakeTelegramBotClient();
        var bot = CreateBot(bot: fakeBot);
        TelegramButtonClickEvent? received = null;
        bot.OnButtonClick += evt => received = evt;

        await ExecuteWebhookAsync(bot,
            $$"""
            {
              "update_id": 3,
              "callback_query": {
                "id": "cb1",
                "from": { "id": 7, "is_bot": false, "first_name": "E" },
                "message": {
                  "message_id": 42,
                  "date": 1700000000,
                  "chat": { "id": 123, "type": "private", "first_name": "E" },
                  "text": "choose"
                },
                "data": {{JsonString(TelegramCardConverter.EncodeCallbackData("approve", "yes"))}}
              }
            }
            """);

        fakeBot.Requests.Should().Contain(request => request.GetType().Name == "AnswerCallbackQueryRequest");
        received.Should().NotBeNull();
        received!.ActionId.Should().Be("approve");
        received.Value.Should().Be("yes");
        received.ThreadId.Should().Be("telegram:123");
        received.MessageId.Should().Be("123:42");
        received.User.UserId.Should().Be("7");
    }

    [Fact]
    public async Task HandleWebhookAsync_MessageReaction_DiffsAddedAndRemovedReactions()
    {
        var bot = CreateBot();
        var received = new List<TelegramReactionEvent>();
        bot.OnReaction += received.Add;

        await ExecuteWebhookAsync(bot,
            """
            {
              "update_id": 4,
              "message_reaction": {
                "chat": { "id": -100123, "type": "supergroup", "title": "Ops" },
                "message_id": 42,
                "date": 1700000000,
                "user": { "id": 7, "is_bot": false, "first_name": "E" },
                "old_reaction": [{ "type": "emoji", "emoji": "👎" }],
                "new_reaction": [{ "type": "emoji", "emoji": "👍" }]
              }
            }
            """);

        received.Should().HaveCount(2);
        received.Should().Contain(evt => evt.Emoji == "👍" && evt.Added);
        received.Should().Contain(evt => evt.Emoji == "👎" && !evt.Added);
        received.Should().OnlyContain(evt => evt.ThreadId == "telegram:-100123" && evt.MessageId == "-100123:42");
    }

    [Fact]
    public async Task HandleWebhookAsync_UnknownUpdate_AcksAndDoesNotCache()
    {
        var bot = CreateBot();
        var ctx = await ExecuteWebhookAsync(bot, """{"update_id":5,"poll":{"id":"p1","question":"q","options":[],"total_voter_count":0,"is_closed":false,"is_anonymous":true,"type":"regular","allows_multiple_answers":false}}""");

        ctx.Response.StatusCode.Should().Be(200);
        var cached = await bot.FetchMessagesAsync("telegram:123", new TelegramFetchOptions());
        cached.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseTelegramMessage_ReconstructsEntitiesAndMentionFlag()
    {
        var bot = CreateBot();
        await ExecuteWebhookAsync(bot,
            """
            {
              "update_id": 6,
              "message": {
                "message_id": 42,
                "date": 1700000000,
                "chat": { "id": -100123, "type": "supergroup", "title": "Ops" },
                "from": { "id": 7, "is_bot": false, "first_name": "E" },
                "text": "@hpd_bot ship now",
                "entities": [
                  { "type": "mention", "offset": 0, "length": 8 },
                  { "type": "bold", "offset": 9, "length": 4 },
                  { "type": "italic", "offset": 14, "length": 3 }
                ]
              }
            }
            """);

        var cached = await bot.FetchMessagesAsync("telegram:-100123", new TelegramFetchOptions());

        cached.Messages.Should().ContainSingle();
        cached.Messages[0].Text.Should().Be("@hpd_bot **ship** _now_");
        cached.Messages[0].IsMention.Should().BeTrue();
    }

    [Fact]
    public async Task ParseTelegramMessage_ExtractsDocumentAttachment()
    {
        var bot = CreateBot();
        await ExecuteWebhookAsync(bot,
            """
            {
              "update_id": 7,
              "message": {
                "message_id": 42,
                "date": 1700000000,
                "chat": { "id": 123, "type": "private", "first_name": "E" },
                "from": { "id": 7, "is_bot": false, "first_name": "E" },
                "caption": "see file",
                "document": {
                  "file_id": "file123",
                  "file_unique_id": "uniq123",
                  "file_name": "report.pdf",
                  "mime_type": "application/pdf",
                  "file_size": 12
                }
              }
            }
            """);

        var cached = await bot.FetchMessagesAsync("telegram:123", new TelegramFetchOptions());

        cached.Messages.Should().ContainSingle();
        cached.Messages[0].Text.Should().Be("see file");
        cached.Messages[0].Attachments.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new
            {
                Kind = "file",
                FileId = "file123",
                FileUniqueId = "uniq123",
                Size = 12L,
                Name = "report.pdf",
                MimeType = "application/pdf",
            });
    }

    [Fact]
    public async Task FetchMessagesAsync_PaginatesBackwardAndForward()
    {
        var bot = CreateBot();
        await ExecuteWebhookAsync(bot, MessageUpdate(messageId: 1, text: "one", date: 1700000001));
        await ExecuteWebhookAsync(bot, MessageUpdate(messageId: 2, text: "two", date: 1700000002));
        await ExecuteWebhookAsync(bot, MessageUpdate(messageId: 3, text: "three", date: 1700000003));

        var backward = await bot.FetchMessagesAsync("telegram:123", new TelegramFetchOptions(Limit: 2));
        var forward = await bot.FetchMessagesAsync("telegram:123", new TelegramFetchOptions(
            Limit: 2,
            Cursor: "123:1",
            Direction: TelegramFetchDirection.Forward));

        backward.Messages.Select(message => message.Text).Should().Equal("two", "three");
        backward.NextCursor.Should().Be("123:2");
        forward.Messages.Select(message => message.Text).Should().Equal("two", "three");
        forward.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task FetchChannelMessagesAsync_AggregatesAcrossForumTopics()
    {
        var bot = CreateBot();
        await ExecuteWebhookAsync(bot, MessageUpdate(messageId: 1, text: "root", chatId: -100123));
        await ExecuteWebhookAsync(bot, MessageUpdate(messageId: 2, text: "topic", chatId: -100123, messageThreadId: 77));

        var result = await bot.FetchChannelMessagesAsync("telegram:-100123", new TelegramFetchOptions());

        result.Messages.Select(message => message.Text).Should().Equal("root", "topic");
    }

    [Fact]
    public void TelegramFormatConverter_RenderTable_UsesEscapedCodeBlock()
    {
        var rendered = new TelegramFormatConverter().RenderTable(
            ["Name", "Status"],
            [["API", "green.ok"]]);

        rendered.Should().StartWith("```\n");
        rendered.Should().EndWith("\n```");
        rendered.Should().Contain("green.ok");
    }

    [Fact]
    public async Task TelegramBot_OpenDmAsync_FormatsUserIdAsPrivateChatThread()
    {
        var bot = CreateBot();

        var result = await bot.OpenDmAsync("123");

        result.Should().Be("telegram:123");
    }

    [Fact]
    public async Task TelegramBot_GetUserAsync_MapsPrivateChatAndIgnoresGroups()
    {
        var fakeBot = new FakeTelegramBotClient
        {
            ChatResponse = new ChatFullInfo
            {
                Id = 123,
                Type = ChatType.Private,
                FirstName = "Ada",
                LastName = "Lovelace",
                Username = "ada",
            },
        };
        var bot = CreateBot(bot: fakeBot);

        var user = await bot.GetUserAsync("123");

        user.Should().BeEquivalentTo(new TelegramUserInfo("123", "ada", "Ada Lovelace", false));

        fakeBot.ChatResponse = new ChatFullInfo
        {
            Id = -100123,
            Type = ChatType.Supergroup,
            Title = "Ops",
        };

        (await bot.GetUserAsync("-100123")).Should().BeNull();
    }

    [Fact]
    public async Task TelegramBot_FetchThreadAndChannelInfo_UseTelegramChatMetadata()
    {
        var fakeBot = new FakeTelegramBotClient
        {
            ChatResponse = new ChatFullInfo
            {
                Id = -100123,
                Type = ChatType.Supergroup,
                Title = "Ops",
            },
            MemberCount = 42,
        };
        var bot = CreateBot(bot: fakeBot);

        var thread = await bot.FetchThreadAsync("telegram:-100123:77");
        var channel = await bot.FetchChannelInfoAsync("telegram:-100123");

        thread.Id.Should().Be("telegram:-100123:77");
        thread.ChannelId.Should().Be("telegram:-100123");
        thread.ChannelName.Should().Be("Ops");
        thread.MessageThreadId.Should().Be("77");
        thread.IsDM.Should().BeFalse();

        channel.Id.Should().Be("telegram:-100123");
        channel.Name.Should().Be("Ops");
        channel.MemberCount.Should().Be(42);
        channel.IsDM.Should().BeFalse();
    }

    [Fact]
    public async Task TelegramBot_PostChannelMessage_DelegatesToTelegramSendMessage()
    {
        var fakeBot = new FakeTelegramBotClient
        {
            MessageResponse = new Message
            {
                Id = 99,
                Date = DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime,
                Chat = new Chat
                {
                    Id = -100123,
                    Type = ChatType.Supergroup,
                    Title = "Ops",
                },
                Text = "hello channel",
            },
        };
        var bot = CreateBot(bot: fakeBot);

        var sent = await bot.PostChannelMessageAsync("telegram:-100123", "hello channel");

        sent.Id.Should().Be("-100123:99");
        sent.ThreadId.Should().Be("telegram:-100123");
        fakeBot.Requests.Should().Contain(request => request.GetType().Name == "SendMessageRequest");
    }

    [Fact]
    public async Task TelegramBot_PostMessage_SplitsLongMessagesAndPutsKeyboardOnLastChunk()
    {
        var fakeBot = new FakeTelegramBotClient();
        var bot = CreateBot(bot: fakeBot);
        var text = new string('a', 4090) + "\n\n" + "tail-value";
        var card = new CardElement(
            Children:
            [
                new CardText(text),
                new CardActions([new CardButton("approve", "Approve")])
            ]);

        await bot.PostMessageAsync("telegram:123", "ignored when card fallback is present", card);

        var sendRequests = fakeBot.Requests
            .Where(request => request.GetType().Name == "SendMessageRequest")
            .ToList();
        sendRequests.Should().HaveCount(2);
        GetRequestProperty<string>(sendRequests[0], "Text").Should().HaveLength(4090);
        GetRequestProperty<object?>(sendRequests[0], "ReplyMarkup").Should().BeNull();
        GetRequestProperty<string>(sendRequests[1], "Text").Should().Be("tail-value");
        GetRequestProperty<object?>(sendRequests[1], "ReplyMarkup").Should().NotBeNull();
    }

    [Fact]
    public void TelegramBot_ChannelHelpers_ParseThreadIds()
    {
        TelegramBotAdapter.ChannelIdFromThreadId("telegram:-100123:77").Should().Be("telegram:-100123");
        TelegramBotAdapter.IsDm("telegram:123").Should().BeTrue();
        TelegramBotAdapter.IsDm("telegram:-100123:77").Should().BeFalse();
    }

    [Fact]
    public async Task TelegramBot_ParseMessage_CachesParsedRawMessage()
    {
        var bot = CreateBot();
        var raw = new Message
        {
            Id = 42,
            Date = DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime,
            Chat = new Chat { Id = 123, Type = ChatType.Private, FirstName = "E" },
            From = new User { Id = 7, IsBot = false, FirstName = "E" },
            Text = "from raw",
        };

        var parsed = bot.ParseMessage(raw);
        var cached = await bot.FetchMessageAsync("telegram:123", "123:42");

        parsed.Text.Should().Be("from raw");
        cached.Should().NotBeNull();
        cached!.Text.Should().Be("from raw");
    }

    [Fact]
    public void TelegramBot_RenderFormatted_EscapesMarkdownV2Text()
    {
        var bot = CreateBot();

        bot.RenderFormatted("hello_world!").Should().Be("""hello\_world\!""");
    }

    [Fact]
    public async Task TelegramBot_AddReaction_ConvertsKnownEmojiPlaceholders()
    {
        var fakeBot = new FakeTelegramBotClient();
        var bot = CreateBot(bot: fakeBot);

        await bot.AddReactionAsync("telegram:123", "123:42", "{{emoji:thumbs_up}}");

        var request = fakeBot.Requests.Single(r => r.GetType().Name == "SetMessageReactionRequest");
        request.Should().BeEquivalentTo(new
        {
            Reaction = new[]
            {
                new { Emoji = "👍" },
            },
        });
    }

    [Fact]
    public async Task TelegramPollingService_AutoMode_PollsOnlyWhenNoWebhookIsConfigured()
    {
        var fakeBot = new FakeTelegramBotClient
        {
            WebhookInfo = new WebhookInfo { Url = "https://example.com/webhook" },
        };

        var withWebhook = await TelegramPollingService.ShouldPollAsync(fakeBot, new TelegramBotConfig
        {
            BotToken = "123456:TEST",
            Mode = TelegramBotMode.Auto,
        });

        fakeBot.WebhookInfo = new WebhookInfo { Url = string.Empty };
        var withoutWebhook = await TelegramPollingService.ShouldPollAsync(fakeBot, new TelegramBotConfig
        {
            BotToken = "123456:TEST",
            Mode = TelegramBotMode.Auto,
        });

        withWebhook.Should().BeFalse();
        withoutWebhook.Should().BeTrue();
    }

    [Fact]
    public void AddTelegramBot_WithInfrastructure_RegistersTelegramServicesAndStreamingOptions()
    {
        var services = new ServiceCollection();

        services.AddTelegramBot(options =>
        {
            options.BotToken = "123456:TEST";
            options.UserName = "hpd_bot";
        }, registerInfrastructure: true);
        using var provider = services.BuildServiceProvider();

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(TelegramBotAdapter));
        provider.GetRequiredService<ITelegramBotClient>().Should().NotBeNull();
        provider.GetRequiredService<TelegramFormatConverter>().Should().NotBeNull();
        provider.GetRequiredService<IOptionsMonitor<BotStreamingOptions>>()
            .Get("telegram")
            .Strategy.Should().Be(StreamingStrategy.PostAndEdit);
    }

    [Fact]
    public void AddTelegramBotWithPolling_RegistersPollingHostedService()
    {
        var services = new ServiceCollection();

        services.AddTelegramBotWithPolling(options =>
        {
            options.BotToken = "123456:TEST";
            options.Mode = TelegramBotMode.Polling;
        });

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(TelegramPollingService));
    }

    private static TelegramBotAdapter CreateBot(
        string? secretToken = null,
        ITelegramBotClient? bot = null)
        => new(Options.Create(new TelegramBotConfig
        {
            BotToken = "123456:TEST",
            SecretToken = secretToken,
            UserName = "hpd_bot",
        }), bot: bot);

    private static async Task<DefaultHttpContext> ExecuteWebhookAsync(
        TelegramBotAdapter bot,
        string body,
        string? secretToken = null)
    {
        var ctx = CreateContext(body, secretToken);
        var result = await bot.HandleWebhookAsync(ctx);
        await result.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(200);
        return ctx;
    }

    private static DefaultHttpContext CreateContext(string body, string? secretToken = null)
    {
        var ctx = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Request.Body = new MemoryStream(bytes);
        ctx.Request.ContentLength = bytes.Length;
        ctx.Response.Body = new MemoryStream();
        ctx.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        if (secretToken is not null)
            ctx.Request.Headers["x-telegram-bot-api-secret-token"] = secretToken;

        return ctx;
    }

    private static string MessageUpdate(
        int messageId,
        string text,
        long chatId = 123,
        int? messageThreadId = null,
        int date = 1700000000)
    {
        var threadJson = messageThreadId is null ? string.Empty : $""","message_thread_id":{messageThreadId}""";
        return $$"""
        {
          "update_id": {{messageId}},
          "message": {
            "message_id": {{messageId}},
            "date": {{date}},
            "chat": { "id": {{chatId}}, "type": "{{(chatId < 0 ? "supergroup" : "private")}}", "first_name": "E", "title": "Ops" },
            "from": { "id": 7, "is_bot": false, "first_name": "E" },
            "text": {{JsonString(text)}}{{threadJson}}
          }
        }
        """;
    }

    private static string JsonString(string value)
        => $"\"{System.Text.Json.JsonEncodedText.Encode(value)}\"";

    private static T? GetRequestProperty<T>(object request, string name)
        => (T?)request.GetType().GetProperty(name)?.GetValue(request);

    private sealed class FakeTelegramBotClient : ITelegramBotClient
    {
        public List<object> Requests { get; } = [];
        public ChatFullInfo ChatResponse { get; set; } = new()
        {
            Id = 123,
            Type = ChatType.Private,
            FirstName = "E",
        };
        public int MemberCount { get; set; } = 1;
        public WebhookInfo WebhookInfo { get; set; } = new() { Url = string.Empty };
        public Message MessageResponse { get; set; } = new()
        {
            Id = 42,
            Date = DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime,
            Chat = new Chat { Id = 123, Type = ChatType.Private, FirstName = "E" },
            Text = "ok",
        };
        public TGFile FileResponse { get; set; } = new()
        {
            FileId = "file123",
            FileUniqueId = "uniq123",
            FilePath = "file/path",
        };

        public bool LocalBotServer => false;
        public long BotId => 123456;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
        public IExceptionParser ExceptionsParser { get; set; } = new DefaultExceptionParser();

        public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest;
        public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived;

        public Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = OnMakingApiRequest;
            _ = OnApiResponseReceived;
            _ = cancellationToken;
            Requests.Add(request);
            object response = request.GetType().Name switch
            {
                "GetChatRequest" => ChatResponse,
                "GetChatMemberCountRequest" => MemberCount,
                "GetWebhookInfoRequest" => WebhookInfo,
                "SendMessageRequest" => MessageResponse,
                "EditMessageTextRequest" => MessageResponse,
                "SendDocumentRequest" => MessageResponse,
                "GetFileRequest" => FileResponse,
                _ when typeof(TResponse) == typeof(bool) => true,
                _ => default(TResponse)!,
            };
            return Task.FromResult((TResponse)response);
        }

        public Task<bool> TestApi(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
