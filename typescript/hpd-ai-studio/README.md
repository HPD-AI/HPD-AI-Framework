# HPD AI Studio

Unified TypeScript workspace for the HPD Studio UI surface.

## Layout

- `shell`: HPD AI Studio host app. Owns app composition, runtime config, routing state, and the final Tailwind build.
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

The shell build writes embedded static assets to `dotnet/HPD-AI.Studio/wwwroot`.

The shell composes the module packages in `modules/*` at build time. `HPD-AI.Studio` hosts the built app and design contract; domain-specific `.Studio` .NET packages carry module source and fluent registration metadata.
