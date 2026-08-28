using HPD.Auth.LegacyImporter;

if (args is not ["validate-source", string sourcePath])
{
    Console.Error.WriteLine($"{LegacyImportFailure.InvalidInvocation}: usage: hpd-auth-legacy-import validate-source <legacy.db>");
    return 2;
}

try
{
    await using LegacySqliteSource source = await LegacySqliteSource.OpenAsync(sourcePath, CancellationToken.None);
    Console.WriteLine($"sourceSchemaId={LegacyImportAssets.SourceSchemaId}");
    Console.WriteLine($"sourceCatalogDigest={LegacyImportAssets.SourceCatalogDigest}");
    return 0;
}
catch (LegacyImportException exception)
{
    Console.Error.WriteLine($"{exception.Code}: {exception.Message}");
    return 1;
}
