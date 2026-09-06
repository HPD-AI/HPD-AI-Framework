using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Observability;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Views;

namespace HPD.Agent.TUI.Views;

public sealed class DefaultAgentTuiShellView : Component, IAgentTuiShellView
{
    private readonly ChatShellModel _model;
    private readonly PromptView _prompt;
    private readonly HpdAgentTuiRegistry _registry;
    private readonly AgentTuiStateBag _state;
    private readonly AgentTuiShellChrome _chrome;
    private readonly RetainedShellStack _shell;
    private readonly TranscriptView _transcript;
    private readonly MainSectionView _mainSection;
    private readonly FixedViewport _mainViewport;
    private int _lastTranscriptHeight;
    private long _presentationEpoch;
    private ScrollbackRow[]? _headerRows;
    private int _headerWidth;
    private int _publishedHeaderRows;
    private int _pendingHeaderRows;
    private ScrollbackBatch? _pendingBatch;
    private ScrollbackBatch? _pendingTranscriptBatch;

    // Full-screen/setup surfaces retain their ordinary live header.
    private bool PublishesChatHeader =>
        _registry.TranscriptHistoryPresentation == TranscriptHistoryPresentation.TerminalScrollback &&
        _chrome.Transcript.Display != ShellSectionDisplay.Hidden;


    public DefaultAgentTuiShellView(AgentTuiShellLayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _model = context.Shell;
        _prompt = context.Prompt;
        _registry = context.Registry;
        _state = context.State;
        _chrome = context.Chrome;
        _lastTranscriptHeight = _chrome.DefaultTranscriptHeight;
        AgentTuiPerformanceDiagnostics.TryGetSink(_state, out var performanceSink);
        _transcript = new TranscriptView(
            _model.Transcript,
            _registry.TranscriptRenderers,
            _lastTranscriptHeight,
            _model.Scope,
            performanceSink);
        _model.Transcript.HistoryPresentation = _registry.TranscriptHistoryPresentation;
        _mainSection = new MainSectionView(this);
        _mainViewport = new FixedViewport(_mainSection, _lastTranscriptHeight);
        _shell = CreateShell();
    }

    /// <inheritdoc />
    public void PrepareFrame(TerminalSize size, Theme theme, ColorSystem colorSystem)
    {
        var context = new RenderContext(size.Width, size.Height, theme, colorSystem);
        UpdateTranscriptHeight(in context);
        _mainSection.Prepare(size.Width, theme, colorSystem);
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        UpdateTranscriptHeight(in context);
        return _shell.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(maxWidth, context.Height));
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        UpdateTranscriptHeight(in context);
        output.Render(_shell, in context, maxWidth);
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        if (IsPageActive())
        {
            if (TryHandleActivePageInput(in key))
            {
                return true;
            }

            return _prompt.HandleInput(in key);
        }

        if (_transcript.HandleInput(in key))
        {
            return true;
        }

        return _prompt.HandleInput(in key);
    }

    /// <inheritdoc />
    public long HistoryRevision => _transcript.HistoryRevision;

    /// <inheritdoc />
    public bool IsFullScreen => IsPageActive();

    /// <inheritdoc />
    public HPD.TUI.Terminal.ManagedTerminalRecoveryPolicy HistoryResetPolicy => _transcript.HistoryResetPolicy;

    /// <inheritdoc />
    public void ResetPresentation(long presentationEpoch, in RenderContext context)
    {
        _presentationEpoch = presentationEpoch;
        _headerRows = null;
        _publishedHeaderRows = _pendingHeaderRows = 0;
        _pendingBatch = _pendingTranscriptBatch = null;
        _transcript.ResetPresentation(presentationEpoch, in context);
    }

    /// <inheritdoc />
    public ScrollbackBatch? PrepareScrollback(in RenderContext context, int maxRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);
        if (IsPageActive()) return null;
        if (_pendingBatch is not null) return _pendingBatch;
        var rows = new List<ScrollbackRow>();
        if (PublishesChatHeader)
        {
            if (_headerRows is null) { _headerRows = CaptureHeader(in context); _headerWidth = context.Width; }
            WrapRemainingHeader(context.Width);
            rows.AddRange(_headerRows.Skip(_publishedHeaderRows).Take(maxRows));
        }
        _pendingHeaderRows = rows.Count;
        if (rows.Count < maxRows)
            _pendingTranscriptBatch = _transcript.PrepareScrollback(in context, maxRows - rows.Count);
        if (_pendingTranscriptBatch is { } transcriptBatch) rows.AddRange(transcriptBatch.Rows);
        if (rows.Count == 0 && _pendingTranscriptBatch is null) return null;
        return _pendingBatch = new ScrollbackBatch(_presentationEpoch,
            _publishedHeaderRows + (_pendingTranscriptBatch?.FirstSequence ?? 0), rows.ToArray());
    }

    /// <inheritdoc />
    public void CommitScrollback(ScrollbackBatch batch)
    {
        if (!ReferenceEquals(batch, _pendingBatch))
            throw new InvalidOperationException("Only the prepared shell publication can be committed.");
        if (_pendingTranscriptBatch is { } transcriptBatch) _transcript.CommitScrollback(transcriptBatch);
        _publishedHeaderRows += _pendingHeaderRows;
        _pendingHeaderRows = 0;
        _pendingBatch = _pendingTranscriptBatch = null;
    }

    /// <inheritdoc />
    public void RollbackScrollback(ScrollbackBatch batch)
    {
        if (!ReferenceEquals(batch, _pendingBatch))
            throw new InvalidOperationException("Only the prepared shell publication can be rolled back.");
        if (_pendingTranscriptBatch is { } transcriptBatch) _transcript.RollbackScrollback(transcriptBatch);
        // A wholly unaccepted header can be rebuilt for a newer width or session state.
        if (_publishedHeaderRows == 0) _headerRows = null;
        _pendingHeaderRows = 0;
        _pendingBatch = _pendingTranscriptBatch = null;
    }

    private void WrapRemainingHeader(int width)
    {
        if (width >= _headerWidth) return;
        _headerWidth = width;
        var rows = _headerRows!.Take(_publishedHeaderRows).ToList();
        foreach (var row in _headerRows.Skip(_publishedHeaderRows))
        {
            var cells = new List<ScrollbackCell>();
            var columns = 0;
            var part = 0;
            foreach (var cell in row.Cells)
            {
                if (columns > 0 && columns + cell.DisplayWidth > width)
                {
                    rows.Add(new ScrollbackRow($"{row.Id}:wrap:{part++}", cells.ToArray()));
                    cells.Clear();
                    columns = 0;
                }
                cells.Add(cell);
                columns += cell.DisplayWidth;
            }
            rows.Add(new ScrollbackRow($"{row.Id}:wrap:{part}", cells.ToArray()));
        }
        _headerRows = rows.ToArray();
    }

    private ScrollbackRow[] CaptureHeader(in RenderContext context)
    {
        if (_registry.Header is null ||
            CreateSection(_chrome.Header, new ShellContributionView(_model, _registry.Header)) is not { } header)
            return [];
        var height = Math.Max(1, header.Measure(in context,
            LayoutConstraints.Loose(context.Width, context.Height)).Height);
        while (true)
        {
            using var grid = TuiCapture.RenderToGrid(header, context.Width, height, context.Theme, context.ColorSystem);
            if (grid.CursorY >= grid.Height) { height = checked(height * 2); continue; }
            var rows = new List<ScrollbackRow>();
            for (var row = 0; row < TuiCapture.GetUsedLineCount(grid); row++)
            {
                var cells = new List<ScrollbackCell>();
                for (var column = 0; column < grid.Width; column++)
                {
                    var cell = grid.GetCell(column, row);
                    if (cell.IsContinuation) continue;
                    cells.Add(new ScrollbackCell(grid.GetGrapheme(cell).ToString(), cell.Style,
                        new TerminalRunMetadata(grid.GetHyperlink(cell)), cell.DisplayWidth));
                }
                while (cells.Count > 0 && cells[^1].Grapheme == " " && cells[^1].Style == Style.Default &&
                    cells[^1].Metadata.Hyperlink is null) cells.RemoveAt(cells.Count - 1);
                rows.Add(new ScrollbackRow($"header:{row}", cells.ToArray()));
            }
            if (rows.Count > 0)
                for (var gap = 0; gap < _chrome.Gap; gap++)
                    rows.Add(new ScrollbackRow($"header:gap:{gap}", Array.Empty<ScrollbackCell>()));
            return rows.ToArray();
        }
    }

    private RetainedShellStack CreateShell()
    {
        var shell = new RetainedShellStack(_chrome.Gap);

        if (_registry.Header is not null)
        {
            AddSection(shell, _chrome.Header, new ShellContributionView(_model, _registry.Header), isMain: false,
                () => !PublishesChatHeader || IsPageActive());
        }

        AddSection(shell, _chrome.Transcript,
            _registry.TranscriptHistoryPresentation == TranscriptHistoryPresentation.TerminalScrollback
                ? _mainSection : _mainViewport, isMain: true);
        AddSection(shell, _chrome.Activity, BuildActivitySection(), isMain: false);

        AddSection(
            shell,
            _chrome.AboveEditor,
            BuildWidgetSection(
                TuiSlot.AboveEditor,
                _model.AboveEditor,
                _registry.AboveEditorWidgets),
            isMain: false,
            () => _model.AboveEditor.Count > 0 || _registry.AboveEditorWidgets.Count > 0);

        if (_registry.PromptStatus is not null)
        {
            AddSection(
                shell,
                _chrome.PromptStatus,
                new ShellContributionView(_model, _registry.PromptStatus),
                isMain: false);
        }

        AddSection(shell, _chrome.Prompt, _prompt, isMain: false);

        AddSection(
            shell,
            _chrome.BelowEditor,
            BuildWidgetSection(
                TuiSlot.BelowEditor,
                _model.BelowEditor,
                _registry.BelowEditorWidgets),
            isMain: false,
            () => _model.BelowEditor.Count > 0 || _registry.BelowEditorWidgets.Count > 0);

        if (_registry.Footer is not null || _registry.FooterItems.Count > 0)
        {
            AddSection(shell, _chrome.Footer, BuildFooterSection(), isMain: false);
        }

        return shell;
    }

    private IComponent BuildActivitySection()
        => new ActivityGroupView(_model.Activities)
        {
            Mode = ActivityGroupDisplayMode.Compact,
            AnimationsEnabled = false
        };

    private IComponent BuildFooterSection()
    {
        var footer = new Stack { Gap = 0 };
        if (_registry.Footer is not null)
            footer.Add(new ShellContributionView(_model, _registry.Footer));

        if (_registry.FooterItems.Count > 0)
            footer.Add(new FooterItemsView(_model, _state, _registry.FooterItems));

        return footer;
    }

    private IComponent BuildMainSection(int height)
    {
        if (!string.IsNullOrWhiteSpace(_model.Navigation.ActivePageId) &&
            _registry.TryFindPage(_model.Navigation.ActivePageId, out var page))
        {
            return page.Render(new AgentTuiPageContext(
                _model.Scope,
                _model,
                _model.Navigation,
                _registry,
                page,
                height,
                _state));
        }

        _transcript.SetHeight(height);
        return _transcript;
    }

    private bool IsPageActive()
        => !string.IsNullOrWhiteSpace(_model.Navigation.ActivePageId) &&
           _registry.TryFindPage(_model.Navigation.ActivePageId, out _);

    private bool TryHandleActivePageInput(in TuiInputEvent key)
    {
        if (string.IsNullOrWhiteSpace(_model.Navigation.ActivePageId) ||
            !_registry.TryFindPage(_model.Navigation.ActivePageId, out var page) ||
            page.HandleInput is null)
        {
            return false;
        }

        var keyEvent = key.KeyEvent;
        return page.HandleInput(new AgentTuiPageContext(
            _model.Scope,
            _model,
            _model.Navigation,
            _registry,
            page,
            _lastTranscriptHeight,
            _state), keyEvent);
    }

    private IComponent BuildWidgetSection(
        TuiSlot slot,
        WidgetSlotModel model,
        IReadOnlyList<AgentTuiContribution<IAgentTuiWidget>> contributions)
    {
        var widgets = new Stack { Gap = 0 };

        if (contributions.Count > 0)
        {
            widgets.Add(new ContributionWidgetSlotView(slot, _model, _state, contributions));
        }

        widgets.Add(new WidgetSlotView(model, ""));

        return widgets;
    }

    private void AddSection(
        RetainedShellStack shell,
        ShellSectionChrome chrome,
        IComponent component,
        bool isMain,
        Func<bool>? isVisible = null)
    {
        if (CreateSection(chrome, component) is { } section)
        {
            shell.Add(new RetainedShellSection(section, isMain, isVisible));
        }
    }

    private IComponent? CreateSection(ShellSectionChrome chrome, IComponent component)
    {
        switch (chrome.Display)
        {
            case ShellSectionDisplay.Hidden:
                return null;

            case ShellSectionDisplay.Separator:
                return new Stack { Gap = 0 }
                    .Add(new Separator(ResolveTitle(chrome))
                    {
                        TitleAlignment = Alignment.Start
                    })
                    .Add(component);

            case ShellSectionDisplay.Frame:
                var frame = Frame.Create(component)
                    .WithBorder(chrome.Border)
                    .WithPadding(chrome.Padding);

                if (ResolveTitle(chrome) is { } title)
                {
                    frame = frame.WithHeader(title);
                }

                return frame;

            default:
                return component;
        }
    }

    private string? ResolveTitle(ShellSectionChrome chrome)
        => _chrome.ShowSectionTitles ? chrome.Title : null;

    private void UpdateTranscriptHeight(in RenderContext context)
    {
        var transcriptHeight = GetTranscriptHeight(in context);
        _lastTranscriptHeight = transcriptHeight;
        _transcript.SetHeight(transcriptHeight);
        _mainViewport.Height = transcriptHeight;
    }

    private int GetTranscriptHeight(in RenderContext context)
    {
        var nonTranscriptRows = 0;
        var visibleSectionCount = 0;
        foreach (var section in _shell.Sections)
        {
            if (!section.IsVisible)
            {
                continue;
            }

            visibleSectionCount++;
            if (!section.IsMain)
            {
                nonTranscriptRows += section.Component.Measure(in context,
                    HPD.TUI.Layout.LayoutConstraints.Loose(context.Width, context.Height)).Height;
            }
        }

        var gapRows = Math.Max(0, visibleSectionCount - 1) * (_chrome.Gap + 1);
        return Math.Max(1, context.Height - nonTranscriptRows - gapRows);
    }

    private sealed class MainSectionView : Component
    {
        private readonly DefaultAgentTuiShellView _owner;
        private string? _pageId;
        private int _pageHeight;
        private IComponent? _pageComponent;
        private int _pageWidth;
        private ThemeKey _pageThemeKey;
        private ColorSystem _pageColorSystem;

        public MainSectionView(DefaultAgentTuiShellView owner)
        {
            _owner = owner;
        }

        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
            => Resolve().Measure(in context, constraints);

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
            => output.Render(Resolve(), in context, output.MaxWidth);

        public override bool HandleInput(in TuiInputEvent key)
            => Resolve().HandleInput(in key);

        internal void Prepare(int width, Theme theme, ColorSystem colorSystem)
        {
            var activePageId = _owner._model.Navigation.ActivePageId;
            if (string.IsNullOrWhiteSpace(activePageId) ||
                !_owner._registry.TryFindPage(activePageId, out var page))
            {
                _pageId = null;
                _pageComponent = null;
                return;
            }
            if (_pageComponent is not null && string.Equals(_pageId, activePageId, StringComparison.OrdinalIgnoreCase) &&
                _pageHeight == _owner._lastTranscriptHeight && _pageWidth == width &&
                _pageThemeKey == theme.Key && _pageColorSystem == colorSystem) return;
            _pageId = activePageId;
            _pageHeight = _owner._lastTranscriptHeight;
            _pageWidth = width;
            _pageThemeKey = theme.Key;
            _pageColorSystem = colorSystem;
            _pageComponent = page.Render(new AgentTuiPageContext(
                _owner._model.Scope, _owner._model, _owner._model.Navigation, _owner._registry,
                page, _pageHeight, _owner._state, width, theme, colorSystem));
        }

        private IComponent Resolve()
        {
            var activePageId = _owner._model.Navigation.ActivePageId;
            if (string.IsNullOrWhiteSpace(activePageId) ||
                !_owner._registry.TryFindPage(activePageId, out _))
            {
                _pageId = null;
                _pageComponent = null;
                _owner._transcript.SetHeight(_owner._lastTranscriptHeight);
                return _owner._transcript;
            }

            if (string.Equals(_pageId, activePageId, StringComparison.OrdinalIgnoreCase) &&
                _pageHeight == _owner._lastTranscriptHeight &&
                _pageComponent is not null)
            {
                return _pageComponent;
            }

            throw new InvalidOperationException("The active page was not prepared for this frame.");
        }
    }

    private sealed class RetainedShellStack : Component
    {
        private readonly List<RetainedShellSection> _sections = [];

        public RetainedShellStack(int gap)
        {
            Gap = gap;
        }

        public int Gap { get; }

        public IReadOnlyList<RetainedShellSection> Sections => _sections;

        public void Add(RetainedShellSection section)
            => _sections.Add(section);

        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
        {
            var maxWidth = constraints.MaxWidth;
            var min = 0;
            var max = 0;
            var height = 0;
            var visibleCount = 0;
            foreach (var section in _sections)
            {
                if (!section.IsVisible)
                {
                    continue;
                }

                var measurement = section.Component.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(maxWidth, context.Height));
                min = Math.Max(min, measurement.MinWidth);
                max = Math.Max(max, measurement.MaxWidth);
                height += measurement.Height;
                visibleCount++;
            }

            height += Math.Max(0, visibleCount - 1) * (Gap + 1);
            return new Measurement(Math.Min(min, maxWidth), Math.Min(max, maxWidth), height);
        }

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            var maxWidth = output.MaxWidth;
            var wrote = false;
            foreach (var section in _sections)
            {
                if (!section.IsVisible)
                {
                    continue;
                }

                if (wrote)
                {
                    for (var gap = 0; gap <= Gap; gap++)
                    {
                        output.WriteLineBreak();
                    }
                }

                output.Render(section.Component, in context, maxWidth);
                wrote = true;
            }
        }

        public override bool HandleInput(in TuiInputEvent key)
        {
            var handled = false;
            foreach (var section in _sections)
            {
                if (section.IsVisible)
                {
                    handled |= section.Component.HandleInput(in key);
                }
            }

            return handled;
        }
    }

    private sealed class RetainedShellSection
    {
        private readonly Func<bool>? _isVisible;

        public RetainedShellSection(IComponent component, bool isMain, Func<bool>? isVisible)
        {
            Component = component ?? throw new ArgumentNullException(nameof(component));
            IsMain = isMain;
            _isVisible = isVisible;
        }

        public IComponent Component { get; }

        public bool IsMain { get; }

        public bool IsVisible => _isVisible?.Invoke() ?? true;
    }
}
