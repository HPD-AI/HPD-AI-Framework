// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent.Middleware;

/// <summary>
/// Required marker interface for middleware designed to be used as toolharness-scoped middleware
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
/// </remarks>
public interface IToolHarnessMiddleware : IAgentMiddleware { }
