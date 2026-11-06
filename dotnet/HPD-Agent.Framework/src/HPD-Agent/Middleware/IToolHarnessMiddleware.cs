// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent.Middleware;

/// <summary>
/// Required contract for middleware designed to be used as toolharness-scoped middleware
/// (declared via <c>[Collapse(Middlewares = [typeof(YourMiddleware)])]</c>).
/// </summary>
/// <remarks>
/// <para>
/// This marker is a compile-time and runtime contract. Only middleware implementing it may be
/// declared by a ToolHarness or supplied through an exact-type activation override.
/// </para>
/// <para>
/// The <c>HPDToolSourceGenerator</c> emits an error when a type listed in
/// <c>[Collapse(Middlewares = ...)]</c> does not implement <c>IToolHarnessMiddleware</c>, guiding
/// authors toward clear intent.
/// </para>
/// <para>
/// ToolHarness activation, expansion, and deactivation are first-class middleware hooks.
/// Override any of them when the middleware needs to observe that lifecycle.
/// </para>
/// </remarks>
public interface IToolHarnessMiddleware : IAgentMiddleware
{
    /// <summary>
    /// Runs after all declared ToolHarness middleware instances are constructed, in declaration order.
    /// </summary>
    ValueTask OnHarnessActivatedAsync(
        ToolHarnessActivationContext context,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>
    /// Runs before disposal, in reverse declaration order.
    /// </summary>
    ValueTask OnHarnessDeactivatingAsync(
        ToolHarnessDeactivationContext context,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>
    /// Runs once after a collapsed ToolHarness expansion has committed.
    /// Directly visible ToolHarnesses do not have an expansion transition.
    /// </summary>
    ValueTask OnHarnessExpandedAsync(
        ToolHarnessExpansionContext context,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
