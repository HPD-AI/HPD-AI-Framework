using System.Text.Json.Serialization;

namespace HPD.Agent.Adapters.Slack;

// ── Slack modal view (outbound to views.open / views.update) ──────────────────

/// <summary>
/// Slack modal view definition sent to <c>views.open</c> and <c>views.update</c>.
/// Produced by <see cref="SlackModalConverter.ToSlackView"/>.
/// </summary>
public record SlackModalView(
    [property: JsonPropertyName("type")]             string Type,           // always "modal"
    [property: JsonPropertyName("title")]            SlackPlainText Title,
    [property: JsonPropertyName("blocks")]           IReadOnlyList<SlackBlock> Blocks,
    [property: JsonPropertyName("submit")]           SlackPlainText? Submit = null,
    [property: JsonPropertyName("close")]            SlackPlainText? Close = null,
    [property: JsonPropertyName("callback_id")]      string? CallbackId = null,
    [property: JsonPropertyName("private_metadata")] string? PrivateMetadata = null,
    [property: JsonPropertyName("notify_on_close")]  bool NotifyOnClose = false
);

// ── Input block (wraps an interactive element with a label) ──────────────────

/// <summary>
/// Slack input block — wraps an interactive element with a label.
/// This is what appears in modal forms.
/// </summary>
public record SlackModalInputBlock(
    [property: JsonPropertyName("label")]    SlackPlainText Label,
    [property: JsonPropertyName("element")]  SlackInputElement Element,
    [property: JsonPropertyName("optional")] bool? Optional = null,
    [property: JsonPropertyName("hint")]     SlackPlainText? Hint = null,
    string? BlockId = null
) : SlackBlock("input", BlockId);

// ── Input elements ─────────────────────────────────────────────────────────────

/// <summary>
/// Base for all input-block interactive elements.
/// [JsonPolymorphic] enables correct subtype serialization when the declared type is
/// SlackInputElement (e.g. in arrays). The base Type parameter is ignored by STJ to avoid
/// a conflict with the polymorphic discriminator — each concrete subtype declares its own
/// [JsonPropertyName("type")] property.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SlackPlainTextInput), "plain_text_input")]
[JsonDerivedType(typeof(SlackStaticSelect),   "static_select")]
[JsonDerivedType(typeof(SlackRadioButtons),   "radio_buttons")]
public abstract record SlackInputElement(
    [property: JsonIgnore]                    string Type,
    [property: JsonPropertyName("action_id")] string ActionId
);

/// <summary>Single-line or multiline plain text input.</summary>
public record SlackPlainTextInput(
    string ActionId,
    [property: JsonPropertyName("placeholder")]   SlackPlainText? Placeholder = null,
    [property: JsonPropertyName("initial_value")] string? InitialValue = null,
    [property: JsonPropertyName("multiline")]     bool? Multiline = null,
    [property: JsonPropertyName("min_length")]    int? MinLength = null,
    [property: JsonPropertyName("max_length")]    int? MaxLength = null,
    [property: JsonPropertyName("focus_on_load")] bool? FocusOnLoad = null
) : SlackInputElement("plain_text_input", ActionId);

/// <summary>Static single-select dropdown.</summary>
public record SlackStaticSelect(
    string ActionId,
    [property: JsonPropertyName("options")]        IReadOnlyList<SlackOption> Options,
    [property: JsonPropertyName("placeholder")]    SlackPlainText? Placeholder = null,
    [property: JsonPropertyName("initial_option")] SlackOption? InitialOption = null,
    [property: JsonPropertyName("focus_on_load")]  bool? FocusOnLoad = null
) : SlackInputElement("static_select", ActionId);

/// <summary>Radio button group — shows all options inline.</summary>
public record SlackRadioButtons(
    string ActionId,
    [property: JsonPropertyName("options")]        IReadOnlyList<SlackOption> Options,
    [property: JsonPropertyName("initial_option")] SlackOption? InitialOption = null,
    [property: JsonPropertyName("focus_on_load")]  bool? FocusOnLoad = null
) : SlackInputElement("radio_buttons", ActionId);
