namespace HPD.Agent.Bots;

/// <summary>
/// Marks a method as the pre-dispatch hook. The generator calls this before
/// deserializing or routing any event. Returning a non-null response short-circuits
/// the dispatch — use this for signature verification, challenge responses, or
/// any platform-specific pre-processing.
/// </summary>
/// <remarks>
/// Method signature must be:
///   private Task&lt;BotAdapterResponse?&gt; {name}(BotRequestContext ctx, byte[] bodyBytes)
///
/// Return null to continue to dispatch. Return any response to short-circuit.
/// Only one [HpdBotPreDispatch] method is allowed per adapter class.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HpdBotPreDispatchAttribute : Attribute { }
