using System.ComponentModel;

namespace HPD.Agent;

/// <summary>Generated native factory output consumed by the common skill materializer.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record GeneratedSkillDefinition(
    string DeclarationMemberName,
    Skill Definition,
    bool RequiresPermission);
