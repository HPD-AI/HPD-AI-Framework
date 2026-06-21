namespace HPD.Auth.Core.Options;

/// <summary>
/// Database connection and behavior configuration.
/// </summary>
public class DatabaseOptions
{
    /// <summary>
    /// ADO.NET connection string for the auth database.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Database schema to use for auth tables.
    /// Defaults to "public" (PostgreSQL) or "dbo" (SQL Server).
    /// </summary>
    public string Schema { get; set; } = "public";

    /// <summary>
    /// Whether to apply pending EF Core migrations automatically on startup.
    /// Recommended: false in production; use explicit migration scripts.
    /// </summary>
    public bool AutoMigrate { get; set; } = false;

    /// <summary>
    /// Command timeout in seconds for database operations.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
