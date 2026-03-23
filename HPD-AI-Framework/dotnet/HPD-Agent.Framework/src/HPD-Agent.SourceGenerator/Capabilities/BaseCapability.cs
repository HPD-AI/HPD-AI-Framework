using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Base implementation of ICapability providing common functionality for all capability types.
/// Concrete capability classes (FunctionCapability, SkillCapability, SubAgentCapability) inherit from this.
/// </summary>
internal abstract class BaseCapability : ICapability
{
    // ========== Core Identification ==========

    /// <summary>
    /// The name of the capability (method name).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The description of the capability shown to the LLM.
    /// Can contain dynamic templates like "{metadata.PropertyName}".
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The type of capability (Function, Skill, or SubAgent).
    /// Must be implemented by concrete classes.
    /// </summary>
    public abstract CapabilityType Type { get; }

    /// <summary>
    /// The namespace of the parent Toolkit class.
    /// </summary>
    public string ParentNamespace { get; set; } = string.Empty;

    /// <summary>
    /// The name of the parent Toolkit class.
    /// </summary>
    public string ParentToolkitName { get; set; } = string.Empty;

    // ========== Context & Conditionals (AIFunction-related attributes) ==========

    /// <summary>
    /// Context type name from [AIFunction&lt;TMetadata&gt;] attribute (null if not using generic context).
    /// This enables dynamic descriptions, conditionals, and conditional parameters.
    /// </summary>
    public string? ContextTypeName { get; set; }

    /// <summary>
    /// Conditional expression from [ConditionalFunction], [ConditionalSkill], or [ConditionalSubAgent] attribute (null if always visible).
    /// Example: "IsPremiumUser" or "FeatureFlags.Contains(\"advanced\")"
    /// </summary>
    public string? ConditionalExpression { get; set; }

    /// <summary>
    /// Whether this capability has conditional visibility (based on ConditionalExpression).
    /// </summary>
    public bool IsConditional => !string.IsNullOrEmpty(ConditionalExpression);

    /// <summary>
    /// Whether this capability has a dynamic description that uses metadata interpolation.
    /// Example: "Analyze {metadata.ProjectType} code"
    /// </summary>
    public bool HasDynamicDescription => Description.Contains("{metadata.");

    /// <summary>
    /// Whether this capability uses the generic AIFunction&lt;TMetadata&gt; attribute.
    /// </summary>
    public bool HasTypedMetadata => !string.IsNullOrEmpty(ContextTypeName);

    // ========== Dual-Context Architecture (CRITICAL - Runtime Compatibility) ==========
    // The runtime ContainerMiddleware distinguishes between ephemeral and persistent instructions.

    /// <summary>
    /// Instructions returned as FUNCTION RESULT when capability is activated (literal value).
    /// These instructions are ephemeral - returned once in the function result, then discarded.
    /// Used for one-time guidance that doesn't need to persist across conversation turns.
    /// </summary>
    public string? FunctionResult { get; set; }

    /// <summary>
    /// Instructions returned as FUNCTION RESULT when capability is activated (expression/method call).
    /// Example: "GetInstructions()" or "InstructionsProperty"
    /// </summary>
    public string? FunctionResultExpression { get; set; }

    /// <summary>
    /// Whether FunctionResultExpression is a static member (true) or instance member (false).
    /// Determines whether to call ClassName.Method() or instance.Method().
    /// </summary>
    public bool FunctionResultIsStatic { get; set; } = true;

    /// <summary>
    /// Instructions injected into SYSTEM PROMPT persistently after activation (literal value).
    /// These instructions are persistent - injected into system prompt and remain for all subsequent turns.
    /// Used for ongoing guidance that should influence all future interactions.
    /// </summary>
    public string?SystemPrompt { get; set; }

    /// <summary>
    /// Instructions injected into SYSTEM PROMPT persistently after activation (expression/method call).
    /// Example: "GetSystemInstructions()" or "SystemInstructionsProperty"
    /// </summary>
    public string?SystemPromptExpression { get; set; }

    /// <summary>
    /// WhetherSystemPromptExpression is a static member (true) or instance member (false).
    /// Determines whether to call ClassName.Method() or instance.Method().
    /// </summary>
    public bool SystemPromptIsStatic { get; set; } = true;

    // ========== Abstract Members (Must Be Implemented By Concrete Classes) ==========

    /// <summary>
    /// Indicates whether this capability requires instance state to execute.
    /// - Static methods: false
    /// - Instance methods: true
    /// Must be implemented by concrete capability classes.
    /// </summary>
    public abstract bool RequiresInstance { get; }

    /// <summary>
    /// Indicates whether this capability is a container that groups other functions.
    /// - Functions: false (direct execution)
    /// - Skills: true (container that expands to constituent functions)
    /// - SubAgents: false (wrapper that delegates to another agent)
    /// Must be implemented by concrete capability classes.
    /// </summary>
    public abstract bool IsContainer { get; }

    /// <summary>
    /// Whether this capability is emitted into the CreateTools() functions list.
    /// - true: emitted as functions.Add(HPDAIFunctionFactory.Create(...)) in CreateTools
    /// - false: has its own registration path (e.g., Skills via helper methods, MCPServers via static property)
    /// </summary>
    public abstract bool EmitsIntoCreateTools { get; }

    /// <summary>
    /// Generates the registration code for this capability.
    /// This creates the HPDAIFunctionFactory.Create(...) call with all necessary metadata.
    /// Must be implemented by concrete capability classes.
    /// </summary>
    /// <param name="parent">The parent Toolkit that contains this capability.</param>
    /// <returns>The generated registration code as a string.</returns>
    public abstract string GenerateRegistrationCode(object parent);

    // ========== Virtual Members (Can Be Overridden) ==========

    /// <summary>
    /// Generates container-specific code if this capability is a container.
    /// Default implementation returns null (non-containers).
    /// Skills override this to generate container functions.
    /// </summary>
    /// <returns>The generated container code, or null if not a container.</returns>
    public virtual string? GenerateContainerCode() => null;

    /// <summary>
    /// Gets additional metadata properties that should be included in the AIFunction's
    /// AdditionalProperties dictionary. This metadata is used at runtime by middleware.
    /// Default implementation includes CapabilityType.
    /// Concrete classes can override to add type-specific metadata.
    /// </summary>
    /// <returns>Dictionary of metadata key-value pairs.</returns>
    public virtual Dictionary<string, object> GetAdditionalProperties() => new()
    {
        ["CapabilityType"] = Type.ToString()
    };

    /// <summary>
    /// Resolves references to other capabilities (primarily for Skills that reference functions).
    /// Default implementation does nothing (most capabilities don't need reference resolution).
    /// Skills override this to resolve function references.
    /// </summary>
    /// <param name="allCapabilities">All capabilities from all Toolkits in the compilation.</param>
    public virtual void ResolveReferences(List<ICapability> allCapabilities)
    {
        // Default: no-op (most capabilities don't need reference resolution)
    }

    // ========== Context Resolver Generation (Shared By All Capabilities) ==========

    /// <summary>
    /// Generates context resolver methods (conditionals, dynamic descriptions).
    /// All capabilities inherit this behavior - this is how feature parity is achieved.
    /// Previously only Functions could use these features; now Skills and SubAgents can too.
    /// </summary>
    /// <returns>Generated C# code for resolver methods.</returns>
    public virtual string GenerateContextResolvers()
    {
        var sb = new StringBuilder();

        // Generate dynamic description resolver if needed
        if (HasDynamicDescription && HasTypedMetadata)
        {
            // Convert {metadata.PropertyName} templates to {typedMetadata.PropertyName} for string interpolation
            var interpolatedDescription = Description.Replace("{metadata.", "{typedMetadata.");

            sb.AppendLine($"    private static string Resolve{Name}Description(IToolMetadata? context)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (context == null) return string.Empty;");
            sb.AppendLine($"        if (context is not {ContextTypeName} typedMetadata) return string.Empty;");
            sb.AppendLine($"        return $@\"{interpolatedDescription.Replace("\"", "\"\"")}\";");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // Generate conditional evaluator if needed
        if (IsConditional && HasTypedMetadata)
        {
            // Ensure all property names in the expression are properly prefixed with "typedMetadata."
            // This handles simple cases like "EnableSearch" and boolean expressions like "HasBrave || HasBing"
            var expression = ConditionalExpression;
            if (!string.IsNullOrEmpty(expression))
            {
                // Use regex to find property names (identifiers starting with uppercase letter)
                // and prefix them with "typedMetadata." if they're not already prefixed
                // Matches: word boundary + uppercase letter + word characters
                // Negative lookbehind: not preceded by "typedMetadata." or "metadata."
                expression = System.Text.RegularExpressions.Regex.Replace(
                    expression,
                    @"(?<!typedMetadata\.)(?<!metadata\.)(\b[A-Z][a-zA-Z0-9_]*\b)",
                    "typedMetadata.$1"
                );
            }

            // Generate method signature that accepts IToolMetadata? and casts to typed context
            // This matches the old DSLCodeGenerator pattern for compatibility
            sb.AppendLine($"    private static bool Evaluate{Name}Condition(IToolMetadata? context)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (context == null) return true;");
            sb.AppendLine($"        if (context is not {ContextTypeName} typedMetadata) return false;");
            sb.AppendLine($"        return {expression};");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ========== Helper Methods ==========

    /// <summary>
    /// Gets the full qualified name of this capability (namespace.toolName.capabilityName).
    /// </summary>
    public string FullName => string.IsNullOrEmpty(ParentNamespace)
        ? $"{ParentToolkitName}.{Name}"
        : $"{ParentNamespace}.{ParentToolkitName}.{Name}";
}
