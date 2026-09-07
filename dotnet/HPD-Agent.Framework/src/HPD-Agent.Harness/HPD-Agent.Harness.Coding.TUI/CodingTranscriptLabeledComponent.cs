using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI;

internal sealed class CodingTranscriptLabeledComponent : HPD.TUI.Core.Component
{
    private readonly string _label;
    private readonly string _depthIndent;
    private readonly IComponent _body;
    private readonly AgentTuiTranscriptRenderServices _services;
    private readonly CodingHarnessTuiTheme _theme;

    public CodingTranscriptLabeledComponent(
        string label,
        string depthIndent,
        IComponent body,
        AgentTuiTranscriptRenderServices services,
        CodingHarnessTuiTheme theme)
    {
        _label = label ?? throw new ArgumentNullException(nameof(label));
        _depthIndent = depthIndent ?? throw new ArgumentNullException(nameof(depthIndent));
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
        => new(Math.Min(constraints.MaxWidth, 20), constraints.MaxWidth);

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        if (maxWidth <= 0)
        {
            return;
        }

        output.Write(_depthIndent.AsSpan(), _theme.ResolveText(context.Theme));
        output.Write(_label.AsSpan(), _theme.ResolveLabel(context.Theme));
        output.WriteLineBreak();
        output.Render(_services.Prefix(
                _body,
                $"{_depthIndent}  ",
                $"{_depthIndent}  "), in context, maxWidth);
    }

    public override bool HandleInput(in TuiInputEvent input)
    {
        return _body.HandleInput(in input);
    }
}
