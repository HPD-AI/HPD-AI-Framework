using System.Text.Json.Serialization;

namespace HPD.Agent.Adapters.Slack;

// ── Composition objects ────────────────────────────────────────────────────────

/// <summary>
/// Base for Slack text objects. The <c>type</c> property is always emitted explicitly
/// by subclasses so the JSON wire format is correct for both direct and polymorphic serialization.
/// </summary>
public abstract record SlackTextObject(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);

/// <summary>Plain text object. Set <c>Emoji = true</c> to allow :emoji: shortcodes.</summary>
public record SlackPlainText(
    string Text,
    [property: JsonPropertyName("emoji")] bool Emoji = true)
    : SlackTextObject("plain_text", Text);

/// <summary>Mrkdwn text object. Used for formatted message content.</summary>
public record SlackMrkdwn(string Text)
    : SlackTextObject("mrkdwn", Text);

/// <summary>
/// An option item used in selects, radio groups, overflow menus.
/// </summary>
public record SlackOption(
    [property: JsonPropertyName("text")]        SlackPlainText Text,
    [property: JsonPropertyName("value")]       string Value,
    [property: JsonPropertyName("description")] SlackPlainText? Description = null
);

/// <summary>
/// Confirmation dialog shown before a destructive action is performed.
/// </summary>
public record SlackConfirmationDialog(
    [property: JsonPropertyName("title")]   SlackPlainText Title,
    [property: JsonPropertyName("text")]    SlackTextObject Text,
    [property: JsonPropertyName("confirm")] SlackPlainText Confirm,
    [property: JsonPropertyName("deny")]    SlackPlainText Deny,
    [property: JsonPropertyName("style")]   string? Style = null  // "primary" | "danger"
);

// ── Block elements ─────────────────────────────────────────────────────────────

/// <summary>
/// A Slack button element. Used inside <see cref="SlackActionsBlock"/>.
/// </summary>
public record SlackButton(
    [property: JsonPropertyName("action_id")]          string ActionId,
    [property: JsonPropertyName("text")]               SlackPlainText Text,
    [property: JsonPropertyName("value")]              string? Value = null,
    [property: JsonPropertyName("style")]              string? Style = null,  // "primary" | "danger"
    [property: JsonPropertyName("url")]                string? Url = null,
    [property: JsonPropertyName("accessibility_label")] string? AccessibilityLabel = null,
    [property: JsonPropertyName("confirm")]            SlackConfirmationDialog? Confirm = null
)
{
    [JsonPropertyName("type")]
    public string Type => "button";
}

// ── Blocks ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Base for all Slack Block Kit blocks.
/// The <c>type</c> property is emitted explicitly by each subclass so it appears
/// in the JSON regardless of whether the object is serialized as the base or concrete type.
/// </summary>
public abstract record SlackBlock(
    [property: JsonPropertyName("type")]     string Type,
    [property: JsonPropertyName("block_id")] string? BlockId = null
);

/// <summary>
/// Section block — text paragraph, optional field list, optional accessory element.
/// <c>Expand = true</c> prevents Slack's "See more" collapse on long text.
/// </summary>
public record SlackSectionBlock(
    [property: JsonPropertyName("text")]    SlackTextObject? Text = null,
    [property: JsonPropertyName("fields")]  IReadOnlyList<SlackTextObject>? Fields = null,
    [property: JsonPropertyName("expand")]  bool? Expand = null,
    string? BlockId = null
) : SlackBlock("section", BlockId);

/// <summary>
/// Actions block — a row of interactive elements (buttons, selects, etc.).
/// The <c>block_id</c> is used to route interaction payloads back to the right handler.
/// </summary>
public record SlackActionsBlock(
    [property: JsonPropertyName("elements")] IReadOnlyList<SlackButton> Elements,
    string? BlockId = null
) : SlackBlock("actions", BlockId);

/// <summary>
/// Header block — bold, larger text. Rendered at the top of a card.
/// </summary>
public record SlackHeaderBlock(
    [property: JsonPropertyName("text")] SlackPlainText Text,
    string? BlockId = null
) : SlackBlock("header", BlockId);

/// <summary>
/// Context block — secondary, smaller text or images. Used for subtitles.
/// </summary>
public record SlackContextBlock(
    [property: JsonPropertyName("elements")] IReadOnlyList<SlackTextObject> Elements,
    string? BlockId = null
) : SlackBlock("context", BlockId);

/// <summary>
/// Image block — a full-width image with optional title and alt text.
/// </summary>
public record SlackImageBlock(
    [property: JsonPropertyName("image_url")] string ImageUrl,
    [property: JsonPropertyName("alt_text")]  string AltText,
    [property: JsonPropertyName("title")]     SlackPlainText? Title = null,
    string? BlockId = null
) : SlackBlock("image", BlockId);

/// <summary>
/// Divider block — a horizontal rule.
/// </summary>
public record SlackDividerBlock(string? BlockId = null)
    : SlackBlock("divider", BlockId);
