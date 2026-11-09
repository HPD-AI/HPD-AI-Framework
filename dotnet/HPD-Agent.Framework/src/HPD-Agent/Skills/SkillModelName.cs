internal static class SkillModelName
{
    internal const int MaximumLength = 64;

    internal static void Validate(string name, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, parameterName);
        if (!IsValid(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a valid model-facing skill name. " +
                $"Use 1-{MaximumLength} ASCII letters, digits, underscores, or hyphens, " +
                "and start with a letter or underscore.",
                parameterName);
        }
    }

    internal static bool IsValid(string? name) =>
        name is { Length: > 0 and <= MaximumLength } &&
        name[0] is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_' &&
        name.All(static character =>
            character is (>= 'A' and <= 'Z') or
                (>= 'a' and <= 'z') or
                (>= '0' and <= '9') or
                '_' or
                '-');
}
