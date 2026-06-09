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
    /// Format: "ToolHarnessName.FunctionName"
    /// </summary>
    public List<string> ResolvedFunctionReferences { get; set; } = new();

    /// <summary>
    /// Resolved ToolHarness types (populated during resolution phase)
    /// </summary>
    public List<string> ResolvedToolHarnessTypes { get; set; } = new();

    /// <summary>
    /// Full name: "ClassName.MethodName"
    /// </summary>
    public string FullQualifiedName => $"{ParentToolHarnessName}.{MethodName}";

    // ========== Code Generation ==========

    /// <summary>
    /// NOT IMPLEMENTED - Skills use helper method registration via SkillCodeGenerator.GenerateSkillRegistrations().
    /// This method exists for API completeness but is never called due to the hybrid registration pattern.
    /// See V2_ARCHITECTURAL_DECISIONS.md Decision 1 for rationale.
    /// </summary>
    /// <param name="parent">The parent ToolHarness that contains this skill (ToolHarnessInfo).</param>
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
    /// </summary>
    public override Dictionary<string, object> GetAdditionalProperties()
    {
        var props = base.GetAdditionalProperties();
        props["IsContainer"] = true;
        props["IsSkill"] = true;
        props["ParentContainer"] = ParentToolHarnessName;
        props["ReferencedFunctions"] = ResolvedFunctionReferences.ToArray();
        props["ReferencedToolHarnesses"] = ResolvedToolHarnessTypes.ToArray();
        props["RequiresPermission"] = RequiresPermission;

        if (!string.IsNullOrEmpty(SystemPrompt))
            props["SystemPrompt"] = SystemPrompt;

        if (!string.IsNullOrEmpty(FunctionResult))
            props["FunctionResult"] = FunctionResult;

        return props;
    }

    /// <summary>
    /// Resolves references to other capabilities (functions and skills).
    /// For Phase 1, this is a placeholder. Full implementation will delegate to SkillResolver
    /// in Phase 2-3, then be fully migrated in Phase 5.
    /// </summary>
    /// <param name="allCapabilities">All capabilities from all ToolHarnesses in the compilation.</param>
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
    /// ToolHarness type name (e.g., "FileSystemToolHarness")
    /// </summary>
    public string ToolHarnessType { get; set; } = string.Empty;

    /// <summary>
    /// Method name (e.g., "ReadFile" or "FileDebugging")
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Full name: "ToolHarnessType.MethodName"
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
