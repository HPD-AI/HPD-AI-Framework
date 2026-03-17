using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Adapters.Modals;

namespace HPD.Agent.Adapters.Slack;

/// <summary>
/// Converts HPD <see cref="ModalElement"/> definitions to Slack <c>views.open</c> /
/// <c>views.update</c> payloads and handles encoding/decoding of <c>private_metadata</c>.
/// </summary>
/// <remarks>
/// Slack's <c>private_metadata</c> is a single string (max 3,000 chars).
/// We Base64-encode a small JSON envelope containing an optional HPD <c>contextId</c>
/// and the caller's own <c>privateMetadata</c> so both survive the platform round-trip
/// without an external store.
/// </remarks>
public static class SlackModalConverter
{
    // ── Metadata encode / decode ───────────────────────────────────────────────

    private record MetadataEnvelope(
        [property: JsonPropertyName("c")] string? ContextId,
        [property: JsonPropertyName("m")] string? PrivateMetadata
    );

    /// <summary>
    /// Encodes <paramref name="contextId"/> and <paramref name="privateMetadata"/>
    /// into a single Base64 string for Slack's <c>private_metadata</c> field.
    /// </summary>
    public static string EncodeMetadata(string? contextId, string? privateMetadata)
    {
        var envelope = new MetadataEnvelope(contextId, privateMetadata);
        var json = JsonSerializer.Serialize(envelope);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Decodes a <c>private_metadata</c> string produced by <see cref="EncodeMetadata"/>.
    /// Returns <c>(null, null)</c> if the value is empty or not a valid envelope
    /// (falls back to treating the raw value as <c>privateMetadata</c>).
    /// </summary>
    public static (string? ContextId, string? PrivateMetadata) DecodeMetadata(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            var envelope = JsonSerializer.Deserialize<MetadataEnvelope>(json);
            return (envelope?.ContextId, envelope?.PrivateMetadata);
        }
        catch
        {
            // Not our envelope — treat entire value as raw privateMetadata.
            return (null, raw);
        }
    }

    // ── View conversion ────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="ModalElement"/> to a <see cref="SlackModalView"/> ready
    /// for <c>views.open</c> or <c>views.update</c>.
    /// </summary>
    /// <param name="modal">The HPD modal definition.</param>
    /// <param name="encodedMetadata">
    /// Pre-encoded <c>private_metadata</c> (from <see cref="EncodeMetadata"/>).
    /// When <c>null</c>, encodes <c>modal.PrivateMetadata</c> with no contextId.
    /// </param>
    public static SlackModalView ToSlackView(ModalElement modal, string? encodedMetadata = null)
    {
        var metadata = encodedMetadata
            ?? (modal.PrivateMetadata is not null
                ? EncodeMetadata(null, modal.PrivateMetadata)
                : null);

        var blocks = modal.Blocks
            .Select(ToSlackBlock)
            .ToList();

        return new SlackModalView(
            Type:            "modal",
            Title:           new SlackPlainText(modal.Title),
            Blocks:          blocks,
            Submit:          modal.SubmitLabel is not null ? new SlackPlainText(modal.SubmitLabel) : null,
            Close:           modal.CloseLabel  is not null ? new SlackPlainText(modal.CloseLabel)  : null,
            CallbackId:      modal.CallbackId,
            PrivateMetadata: metadata,
            NotifyOnClose:   modal.NotifyOnClose);
    }

    private static SlackBlock ToSlackBlock(ModalBlock block) => block switch
    {
        ModalTextInput  t => new SlackModalInputBlock(
            Label:    new SlackPlainText(t.Label),
            Element:  new SlackPlainTextInput(
                ActionId:     t.ActionId,
                Placeholder:  t.Placeholder  is not null ? new SlackPlainText(t.Placeholder)  : null,
                InitialValue: t.InitialValue,
                Multiline:    t.Multiline ? true : null,
                MinLength:    t.MinLength,
                MaxLength:    t.MaxLength),
            Optional: t.Optional ? true : null,
            BlockId:  t.BlockId),

        ModalSelect s => new SlackModalInputBlock(
            Label:    new SlackPlainText(s.Label),
            Element:  new SlackStaticSelect(
                ActionId:     s.ActionId,
                Options:      s.Options.Select(ToSlackOption).ToList(),
                Placeholder:  s.Placeholder is not null ? new SlackPlainText(s.Placeholder) : null,
                InitialOption: s.InitialValue is not null
                    ? s.Options.Where(o => o.Value == s.InitialValue)
                               .Select(ToSlackOption).FirstOrDefault()
                    : null),
            Optional: s.Optional ? true : null,
            BlockId:  s.BlockId),

        ModalRadioGroup r => new SlackModalInputBlock(
            Label:    new SlackPlainText(r.Label),
            Element:  new SlackRadioButtons(
                ActionId:     r.ActionId,
                Options:      r.Options.Select(ToSlackOption).ToList(),
                InitialOption: r.InitialValue is not null
                    ? r.Options.Where(o => o.Value == r.InitialValue)
                               .Select(ToSlackOption).FirstOrDefault()
                    : null),
            Optional: r.Optional ? true : null,
            BlockId:  r.BlockId),

        ModalSection sec => new SlackSectionBlock(
            Text:    new SlackMrkdwn(sec.Text),
            BlockId: sec.BlockId),

        ModalDivider d => new SlackDividerBlock(BlockId: d.BlockId),

        _ => throw new NotSupportedException($"Unknown ModalBlock type: {block.GetType().Name}")
    };

    private static SlackOption ToSlackOption(ModalOption o) => new(
        Text:        new SlackPlainText(o.Label),
        Value:       o.Value,
        Description: o.Description is not null ? new SlackPlainText(o.Description) : null);

    // ── View submission responses ──────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="ModalResponse"/> to the JSON-serializable object returned
    /// from a <c>view_submission</c> handler. Slack reads the response body to decide
    /// what to do with the modal.
    /// </summary>
    public static object ToViewSubmissionResponse(ModalResponse response, string? contextId = null)
        => response switch
        {
            ModalCloseResponse { ClearAll: true }  => new { response_action = "clear" },
            ModalCloseResponse                     => new { response_action = "clear" },

            ModalUpdateResponse update => new
            {
                response_action = "update",
                view = ToSlackView(update.View,
                    contextId is not null
                        ? EncodeMetadata(contextId, update.View.PrivateMetadata)
                        : null)
            },

            ModalPushResponse push => new
            {
                response_action = "push",
                view = ToSlackView(push.View,
                    contextId is not null
                        ? EncodeMetadata(contextId, push.View.PrivateMetadata)
                        : null)
            },

            ModalErrorsResponse errors => new
            {
                response_action = "errors",
                errors = errors.Errors
            },

            _ => throw new NotSupportedException($"Unknown ModalResponse type: {response.GetType().Name}")
        };
}
