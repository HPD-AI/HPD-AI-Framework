namespace HPD.Agent.Adapters;

/// <summary>
/// Marks a method as the handler for a specific webhook event type.
/// Can be applied multiple times to handle multiple event types with one method
/// (e.g. both "message" and "app_mention" feeding the same handler).
/// </summary>
/// <remarks>
/// The method must be <c>private</c> or <c>internal</c>. The generator produces
/// the public <c>HandleWebhookAsync</c> entry point that calls this method.
/// </remarks>
/// <param name="eventType">
/// The event type string to route to this handler
/// (e.g. "app_mention", "block_actions", "message").
/// </param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class HpdWebhookHandlerAttribute(string eventType) : Attribute
{
    /// <summary>The event type string this handler responds to.</summary>
    public string EventType => eventType;
}
