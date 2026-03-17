// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Middleware;

/// <summary>
/// Optional marker interface for middleware designed to be used as toolkit-scoped middleware
/// (declared via <c>[Collapse(Middlewares = [typeof(YourMiddleware)])]</c>).
/// </summary>
/// <remarks>
/// <para>
/// Implementing this interface has no runtime effect — it is documentation and tooling support only.
/// Any <see cref="IAgentMiddleware"/> can be registered as toolkit-scoped middleware.
/// This marker exists to signal authorial intent in a toolkit's public API.
/// </para>
/// <para>
/// The <c>HPDToolSourceGenerator</c> emits a warning when a type listed in
/// <c>[Collapse(Middlewares = ...)]</c> does not implement <c>IToolkitMiddleware</c>, guiding
/// authors toward clear intent.
/// </para>
/// </remarks>
public interface IToolkitMiddleware : IAgentMiddleware { }
