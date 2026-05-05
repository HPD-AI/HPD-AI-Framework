namespace HPD.Agent.Bots.Discord;

/// <summary>
/// Platform key codec for Discord guild, channel, and thread routing.
/// </summary>
[ThreadId("discord:{GuildId}:{ChannelId}:{ThreadId}")]
public partial record DiscordThreadId(string GuildId, string ChannelId, string ThreadId = "")
{
    public bool IsDM => GuildId == "@me";

    public bool IsThread => !string.IsNullOrEmpty(ThreadId);

    public string PostChannelId => IsThread ? ThreadId : ChannelId;
}
