using HPD.Agent.Bots.Contracts;

namespace HPD.Agent.Bots.Discord;

[BotErrors("discord")]
[ErrorCode("10003", typeof(BotNotFoundException))]
[ErrorCode("10004", typeof(BotNotFoundException))]
[ErrorCode("10008", typeof(BotNotFoundException))]
[ErrorCode("50001", typeof(BotPermissionException))]
[ErrorCode("50013", typeof(BotPermissionException))]
[ErrorCode("50035", typeof(BotPermissionException))]
[ErrorCode("20012", typeof(BotPermissionException))]
[ErrorCode("40062", typeof(BotPermissionException))]
public partial class DiscordErrorHandler
{
    public static void ThrowMapped(string discordErrorCode, Exception inner)
    {
        throw discordErrorCode switch
        {
            "10003" => new BotNotFoundException(discordErrorCode, inner),
            "10004" => new BotNotFoundException(discordErrorCode, inner),
            "10008" => new BotNotFoundException(discordErrorCode, inner),
            "50001" => new BotPermissionException(discordErrorCode, inner),
            "50013" => new BotPermissionException(discordErrorCode, inner),
            "50035" => new BotPermissionException(discordErrorCode, inner),
            "20012" => new BotPermissionException(discordErrorCode, inner),
            "40062" => new BotPermissionException(discordErrorCode, inner),
            _ => inner,
        };
    }
}
