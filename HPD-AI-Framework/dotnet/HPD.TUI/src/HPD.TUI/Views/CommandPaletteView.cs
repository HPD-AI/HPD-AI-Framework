using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;

namespace HPD.TUI.Views;

public sealed class CommandPaletteView : IFocusable
{
    private readonly CommandRouter _router;
    private readonly SelectionModel<CommandDescriptor> _selection = new() { EmptyText = "No commands" };
    private readonly SelectionController<CommandDescriptor> _controller;
    private readonly SelectionView<CommandDescriptor> _view;

    public CommandPaletteView(CommandModel model, CommandRouter router)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _controller = new SelectionController<CommandDescriptor>(_selection);
        _controller.Submitted = item => _router.TryExecute(item.Value.SlashName.AsSpan());
        _view = new SelectionView<CommandDescriptor>(_selection, _controller);
        Refresh(model);
    }

    public CommandModel Model { get; }

    public CommandRouter Router => _router;

    public bool IsFocused
    {
        get => _view.IsFocused;
        set => _view.IsFocused = value;
    }

    public Measurement Measure(in RenderContext context, int maxWidth) => _view.Measure(in context, maxWidth);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output) => _view.Render(in context, maxWidth, ref output);

    public void HandleInput(in KeyEvent key) => _view.HandleInput(in key);

    public void Invalidate() => _view.Invalidate();

    public static CommandPaletteView Create(IEnumerable<CommandDescriptor> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var model = new CommandModel();
        foreach (var command in commands)
        {
            model.Register(command);
        }

        return new CommandPaletteView(model, new CommandRouter(model));
    }

    private void Refresh(CommandModel model)
    {
        foreach (var command in model.Commands)
        {
            if (command.Hidden)
            {
                continue;
            }

            _selection.Add(new CollectionItem<CommandDescriptor>(
                command.SlashName,
                command,
                "/" + command.SlashName,
                command.Description,
                command.Category));
        }
    }
}
