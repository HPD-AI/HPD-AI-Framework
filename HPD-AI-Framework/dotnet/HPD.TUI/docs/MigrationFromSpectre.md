# Migration From Spectre.Console

HPD.TUI is retained-mode and grid-based. Spectre renderables are immediate-mode.

Recommended migration order:

1. Move plain assistant text to `Text` or `Markdown`.
2. Replace static panels with `Frame`.
3. Replace prompt state with `PromptModel`, `PromptController`, and `PromptView`.
4. Replace list prompts with `SelectionModel<T>`, `SelectionController<T>`, and `SelectionView<T>`.
5. Register semantic contributions with `TuiExtensionRegistry`.
6. Use `TuiApplication.RunAsync` for full-screen interactive flows.

Keep Spectre for legacy commands until the equivalent HPD.TUI component exists.
