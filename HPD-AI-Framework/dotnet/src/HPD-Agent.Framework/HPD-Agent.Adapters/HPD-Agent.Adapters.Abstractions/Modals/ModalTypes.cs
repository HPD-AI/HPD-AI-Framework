namespace HPD.Agent.Adapters.Modals;

// ── Modal input block children ─────────────────────────────────────────────────

/// <summary>
/// Base type for all elements that appear as input blocks inside a <see cref="ModalElement"/>.
/// Each variant maps to a platform-specific input control (text field, select, radio group, etc.).
/// </summary>
public abstract record ModalBlock(string Label, string BlockId);

/// <summary>Single-line or multiline plain text input.</summary>
public record ModalTextInput(
    string Label,
    string BlockId,
    string ActionId,
    string? Placeholder = null,
    string? InitialValue = null,
    bool Multiline = false,
    int? MinLength = null,
    int? MaxLength = null,
    bool Optional = false
) : ModalBlock(Label, BlockId);

/// <summary>Single-select dropdown from a static list of options.</summary>
public record ModalSelect(
    string Label,
    string BlockId,
    string ActionId,
    IReadOnlyList<ModalOption> Options,
    string? Placeholder = null,
    string? InitialValue = null,
    bool Optional = false
) : ModalBlock(Label, BlockId);

/// <summary>Radio button group — shows all options inline.</summary>
public record ModalRadioGroup(
    string Label,
    string BlockId,
    string ActionId,
    IReadOnlyList<ModalOption> Options,
    string? InitialValue = null,
    bool Optional = false
) : ModalBlock(Label, BlockId);

/// <summary>A static section of explanatory text (not an input — no BlockId key in submission state).</summary>
public record ModalSection(
    string Text,
    string BlockId
) : ModalBlock(Text, BlockId);

/// <summary>A visual separator between blocks.</summary>
public record ModalDivider(string BlockId) : ModalBlock("", BlockId);

/// <summary>An option entry for <see cref="ModalSelect"/> and <see cref="ModalRadioGroup"/>.</summary>
public record ModalOption(string Label, string Value, string? Description = null);

// ── View submission responses ──────────────────────────────────────────────────

/// <summary>
/// What the platform should do after a modal form is submitted.
/// Returned from a <c>view_submission</c> handler via <c>SlackModalConverter.ToViewSubmissionResponse</c>.
/// </summary>
public abstract record ModalResponse;

/// <summary>Close the modal (and all stacked views if <c>ClearAll = true</c>).</summary>
public record ModalCloseResponse(bool ClearAll = false) : ModalResponse;

/// <summary>Replace the current modal view with a new one.</summary>
public record ModalUpdateResponse(ModalElement View) : ModalResponse;

/// <summary>Push a new view onto the modal stack.</summary>
public record ModalPushResponse(ModalElement View) : ModalResponse;

/// <summary>
/// Return field-level validation errors. The modal stays open with error messages
/// shown under the relevant input blocks. Key = <c>BlockId</c>, Value = error text.
/// </summary>
public record ModalErrorsResponse(IReadOnlyDictionary<string, string> Errors) : ModalResponse;

// ── Root modal element ─────────────────────────────────────────────────────────

/// <summary>
/// The cross-platform modal definition passed to <c>SlackModalConverter.ToSlackView()</c>.
/// Adapters convert this to their platform's native modal format
/// (Slack views.open, Teams task modules, etc.).
/// </summary>
public record ModalElement(
    string Title,
    IReadOnlyList<ModalBlock> Blocks,
    string? SubmitLabel = null,
    string? CloseLabel = null,
    string? CallbackId = null,
    /// <summary>
    /// App-specific data preserved round-trip through the platform.
    /// For Slack: stored in <c>private_metadata</c> (Base64-encoded via
    /// <c>SlackModalConverter.EncodeMetadata</c>).
    /// </summary>
    string? PrivateMetadata = null,
    /// <summary>
    /// Whether Slack should send a <c>view_closed</c> event when the user dismisses
    /// the modal without submitting.
    /// </summary>
    bool NotifyOnClose = false
);
