namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Unified interface for all capability types (Functions, Skills, SubAgents).
/// Enables polymorphic processing during source generation.
/// </summary>
internal interface ICapability
{
    /// <summary>
    /// The name of the capability (method name).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The type of capability (Function, Skill, or SubAgent).
    /// </summary>
    CapabilityType Type { get; }

    /// <summary>
    /// Indicates whether this capability is a container that groups other functions.
    /// - Functions: false (direct execution)
    /// - Skills: true (container that expands to constituent functions)
    /// - SubAgents: false (wrapper that delegates to another agent)
    /// </summary>
    bool IsContainer { get; }

    /// <summary>
    /// Indicates whether this capability has conditional visibility.
    /// Capabilities with conditionals are only registered when their condition evaluates to true.
    /// </summary>
    bool IsConditional { get; }

    /// <summary>
    /// Indicates whether this capability requires instance state to execute.
    /// - Static methods: false
    /// - Instance methods: true
    /// </summary>
    bool RequiresInstance { get; }

    /// <summary>
    /// Whether this capability is emitted into the CreateTools() functions list.
    /// - true: emitted as functions.Add(HPDAIFunctionFactory.Create(...)) in CreateTools
    /// - false: has its own registration path (e.g., Skills via helper methods, MCPServers via static property)
    /// </summary>
    bool EmitsIntoCreateTools { get; }

    /// <summary>
    /// Indicates whether this capability has a dynamic description that uses metadata interpolation.
    /// Example: "Analyze {metadata.ProjectType} code"
    /// </summary>
    bool HasDynamicDescription { get; }

    /// <summary>
    /// Generates the registration code for this capability.
    /// This creates the HPDAIFunctionFactory.Create(...) call with all necessary metadata.
    /// </summary>
    /// <param name="parent">The parent Toolkit that contains this capability.</param>
    /// <returns>The generated registration code as a string.</returns>
    string GenerateRegistrationCode(object parent);

    /// <summary>
    /// Generates container-specific code if this capability is a container.
    /// For Skills, this generates the container function that groups constituent functions.
    /// For non-containers (Functions, SubAgents), returns null.
    /// </summary>
    /// <returns>The generated container code, or null if not a container.</returns>
    string? GenerateContainerCode();

    /// <summary>
    /// Gets additional metadata properties that should be included in the AIFunction's
    /// AdditionalProperties dictionary. This metadata is used at runtime by middleware.
    /// </summary>
    /// <returns>Dictionary of metadata key-value pairs.</returns>
    Dictionary<string, object> GetAdditionalProperties();

    /// <summary>
    /// Resolves references to other capabilities (primarily for Skills that reference functions).
    /// This is called after all capabilities have been analyzed to allow cross-capability and
    /// cross-assembly reference resolution.
    /// </summary>
    /// <param name="allCapabilities">All capabilities from all Toolkits in the compilation.</param>
    void ResolveReferences(List<ICapability> allCapabilities);

    /// <summary>
    /// Generates context resolver methods for dynamic descriptions and conditional evaluation.
    /// This enables all capability types (Functions, Skills, SubAgents) to use:
    /// - Dynamic descriptions with metadata interpolation (e.g., "{metadata.PropertyName}")
    /// - Conditional visibility based on metadata (e.g., [ConditionalFunction], [ConditionalSkill], [ConditionalSubAgent])
    /// Returns empty string if no resolvers are needed.
    /// </summary>
    /// <returns>Generated C# code for resolver methods, or empty string if not needed.</returns>
    string GenerateContextResolvers();
}
