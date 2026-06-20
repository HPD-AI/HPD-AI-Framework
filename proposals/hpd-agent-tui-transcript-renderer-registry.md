# HPD-Agent.TUI Transcript Renderer Registry Proposal

## Status

Draft proposal.

## Summary

`HPD-Agent.TUI` should introduce an agent-level transcript renderer registry. The registry should let framework defaults and feature packages register strongly typed renderers for transcript cell models, and let applications replace or decorate individual renderers without replacing the whole shell, transcript view, or feature pipeline.

This proposal intentionally assumes no backward compatibility requirement. The library is still early enough that stale or narrow customization APIs can be removed instead of preserved.

The first implementation target is transcript cell rendering in `HPD-Agent.TUI`. Later, packaged features such as `HPD-Agent.Harness.Coding.TUI` can adopt the same pattern by contributing typed transcript cells and renderers.

## Problem

`HPD-Agent.TUI` is intended to be a customizable shell that applications can compose and brand. It already has good customization surfaces for large shell pieces:

- Header and footer shell components.
- Prompt factory.
- Shell layout.
- Status items.
- Widgets above and below the editor.
- Slash commands.
- Autocomplete providers.
- Shortcuts.
- Event handlers.
- Interaction handlers.
- Shell chrome.

However, transcript rendering is currently not customizable at the same granularity.

The current `TranscriptView` owns both:

- Transcript behavior: scrolling, cache invalidation, row measurement, row clipping, viewport behavior.
- Transcript cell presentation: how user messages, assistant messages, reasoning messages, notices, run statuses, tool calls, and custom component cells are visually rendered.

Those responsibilities should be separated.

The concrete issue that exposed the gap was run status text. The default transcript rendered statuses like:

```text
completed run dd259c15
```

For a user-facing TUI, the run id is usually noise. A better default is:

```text
completed
```

But an operations-heavy shell may want the id back. Today, the application cannot cleanly keep the default shell and default transcript while changing only `RunStatusCell` presentation. The choices are too coarse:

- Accept the framework default.
- Insert a different cell type such as `CustomComponentCell`.
- Replace the whole shell layout or transcript rendering path.

That is the missing middle layer.

## General Primitive

The broader primitive is:

> A packaged TUI feature should be reusable as a whole while still letting the app customize selected internal presentation decisions.

This matters beyond run status. A feature package may own event handling, state aggregation, persistence conventions, and transcript entry creation, while the application owns final product feel. The application should not need to fork or reimplement the feature just to change one label, one row, or one visual treatment.

## Current HPD-Agent.TUI Shape

Today the default shell flow is:

```text
HpdAgentTuiApp
  -> HpdAgentTuiRegistry
  -> IAgentTuiShellLayout
  -> DefaultAgentTuiShellView
  -> TranscriptView
  -> internal TranscriptCellView
```

`DefaultAgentTuiShellView` directly constructs `TranscriptView`.

`TranscriptView` internally creates `TranscriptCellView` for each transcript entry.

`TranscriptCellView` is internal and hardcodes rendering by switching on the cell type:

```csharp
switch (_entry.Cell)
{
    case UserMessageCell cell:
        RenderUserMessage(...);
        break;
    case AssistantMessageCell cell:
        RenderAssistantMessage(...);
        break;
    case RunStatusCell cell:
        RenderRunStatus(...);
        break;
    // ...
}
```

This means an app can control which cells are inserted into the transcript, but not how built-in cells render unless it abandons the default rendering path.

## Current HPD-TUI.Framework Shape

`HPD-TUI.Framework` is lower-level and intentionally generic. It provides:

- `IComponent`
- `RenderContext`
- `SegmentWriter`
- layout primitives
- terminal rendering
- focus and dialog primitives
- generic extension concepts such as content renderers and view strategies

It should not know what an agent transcript cell is. The transcript renderer registry belongs in `HPD-Agent.TUI`, not in `HPD-TUI.Framework`.

However, the design should be inspired by the lower framework's existing pattern:

- Models are separate from views.
- Rendering is performed by components.
- Registries resolve model/content to renderable components.

## Pi Comparison

The Pi extension API has a runtime-first model. It exposes runtime UI mutators such as:

- `ctx.ui.setStatus(...)`
- `ctx.ui.setWidget(...)`
- `ctx.ui.setFooter(...)`
- `ctx.ui.setHeader(...)`
- `ctx.ui.setWorkingMessage(...)`
- `ctx.ui.setHiddenThinkingLabel(...)`

It also has renderer hooks for extension-owned things:

- `pi.registerMessageRenderer(customType, renderer)` for custom messages.
- `renderCall` and `renderResult` on tool definitions.

Pi clearly recognized part of this problem, especially for tools. Tool behavior and tool rendering can be customized together, and built-in tool renderers can be reused in some override paths.

But Pi appears uneven for built-in message/app rendering. Some built-in rendering decisions are exposed through targeted runtime setters, such as hidden thinking label, rather than through a universal message renderer registry.

HPD can take the good idea and adapt it to its compile-time composition style:

- Strongly typed renderers.
- Registered at application composition time.
- Replaceable and decoratable by key.
- Usable by framework defaults and package features.

## Goals

1. Make built-in transcript cell rendering replaceable without replacing the shell.
2. Make renderer customization strongly typed.
3. Let feature packages contribute transcript cell models and renderers.
4. Let applications replace or decorate one renderer from a package while keeping the rest of the package.
5. Keep `TranscriptView` focused on transcript mechanics: scrolling, caching, row measurement, viewport behavior.
6. Remove stale one-off customization APIs if they become unnecessary.
7. Keep defaults simple and polished.

## Non-Goals

1. Do not move agent-specific concepts into `HPD-TUI.Framework`.
2. Do not make every internal helper public.
3. Do not solve every coding harness customization point in the first pass.
4. Do not introduce a dynamic runtime plugin system. HPD's preferred model remains compile-time composition.
5. Do not require feature packages to expose all state models immediately.

## Proposed API

Introduce transcript renderer interfaces and registry support in `HPD-Agent.TUI`.

### Renderer Interface

```csharp
public interface IAgentTuiTranscriptRenderer<in TCell>
    where TCell : TranscriptCell
{
    IComponent Create(AgentTuiTranscriptRenderContext<TCell> context);
}
```

### Delegate Adapter

```csharp
public sealed class DelegateAgentTuiTranscriptRenderer<TCell> : IAgentTuiTranscriptRenderer<TCell>
    where TCell : TranscriptCell
{
    public DelegateAgentTuiTranscriptRenderer(
        Func<AgentTuiTranscriptRenderContext<TCell>, IComponent> create)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
    }

    public IComponent Create(AgentTuiTranscriptRenderContext<TCell> context)
        => _create(context);
}
```

### Render Context

```csharp
public sealed class AgentTuiTranscriptRenderContext<TCell>
    where TCell : TranscriptCell
{
    public AgentTuiTranscriptRenderContext(
        TranscriptEntry entry,
        TCell cell,
        AgentTuiTranscriptRenderServices services)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Cell = cell ?? throw new ArgumentNullException(nameof(cell));
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public TranscriptEntry Entry { get; }

    public TCell Cell { get; }

    public TranscriptEntryMetadata Metadata => Entry.Metadata;

    public AgentTuiTranscriptRenderServices Services { get; }
}
```

### Render Services

Renderers need access to common formatting helpers without duplicating the current internal helper logic.

```csharp
public sealed class AgentTuiTranscriptRenderServices
{
    public IComponent Prefix(
        IComponent body,
        string firstPrefix,
        string subsequentPrefix,
        AgentTuiTranscriptPrefixStyle style);

    public IComponent PrefixedText(
        string text,
        string firstPrefix,
        string subsequentPrefix,
        AgentTuiTranscriptTextStyle style);

    public string FormatRunState(TranscriptRunState state, string? detail = null);

    public string FormatDuration(TimeSpan duration);
}
```

The exact helper API can start smaller. The important point is that moved default renderers should not each clone wrapping, prefixing, and duration formatting logic.

### Builder API

```csharp
public sealed class HpdAgentTuiBuilder
{
    public HpdAgentTuiBuilder AddDefaultTranscriptRenderers();

    public HpdAgentTuiBuilder AddTranscriptRenderer<TCell>(
        string key,
        IAgentTuiTranscriptRenderer<TCell> renderer)
        where TCell : TranscriptCell;

    public HpdAgentTuiBuilder AddTranscriptRenderer<TCell>(
        string key,
        Func<AgentTuiTranscriptRenderContext<TCell>, IComponent> create)
        where TCell : TranscriptCell;

    public HpdAgentTuiBuilder TryAddTranscriptRenderer<TCell>(
        string key,
        IAgentTuiTranscriptRenderer<TCell> renderer)
        where TCell : TranscriptCell;

    public HpdAgentTuiBuilder ReplaceTranscriptRenderer<TCell>(
        string key,
        IAgentTuiTranscriptRenderer<TCell> renderer)
        where TCell : TranscriptCell;

    public HpdAgentTuiBuilder DecorateTranscriptRenderer<TCell>(
        string key,
        Func<IAgentTuiTranscriptRenderer<TCell>, IAgentTuiTranscriptRenderer<TCell>> decorate)
        where TCell : TranscriptCell;
}
```

`AddAgentTuiDefaults()` should call `AddDefaultTranscriptRenderers()`.

### Registry API

`HpdAgentTuiRegistry` should expose a transcript renderer registry:

```csharp
public AgentTuiTranscriptRendererRegistry TranscriptRenderers { get; }
```

The registry should resolve renderers by cell runtime type.

Default behavior for unknown cells should be graceful:

```text
UnknownCellTypeName
```

or a notice-style fallback component.

## Default Renderers

Move the existing hardcoded `TranscriptCellView` branches into default renderers:

- `UserMessageCellRenderer`
- `AssistantMessageCellRenderer`
- `ReasoningMessageCellRenderer`
- `NoticeCellRenderer`
- `RunStatusCellRenderer`
- `ToolCallCellRenderer`
- `CustomComponentCellRenderer`
- `FallbackTranscriptCellRenderer`

The initial behavior should match the current output, except for intentional UX changes already agreed on, such as not showing the run id by default.

Default `RunStatusCellRenderer` should render:

```text
running
completed
cancelled
failed
cancelling
```

and include duration and detail when present:

```text
cancelled  2.4s
failed  12.1s - Provider returned 400
```

The run id stays in the data model. It is not primary UI.

## Example Usage

### Show Run Ids Again

```csharp
builder.ReplaceTranscriptRenderer<RunStatusCell>(
    "hpd.run-status",
    context =>
    {
        var cell = context.Cell;
        var title = $"{cell.State.ToString().ToLowerInvariant()} run {ShortId(cell.RuntimeRunId)}";
        return new Text(title);
    });
```

### Decorate Tool Calls

```csharp
builder.DecorateTranscriptRenderer<ToolCallCell>(
    "hpd.tool-call",
    inner => new HighlightDangerousToolRenderer(inner));
```

### Package Feature Renderer

A feature package could define:

```csharp
public sealed record CodingCommandCell(
    string CommandId,
    string DisplayCommand,
    CodingCommandDisplayState State) : TranscriptCell;
```

and register:

```csharp
builder.TryAddTranscriptRenderer<CodingCommandCell>(
    "hpd.coding.command",
    new CodingCommandCellRenderer());
```

Then an application can customize just the visual:

```csharp
builder.ReplaceTranscriptRenderer<CodingCommandCell>(
    "hpd.coding.command",
    new HpdosCodingCommandRenderer());
```

The app keeps the coding harness event handlers and state logic.

## Feature Package Design

Packages should eventually avoid using `CustomComponentCell` as the only escape hatch for rich feature UI.

Instead, a package can provide:

- Typed transcript cell records.
- Default transcript renderers.
- Event handlers that create/update those cells.
- Optional package-specific options as convenience sugar.

Example:

```csharp
builder.AddCodingHarnessTui(options =>
{
    options.Commands.CompactOutput = true;
});
```

Those options should configure package defaults, but the renderer registry remains the deeper escape hatch.

## Why Not Only Options?

An options object such as `ConfigureTranscript(...)` would solve the immediate run-status wording issue, but it is too narrow.

Options are good for common knobs:

- Compact or verbose mode.
- Max output lines.
- Label text.
- Visibility toggles.

But options do not solve the core primitive:

> Replace the rendering of one contributed cell type while keeping the rest of the feature.

The renderer registry solves that. Options can be layered on top later.

## Why Not Runtime Setters?

Runtime setters are useful for dynamic UI state, and Pi uses them effectively.

But HPD's preferred model is compile-time composition:

- A C# app composes its shell at startup.
- Contributions are strongly typed.
- Duplicate keys and replacement errors are caught early.
- Package APIs can be documented and tested as normal .NET APIs.

Runtime setters may still be useful for dynamic state such as current footer text, working status, or widgets. They should not be the main mechanism for replacing built-in renderers.

## Migration Plan

Because backward compatibility is not required, this can be done as a direct refactor.

### Phase 1: Add Core Renderer Registry

1. Add transcript renderer interfaces and context types.
2. Add transcript renderer storage to `HpdAgentTuiBuilder`.
3. Add transcript renderer storage to `HpdAgentTuiRegistry`.
4. Register default renderers from `AddAgentTuiDefaults()`.
5. Pass the registry into `TranscriptView`.

### Phase 2: Move Built-In Rendering

1. Split current `TranscriptCellView` branches into renderer classes.
2. Move shared prefix/wrap/format helpers into internal renderer helper components or services.
3. Delete stale hardcoded switch logic.
4. Keep `TranscriptView` focused on row caching and viewport mechanics.

### Phase 3: Tests

Add tests for:

- Default renderers are installed by `AddAgentTuiDefaults()`.
- Duplicate renderer keys fail for `AddTranscriptRenderer`.
- `TryAddTranscriptRenderer` preserves existing renderer.
- `ReplaceTranscriptRenderer` replaces existing renderer.
- `DecorateTranscriptRenderer` wraps existing renderer.
- Default `RunStatusCell` rendering hides runtime run id.
- Custom `RunStatusCell` renderer can show runtime run id.
- Unknown transcript cell has a graceful fallback.
- Existing transcript scroll/cache tests still pass.

### Phase 4: HPD-OS Cleanup

HPD-OS can keep its event handler behavior, activity indicator, and footer text.

If HPD-OS wants custom transcript rendering, it can replace renderers explicitly.

Otherwise it can simply use the framework default.

### Phase 5: Feature Package Adoption

Later, migrate `HPD-Agent.Harness.Coding.TUI` from `CustomComponentCell`-heavy rendering toward typed cells and registered renderers.

Potential cells:

- `CodingCommandCell`
- `CodingExplorationCell`
- `FileMutationCell`
- `DiagnosticsCell`

This is not required for the first pass.

## Estimated Change Size

The first pass is a medium refactor, not a rewrite.

Estimated scope:

- `HPD-Agent.TUI`: 6-10 files touched.
- Tests: 2-4 files touched.
- `HPD-Agent.Harness.Coding.TUI`: no required changes in first pass.
- HPD-OS app: 0-1 files touched.

Estimated line count:

- Around 300-600 changed lines.
- Much of the change is moving existing rendering logic out of `TranscriptCellView`.

## Risks

### Renderer Context Gets Too Large

If renderer context becomes a grab bag, it will become hard to maintain.

Mitigation:

- Start with `Entry`, `Cell`, `Metadata`, and small helper services.
- Add context fields only when needed by real renderers.

### Too Many Tiny Renderer Classes

Splitting each cell renderer into a class may feel verbose.

Mitigation:

- Support delegate renderers.
- Keep default renderers internal.
- Group built-in renderer implementations in a single file if that reads better.

### Type Resolution Ambiguity

If renderers can be registered for base classes and derived classes, resolution rules must be clear.

Mitigation:

- First implementation can require exact cell type registration.
- Add base-type fallback later only if needed.

### Package State Exposure

Feature packages may need to expose typed cell models for app-level renderer replacement.

Mitigation:

- Do not migrate feature packages immediately.
- Keep package internals internal until a typed cell boundary is ready.

## Open Questions

1. Should renderer keys be required, or should type alone be sufficient?
2. Should `DecorateTranscriptRenderer` be included in the first implementation or added after replacement works?
3. Should renderer resolution support base cell type fallback?
4. Should renderer failures fall back to a notice cell, a type-name row, or an error row?
5. Should transcript renderers return `IComponent`, or should they render directly into `SegmentWriter`?

## Recommendation

Implement the transcript renderer registry now, before the TUI APIs harden.

This removes a real primitive limitation in the current design:

> Users can compose large pieces, but cannot customize the presentation of one built-in or package-contributed transcript row without replacing too much.

The registry gives HPD a cleaner compile-time version of the flexibility Pi discovered at runtime:

- Framework-owned cells are customizable.
- Package-contributed cells are customizable.
- Apps retain control of product feel.
- Feature packages retain control of behavior and state.

The first proof case should be `RunStatusCell`.

