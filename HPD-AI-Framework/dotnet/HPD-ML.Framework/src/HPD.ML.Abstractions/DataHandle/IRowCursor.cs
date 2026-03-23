namespace HPD.ML.Abstractions;

/// <summary>Forward-only cursor over selected columns.</summary>
public interface IRowCursor : IDisposable
{
    bool MoveNext();
    IRow Current { get; }
}
