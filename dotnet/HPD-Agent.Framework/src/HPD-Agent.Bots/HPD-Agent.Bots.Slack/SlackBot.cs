using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Web;
using HPD.Agent;
using HPD.Agent.Bots.Cards;
using HPD.Agent.Bots.AspNetCore.Verification;
using HPD.Agent.Bots.Session;
using HPD.Agent.Bots.Slack.Payloads;
using HPD.Agent.Bots.Slack.SocketMode;
using HPD.Agent.Bots.Streaming;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

[assembly: InternalsVisibleTo("HPD-Agent.Bots.Tests")]

namespace HPD.Agent.Bots.Slack;

// ── Permission context ─────────────────────────────────────────────────────────

/// <summary>
/// Carries the Slack context needed to post a permission request message.
/// Passed to <see cref="SlackBot.RenderPermissionAsync"/> by <c>StreamToSlackAsync</c>.
/// </summary>
public record SlackPermissionContext(
    string Channel,
    string ThreadTs,
    string SessionId,
    CancellationToken RequestAborted
);

// ── Bot event types ────────────────────────────────────────────────────────

/// <summary>Raised when a slash command is received.</summary>
public record SlackSlashCommandReceivedEvent(
    SlackSlashCommandPayload Payload,
    string UserName);

/// <summary>Raised when a Slack reaction is added or removed on any message.</summary>
public record SlackReactionReceivedEvent(
    SlackReactionEvent Payload,
    string? TeamId);

/// <summary>Raised when a Slack block action is NOT a permission response.</summary>
public record SlackBlockActionReceivedEvent(
    SlackAction Action,
    SlackBlockActionsPayload Payload);

/// <summary>Raised on view_submission.</summary>
public record SlackViewSubmittedEvent(
    string CallbackId,
    string ViewId,
    IReadOnlyDictionary<string, string> Values,
    string? PrivateMetadata,
    string? ContextId,
    SlackUser User);

/// <summary>Raised on view_closed.</summary>
public record SlackViewClosedEvent(
    string CallbackId,
    string ViewId,
    string? PrivateMetadata,
    string? ContextId,
    SlackUser User);

/// <summary>Raised when assistant thread context changes (user navigates channels).</summary>
public record SlackAssistantContextChangedReceivedEvent(
    SlackAssistantContextChangedEvent Payload);

/// <summary>Raised when user opens the bot's Home tab.</summary>
public record SlackAppHomeOpenedReceivedEvent(
    SlackAppHomeOpenedPayload Payload);

// ── Main adapter class ─────────────────────────────────────────────────────────

/// <summary>
/// Connects an HPD agent to Slack via the Events API and Block Kit.
/// Receives inbound webhooks, routes them to the agent via <see cref="SessionManager"/> and
/// <see cref="AgentManager"/>, consumes the <see cref="AgentEvent"/> stream, and posts
/// responses back via <see cref="SlackApiClient"/>.
/// </summary>
[HpdSocketTransport(typeof(SlackSocketModeService), ConfigProperty = nameof(SlackBotConfig.AppToken))]
[HpdBot("slack")]
[HpdStreaming(StreamingStrategy.PostAndEdit, DebounceMs = 500)]
public partial class SlackBot(
    IOptions<SlackBotConfig> options,
    SessionManager sessionManager,
    AgentManager agentManager,
    PlatformSessionMapper sessionMapper,
    SlackApiClient api,
    SlackFormatConverter formatter,
    SlackUserCache userCache,
    IOptionsMonitor<BotStreamingOptions>? streamingOptions = null)
{
    private readonly SlackBotConfig _config = options.Value;

    // ── Pre-dispatch: signature verification ───────────────────────────────────

    [HpdBotPreDispatch]
    private async Task<BotAdapterResponse?> PreDispatchAsync(BotRequestContext ctx, byte[] bodyBytes)
    {
        // url_verification challenge must respond without signature check (Slack sends none)
        var quickType = ExtractJsonType(bodyBytes);
        if (quickType == "url_verification")
            return null; // let it fall through to the handler

        if (!WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody,
            bodyBytes,
            ctx.Headers,
            _config.SigningSecret,
            "X-Slack-Signature",
            "X-Slack-Request-Timestamp",
            300))
        {
            return BotAdapterResponse.Status(401);
        }

        return null; // verified — continue
    }

    // ── Body extractor: form-urlencoded interactive payloads ───────────────────

    [HpdBotEnvelopeExtractor]
    private (string? eventType, byte[] dispatchBytes) ExtractDispatch(
        BotRequestContext ctx, byte[] bodyBytes)
    {
        // Slack sends interactive payloads (block_actions, view_submission, etc.) as form-urlencoded
        var contentType = ctx.Header("Content-Type") ?? "";
        if (contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            var form = Encoding.UTF8.GetString(bodyBytes);
            var payloadJson = HttpUtility.ParseQueryString(form)["payload"];
            if (payloadJson is not null)
            {
                var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
                return (ExtractJsonType(payloadBytes), payloadBytes);
            }
        }

        // JSON events: outer type or inner event.type for event_callback
        return (ExtractEventType(bodyBytes), bodyBytes);
    }

    // ── Type extraction helpers ────────────────────────────────────────────────

    private static string? ExtractJsonType(byte[] bodyBytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bodyBytes);
            if (doc.RootElement.TryGetProperty("type", out var t)) return t.GetString();
        }
        catch { }
        return null;
    }

    private static string? ExtractEventType(byte[] bodyBytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bodyBytes);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var outerType))
            {
                var outer = outerType.GetString();
                if (outer == "event_callback" &&
                    root.TryGetProperty("event", out var evt) &&
                    evt.TryGetProperty("type", out var innerType))
                {
                    return innerType.GetString();
                }
                return outer;
            }
        }
        catch { }
        return null;
    }

    // ── Bot events (user code subscribes to these) ─────────────────────────

    public event Action<SlackSlashCommandReceivedEvent>? OnSlashCommand;
    public event Action<SlackReactionReceivedEvent>? OnReaction;
    public event Action<SlackBlockActionReceivedEvent>? OnBlockAction;
    public event Action<SlackViewSubmittedEvent>? OnViewSubmission;
    public event Action<SlackViewClosedEvent>? OnViewClosed;
    public event Action<SlackAssistantContextChangedReceivedEvent>? OnAssistantContextChanged;
    public event Action<SlackAppHomeOpenedReceivedEvent>? OnAppHomeOpened;

    // ── Bot event handlers ───────────────────────────────────────────────────────

    [HpdBotEventHandler("url_verification")]
    private Task<BotAdapterResponse> HandleUrlVerificationAsync(
        BotRequestContext ctx, SlackEventEnvelope envelope)
    {
        var body = System.Text.Encoding.UTF8.GetBytes($"{{\"challenge\":\"{envelope.Challenge}\"}}");
        _ = ctx;
        return Task.FromResult(new BotAdapterResponse
        {
            ContentType = "application/json",
            Body = body,
        });
    }

    [HpdBotEventHandler("app_mention")]
    [HpdBotEventHandler("message")]
    private async Task<BotAdapterResponse> HandleMessageAsync(
        BotRequestContext ctx, SlackEventEnvelope envelope)
    {
        var ev = DeserializeEvent<SlackMessageEvent>(envelope.Event);
        if (ShouldSkip(ev)) return BotAdapterResponse.Ok();

        var threadTs    = GetThreadTs(ev!);
        var platformKey = SlackThreadId.Format(ev!.Channel!, threadTs);
        var (sessionId, threadId) = await sessionMapper.ResolveAsync(platformKey, ctx.CancellationToken);
        var input = await BuildInputAsync(ev, ctx.CancellationToken);

        Console.WriteLine($"[SLACK] HandleMessageAsync: channel={ev.Channel} channelType={ev.ChannelType} user={ev.User} botId={ev.BotId} subtype={ev.Subtype} text={ev.Text} threadTs={threadTs} sessionId={sessionId} threadId={threadId}");

        // Fire-and-forget: Slack requires 200 within 3 seconds.
        _ = StreamToSlackAsync(sessionId, threadId, input, ev.Channel!, threadTs, CancellationToken.None);
        return BotAdapterResponse.Ok();
    }

    [HpdBotEventHandler("reaction_added")]
    [HpdBotEventHandler("reaction_removed")]
    private Task<BotAdapterResponse> HandleReactionAsync(
        BotRequestContext ctx, SlackEventEnvelope envelope)
    {
        var ev = DeserializeEvent<SlackReactionEvent>(envelope.Event);
        OnReaction?.Invoke(new SlackReactionReceivedEvent(ev!, envelope.TeamId));
        return Task.FromResult(BotAdapterResponse.Ok());
    }

    [HpdBotEventHandler("assistant_thread_started")]
    private async Task<BotAdapterResponse> HandleAssistantThreadStartedAsync(
        BotRequestContext ctx, SlackEventEnvelope envelope)
    {
        var ev     = DeserializeEvent<SlackAssistantThreadStartedEvent>(envelope.Event);
        var thread = ev!.AssistantThread;
        var platformKey = SlackThreadId.Format(thread.ChannelId, thread.ThreadTs);
        await sessionMapper.ResolveAsync(platformKey, ctx.CancellationToken); // ensure session exists
        await api.TrySetAssistantStatusAsync(thread.ChannelId, thread.ThreadTs, "Ready", ctx.CancellationToken);
        return BotAdapterResponse.Ok();
    }

    [HpdBotEventHandler("assistant_thread_context_changed")]
    private Task<BotAdapterResponse> HandleAssistantContextChangedAsync(
        BotRequestContext ctx, SlackEventEnvelope envelope)
    {
        var ev = DeserializeEvent<SlackAssistantContextChangedEvent>(envelope.Event);
        OnAssistantContextChanged?.Invoke(new SlackAssistantContextChangedReceivedEvent(ev!));
        return Task.FromResult(BotAdapterResponse.Ok());
    }

    [HpdBotEventHandler("app_home_opened")]
    private Task<BotAdapterResponse> HandleAppHomeOpenedAsync(
        BotRequestContext ctx, SlackEventEnvelope envelope)
    {
        var ev = DeserializeEvent<SlackAppHomeOpenedPayload>(envelope.Event);
        if (ev?.Tab == "home")
            OnAppHomeOpened?.Invoke(new SlackAppHomeOpenedReceivedEvent(ev));
        return Task.FromResult(BotAdapterResponse.Ok());
    }

    // Slash commands arrive as form-urlencoded with a `command` field (no `payload` wrapper).
    // The generator detects form-urlencoded Content-Type + `command` field and routes here.
    // TriggerId (valid 3s) and ResponseUrl (valid 30min) are preserved in Extensions.

    [HpdBotEventHandler("slash_command")]
    private async Task<BotAdapterResponse> HandleSlashCommandAsync(
        BotRequestContext ctx, SlackSlashCommandPayload payload)
    {
        var userName    = await userCache.GetDisplayNameAsync(payload.UserId, ctx.CancellationToken);
        var platformKey = SlackThreadId.Format(payload.ChannelId, ""); // slash commands have no thread
        var (sessionId, threadId) = await sessionMapper.ResolveAsync(platformKey, ctx.CancellationToken);

        var input = new AgentInput(
            Text:      $"{payload.Command} {payload.Text}".Trim(),
            UserId:    payload.UserId,
            UserName:  userName,
            IsMention: true,
            Extensions: new Dictionary<string, string>
            {
                ["slack:triggerId"]   = payload.TriggerId,
                ["slack:responseUrl"] = payload.ResponseUrl
            });

        // Fire-and-forget: Slack requires 200 within 3 seconds.
        _ = StreamToSlackAsync(sessionId, threadId, input, payload.ChannelId, "", CancellationToken.None);
        return BotAdapterResponse.Ok();
    }

    // Interactive payloads arrive as form-urlencoded with a `payload` JSON field.
    // The generator detects Content-Type and routes accordingly.

    [HpdBotEventHandler("block_actions")]
    private async Task<BotAdapterResponse> HandleBlockActionsAsync(
        BotRequestContext ctx, SlackBlockActionsPayload payload)
    {
        foreach (var action in payload.Actions)
        {
            // Permission response: action IDs are GUIDs (the PermissionId).
            // block_id carries the sessionId set when BuildPermissionBlocks posted the message.
            if (IsPermissionAction(action.ActionId))
            {
                var agent = agentManager.GetAgent(_config.ResolveAgentId());
                if (agent is not null)
                {
                    var approved = action.Value == "approve";
                    await agent.AnswerRequestAsync(new PermissionResponseEvent(
                        PermissionId: action.ActionId,
                        SourceName:   "slack",
                        ChoiceId:     approved ? "allow_once" : "deny_once"));
                }
                continue;
            }

            OnBlockAction?.Invoke(new SlackBlockActionReceivedEvent(action, payload));
        }
        return BotAdapterResponse.Ok();
    }

    [HpdBotEventHandler("view_submission")]
    private Task<BotAdapterResponse> HandleViewSubmissionAsync(
        BotRequestContext ctx, SlackViewSubmissionPayload payload)
    {
        var (contextId, privateMetadata) = SlackModalConverter.DecodeMetadata(payload.View.PrivateMetadata);
        var values = FlattenViewState(payload.View.State.Values);
        OnViewSubmission?.Invoke(new SlackViewSubmittedEvent(
            CallbackId:      payload.View.CallbackId ?? "",
            ViewId:          payload.View.Id,
            Values:          values,
            PrivateMetadata: privateMetadata,
            ContextId:       contextId,
            User:            payload.User));
        return Task.FromResult(BotAdapterResponse.Ok());
    }

    [HpdBotEventHandler("view_closed")]
    private Task<BotAdapterResponse> HandleViewClosedAsync(
        BotRequestContext ctx, SlackViewClosedPayload payload)
    {
        var (contextId, privateMetadata) = SlackModalConverter.DecodeMetadata(payload.View.PrivateMetadata);
        OnViewClosed?.Invoke(new SlackViewClosedEvent(
            CallbackId:      payload.View.CallbackId ?? "",
            ViewId:          payload.View.Id,
            PrivateMetadata: privateMetadata,
            ContextId:       contextId,
            User:            payload.User));
        return Task.FromResult(BotAdapterResponse.Ok());
    }

    // ── Permission handler ─────────────────────────────────────────────────────

    /// <summary>
    /// Posts Approve/Deny Block Kit buttons when the agent yields a
    /// <see cref="PermissionRequestEvent"/>. The block_id encodes the sessionId so
    /// <see cref="HandleBlockActionsAsync"/> can route the button click back to the
    /// waiting agent loop via <c>agent.AnswerRequestAsync(PermissionResponseEvent)</c>.
    /// </summary>
    [HpdPermissionHandler]
    private async Task RenderPermissionAsync(
        PermissionRequestEvent req,
        SlackPermissionContext ctx)
    {
        var blocks = BuildPermissionBlocks(req, blockId: ctx.SessionId);
        await api.PostMessageAsync(ctx.Channel, ctx.ThreadTs, blocks, ctx.RequestAborted);
    }

    // ── Streaming ──────────────────────────────────────────────────────────────

    private async Task StreamToSlackAsync(
        string sessionId, string threadId,
        AgentInput input,
        string channel, string threadTs,
        CancellationToken ct)
    {
        try
        {
            var context = new SlackStreamContext(
                SessionId: sessionId,
                Input: input,
                Channel: channel,
                ThreadTs: threadTs);

            var runner = new BotStreamingRunner(sessionManager, agentManager);
            var streaming = ResolveStreamingOptions();
            var agentId = _config.ResolveAgentId();
            var started = await runner.RunAsync(
                new BotStreamingRequest<SlackStreamContext>(
                    AgentId: agentId,
                    SessionId: sessionId,
                    ThreadId: threadId,
                    Text: input.Text,
                    Context: context,
                    Strategy: streaming.Strategy,
                    DebounceMs: streaming.DebounceMs),
                new BotStreamingCallbacks<SlackStreamContext>
                {
                    InitializeAsync = InitializeSlackStreamAsync,
                    UpdateTextAsync = UpdateSlackTextAsync,
                    CompleteTextAsync = CompleteSlackTextAsync,
                    CompleteCardAsync = CompleteSlackCardAsync,
                    HandlePermissionAsync = async (ctx, _, req, token) =>
                        await RenderPermissionAsync(
                            req,
                            new SlackPermissionContext(
                                ctx.Channel,
                                ctx.ThreadTs,
                                ctx.SessionId,
                                token)),
                },
                ct);

            if (!started)
                Console.WriteLine($"[SLACK] StreamToSlackAsync: thread operation lock already held for session={sessionId} thread={threadId}, dropping");
            else
                Console.WriteLine("[SLACK] StreamToSlackAsync: agent stream complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SLACK] StreamToSlackAsync EXCEPTION: {ex}");
        }
    }

    private BotStreamingOptions ResolveStreamingOptions()
    {
        var defaults = streamingOptions?.Get("slack")
            ?? new BotStreamingOptions { DebounceMs = 500 };

        return new BotStreamingOptions
        {
            Strategy = defaults.Strategy,
            DebounceMs = _config.StreamingDebounceMs is > 0
                ? _config.StreamingDebounceMs.Value
                : defaults.DebounceMs,
        };
    }

    private async Task InitializeSlackStreamAsync(SlackStreamContext context, CancellationToken ct)
    {
        Console.WriteLine($"[SLACK] StreamToSlackAsync: starting for session={context.SessionId} channel={context.Channel} threadTs={context.ThreadTs}");

        // UseNativeStreaming: chat.startStream -> chat.appendStream -> chat.stopStream.
        // Only available when recipientUserId is known (Assistants threads only).
        // PostAndEdit: post placeholder -> chat.update per debounce tick -> final update.
        context.UseNative = _config.UseNativeStreaming
            && context.Input.RecipientUserId is not null
            && context.Input.RecipientTeamId is not null;

        if (context.UseNative)
        {
            context.PlaceholderTs = await api.StartStreamAsync(
                context.Channel,
                context.ThreadTs,
                context.Input.RecipientUserId!,
                context.Input.RecipientTeamId!,
                ct);
            return;
        }

        Console.WriteLine("[SLACK] StreamToSlackAsync: posting placeholder message...");
        context.PlaceholderTs = await api.PostMessageAsync(context.Channel, context.ThreadTs, "...", ct);
        Console.WriteLine($"[SLACK] StreamToSlackAsync: placeholder posted ts={context.PlaceholderTs}");
        await api.TrySetAssistantStatusAsync(context.Channel, context.ThreadTs, "Typing...", ct);
    }

    private async Task UpdateSlackTextAsync(SlackStreamContext context, string content, CancellationToken ct)
    {
        if (context.PlaceholderTs is null)
            return;

        var mrkdwn = formatter.ToMrkdwn(content);
        if (context.UseNative)
            await api.AppendStreamAsync(context.Channel, context.PlaceholderTs, mrkdwn, ct);
        else
            await api.UpdateMessageAsync(context.Channel, context.PlaceholderTs, mrkdwn, ct);
    }

    private async Task CompleteSlackTextAsync(SlackStreamContext context, string content, CancellationToken ct)
    {
        if (context.PlaceholderTs is null)
            return;

        var finalMrkdwn = formatter.ToMrkdwn(content);
        Console.WriteLine($"[SLACK] StreamToSlackAsync: TextMessageEnd, posting final update len={finalMrkdwn.Length}");
        if (context.UseNative)
        {
            await api.StopStreamAsync(context.Channel, context.PlaceholderTs, finalMrkdwn, null, ct);
            return;
        }

        await api.UpdateMessageAsync(context.Channel, context.PlaceholderTs, finalMrkdwn, ct);
        await api.TryClearAssistantStatusAsync(context.Channel, context.ThreadTs, ct);
    }

    private async Task CompleteSlackCardAsync(SlackStreamContext context, CardElement card, CancellationToken ct)
    {
        if (context.PlaceholderTs is null)
            return;

        var cardBlocks = new SlackCardRenderer().RenderCard(card);
        var cardFallback = CardFallbackText.From(card);
        if (context.UseNative)
            await api.StopStreamAsync(context.Channel, context.PlaceholderTs, cardFallback, cardBlocks, ct);
        else
            await api.UpdateMessageAsync(context.Channel, context.PlaceholderTs, cardFallback, cardBlocks, ct);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static bool ShouldSkip(SlackMessageEvent? ev)
    {
        if (ev is null) return true;
        if (ev.BotId is not null && ev.Type != "app_mention") return true; // suppress echo loops
        if (ev.Subtype is not null && ev.Subtype != "bot_message") return true; // skip edits/deletes
        return false;
    }

    private static string GetThreadTs(SlackMessageEvent ev)
        // DM top-level → empty (single conversation per DM).
        // DM thread reply / channel message → thread_ts ?? ts.
        => ev.ChannelType == "im" && ev.ThreadTs is null
            ? ""
            : ev.ThreadTs ?? ev.Ts ?? "";

    private async Task<AgentInput> BuildInputAsync(
        SlackMessageEvent ev, CancellationToken ct)
    {
        var text     = ev.Text ?? "";
        var userName = ev.Username ?? await userCache.GetDisplayNameAsync(ev.User ?? "unknown", ct);
        var resolved = await userCache.ResolveInlineMentionsAsync(text, ev.User, ct);
        var content  = formatter.ToPlainText(resolved);

        return new AgentInput(
            Text:        content,
            UserId:      ev.User ?? "unknown",
            UserName:    userName,
            Attachments: ev.Files?.Select(MapAttachment).ToArray() ?? [],
            IsMention:   ev.ChannelType == "im" || ev.Type == "app_mention");
    }

    private static SlackFileInfo MapAttachment(SlackFileInfo f) => f; // identity for now

    private static IReadOnlyList<SlackBlock> BuildPermissionBlocks(
        PermissionRequestEvent req, string blockId)
    {
        // block_id = sessionId so HandleBlockActionsAsync can route the button click
        // back to the waiting agent loop via GetRunningAgent(action.BlockId).
        // action_id = req.PermissionId (GUID) on both buttons so IsPermissionAction()
        // recognises them and the agent can match request to response.

        var blocks = new List<SlackBlock>
        {
            new SlackSectionBlock(
                Text: new SlackMrkdwn(
                    $"*{req.SourceName}* wants to call `{req.FunctionName}`"))
        };

        if (!string.IsNullOrWhiteSpace(req.Evaluation.Summary))
            blocks.Add(new SlackSectionBlock(Text: new SlackMrkdwn(req.Evaluation.Summary)));

        blocks.Add(new SlackActionsBlock(
            Elements: new[]
            {
                new SlackButton(
                    ActionId: req.PermissionId,
                    Text:     new SlackPlainText("Approve"),
                    Value:    "approve",
                    Style:    "primary"),
                new SlackButton(
                    ActionId: req.PermissionId,
                    Text:     new SlackPlainText("Deny"),
                    Value:    "deny",
                    Style:    "danger")
            },
            BlockId: blockId));

        return blocks;
    }

    private static bool IsPermissionAction(string actionId) =>
        Guid.TryParse(actionId, out _);

    private static TEvent? DeserializeEvent<TEvent>(JsonElement? element) where TEvent : class
    {
        if (element is null)
            return null;

        if (typeof(TEvent) == typeof(SlackMessageEvent))
            return (TEvent?)(object?)element.Value.Deserialize(SlackBotJsonContext.Default.SlackMessageEvent);
        if (typeof(TEvent) == typeof(SlackReactionEvent))
            return (TEvent?)(object?)element.Value.Deserialize(SlackBotJsonContext.Default.SlackReactionEvent);
        if (typeof(TEvent) == typeof(SlackAssistantThreadStartedEvent))
            return (TEvent?)(object?)element.Value.Deserialize(SlackBotJsonContext.Default.SlackAssistantThreadStartedEvent);
        if (typeof(TEvent) == typeof(SlackAssistantContextChangedEvent))
            return (TEvent?)(object?)element.Value.Deserialize(SlackBotJsonContext.Default.SlackAssistantContextChangedEvent);
        if (typeof(TEvent) == typeof(SlackAppHomeOpenedPayload))
            return (TEvent?)(object?)element.Value.Deserialize(SlackBotJsonContext.Default.SlackAppHomeOpenedPayload);

        throw new NotSupportedException($"Slack event type '{typeof(TEvent).Name}' is not registered.");
    }

    private static Dictionary<string, string> FlattenViewState(
        Dictionary<string, Dictionary<string, SlackViewStateValue>> values)
    {
        var flat = new Dictionary<string, string>();
        foreach (var block in values.Values)
            foreach (var (actionId, input) in block)
                flat[actionId] = input.Value ?? input.SelectedOption?.Value ?? "";
        return flat;
    }


    // ── Socket Mode dispatch ───────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="SlackSocketModeService"/> for each envelope received over
    /// the Socket Mode WebSocket. Deserializes and dispatches to the same private handlers
    /// used by the HTTP path — session resolution, streaming, and permission handling are
    /// identical regardless of transport.
    /// </summary>
    /// <returns>true = ACK sent (always); false = NACK (never in current implementation).</returns>
    internal async Task<bool> HandleSocketEnvelopeAsync(
        SlackSocketEnvelope envelope, CancellationToken ct)
    {
        // Deserialize using the same hand-written JsonSerializerContext as the HTTP path.
        // JsonContextGenerator is a confirmed no-op — the context must be referenced explicitly.
        // Using JsonSerializer.Deserialize<T>(bytes) without options would break NativeAOT.
        switch (envelope.Type)
        {
            case "events_api":
            {
                var inner = envelope.Payload?.Deserialize(SlackBotJsonContext.Default.SlackEventEnvelope);
                if (inner is null) return true;
                switch (inner.Event?.GetProperty("type").GetString())
                {
                    case "message":
                    case "app_mention":
                    {
                        var ev = DeserializeEvent<SlackMessageEvent>(inner.Event);
                        if (!ShouldSkip(ev))
                        {
                            var threadTs    = GetThreadTs(ev!);
                            var platformKey = SlackThreadId.Format(ev!.Channel!, threadTs);
                            var (sessionId, threadId) = await sessionMapper.ResolveAsync(platformKey, ct);
                            var input = await BuildInputAsync(ev, ct);
                            _ = StreamToSlackAsync(sessionId, threadId, input, ev.Channel!, threadTs, CancellationToken.None);
                        }
                        break;
                    }
                    case "reaction_added":
                    case "reaction_removed":
                    {
                        var ev = DeserializeEvent<SlackReactionEvent>(inner.Event);
                        if (ev is not null) OnReaction?.Invoke(new SlackReactionReceivedEvent(ev, inner.TeamId));
                        break;
                    }
                    case "assistant_thread_started":
                    {
                        var ev = DeserializeEvent<SlackAssistantThreadStartedEvent>(inner.Event);
                        if (ev is not null)
                        {
                            var thread = ev.AssistantThread;
                            var platformKey = SlackThreadId.Format(thread.ChannelId, thread.ThreadTs);
                            await sessionMapper.ResolveAsync(platformKey, ct);
                            await api.TrySetAssistantStatusAsync(thread.ChannelId, thread.ThreadTs, "Ready", ct);
                        }
                        break;
                    }
                    case "assistant_thread_context_changed":
                    {
                        var ev = DeserializeEvent<SlackAssistantContextChangedEvent>(inner.Event);
                        if (ev is not null) OnAssistantContextChanged?.Invoke(new SlackAssistantContextChangedReceivedEvent(ev));
                        break;
                    }
                    case "app_home_opened":
                    {
                        var ev = DeserializeEvent<SlackAppHomeOpenedPayload>(inner.Event);
                        if (ev?.Tab == "home") OnAppHomeOpened?.Invoke(new SlackAppHomeOpenedReceivedEvent(ev));
                        break;
                    }
                }
                break;
            }
            case "interactive":
            {
                var payload = envelope.Payload?.Deserialize(SlackBotJsonContext.Default.SlackBlockActionsPayload);
                if (payload is not null)
                {
                    // Reuse the same block actions logic as the HTTP path (synthetic HttpContext not needed).
                    foreach (var action in payload.Actions)
                    {
                        if (IsPermissionAction(action.ActionId))
                        {
                            var agent = agentManager.GetAgent(_config.ResolveAgentId());
                            if (agent is not null)
                            {
                                var approved = action.Value == "approve";
                                await agent.AnswerRequestAsync(new PermissionResponseEvent(
                                    PermissionId: action.ActionId,
                                    SourceName:   "slack",
                                    ChoiceId:     approved ? "allow_once" : "deny_once"));
                            }
                        }
                        else
                        {
                            OnBlockAction?.Invoke(new SlackBlockActionReceivedEvent(action, payload));
                        }
                    }
                }
                break;
            }
            // disconnect_warning and unknown types: ACK and ignore — safe by design.
            // The forced close arrives 10s later and the normal reconnect path handles it.
        }
        return true;
    }

    // ── Bot-internal input bag ─────────────────────────────────────────────
    // Crosses the agent boundary as UserMessagesInputEvent in StreamToSlackAsync.
    // RecipientUserId/RecipientTeamId drive native streaming eligibility.
    // Extensions carries Slack-specific values (triggerId, responseUrl) for
    // post-stream use by user code subscribing to adapter events.

    private record AgentInput(
        string Text,
        string UserId,
        string UserName,
        bool IsMention,
        SlackFileInfo[]? Attachments = null,
        string? RecipientUserId = null,
        string? RecipientTeamId = null,
        IReadOnlyDictionary<string, string>? Extensions = null);

    private sealed record SlackStreamContext(
        string SessionId,
        AgentInput Input,
        string Channel,
        string ThreadTs)
    {
        public bool UseNative { get; set; }
        public string? PlaceholderTs { get; set; }
    }
}

// ── JSON serializer context ────────────────────────────────────────────────────

/// <summary>
/// AOT-safe source-generated JSON context for all Slack payload types.
/// Keep this list explicit because the bot source generator intentionally does
/// not emit <c>JsonSerializerContext</c> subclasses.
/// </summary>
// Socket Mode envelope
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackSocketEnvelope))]
// Inbound bot payloads
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackEventEnvelope))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackMessageEvent))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackReactionEvent))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackBlockActionsPayload))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackViewSubmissionPayload))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackViewClosedPayload))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackAssistantThreadStartedEvent))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackAssistantContextChangedEvent))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackAppHomeOpenedPayload))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackSlashCommandPayload))]
// Web API helper types
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackEphemeralMessageEnvelope))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackChannelInfo))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackUserInfo))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackUserProfile))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackMessage))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackSuggestedPrompt[]))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackFileCompletion[]))]
// Block Kit outbound types
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackBlock[]))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackSectionBlock))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackActionsBlock))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackHeaderBlock))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackContextBlock))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackImageBlock))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackDividerBlock))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackButton))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackPlainText))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackMrkdwn))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackConfirmationDialog))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackOption))]
// Modal view types
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackView))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackModalView))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackModalInputBlock))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackPlainTextInput))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackStaticSelect))]
[System.Text.Json.Serialization.JsonSerializable(typeof(SlackRadioButtons))]
internal partial class SlackBotJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
