namespace HPD.Agent;

/// <summary>Validates caller-supplied identities before they become local storage paths.</summary>
internal static class StoragePathSegment
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars()
        .Concat(['\\', ':', '*', '?', '"', '<', '>', '|'])
        .Distinct()
        .ToArray();

    public static string ValidateSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value is "." or ".." ||
            Path.IsPathRooted(value) ||
            value.IndexOfAny(InvalidChars) >= 0 ||
            value.Any(char.IsControl) ||
            value.EndsWith(".", StringComparison.Ordinal) ||
            value.EndsWith(' ') ||
            System.Text.Encoding.UTF8.GetByteCount(value) > 240 ||
            IsReservedWindowsName(value))
        {
            throw new ArgumentException(
                $"'{parameterName}' must be a single, portable path segment.",
                parameterName);
        }

        return value;
    }

    public static string ValidateRelativePath(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (Path.IsPathRooted(value) || value.Any(char.IsControl))
            throw InvalidPath(parameterName);

        foreach (var segment in value.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
                throw InvalidPath(parameterName);
            ValidateSegment(segment, parameterName);
        }

        return value;
    }

    public static string EncodeScope(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value is "." or ".." ||
            Path.IsPathRooted(value) ||
            value.Contains('/') ||
            value.Contains('\\') ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"'{parameterName}' must be a single logical scope, without path separators.",
                parameterName);
        }

        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return $"scope-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static ArgumentException InvalidPath(string parameterName) =>
        new($"'{parameterName}' contains an empty, rooted, or traversal path segment.", parameterName);

    private static bool IsReservedWindowsName(string value)
    {
        var stem = value.Split('.', 2)[0];
        return stem.ToUpperInvariant() is
            "CON" or "PRN" or "AUX" or "NUL" or
            "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or
            "COM6" or "COM7" or "COM8" or "COM9" or
            "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or
            "LPT6" or "LPT7" or "LPT8" or "LPT9";
    }
}
