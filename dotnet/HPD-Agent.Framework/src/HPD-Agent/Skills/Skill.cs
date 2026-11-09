/// <summary>
/// Describes an immutable progressively disclosed skill.
/// </summary>
/// <remarks>
/// A skill is owned by a tool harness. Before activation the model sees only
/// <see cref="Name"/> and <see cref="Description"/>. Activating it returns
/// <see cref="Instructions"/> and reveals its child <see cref="Capabilities"/>.
/// </remarks>
public sealed record Skill
{
    private Skill()
    {
    }

    /// <summary>Gets the logical definition identifier.</summary>
    public string Id { get; internal init; } = null!;

    /// <summary>Gets the model-visible activation function name.</summary>
    public string Name { get; internal init; } = null!;

    /// <summary>Gets the discovery description visible before activation.</summary>
    public string Description { get; internal init; } = null!;

    /// <summary>Gets the authoritative instructions returned by activation.</summary>
    public SkillInstructionProvider Instructions { get; internal init; } = null!;

    /// <summary>Gets optional higher-priority reinforcement instructions.</summary>
    public SkillInstructionProvider? Reinforcement { get; internal init; }

    /// <summary>Gets the capabilities revealed after activation.</summary>
    public IReadOnlyList<SkillCapability> Capabilities { get; internal init; } = Array.Empty<SkillCapability>();

    /// <summary>Gets the activation lifetime.</summary>
    public SkillActivationLifetime Lifetime { get; internal init; } = SkillActivationLifetime.MessageTurn;

    /// <summary>Gets optional source and trust provenance.</summary>
    public SkillProvenance? Provenance { get; internal init; }

    /// <summary>Creates and validates an immutable skill definition.</summary>
    /// <param name="name">The model-visible activation name.</param>
    /// <param name="description">The discovery description visible before activation.</param>
    /// <param name="instructions">The authoritative activation instruction provider.</param>
    /// <param name="capabilities">Capabilities revealed by activation.</param>
    /// <param name="reinforcement">Optional higher-priority reinforcement provider.</param>
    /// <param name="id">An optional logical definition identifier; defaults to <paramref name="name"/>.</param>
    /// <param name="provenance">Optional source and trust provenance.</param>
    /// <returns>A validated immutable skill.</returns>
    public static Skill Create(
        string name,
        string description,
        SkillInstructionProvider instructions,
        IReadOnlyList<SkillCapability>? capabilities = null,
        SkillInstructionProvider? reinforcement = null,
        string? id = null,
        SkillProvenance? provenance = null)
    {
        SkillModelName.Validate(name, nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(instructions);

        var capabilitySnapshot = capabilities is null
            ? Array.Empty<SkillCapability>()
            : capabilities.ToArray();

        if (capabilitySnapshot.Any(static capability => capability is null))
            throw new ArgumentException("Skill capabilities cannot contain null values.", nameof(capabilities));

        return new Skill
        {
            Id = string.IsNullOrWhiteSpace(id) ? name : id,
            Name = name,
            Description = description,
            Instructions = instructions,
            Capabilities = capabilitySnapshot,
            Reinforcement = reinforcement,
            Lifetime = SkillActivationLifetime.MessageTurn,
            Provenance = provenance
        };
    }
}
