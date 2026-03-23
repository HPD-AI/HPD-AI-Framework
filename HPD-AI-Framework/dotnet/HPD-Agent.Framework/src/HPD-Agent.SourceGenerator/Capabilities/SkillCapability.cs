using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Represents a skill capability - a container that groups related functions together.
/// Decorated with [Skill] attribute. Skills ARE containers that expand to their constituent functions.
/// </summary>
internal class SkillCapability : BaseCapability
{
    public override CapabilityType Type => CapabilityType.Skill;
    public override bool IsContainer => true;  // Skills ARE containers
    public override bool EmitsIntoCreateTools => false;  // Skills registered via GenerateSkillRegistrations()
    public override bool RequiresInstance => true;  // Skills require instance to execute

    // ========== Skill-Specific Properties ==========

    /// <summary>
    /// Method name (e.g., "FileDebugging")
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the skill is marked with [RequiresPermission].
    /// When true, invoking this skill requires user approval.
    /// </summary>
    public bool RequiresPermission { get; set; }

    /// <summary>
    /// Skill options extracted from Skill builder
    /// </summary>
    public SkillOptionsInfo Options { get; set; } = new();

    /// <summary>
    /// Unresolved references to functions or other skills (before resolution phase)
    /// </summary>
    public List<ReferenceInfo> UnresolvedReferences { get; set; } = new();

    /// <summary>
    /// Resolved function references (populated during resolution phase)
    /// Format: "ToolkitName.FunctionName"
    /// </summary>
    public List<string> ResolvedFunctionReferences { get; set; } = new();

    /// <summary>
    /// Resolved Toolkit types (populated during resolution phase)
    /// </summary>
    public List<string> ResolvedToolkitTypes { get; set; } = new();

    /// <summary>
    /// Full name: "ClassName.MethodName"
    /// </summary>
    public string FullQualifiedName => $"{ParentToolkitName}.{MethodName}";

    // ========== Code Generation ==========

    /// <summary>
    /// NOT IMPLEMENTED - Skills use helper method registration via SkillCodeGenerator.GenerateSkillRegistrations().
    /// This method exists for API completeness but is never called due to the hybrid registration pattern.
    /// See V2_ARCHITECTURAL_DECISIONS.md Decision 1 for rationale.
    /// </summary>
    /// <param name="parent">The parent Toolkit that contains this skill (ToolkitInfo).</param>
    /// <returns>The generated registration code as a string.</returns>
    /// <exception cref="NotImplementedException">
    /// Skills use helper method registration. This method should never be called.
    /// If you see this exception, there's a bug in the registration code generation.
    /// </exception>
    public override string GenerateRegistrationCode(object parent)
    {
        throw new NotImplementedException(
            "Skills use helper method registration via SkillCodeGenerator.GenerateSkillRegistrations(). " +
            "This method exists for API completeness but should never be called. " +
            "See V2_ARCHITECTURAL_DECISIONS.md Decision 1 for details.");
    }

    /// <summary>
    /// Skills ARE containers, so this generates the container function.
    /// For Phase 1, returns null as container generation is handled in GenerateRegistrationCode().
    /// </summary>
    public override string? GenerateContainerCode()
    {
        // For skills, container generation is integrated into GenerateRegistrationCode()
        return null;
    }

    /// <summary>
    /// Gets additional metadata properties for this skill.
    /// CRITICAL: This metadata schema must be byte-for-byte identical to the old system
    /// for runtime ContainerMiddleware compatibility.
    /// </summary>
    public override Dictionary<string, object> GetAdditionalProperties()
    {
        var props = base.GetAdditionalProperties();
        props["IsContainer"] = true;
        props["IsSkill"] = true;
        props["ParentContainer"] = ParentToolkitName;
        props["ReferencedFunctions"] = ResolvedFunctionReferences.ToArray();
        props["ReferencedToolkits"] = ResolvedToolkitTypes.ToArray();
        props["RequiresPermission"] = RequiresPermission;

        // Dual-context support (CRITICAL for runtime compatibility)
        if (!string.IsNullOrEmpty(SystemPrompt))
            props["SystemPrompt"] =SystemPrompt;

        // NOTE: Use "Instructions" key (not "FunctionResult") for backward compatibility
        // with runtime ContainerMiddleware. This matches the generated code in GenerateRegistrationCode().
        if (!string.IsNullOrEmpty(FunctionResult))
            props["Instructions"] = FunctionResult;

        return props;
    }

    /// <summary>
    /// Resolves references to other capabilities (functions and skills).
    /// For Phase 1, this is a placeholder. Full implementation will delegate to SkillResolver
    /// in Phase 2-3, then be fully migrated in Phase 5.
    /// </summary>
    /// <param name="allCapabilities">All capabilities from all Toolkits in the compilation.</param>
    public override void ResolveReferences(List<ICapability> allCapabilities)
    {
        // TODO: For Phase 1, this is a placeholder
        // Phase 2-3: Delegate to existing SkillResolver
        // Phase 5: Migrate full logic from SkillResolver to here

        // For now, just keep unresolved references as-is for compilation
    }

    // ========== Helper Methods ==========

    /// <summary>
    /// Formats a property value for code generation.
    /// </summary>
    private string FormatPropertyValue(object value)
    {
        return value switch
        {
            string s => $"@\"{s.Replace("\"", "\"\"")}\"",
            bool b => b.ToString().ToLower(),
            int i => i.ToString(),
            string[] arr => $"new string[] {{ {string.Join(", ", arr.Select(s => $"\"{s}\""))} }}",
            _ => value.ToString() ?? "null"
        };
    }
}

// ========== Supporting Classes (Duplicated from SkillInfo.cs for Phase 1) ==========
// In Phase 2, we'll consolidate these to avoid duplication

/// <summary>
/// Information about skill options extracted from Skill builder
/// </summary>
internal class SkillOptionsInfo
{
    /// <summary>
    /// Optional instruction document paths (OLD - kept for backwards compatibility)
    /// </summary>
    public List<string> InstructionDocuments { get; set; } = new();

    /// <summary>
    /// Base directory for instruction documents (OLD - kept for backwards compatibility)
    /// </summary>
    public string InstructionDocumentBaseDirectory { get; set; } = "agent-skills/documents/";

    /// <summary>
    /// Document references (from AddDocument calls)
    /// </summary>
    public List<DocumentReferenceInfo> DocumentReferences { get; set; } = new();

    /// <summary>
    /// Document uploads (from AddDocumentFromFile calls)
    /// </summary>
    public List<DocumentUploadInfo> DocumentUploads { get; set; } = new();
}

/// <summary>
/// Information about a document reference (from AddDocument call)
/// </summary>
internal class DocumentReferenceInfo
{
    /// <summary>
    /// Document ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Optional description override
    /// </summary>
    public string? DescriptionOverride { get; set; }
}

/// <summary>
/// Information about a document upload (from AddDocumentFromFile or AddDocumentFromUrl call)
/// </summary>
internal class DocumentUploadInfo
{
    /// <summary>
    /// File path to upload (for file-based documents)
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// URL to download (for URL-based documents)
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Document ID (auto-derived or explicit)
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Document description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Source type of the document
    /// </summary>
    public DocumentSourceType SourceType { get; set; }
}

/// <summary>
/// Type of document source
/// </summary>
internal enum DocumentSourceType
{
    /// <summary>
    /// File path-based document (AddDocumentFromFile)
    /// </summary>
    FilePath,

    /// <summary>
    /// URL-based document (AddDocumentFromUrl)
    /// </summary>
    Url
}

/// <summary>
/// Information about a reference in a skill (function or skill reference)
/// </summary>
internal class ReferenceInfo
{
    /// <summary>
    /// Type of reference (function or skill)
    /// </summary>
    public ReferenceType ReferenceType { get; set; }

    /// <summary>
    /// Toolkit type name (e.g., "FileSystemToolkit")
    /// </summary>
    public string ToolkitType { get; set; } = string.Empty;

    /// <summary>
    /// Method name (e.g., "ReadFile" or "FileDebugging")
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Full name: "ToolkitType.MethodName"
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Location in source code (for diagnostics)
    /// </summary>
    public object? Location { get; set; }
}

/// <summary>
/// Type of reference
/// </summary>
internal enum ReferenceType
{
    /// <summary>
    /// Reference to a function (method with [AIFunction])
    /// </summary>
    Function,

    /// <summary>
    /// Reference to another skill (method returning Skill)
    /// </summary>
    Skill
}
