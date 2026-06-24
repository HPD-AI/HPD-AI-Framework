using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI;

internal sealed class CodingTranscriptLabeledComponent : IComponent
{
    private readonly string _label;
    private readonly string _depthIndent;
    private readonly IComponent _body;
    private readonly AgentTuiTranscriptRenderServices _services;

    public CodingTranscriptLabeledComponent(
        string label,
        string depthIndent,
        IComponent body,
        AgentTuiTranscriptRenderServices services)
    {
        _label = label ?? throw new ArgumentNullException(nameof(label));
        _depthIndent = depthIndent ?? throw new ArgumentNullException(nameof(depthIndent));
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
        => new(Math.Min(maxWidth, 20), maxWidth);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        output.Write(_depthIndent.AsSpan(), context.Theme.Text);
        output.Write(_label.AsSpan(), new Style(Color.Default, Color.Default, TextAttributes.Bold));
        output.WriteLineBreak();
        _services.Prefix(
                _body,
                $"{_depthIndent}  ",
                $"{_depthIndent}  ")
            .Render(in context, maxWidth, ref output);
    }

    public bool HandleInput(in TuiInputEvent input)
    {
        return _body.HandleInput(in input);
    }
}
