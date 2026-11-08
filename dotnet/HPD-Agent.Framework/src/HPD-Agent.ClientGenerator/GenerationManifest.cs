namespace HPD.Agent.ClientGenerator;

/// <summary>Machine-readable description of one generated SDK package.</summary>
public sealed record GenerationManifest(
    string GeneratorVersion,
    string IrVersion,
    string OpenApiVersion,
    string OpenApiSha256,
    string? EventDigest,
    string InputDigest,
    string IrSha256,
    string PublicApiSha256,
    int OperationCount,
    int ModelCount,
    int EventCount,
    int InputCount,
    int DiagnosticCount,
    int RuntimeSchemaCount);
