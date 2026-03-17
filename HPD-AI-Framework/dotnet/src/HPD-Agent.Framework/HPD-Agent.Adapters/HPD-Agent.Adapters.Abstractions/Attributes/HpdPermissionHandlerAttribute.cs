namespace HPD.Agent.Adapters;

/// <summary>
/// Marks a method as the permission-request renderer for this adapter.
/// Only one method per adapter class may carry this attribute.
/// </summary>
/// <remarks>
/// <para>
/// When the agent emits a <c>PermissionRequestEvent</c>, the generator's dispatch loop:
/// <list type="number">
///   <item>Calls the decorated method to render the platform-specific permission UI
///         (e.g. Block Kit approve/deny buttons in Slack).</item>
///   <item>Calls <c>coordinator.WaitForResponseAsync&lt;PermissionResponseEvent&gt;</c>
///         with the <c>PermissionId</c> as the request key.</item>
///   <item>The response is delivered when a button-click handler calls
///         <c>coordinator.SendResponse</c> with the same key.</item>
/// </list>
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HpdPermissionHandlerAttribute : Attribute { }
