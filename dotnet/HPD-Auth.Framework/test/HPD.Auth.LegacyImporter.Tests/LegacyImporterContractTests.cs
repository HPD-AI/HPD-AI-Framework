using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HPD.Auth.LegacyImporter.Tests;

public sealed class LegacyImporterContractTests
{
    [Fact]
    public void Embedded_catalog_matches_the_normative_digest()
    {
        byte[] catalog = LegacyImportAssets.ReadCatalog();
        Convert.ToHexStringLower(SHA256.HashData(catalog)).Should().Be(LegacyImportAssets.SourceCatalogDigest);
        catalog[^1].Should().Be((byte)'\n');
    }

    [Fact]
    public void Embedded_extraction_plan_is_fixed_and_parameterless()
    {
        IReadOnlyList<LegacyExtractionStatement> statements = LegacyExtractionPlan.Load();
        statements.Should().HaveCount(15);
        statements.Should().OnlyContain(statement => statement.CommandText.StartsWith("SELECT ", StringComparison.Ordinal));
        statements.Should().OnlyContain(statement => !statement.CommandText.Contains("SELECT *", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Legacy_refresh_digest_decodes_the_bearer_bytes_not_its_text()
    {
        string bearer = Convert.ToBase64String(Enumerable.Range(0, 64).Select(value => (byte)value).ToArray());
        byte[] digest = LegacyValueCodec.ComputeLegacyRefreshDigest(bearer);
        digest.Should().HaveCount(32);
        Action nonCanonical = () => LegacyValueCodec.ComputeLegacyRefreshDigest(" " + bearer);
        nonCanonical.Should().Throw<LegacyImportException>().Which.Code.Should().Be(LegacyImportFailure.SourceSchemaMismatch);
    }

    [Fact]
    public async Task Source_with_wal_sidecar_is_rejected_before_open()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hpd-auth-importer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "legacy.db");
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Sample(Id INTEGER PRIMARY KEY);";
                await command.ExecuteNonQueryAsync();
            }
            await File.WriteAllBytesAsync(path + "-wal", [1]);

            Func<Task> action = async () => await LegacySqliteSource.OpenAsync(path, CancellationToken.None);
            LegacyImportException failure = (await action.Should().ThrowAsync<LegacyImportException>()).Which;
            failure.Code.Should().Be(LegacyImportFailure.SourceUnavailable);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Unknown_source_schema_fails_with_the_stable_code()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-auth-importer-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Sample(Id INTEGER PRIMARY KEY);";
                await command.ExecuteNonQueryAsync();
            }

            Func<Task> action = async () => await LegacySqliteSource.OpenAsync(path, CancellationToken.None);
            LegacyImportException failure = (await action.Should().ThrowAsync<LegacyImportException>()).Which;
            failure.Code.Should().Be(LegacyImportFailure.SourceSchemaMismatch);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Frozen_catalog_is_accepted_through_the_real_sqlite_pragma_pipeline()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-auth-importer-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await CreateFrozenSchemaAsync(path);
            await using LegacySqliteSource source = await LegacySqliteSource.OpenAsync(path, CancellationToken.None);
            await source.VerifyUnchangedAsync(CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Extra_schema_object_is_rejected_by_the_real_sqlite_pragma_pipeline()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-auth-importer-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await CreateFrozenSchemaAsync(path);
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "CREATE TRIGGER unexpected_trigger AFTER INSERT ON \"AspNetUsers\" BEGIN SELECT 1; END;";
                await command.ExecuteNonQueryAsync();
            }

            Func<Task> action = async () => await LegacySqliteSource.OpenAsync(path, CancellationToken.None);
            LegacyImportException failure = (await action.Should().ThrowAsync<LegacyImportException>()).Which;
            failure.Code.Should().Be(LegacyImportFailure.SourceSchemaMismatch);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Fixed_extraction_streams_at_most_two_hundred_deeply_owned_rows_per_chunk()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-auth-importer-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await CreateFrozenSchemaAsync(path);
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using System.Data.Common.DbTransaction transaction = await connection.BeginTransactionAsync();
                for (int index = 0; index < 201; index++)
                {
                    await using SqliteCommand command = connection.CreateCommand();
                    command.Transaction = (SqliteTransaction)transaction;
                    command.CommandText = "INSERT INTO \"AspNetRoles\" (\"Id\",\"InstanceId\",\"Description\",\"Created\",\"Name\",\"NormalizedName\",\"ConcurrencyStamp\") VALUES ($id,$tenant,NULL,$created,$name,$normalized,$stamp);";
                    command.Parameters.AddWithValue("$id", GuidFromIndex(index).ToString("D"));
                    command.Parameters.AddWithValue("$tenant", Guid.Empty.ToString("D"));
                    command.Parameters.AddWithValue("$created", DateTimeOffset.UnixEpoch.ToString("O"));
                    command.Parameters.AddWithValue("$name", $"role-{index:D3}");
                    command.Parameters.AddWithValue("$normalized", $"ROLE-{index:D3}");
                    command.Parameters.AddWithValue("$stamp", $"stamp-{index:D3}");
                    await command.ExecuteNonQueryAsync();
                }
                await transaction.CommitAsync();
            }

            await using LegacySqliteSource source = await LegacySqliteSource.OpenAsync(path, CancellationToken.None);
            LegacyExtractionStatement roles = LegacyExtractionPlan.Load().Single(statement => statement.Table == "AspNetRoles");
            var chunks = new List<LegacyExtractedChunk>();
            await foreach (LegacyExtractedChunk chunk in source.ReadChunksAsync(roles, CancellationToken.None)) chunks.Add(chunk);

            chunks.Select(chunk => chunk.Rows.Length).Should().Equal(200, 1);
            chunks.Select(chunk => chunk.ChunkOrdinal).Should().Equal(0, 1);
            chunks[0].Rows[0].Values[0].Text.Should().Be(GuidFromIndex(0).ToString("D"));
            chunks[1].Rows[0].Values[0].Text.Should().Be(GuidFromIndex(200).ToString("D"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task CreateFrozenSchemaAsync(string path)
    {
        using JsonDocument catalog = JsonDocument.Parse(LegacyImportAssets.ReadCatalog());
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        foreach (JsonElement table in catalog.RootElement.GetProperty("tables").EnumerateArray())
        {
            string tableName = table.GetProperty("name").GetString()!;
            var clauses = new List<string>();
            foreach (JsonElement column in table.GetProperty("columns").EnumerateArray())
            {
                string name = column.GetProperty("name").GetString()!;
                string type = column.GetProperty("type").GetString()!;
                bool nullable = column.GetProperty("nullable").GetBoolean();
                clauses.Add($"{Quote(name)} {type}{(nullable ? string.Empty : " NOT NULL")}");
            }
            string[] primaryKey = table.GetProperty("primaryKey").EnumerateArray().Select(value => Quote(value.GetString()!)).ToArray();
            if (primaryKey.Length != 0) clauses.Add($"PRIMARY KEY ({string.Join(',', primaryKey)})");
            foreach (JsonElement foreignKey in table.GetProperty("foreignKeys").EnumerateArray())
            {
                string name = foreignKey.GetProperty("name").GetString()!;
                string principal = foreignKey.GetProperty("principalTable").GetString()!;
                string[] columns = foreignKey.GetProperty("columns").EnumerateArray().Select(value => Quote(value.GetString()!)).ToArray();
                string[] principalColumns = foreignKey.GetProperty("principalColumns").EnumerateArray().Select(value => Quote(value.GetString()!)).ToArray();
                string onDelete = foreignKey.GetProperty("onDelete").GetString()!.ToUpperInvariant();
                clauses.Add($"CONSTRAINT {Quote(name)} FOREIGN KEY ({string.Join(',', columns)}) REFERENCES {Quote(principal)} ({string.Join(',', principalColumns)}) ON DELETE {onDelete}");
            }
            await ExecuteAsync(connection, $"CREATE TABLE {Quote(tableName)} ({string.Join(',', clauses)});");
        }

        foreach (JsonElement table in catalog.RootElement.GetProperty("tables").EnumerateArray())
        {
            string tableName = table.GetProperty("name").GetString()!;
            foreach (JsonElement index in table.GetProperty("indexes").EnumerateArray())
            {
                string name = index.GetProperty("name").GetString()!;
                string unique = index.GetProperty("unique").GetBoolean() ? "UNIQUE " : string.Empty;
                string[] columns = index.GetProperty("columns").EnumerateArray().Select(value => Quote(value.GetString()!)).ToArray();
                await ExecuteAsync(connection, $"CREATE {unique}INDEX {Quote(name)} ON {Quote(tableName)} ({string.Join(',', columns)});");
            }
        }
        await ExecuteAsync(connection, $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\",\"ProductVersion\") VALUES ('{LegacyImportAssets.MigrationId}','10.0.0');");
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    private static Guid GuidFromIndex(int value)
    {
        Span<byte> bytes = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes[12..], value);
        return new Guid(bytes, bigEndian: true);
    }
}
