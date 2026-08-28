namespace HPD.Auth.LegacyImporter;

/// <summary>Provides the immutable, parameterless extraction statements reviewed by L2B.</summary>
internal static class LegacyExtractionPlan
{
    private static readonly string[] ExpectedTables =
    [
        "AspNetRoles", "AspNetUsers", "AuthAuditEntries", "DataProtectionKeys",
        "SSOProviders", "TenantSettings", "AspNetRoleClaims", "AspNetUserClaims",
        "AspNetUserLogins", "AspNetUserRoles", "AspNetUserTokens", "RefreshTokens",
        "UserIdentities", "UserPasskeys", "UserSessions",
    ];

    internal static IReadOnlyList<LegacyExtractionStatement> Load()
    {
        string sql = LegacyImportAssets.ReadExtractionSql();
        var statements = new List<LegacyExtractionStatement>(ExpectedTables.Length);
        foreach (string table in ExpectedTables)
        {
            string marker = $"-- {table}\n";
            int markerOffset = sql.IndexOf(marker, StringComparison.Ordinal);
            if (markerOffset < 0) Invalid();
            int start = markerOffset + marker.Length;
            int end = sql.IndexOf(';', start);
            if (end < 0) Invalid();
            string commandText = sql[start..(end + 1)].Trim();
            if (!commandText.StartsWith("SELECT ", StringComparison.Ordinal)
                || commandText.Contains("SELECT *", StringComparison.OrdinalIgnoreCase)
                || commandText.Contains('@', StringComparison.Ordinal)
                || commandText.Contains('$', StringComparison.Ordinal))
                Invalid();
            statements.Add(new LegacyExtractionStatement(table, commandText));
        }

        if (statements.Count != ExpectedTables.Length
            || sql.Count(character => character == ';') != ExpectedTables.Length)
            Invalid();
        return statements;
    }

    private static void Invalid() => throw new InvalidOperationException("The embedded extraction plan is not the reviewed L2B plan.");
}

/// <summary>One fixed table extraction statement.</summary>
/// <param name="Table">The exact legacy table name.</param>
/// <param name="CommandText">The exact parameterless SQL statement.</param>
internal sealed record LegacyExtractionStatement(string Table, string CommandText);
