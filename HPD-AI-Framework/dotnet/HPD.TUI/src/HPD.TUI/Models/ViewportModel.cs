namespace HPD.TUI.Models;

public sealed class ViewportModel
{
    public int Offset { get; private set; }

    public int WindowSize { get; private set; }

    public void SetWindowSize(int windowSize, int totalCount)
    {
        WindowSize = Math.Max(0, windowSize);
        Clamp(totalCount);
    }

    public void EnsureVisible(int index, int totalCount)
    {
        if (totalCount <= 0 || WindowSize <= 0)
        {
            Offset = 0;
            return;
        }

        index = Math.Clamp(index, 0, totalCount - 1);
        if (index < Offset)
        {
            Offset = index;
        }
        else if (index >= Offset + WindowSize)
        {
            Offset = index - WindowSize + 1;
        }

        Clamp(totalCount);
    }

    public void ScrollBy(int delta, int totalCount)
    {
        Offset += delta;
        Clamp(totalCount);
    }

    public void MoveToStart()
    {
        Offset = 0;
    }

    public void MoveToEnd(int totalCount)
    {
        Offset = Math.Max(0, totalCount - WindowSize);
    }

    private void Clamp(int totalCount)
    {
        var maxOffset = Math.Max(0, totalCount - WindowSize);
        Offset = Math.Clamp(Offset, 0, maxOffset);
    }
}
