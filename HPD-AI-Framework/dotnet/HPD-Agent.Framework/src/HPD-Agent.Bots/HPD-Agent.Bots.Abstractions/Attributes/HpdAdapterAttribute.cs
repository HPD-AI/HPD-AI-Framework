namespace HPD.Agent.Bots;

/// <summary>
/// Marks a class as an HPD platform adapter.
/// The source generator will produce <c>AddXxxBot()</c> and <c>MapXxxWebhook()</c>
/// extension methods, a <c>HandleWebhookAsync</c> dispatch entry point, and an
/// <c>BotRegistry</c> entry for this adapter.
/// </summary>
/// <param name="name">
/// Lowercase platform identifier (e.g. "slack", "teams", "discord").
/// Used as the suffix in generated method names and as the default webhook path segment.
/// </param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HpdBotAttribute(string name) : Attribute
{
    /// <summary>Lowercase platform identifier.</summary>
    public string Name => name;
}
