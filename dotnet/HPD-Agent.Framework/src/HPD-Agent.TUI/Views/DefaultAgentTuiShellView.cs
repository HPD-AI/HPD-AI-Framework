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

public sealed class DefaultAgentTuiShellView : Component, IAgentTuiFramePreparable, IScrollbackSource
{
    private readonly ChatShellModel _model;
    private readonly PromptView _prompt;
    private readonly HpdAgentTuiRegistry _registry;
    private readonly AgentTuiStateBag _state;
    private readonly AgentTuiShellChrome _chrome;
    private readonly RetainedShellStack _shell;
    private readonly TranscriptView _transcript;
    private readonly MainSectionView _mainSection;
    private int _lastTranscriptHeight;

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
        _shell = CreateShell();
    }

    /// <inheritdoc />
    public void PrepareFrame(TerminalSize size, Theme theme, ColorSystem colorSystem)
    {
        var context = new RenderContext(size.Width, size.Height, theme, colorSystem);
        UpdateTranscriptHeight(in context);
        _mainSection.Prepare(size.Width, theme, colorSystem);
    }

    public override Measurement Measure(in RenderContext context, int maxWidth)
    {
        UpdateTranscriptHeight(in context);
        return _shell.Measure(in context, maxWidth);
    }

    public override void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        UpdateTranscriptHeight(in context);
        _shell.Render(in context, maxWidth, ref output);
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
    public ScrollbackBatch? PrepareScrollback(in RenderContext context, int maxRows)
        => IsPageActive() ? null : _transcript.PrepareScrollback(in context, maxRows);

    /// <inheritdoc />
    public void CommitScrollback(ScrollbackBatch batch)
        => _transcript.CommitScrollback(batch);

    /// <inheritdoc />
    public void RollbackScrollback(ScrollbackBatch batch)
        => _transcript.RollbackScrollback(batch);

    private RetainedShellStack CreateShell()
    {
        var shell = new RetainedShellStack(_chrome.Gap);

        if (_registry.Header is not null)
        {
            AddSection(shell, _chrome.Header, new ShellContributionView(_model, _registry.Header), isMain: false);
        }

        AddSection(shell, _chrome.Transcript, _mainSection, isMain: true);
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

        _transcript.Height = height;
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
        _transcript.Height = transcriptHeight;
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
                nonTranscriptRows += section.Component.Measure(in context, context.Width).Height;
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

        public override Measurement Measure(in RenderContext context, int maxWidth)
            => Resolve().Measure(in context, maxWidth);

        public override void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
            => Resolve().Render(in context, maxWidth, ref output);

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
                _owner._transcript.Height = _owner._lastTranscriptHeight;
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

        public override Measurement Measure(in RenderContext context, int maxWidth)
        {
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

                var measurement = section.Component.Measure(in context, maxWidth);
                min = Math.Max(min, measurement.MinWidth);
                max = Math.Max(max, measurement.MaxWidth);
                height += measurement.Height;
                visibleCount++;
            }

            height += Math.Max(0, visibleCount - 1) * (Gap + 1);
            return new Measurement(Math.Min(min, maxWidth), Math.Min(max, maxWidth), height);
        }

        public override void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
        {
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

                section.Component.Render(in context, maxWidth, ref output);
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
