using System.Collections.Generic;

namespace HPD.Agent.SourceGenerator.Memory;

/// <summary>
/// Information about a pipeline handler discovered during source generation.
/// </summary>
internal class HandlerInfo
{
    /// <summary>
    /// The handler class name (e.g., "TextExtractionHandler")
    /// </summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>
    /// The full namespace of the handler class
    /// </summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>
    /// The step name for this handler (from attribute or derived from class name)
    /// </summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>
    /// The context type this handler works with (e.g., "DocumentIngestionContext")
    /// </summary>
    public string ContextTypeName { get; set; } = string.Empty;

    /// <summary>
    /// The fully qualified context type (e.g., "HPDAgent.Memory.Core.Contexts.DocumentIngestionContext")
    /// </summary>
    public string ContextTypeFullName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this handler is public and can be registered
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// Whether this handler is abstract (should be skipped)
    /// </summary>
    public bool IsAbstract { get; set; }
}

/// <summary>
/// Groups handlers by their context type for registration generation.
/// </summary>
internal class HandlerGroup
{
    /// <summary>
    /// The context type name (e.g., "DocumentIngestionContext")
    /// </summary>
    public string ContextTypeName { get; set; } = string.Empty;

    /// <summary>
    /// The fully qualified context type
    /// </summary>
    public string ContextTypeFullName { get; set; } = string.Empty;

    /// <summary>
    /// All handlers for this context type
    /// </summary>
    public List<HandlerInfo> Handlers { get; set; } = new();

    /// <summary>
    /// The generated extension method name (e.g., "AddGeneratedDocumentIngestionHandlers")
    /// </summary>
    public string ExtensionMethodName => $"AddGenerated{ContextTypeName}Handlers";
}
