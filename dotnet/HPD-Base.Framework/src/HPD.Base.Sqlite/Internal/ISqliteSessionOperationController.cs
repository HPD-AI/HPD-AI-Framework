namespace HPD.Base.Sqlite;

internal interface ISqliteSessionOperationController
{
    /// <summary>Executes the before execute async operation.</summary>
    ValueTask BeforeExecuteAsync(CancellationToken cancellationToken);
}

internal sealed class DefaultSqliteSessionOperationController : ISqliteSessionOperationController
{
    /// <summary>Gets the instance.</summary>
    public static DefaultSqliteSessionOperationController Instance { get; } = new();

    private DefaultSqliteSessionOperationController()
    {
    }

    /// <summary>Executes the before execute async operation.</summary>
    public ValueTask BeforeExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
