namespace HPD.Agent.Bots;

/// <summary>
/// Marks a method as the permission-request renderer for this adapter.
/// This internal pre-1.0 marker is retained for future generator work; current
/// dispatch does not invoke the attributed method automatically.
/// </summary>
/// <remarks>
/// <para>
/// The production permission flow is currently implemented directly by each platform
/// adapter's streaming callback. Do not document this marker as a supported extension
/// point until generator routing is implemented and tested.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class HpdPermissionHandlerAttribute : Attribute { }
