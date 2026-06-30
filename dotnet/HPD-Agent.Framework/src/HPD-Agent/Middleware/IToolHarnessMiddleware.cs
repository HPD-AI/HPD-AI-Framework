// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent.Middleware;

/// <summary>
/// Optional marker interface for middleware designed to be used as toolharness-scoped middleware
/// (declared via <c>[Collapse(Middlewares = [typeof(YourMiddleware)])]</c>).
/// </summary>
/// <remarks>
/// <para>
/// Implementing this interface has no runtime effect — it is documentation and tooling support only.
/// Any <see cref="IAgentMiddleware"/> can be registered as toolharness-scoped middleware.
/// This marker exists to signal authorial intent in a toolharness's public API.
/// </para>
/// <para>
/// The <c>HPDToolSourceGenerator</c> emits a warning when a type listed in
/// <c>[Collapse(Middlewares = ...)]</c> does not implement <c>IToolHarnessMiddleware</c>, guiding
/// authors toward clear intent.
/// </para>
/// </remarks>
public interface IToolHarnessMiddleware : IAgentMiddleware { }
