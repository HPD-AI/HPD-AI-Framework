using HPD.TUI.Models;

namespace HPD.TUI.Controllers;

public sealed class CollectionNavigationController<T>
{
    private readonly CollectionModel<T> _model;
    private int _activeIndex;

    public CollectionNavigationController(CollectionModel<T> model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        MoveFirst();
    }

    public int ActiveIndex => _activeIndex;

    public CollectionItem<T>? ActiveItem => _model.GetItemOrDefault(_activeIndex);

    public ViewportModel Viewport => _model.Viewport;

    public void Move(int delta)
    {
        if (_model.Items.Count == 0 || _model.VisibleCount == 0 || delta == 0)
        {
            return;
        }

        var index = _activeIndex;
        for (var attempts = 0; attempts < _model.Items.Count; attempts++)
        {
            index += delta;
            if (_model.WrapNavigation)
            {
                index = Wrap(index, _model.Items.Count);
            }
            else
            {
                index = Math.Clamp(index, 0, _model.Items.Count - 1);
            }

            if (_model.IsVisible(index) && !_model.Items[index].Disabled)
            {
                _activeIndex = index;
                EnsureActiveVisible();
                return;
            }

            if (!_model.WrapNavigation && (index == 0 || index == _model.Items.Count - 1))
            {
                return;
            }
        }
    }

    public void MoveFirst()
    {
        for (var i = 0; i < _model.Items.Count; i++)
        {
            if (_model.IsVisible(i) && !_model.Items[i].Disabled)
            {
                _activeIndex = i;
                EnsureActiveVisible();
                return;
            }
        }

        _activeIndex = 0;
    }

    public void MoveLast()
    {
        for (var i = _model.Items.Count - 1; i >= 0; i--)
        {
            if (_model.IsVisible(i) && !_model.Items[i].Disabled)
            {
                _activeIndex = i;
                EnsureActiveVisible();
                return;
            }
        }

        _activeIndex = 0;
    }

    public void Page(int visibleCount, int direction)
    {
        if (direction == 0)
        {
            return;
        }

        var rows = visibleCount > 0
            ? visibleCount
            : _model.Viewport.WindowSize > 0
                ? _model.Viewport.WindowSize
                : 10;
        Move(Math.Sign(direction) * Math.Max(1, rows));
    }

    private static int Wrap(int index, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        var wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }

    private void EnsureActiveVisible()
    {
        var visibleIndex = _model.GetVisibleIndex(_activeIndex);
        if (visibleIndex >= 0)
        {
            _model.Viewport.EnsureVisible(visibleIndex, _model.VisibleCount);
        }
    }
}
