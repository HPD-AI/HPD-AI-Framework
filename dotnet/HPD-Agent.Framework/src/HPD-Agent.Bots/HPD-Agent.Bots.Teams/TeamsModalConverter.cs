using HPD.Agent.Bots.Modals;

namespace HPD.Agent.Bots.Teams;

public sealed class TeamsModalConverter
{
    public TeamsTaskModuleResponse ToTaskFetchResponse(ModalElement modal)
        => Continue(modal);

    public TeamsTaskModuleResponse ToTaskSubmitResponse(ModalResponse response)
        => response switch
        {
            ModalCloseResponse => new TeamsTaskModuleResponse(new TeamsTaskModuleTask("message", string.Empty)),
            ModalUpdateResponse update => Continue(update.View),
            ModalPushResponse push => Continue(push.View),
            ModalErrorsResponse errors => new TeamsTaskModuleResponse(new TeamsTaskModuleTask(
                "continue",
                new TeamsTaskModuleCardValue("Validation errors", RenderErrors(errors.Errors)))),
            _ => new TeamsTaskModuleResponse(new TeamsTaskModuleTask("message", string.Empty))
        };

    public TeamsAdaptiveCard Render(ModalElement modal, IReadOnlyDictionary<string, string>? errors = null)
    {
        ArgumentNullException.ThrowIfNull(modal);

        var body = new List<object>();
        foreach (var block in modal.Blocks)
        {
            if (errors?.TryGetValue(block.BlockId, out var error) == true)
                body.Add(new TeamsTextBlock(error, Color: "Attention"));

            RenderBlock(block, body);
        }

        var actions = new List<object>
        {
            new TeamsSubmitAction(
                modal.SubmitLabel ?? "Submit",
                new Dictionary<string, string>
                {
                    ["actionId"] = modal.CallbackId ?? "modal_submit",
                    ["privateMetadata"] = modal.PrivateMetadata ?? string.Empty,
                })
        };

        return new TeamsAdaptiveCard(body, actions);
    }

    private static void RenderBlock(ModalBlock block, List<object> body)
    {
        switch (block)
        {
            case ModalTextInput text:
                body.Add(new TeamsInputText(
                    Id: text.ActionId,
                    Label: text.Label,
                    Placeholder: text.Placeholder,
                    Value: text.InitialValue,
                    IsMultiline: text.Multiline,
                    IsRequired: !text.Optional,
                    MaxLength: text.MaxLength));
                break;

            case ModalSelect select:
                body.Add(new TeamsChoiceSet(
                    Id: select.ActionId,
                    Choices: select.Options.Select(option => new TeamsChoice(option.Label, option.Value)).ToArray(),
                    Placeholder: select.Placeholder,
                    Value: select.InitialValue,
                    Style: "compact",
                    Label: select.Label,
                    IsRequired: !select.Optional));
                break;

            case ModalRadioGroup radio:
                body.Add(new TeamsChoiceSet(
                    Id: radio.ActionId,
                    Choices: radio.Options.Select(option => new TeamsChoice(option.Label, option.Value)).ToArray(),
                    Value: radio.InitialValue,
                    Style: "expanded",
                    Label: radio.Label,
                    IsRequired: !radio.Optional));
                break;

            case ModalSection section:
                body.Add(new TeamsTextBlock(section.Text));
                break;

            case ModalDivider:
                body.Add(new TeamsContainer([], Separator: true));
                break;
        }
    }

    private TeamsTaskModuleResponse Continue(ModalElement modal)
        => new(new TeamsTaskModuleTask(
            "continue",
            new TeamsTaskModuleCardValue(modal.Title, Render(modal))));

    private TeamsAdaptiveCard RenderErrors(IReadOnlyDictionary<string, string> errors)
        => new(errors.Select(error => new TeamsTextBlock(error.Value, Color: "Attention")).Cast<object>().ToArray());
}
