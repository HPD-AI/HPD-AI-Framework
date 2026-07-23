using HPD.Agent;

/// <summary>Base type for a capability revealed by a skill.</summary>
public abstract class SkillCapability
{
    /// <summary>Initializes a skill capability.</summary>
    /// <param name="name">The model-visible capability name.</param>
    /// <param name="description">The model-visible capability description.</param>
    protected SkillCapability(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    /// <summary>Gets the model-visible name.</summary>
    public string Name { get; }

    /// <summary>Gets the model-visible description.</summary>
    public string Description { get; }
}

/// <summary>Provides the non-generic contract for a generated tool-harness function reference.</summary>
internal interface ISkillFunctionReference
{
    Type ToolHarnessType { get; }
    string MemberName { get; }
}

public sealed class SkillFunctionReference<TToolHarness> : SkillCapability, ISkillFunctionReference
    where TToolHarness : class
{
    internal SkillFunctionReference(string memberName)
        : base(memberName, string.Empty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        MemberName = memberName;
    }

    /// <summary>Gets the referenced C# member name.</summary>
    public string MemberName { get; }

    Type ISkillFunctionReference.ToolHarnessType => typeof(TToolHarness);
}

/// <summary>Creates structured child capability declarations for skills.</summary>
public static partial class SkillCapabilities
{
    /// <summary>References an AI function declared by a generated tool harness.</summary>
    /// <typeparam name="TToolHarness">The owning tool harness type.</typeparam>
    /// <param name="memberName">The referenced C# member name, normally supplied with <see langword="nameof"/>.</param>
    /// <returns>A symbol-analyzable function reference.</returns>
    public static SkillFunctionReference<TToolHarness> Function<TToolHarness>(string memberName)
        where TToolHarness : class
        => new(memberName);

    /// <summary>Creates an inline text resource.</summary>
    public static SkillResource Resource(string name, string description, string content)
        => new InlineSkillResource(name, description, content);

    /// <summary>Uses an explicitly constructed resource capability.</summary>
    public static SkillResource Resource(SkillResource resource)
        => resource ?? throw new ArgumentNullException(nameof(resource));

    /// <summary>Creates an external script capability.</summary>
    /// <param name="name">The model-visible function name.</param>
    /// <param name="description">The model-visible selection and side-effect description.</param>
    /// <param name="reference">The storage-neutral script reference and runtime identifier.</param>
    /// <param name="input">The required explicit script input contract.</param>
    /// <param name="requiresPermission">Whether invocation requires permission.</param>
    /// <param name="timeout">The execution deadline, or the default two-minute deadline.</param>
    /// <param name="maximumOutputBytes">The maximum encoded result size.</param>
    /// <returns>The configured script capability.</returns>
    public static SkillScript Script(
        string name,
        string description,
        SkillScriptReference reference,
        SkillScriptInputContract input,
        bool requiresPermission = true,
        TimeSpan? timeout = null,
        long maximumOutputBytes = 1_048_576)
        => new(name, description)
        {
            Reference = reference ?? throw new ArgumentNullException(nameof(reference)),
            InputContract = input ?? throw new ArgumentNullException(nameof(input)),
            RequiresPermission = requiresPermission,
            Timeout = timeout ?? TimeSpan.FromMinutes(2),
            MaximumOutputBytes = maximumOutputBytes
        };

    /// <summary>Creates an external script capability from a reusable generated input contract.</summary>
    /// <typeparam name="T">The generated CLR input type.</typeparam>
    /// <param name="name">The model-visible function name.</param>
    /// <param name="description">The model-visible selection and side-effect description.</param>
    /// <param name="reference">The storage-neutral script reference and runtime identifier.</param>
    /// <param name="input">The required reusable generated input contract.</param>
    /// <param name="requiresPermission">Whether invocation requires permission.</param>
    /// <param name="timeout">The execution deadline, or the default two-minute deadline.</param>
    /// <param name="maximumOutputBytes">The maximum encoded result size.</param>
    /// <returns>The configured script capability.</returns>
    public static SkillScript Script<T>(
        string name,
        string description,
        SkillScriptReference reference,
        IAIInputContract<T> input,
        bool requiresPermission = true,
        TimeSpan? timeout = null,
        long maximumOutputBytes = 1_048_576)
        => Script(
            name,
            description,
            reference,
            SkillScriptInput.Generated(input),
            requiresPermission,
            timeout,
            maximumOutputBytes);
}
