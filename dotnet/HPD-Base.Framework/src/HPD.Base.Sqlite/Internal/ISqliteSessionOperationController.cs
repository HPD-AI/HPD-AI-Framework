namespace HPD.Base.Sqlite;

internal interface ISqliteSessionOperationController
{
    ValueTask BeforeExecuteAsync(CancellationToken cancellationToken);
}

internal sealed class DefaultSqliteSessionOperationController : ISqliteSessionOperationController
{
    public static DefaultSqliteSessionOperationController Instance { get; } = new();

    private DefaultSqliteSessionOperationController()
    {
    }

    public ValueTask BeforeExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
