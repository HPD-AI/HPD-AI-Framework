using System.Reflection;
using FluentAssertions;

namespace HPD.Agent.Bots.Tests.Unit;

public class PublicSurfaceTests
{
    public static TheoryData<Assembly, string[]> ImplementationTypesByAssembly => new()
    {
        {
            typeof(HPD.Agent.Bots.HpdBotAttribute).Assembly,
            [
                "HPD.Agent.Bots.BlockquoteAttribute",
                "HPD.Agent.Bots.BoldAttribute",
                "HPD.Agent.Bots.BotEmojiFormat",
                "HPD.Agent.Bots.BotEmojiResolver",
                "HPD.Agent.Bots.BotErrorsAttribute",
                "HPD.Agent.Bots.CardFallbackText",
                "HPD.Agent.Bots.CardRendererAttribute",
                "HPD.Agent.Bots.CodeAttribute",
                "HPD.Agent.Bots.CodeBlockAttribute",
                "HPD.Agent.Bots.ErrorCodeAttribute",
                "HPD.Agent.Bots.HpdBotPayloadAttribute",
                "HPD.Agent.Bots.HpdPermissionHandlerAttribute",
                "HPD.Agent.Bots.ItalicAttribute",
                "HPD.Agent.Bots.LinkAttribute",
                "HPD.Agent.Bots.ListItemAttribute",
                "HPD.Agent.Bots.OrderedListItemAttribute",
                "HPD.Agent.Bots.PlatformFormatConverterAttribute",
                "HPD.Agent.Bots.Streaming.BotDebounceTimer",
                "HPD.Agent.Bots.StrikeAttribute",
            ]
        },
        {
            typeof(global::HPD.Agent.Bots.Slack.SlackBot).Assembly,
            [
                "HPD.Agent.Bots.Slack.OAuth.TokenStoreSecretResolver",
                "HPD.Agent.Bots.Slack.SlackCardRenderer",
                "HPD.Agent.Bots.Slack.SlackErrorHandler",
                "HPD.Agent.Bots.Slack.SlackFormatConverter",
                "HPD.Agent.Bots.Slack.SlackModalConverter",
                "HPD.Agent.Bots.Slack.SlackPermissionContext",
                "HPD.Agent.Bots.Slack.SlackUserCache",
                "HPD.Agent.Bots.Slack.SocketMode.SlackSocketEnvelope",
                "HPD.Agent.Bots.Slack.SocketMode.SlackSocketModeClient",
                "HPD.Agent.Bots.Slack.SocketMode.SlackSocketModeService",
            ]
        },
        {
            typeof(global::HPD.Agent.Bots.Discord.DiscordBot).Assembly,
            [
                "HPD.Agent.Bots.Discord.DiscordCardRenderer",
                "HPD.Agent.Bots.Discord.DiscordErrorHandler",
                "HPD.Agent.Bots.Discord.DiscordFormatConverter",
                "HPD.Agent.Bots.Discord.Gateway.DiscordGatewayClient",
                "HPD.Agent.Bots.Discord.Gateway.DiscordGatewayService",
            ]
        },
        {
            typeof(global::HPD.Agent.Bots.Telegram.TelegramBot).Assembly,
            [
                "HPD.Agent.Bots.Telegram.TelegramCardConverter",
                "HPD.Agent.Bots.Telegram.TelegramFormatConverter",
                "HPD.Agent.Bots.Telegram.TelegramPollingService",
                "HPD.Agent.Bots.Telegram.TelegramRenderMode",
            ]
        },
        {
            typeof(global::HPD.Agent.Bots.WhatsApp.WhatsAppBot).Assembly,
            [
                "HPD.Agent.Bots.WhatsApp.WhatsAppCardConverter",
                "HPD.Agent.Bots.WhatsApp.WhatsAppCardResult",
                "HPD.Agent.Bots.WhatsApp.WhatsAppFormatConverter",
                "HPD.Agent.Bots.WhatsApp.WhatsAppInteractiveButton",
                "HPD.Agent.Bots.WhatsApp.WhatsAppInteractiveMessage",
            ]
        },
        {
            typeof(global::HPD.Agent.Bots.Teams.TeamsBot).Assembly,
            [
                "HPD.Agent.Bots.Teams.ITeamsTurn",
                "HPD.Agent.Bots.Teams.NoopTeamsHistoryService",
                "HPD.Agent.Bots.Teams.TeamsAdaptiveCard",
                "HPD.Agent.Bots.Teams.TeamsCardRenderer",
                "HPD.Agent.Bots.Teams.TeamsFormatConverter",
                "HPD.Agent.Bots.Teams.TeamsGraphService",
                "HPD.Agent.Bots.Teams.TeamsModalConverter",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(ImplementationTypesByAssembly))]
    public void BotAssemblies_DoNotExportImplementationTypes(
        Assembly assembly,
        string[] expectedInternalTypes)
    {
        var exportedTypes = assembly.GetExportedTypes()
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);

        exportedTypes.Should().NotIntersectWith(expectedInternalTypes);
    }
}
