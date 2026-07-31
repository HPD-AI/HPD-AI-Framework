namespace HPD.Base;

internal static class BaseApplicationId
{
    public static void Validate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 128)
        {
            throw new ArgumentOutOfRangeException(parameterName, "BASE identifiers may contain at most 128 characters.");
        }

        if (!char.IsAsciiLetterOrDigit(value[0]))
        {
            throw new ArgumentException("A BASE identifier must start with an ASCII letter or digit.", parameterName);
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) &&
                character is not '.' and not '-' and not '_')
            {
                throw new ArgumentException(
                    "A BASE identifier may contain only ASCII letters, digits, '.', '-', and '_'.",
                    parameterName);
            }
        }
    }
}
