namespace HPD.Auth.LegacyImporter;

/// <summary>Stable failure codes emitted by the frozen legacy importer.</summary>
internal static class LegacyImportFailure
{
    internal const string SourceSchemaMismatch = "auth.import.sourceSchemaMismatch";
    internal const string SourceUnavailable = "auth.import.sourceUnavailable";
    internal const string SourceChanged = "auth.import.sourceChanged";
    internal const string InvalidInvocation = "auth.import.invalidInvocation";
}

/// <summary>Represents a safe, operator-facing importer failure.</summary>
internal sealed class LegacyImportException : Exception
{
    internal LegacyImportException(string code, string message) : base(message) => Code = code;

    /// <summary>Gets the stable failure code.</summary>
    internal string Code { get; }
}
