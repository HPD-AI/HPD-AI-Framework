namespace HPD.Agent.Bots;

/// <summary>
/// Marks a method as the pre-dispatch hook. The generator calls this before
/// deserializing or routing any event. Returning a non-null IResult short-circuits
/// the dispatch — use this for signature verification, challenge responses, or
/// any platform-specific pre-processing.
/// </summary>
/// <remarks>
/// Method signature must be:
///   private async Task&lt;IResult?&gt; {name}(HttpContext ctx, byte[] bodyBytes)
///
/// Return null to continue to dispatch. Return any IResult to short-circuit.
/// Only one [HpdPreDispatch] method is allowed per adapter class.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HpdPreDispatchAttribute : Attribute { }
