# HPD-Agent Framework Release Notes

## 0.5.5

HPD-Agent is pre-1.0. Until `1.0.0`, API and persistence contracts may continue
to evolve as the framework stabilizes. This release documents the current model:
sessions contain threads, and thread event streams are the durable source of
conversation history.

Notable updates:

- Session branching APIs have been formalized as thread APIs across core,
  hosting, ASP.NET, TUI, audio projection, evaluations, bots, and sub-agents.
- ASP.NET hosting routes now use `/threads` and `/thread-graph` endpoints.
- `ISessionStore` now persists session metadata separately from event-sourced
  thread history.
- Middleware fork hooks and persistent state scope names now use thread
  terminology.
- TUI transcript rendering now uses transcript cells and renderer
  registrations.
- Provider-specific configuration is represented through `ProviderOptions` and
  provider config helpers.
