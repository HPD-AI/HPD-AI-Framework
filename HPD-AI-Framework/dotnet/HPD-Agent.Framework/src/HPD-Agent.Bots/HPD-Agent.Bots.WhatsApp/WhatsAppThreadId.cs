using HPD.Agent.Bots;

namespace HPD.Agent.Bots.WhatsApp;

[ThreadId("whatsapp:{PhoneNumberId}:{UserWaId}")]
public partial record WhatsAppThreadId(string PhoneNumberId, string UserWaId)
{
    public bool IsDM => true;

    public string ChannelId => FormatChannel(PhoneNumberId);

    public static string FormatChannel(string phoneNumberId) => $"whatsapp:{phoneNumberId}";

    public static string ChannelIdFromThreadId(string threadId)
        => Parse(threadId).ChannelId;
}
