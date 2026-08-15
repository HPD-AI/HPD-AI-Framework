# HPD AI Platform

Unified TypeScript workspace for the HPD Studio UI surface.

## Layout

- `../hpd-studio-core`: the sole product-neutral module, route, navigation,
  lifecycle, private-context, authentication, quarantine, and composition
  contract.
- `shell`: the Svelte 5 host app. Owns startup-frozen composition, browser
  hash projection, and the final Tailwind CSS v4 build.
- `modules/hpd-agent-studio`: HPD Agent Studio module package. Owns agent-specific API clients, module registration, routes, and components.
- `modules/hpd-graph-studio`: HPD Graph and workflow module package.
- `modules/hpd-rag-studio`: HPD RAG module package.
- `modules/hpd-auth-studio`: HPD Auth module package.
- `modules/hpd-ml-studio`: HPD ML module package.
- `packages/hpd-studio-design`: shared design package. Owns Tailwind theme tokens, base rules, and public `studio-*` utilities.

## Build

```bash
cd shell
npm install
npm run build
```

The shell build writes embedded static assets to `dotnet/HPD-AI.Platform/wwwroot`.

The shell composes the module packages in `modules/*` at build time. `HPD-AI.Platform` hosts the built app and design contract; domain-specific `.Studio` .NET packages carry module source and fluent registration metadata.

Every module imports `@hpd-research/hpd-studio-core`; modules do not redeclare
shared types. Product state belongs to its module. In particular,
Agent/session/thread/run/event selection is private Agent-module context and is
not shell state. The shell exposes no product authorization cache or generic
capability conclusion.

## Verification

```bash
cd ../hpd-studio-core
npm install
npm test
npm run typecheck

cd ../hpd-ai-studio/shell
npm install
npm test
npm run typecheck
npm run build
```
