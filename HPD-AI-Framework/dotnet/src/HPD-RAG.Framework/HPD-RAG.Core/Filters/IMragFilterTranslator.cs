namespace HPD.RAG.Core.Filters;

/// <summary>
/// Translates a MragFilterNode AST to the native filter object expected by a specific
/// vector store backend (SQL WHERE string, gRPC Filter, OData string, MQL BsonDocument, etc.).
///
/// Each HPD.RAG.VectorStores.* package supplies its own implementation.
/// Translation logic never lives in HPD.RAG.Core — no central switch over provider keys.
///
/// Security: Implementations must extract typed values from JsonElement
/// (.GetString(), .GetInt32(), etc.) and use parameterized queries — never raw string interpolation.
/// </summary>
public interface IMragFilterTranslator
{
    /// <summary>
    /// Translate the filter AST to the native filter object the backend expects.
    /// Returns null when node is null (no filter).
    /// </summary>
    object? Translate(MragFilterNode? node);
}
