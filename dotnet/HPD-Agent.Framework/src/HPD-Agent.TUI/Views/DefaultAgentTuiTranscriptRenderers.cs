using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Views;

internal sealed class UserMessageCellRenderer : IAgentTuiTranscriptRenderer<UserMessageCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<UserMessageCell> context)
        => context.Services.Prefix(
            context.Cell.Body,
            "› ",
            "  ",
            AgentTuiTranscriptPrefixStyle.Accent);
}

internal sealed class AssistantMessageCellRenderer : IAgentTuiTranscriptRenderer<AssistantMessageCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<AssistantMessageCell> context)
        => new TranscriptRenderComponent((in RenderContext renderContext, int maxWidth, ref SegmentWriter output) =>
        {
            var name = string.IsNullOrWhiteSpace(context.Cell.Name)
                ? context.Metadata.AgentName ?? "assistant"
                : context.Cell.Name;
            output.Write(context.DepthIndent.AsSpan(), renderContext.Theme.Text);
            output.Write(name.AsSpan(), new Style(Color.Default, Color.Default, TextAttributes.Bold));
            output.WriteLineBreak();
            context.Services.Prefix(
                    context.Cell.Body,
                    $"{context.DepthIndent}  ",
                    $"{context.DepthIndent}  ")
                .Render(in renderContext, maxWidth, ref output);
        });
}

internal sealed class ReasoningMessageCellRenderer : IAgentTuiTranscriptRenderer<ReasoningMessageCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<ReasoningMessageCell> context)
        => new TranscriptRenderComponent((in RenderContext renderContext, int maxWidth, ref SegmentWriter output) =>
        {
            output.Write(context.DepthIndent.AsSpan(), renderContext.Theme.Text);
            output.Write("reasoning".AsSpan(), AgentTuiTranscriptRenderServices.Muted);
            output.WriteLineBreak();
            var mutedContext = new RenderContext(
                renderContext.Width,
                renderContext.Height,
                context.Services.CreateMutedTheme(renderContext.Theme),
                renderContext.ColorSystem,
                renderContext.Elapsed);
            context.Services.Prefix(
                    context.Cell.Body,
                    $"{context.DepthIndent}  ",
                    $"{context.DepthIndent}  ")
                .Render(in mutedContext, maxWidth, ref output);
        });
}

internal sealed class NoticeCellRenderer : IAgentTuiTranscriptRenderer<NoticeCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<NoticeCell> context)
        => new TranscriptRenderComponent((in RenderContext renderContext, int maxWidth, ref SegmentWriter output) =>
        {
            var prefix = context.Cell.Severity switch
            {
                TranscriptSeverity.Error => "!! ",
                TranscriptSeverity.Warning => "! ",
                TranscriptSeverity.Success => "OK ",
                _ => "• "
            };
            var style = context.Services.StyleForSeverity(context.Cell.Severity);
            output.Write(context.DepthIndent.AsSpan(), renderContext.Theme.Text);
            output.Write(prefix.AsSpan(), style);
            output.Write(context.Cell.Title.AsSpan(), style);

            if (context.Cell.Body is not null)
            {
                output.WriteLineBreak();
                context.Services.Prefix(
                        context.Cell.Body,
                        $"{context.DepthIndent}  ",
                        $"{context.DepthIndent}  ")
                    .Render(in renderContext, maxWidth, ref output);
            }
        });
}

internal sealed class RunStatusCellRenderer : IAgentTuiTranscriptRenderer<RunStatusCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<RunStatusCell> context)
        => new TranscriptRenderComponent((in RenderContext renderContext, int maxWidth, ref SegmentWriter output) =>
        {
            var stateStyle = context.Services.StyleForRunState(context.Cell.State);
            var title = context.Services.FormatRunState(context.Cell.State);
            if (context.Cell.Duration is { } duration)
            {
                title += $"  {context.Services.FormatDuration(duration)}";
            }

            if (!string.IsNullOrWhiteSpace(context.Cell.Detail))
            {
                title += $" - {context.Cell.Detail}";
            }

            output.Write(context.DepthIndent.AsSpan(), renderContext.Theme.Text);
            output.Write(title.AsSpan(), stateStyle);

            if (!string.IsNullOrWhiteSpace(context.Cell.Hint))
            {
                output.WriteLineBreak();
                context.Services.PrefixedText(
                        context.Cell.Hint,
                        $"{context.DepthIndent}  ",
                        $"{context.DepthIndent}  ")
                    .Render(in renderContext, maxWidth, ref output);
            }
        });
}

internal sealed class ToolCallCellRenderer : IAgentTuiTranscriptRenderer<ToolCallCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<ToolCallCell> context)
        => new TranscriptRenderComponent((in RenderContext renderContext, int maxWidth, ref SegmentWriter output) =>
        {
            var stateStyle = context.Services.StyleForRunState(context.Cell.State);
            output.Write(context.DepthIndent.AsSpan(), renderContext.Theme.Text);
            output.Write("• ".AsSpan(), stateStyle);
            output.Write(context.Cell.Name.AsSpan(), stateStyle);
            output.WriteLineBreak();

            if (context.Cell.Summary is null)
            {
                context.Services.PrefixedText(
                        context.Services.FormatRunState(context.Cell.State, context.Cell.StateDetail),
                        $"{context.DepthIndent}  └ ",
                        $"{context.DepthIndent}    ")
                    .Render(in renderContext, maxWidth, ref output);
            }
            else
            {
                context.Services.Prefix(
                        context.Cell.Summary,
                        $"{context.DepthIndent}  └ ",
                        $"{context.DepthIndent}    ")
                    .Render(in renderContext, maxWidth, ref output);
            }

            if (context.Cell.Detail is not null)
            {
                output.WriteLineBreak();
                context.Services.Prefix(
                        context.Cell.Detail,
                        $"{context.DepthIndent}  │ ",
                        $"{context.DepthIndent}  │ ")
                    .Render(in renderContext, maxWidth, ref output);
            }
        });
}

internal sealed class CustomComponentCellRenderer : IAgentTuiTranscriptRenderer<CustomComponentCell>
{
    public IComponent Create(AgentTuiTranscriptRenderContext<CustomComponentCell> context)
        => new TranscriptRenderComponent((in RenderContext renderContext, int maxWidth, ref SegmentWriter output) =>
        {
            var indent = $"{context.DepthIndent}{new string(' ', Math.Max(0, context.Cell.Indent))}";
            output.Write(indent.AsSpan(), renderContext.Theme.Text);
            output.Write(context.Cell.Label.AsSpan(), new Style(Color.Default, Color.Default, TextAttributes.Bold));
            output.WriteLineBreak();
            context.Services.Prefix(
                    context.Cell.Component,
                    $"{indent}  ",
                    $"{indent}  ")
                .Render(in renderContext, maxWidth, ref output);
        });
}

internal sealed class TranscriptRenderComponent : IComponent
{
    private readonly RenderTranscriptComponent _render;

    public TranscriptRenderComponent(RenderTranscriptComponent render)
    {
        _render = render ?? throw new ArgumentNullException(nameof(render));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
        => new(Math.Min(maxWidth, 20), maxWidth);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
        => _render(in context, maxWidth, ref output);

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }
}

internal delegate void RenderTranscriptComponent(
    in RenderContext context,
    int maxWidth,
    ref SegmentWriter output);

