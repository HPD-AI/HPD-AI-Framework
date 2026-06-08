# HPD-Agent.TUI API Reference

This page groups the main `HPD-Agent.TUI` APIs by role.

## App

### `HpdAgentTuiApp`

Creates and runs the terminal shell.

```csharp
await using var app = HpdAgentTuiApp.Create(
    runtime,
    scope,
    configure: tui => tui.AddAgentTuiDefaults());

await app.RunAsync();
```

`Create` applies your builder configuration and then builds the registry. Your configuration must register shell pieces, usually by calling `AddAgentTuiDefaults()`.

## Runtime

### `IHpdAgentTuiRuntime`

Boundary between the TUI and the agent runtime.

Methods:

- `EnsureScopeAsync`;
- `ObserveAsync`;
- `SubmitInputAsync`;
- `RespondAsync`;
- `GetBranchEventsAsync`;
- `GetActiveRunAsync`.

### `InMemoryAgentTuiRuntime`

Runtime for an `Agent` in the same process as the TUI.

### `HostedAgentTuiRuntime`

Runtime for an HTTP-hosted HPD Agent API.

### `HostedAgentTuiRuntimeOptions`

Configures `HostedAgentTuiRuntime`.

Main properties:

- `BaseAddress`;
- `DefaultScope`;
- `MessageHandler`;
- `RequestTimeout`.

### `AgentTuiRuntimeScope`

Identifies the active agent/session/branch.

```csharp
public sealed record AgentTuiRuntimeScope(
    string AgentId,
    string SessionId,
    string BranchId);
```

### `AgentTuiBranchRun`

Describes the active or completed branch run state known to the runtime.

## Composition

### `HpdAgentTuiBuilder`

Registers shell and UI contributions.

Contribution methods include:

- `AddAgentTuiDefaults`;
- `AddDefaultShell`;
- `AddDefaultPrompt`;
- `AddDefaultCommandSupport`;
- `AddDefaultShellCommands`;
- `AddEventHandler`;
- `AddSlashCommand`;
- `AddStatusItem`;
- `AddWidget`;
- `AddPage`;
- `AddAutocompleteProvider`;
- `AddShortcut`;
- `AddInteractionHandler`;
- `AddHeader`;
- `AddFooter`;
- `AddPrompt`;
- `AddShellLayout`;
- `ConfigureShellChrome`;
- `UseTheme`.
- `SetRunConfigComposer`;
- `ClearRunConfigComposer`;
- `UseModelSelectionRunConfig`;
- `AddModelSelectionCommand`;
- `AddModelSelection`.

Most contribution families support `Add*`, `TryAdd*`, and `Replace*` variants.

### `HpdAgentTuiRegistry`

Immutable registry produced by `HpdAgentTuiBuilder.Build()`.

Used by the shell to find commands, pages, status items, widgets, autocomplete providers, shortcuts, event handlers, interaction handlers, and shell components.

### `AgentTuiContribution<T>`

Keyed contribution wrapper.

## Shell Models

### `ChatShellModel`

Mutable shell state.

Main properties:

- `Scope`;
- `HeaderText`;
- `FooterText`;
- `Runtime`;
- `SwitchScopeAsync`;
- `Navigation`;
- `Transcript`;
- `Activities`;
- `AboveEditor`;
- `BelowEditor`.

### `TranscriptModel`

Thread-safe transcript row collection.

Main methods:

- `Append`;
- `Update`;
- `Remove`;
- `Clear`;
- `ScrollUp`;
- `ScrollDown`;
- `ScrollToTop`;
- `ScrollToBottom`;

### `TranscriptEntry`

One transcript entry.

Fields:

- `Id`;
- `EntryKey`;
- `Cell`;
- `Metadata`.

### `TranscriptCell`

Base type for built-in transcript cell shapes.

Built-in cells:

- `UserMessageCell`;
- `AssistantMessageCell`;
- `ReasoningMessageCell`;
- `NoticeCell`;
- `ToolCallCell`;
- `CustomComponentCell`.

### `TranscriptEntryMetadata`

Agent attribution for transcript rendering.

Fields:

- `AgentId`;
- `AgentName`;
- `ParentAgentId`;
- `AgentChain`;
- `AgentDepth`.

### `AgentTuiNavigationModel`

Tracks transcript/page navigation.

Main members:

- `ActivePageId`;
- `IsTranscriptActive`;
- `CanGoBack`;
- `BackStack`;
- `GoToTranscript`;
- `GoToPage`;
- `Back`;
- `Clear`.

### `TranscriptView`

Default transcript renderer.

Constructor:

```csharp
new TranscriptView(model, height: 17);
```

Main properties:

- `Height`.

### `WidgetSlotModel`

Runtime component list for above-editor and below-editor widgets.

## Event Handling

### `IAgentTuiEventHandler`

Stateful event handler interface.

```csharp
public interface IAgentTuiEventHandler
{
    bool CanHandle(AgentEvent evt);

    ValueTask HandleAsync(
        AgentEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken);
}
```

### `AgentTuiEventHandler<TEvent>`

Typed base class for handling one event type.

### `AgentTuiEventContext`

Context passed to event handlers.

Properties:

- `Scope`;
- `Shell`;
- `Registry`;
- `State`.

### `AgentTuiStateBag`

Typed state bag for event handlers.

Methods:

- `GetOrCreate`;
- `TryGet`;
- `Set`;
- `Remove`.

## Commands And Input

### `HpdAgentTuiCommandDescriptor`

Slash command descriptor.

Properties:

- `Name`;
- `SlashName`;
- `Title`;
- `Description`;
- `Hidden`;
- `Execute`.

### `AgentTuiCommandContext`

Context passed to slash commands.

Properties:

- `Scope`;
- `Shell`;
- `Navigation`;
- `Runtime`;
- `Dialogs`;
- `SwitchScopeAsync`;
- `Command`;
- `Arguments`.

### `AgentTuiRunConfigComposer`

Delegate used to create an `AgentRunConfig` from prompt context before normal text input is submitted.

```csharp
tui.SetRunConfigComposer(context => new AgentRunConfig
{
    ProviderKey = "openrouter",
    ModelName = "deepseek/deepseek-v4-flash"
});
```

### `AgentTuiRunConfigContext`

Context passed to the run config composer.

Properties:

- `Scope`;
- `Shell`;
- `Prompt`.

### `IAgentTuiAutocompleteProvider`

Agent TUI autocomplete contribution.

### `HpdAgentTuiShortcutDescriptor`

Keyboard shortcut descriptor.

### `KeyGesture`

Keyboard gesture matcher.

## Pages

### `HpdAgentTuiPageDescriptor`

Registers a page that can replace the main transcript area.

Properties:

- `Id`;
- `Title`;
- `Description`;
- `Hidden`;
- `Render`.

### `AgentTuiPageContext`

Context passed to page render functions.

Properties:

- `Scope`;
- `Shell`;
- `Registry`;
- `Page`;
- `Height`.

## Layout And Chrome

### `IAgentTuiShellLayout`

Renders the shell from `AgentTuiShellLayoutContext`.

### `DefaultAgentTuiShellLayout`

Default shell layout implementation.

### `AgentTuiShellChrome`

Configures section titles, framing, spacing, and transcript height.

### `ShellSectionChrome`

Section display settings.

Factory methods:

- `Bare`;
- `Hidden`;
- `Separator`;
- `Frame`.

### `ShellSectionDisplay`

Display modes:

- `Bare`;
- `Separator`;
- `Frame`;
- `Hidden`.

## Shell Components

### `IAgentTuiShellComponent`

Renders header or footer content.

### `IAgentTuiStatusItem`

Renders compact status content.

### `IAgentTuiWidget`

Renders above-editor or below-editor widget content.

### `IAgentTuiPromptFactory`

Creates the prompt view.

### `DefaultAgentTuiPromptFactory`

Default prompt factory for the built-in shell.

Properties:

- `Placeholder`;
- `Multiline`.

## Model Selection

### `IAgentTuiModelCatalog`

Catalog abstraction used by model selection commands.

### `AgentTuiModelSelectionState`

Mutable selection state used to produce `AgentRunConfig` values.

### `AddModelSelection(...)`

Builder helper that registers a model command and connects the selected model to the run config composer.

## Interactions

### `IAgentTuiInteractionHandler`

Handles bidirectional request events and returns response events.

### `AgentTuiInteractionHandler<TRequest>`

Typed base class for interaction handlers.

### `AgentTuiInteractionContext`

Context passed to interaction handlers.

Properties:

- `Scope`;
- `Shell`;
- `Runtime`;
- `Dialogs`;
- `Request`.

### `IAgentTuiDialogService`

Dialog service available to interaction handlers.

Methods:

- `Show`;
- `Close`;
- `ConfirmAsync`;
- `SelectAsync`;
- `InputAsync`.

### Built-In Interaction Handlers

Built-in handlers include:

- `PermissionRequestInteractionHandler`;
- `ContinuationRequestInteractionHandler`;
- `ClarificationRequestInteractionHandler`.
