namespace HPD.Base.Sqlite;

internal interface ISqliteAdministrationOperationController
{
    ValueTask BeforePhaseAsync(string phase, CancellationToken cancellationToken);
}

internal sealed class DefaultSqliteAdministrationOperationController : ISqliteAdministrationOperationController
{
    public static DefaultSqliteAdministrationOperationController Instance { get; } = new();
    private DefaultSqliteAdministrationOperationController() { }
    public ValueTask BeforePhaseAsync(string phase, CancellationToken cancellationToken)
    {
        _ = phase;
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
