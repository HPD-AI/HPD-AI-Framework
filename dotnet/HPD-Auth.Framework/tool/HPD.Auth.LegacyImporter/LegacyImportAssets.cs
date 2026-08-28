using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Auth.LegacyImporter;

/// <summary>Loads and verifies the immutable L2B importer assets.</summary>
internal static class LegacyImportAssets
{
    internal const string ProtocolId = "hpd.auth.legacy-import.v1";
    internal const string SourceSchemaId = "hpd.auth.legacy.sqlite.20260804000048.v1";
    internal const string MigrationId = "20260804000048_InitialSqlite";
    internal const string SourceCatalogDigest = "9b90599a0a9cfc89e0b2462ec9e2a23ac296d6c431d41381905336726d719be9";

    private const string CatalogSuffix = "Assets.legacy-sqlite-20260804000048.catalog.json";
    private const string ExtractionSuffix = "Assets.legacy-sqlite-20260804000048.extract.sql";

    internal static byte[] ReadCatalog()
    {
        byte[] bytes = ReadEmbedded(CatalogSuffix);
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!StringComparer.Ordinal.Equals(digest, SourceCatalogDigest))
            throw new InvalidOperationException("The embedded source catalog does not match its reviewed digest.");
        return bytes;
    }

    internal static string ReadExtractionSql()
    {
        byte[] bytes = ReadEmbedded(ExtractionSuffix);
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static byte[] ReadEmbedded(string suffix)
    {
        Assembly assembly = typeof(LegacyImportAssets).Assembly;
        string name = assembly.GetManifestResourceNames().Single(candidate => candidate.EndsWith(suffix, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded importer asset '{suffix}' is unavailable.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
