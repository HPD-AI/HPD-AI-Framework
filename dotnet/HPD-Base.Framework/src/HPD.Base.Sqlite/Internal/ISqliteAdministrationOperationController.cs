namespace HPD.Base.Sqlite;

internal interface ISqliteAdministrationOperationController
{
    ValueTask BeforePhaseAsync(string phase, CancellationToken cancellationToken);
    void DeleteFile(string path);
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

    public void DeleteFile(string path) => File.Delete(path);
}
