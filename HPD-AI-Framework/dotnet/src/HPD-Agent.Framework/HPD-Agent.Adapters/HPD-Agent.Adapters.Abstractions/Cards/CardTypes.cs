namespace HPD.Agent.Adapters.Cards;

// ── Card child discriminated union ────────────────────────────────────────────

/// <summary>
/// Base type for all elements that can appear as children inside a <see cref="CardElement"/>
/// or a <see cref="CardSection"/>.
/// </summary>
public abstract record CardChild;

/// <summary>A paragraph of text. <c>Style</c> defaults to normal; "muted" renders as secondary text.</summary>
public record CardText(string Text, string? Style = null) : CardChild;

/// <summary>A two-column key/value field list.</summary>
public record CardFields(IReadOnlyList<CardField> Fields) : CardChild;

/// <summary>A single field in a <see cref="CardFields"/> block.</summary>
public record CardField(string Label, string Value);

/// <summary>A hyperlink.</summary>
public record CardLink(string Label, string Url) : CardChild;

/// <summary>An image with optional alt text and title.</summary>
public record CardImage(string Url, string? AltText = null, string? Title = null) : CardChild;

/// <summary>A horizontal rule / separator.</summary>
public record CardDivider : CardChild;

/// <summary>A group of interactive actions (buttons, selects, etc.).</summary>
public record CardActions(IReadOnlyList<CardAction> Actions) : CardChild;

/// <summary>A named section that groups child elements.</summary>
public record CardSection(string? Title = null, IReadOnlyList<CardChild>? Children = null) : CardChild;

// ── Card action elements ───────────────────────────────────────────────────────

/// <summary>Base type for interactive action elements within a <see cref="CardActions"/> block.</summary>
public abstract record CardAction(string ActionId);

/// <summary>A button. <c>Style</c>: "primary" | "danger" | null (default).</summary>
public record CardButton(
    string ActionId,
    string Label,
    string? Value = null,
    string? Style = null,
    string? Url = null) : CardAction(ActionId);

/// <summary>A static single-select menu.</summary>
public record CardSelect(
    string ActionId,
    string Placeholder,
    IReadOnlyList<CardSelectOption> Options,
    string? InitialValue = null) : CardAction(ActionId);

/// <summary>A single option in a <see cref="CardSelect"/>.</summary>
public record CardSelectOption(string Label, string Value);

/// <summary>A radio button group (rendered as overflow menu on some platforms).</summary>
public record CardRadioSelect(
    string ActionId,
    string Placeholder,
    IReadOnlyList<CardSelectOption> Options,
    string? InitialValue = null) : CardAction(ActionId);

// ── Root card element ──────────────────────────────────────────────────────────

/// <summary>
/// The root element passed to platform card renderers and <see cref="CardFallbackText"/>.
/// Represents a structured card that adapters convert to Block Kit, Adaptive Cards, etc.
/// </summary>
public record CardElement(
    string? Title = null,
    string? Subtitle = null,
    string? ImageUrl = null,
    IReadOnlyList<CardChild>? Children = null);
