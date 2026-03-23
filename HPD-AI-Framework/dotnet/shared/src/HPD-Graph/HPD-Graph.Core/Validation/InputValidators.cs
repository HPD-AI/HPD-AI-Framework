using HPDAgent.Graph.Abstractions.Validation;
using System.Collections;
using System.Text.RegularExpressions;

namespace HPDAgent.Graph.Core.Validation;

/// <summary>
/// Built-in validators for common input patterns.
/// </summary>
public static class InputValidators
{
    /// <summary>Validate integer is within range (inclusive).</summary>
    public static IInputValidator Range(int min, int max)
        => new RangeValidator(min, max);

    /// <summary>Validate string is a valid URL.</summary>
    public static IInputValidator Url()
        => new UrlValidator();

    /// <summary>Validate string matches regex pattern.</summary>
    public static IInputValidator Regex(string pattern)
        => new RegexValidator(pattern);

    /// <summary>Validate string is a valid email address.</summary>
    public static IInputValidator Email()
        => new EmailValidator();

    /// <summary>Validate value is a valid enum value.</summary>
    public static IInputValidator Enum<TEnum>() where TEnum : struct, Enum
        => new EnumValidator<TEnum>();

    /// <summary>Validate string length is within bounds.</summary>
    public static IInputValidator StringLength(int minLength, int maxLength)
        => new StringLengthValidator(minLength, maxLength);

    /// <summary>Validate collection count is within bounds.</summary>
    public static IInputValidator CollectionCount(int minCount, int maxCount)
        => new CollectionCountValidator(minCount, maxCount);
}

// ===== VALIDATOR IMPLEMENTATIONS =====

internal sealed class RangeValidator : IInputValidator
{
    private readonly int _min;
    private readonly int _max;

    public RangeValidator(int min, int max)
    {
        _min = min;
        _max = max;
    }

    public ValidationResult Validate(string inputName, object? value)
    {
        if (value is not int intValue)
            return ValidationResult.Failure($"{inputName} must be an integer");

        if (intValue < _min || intValue > _max)
            return ValidationResult.Failure(
                $"{inputName} must be between {_min} and {_max}, got {intValue}");

        return ValidationResult.Success();
    }
}

internal sealed class UrlValidator : IInputValidator
{
    public ValidationResult Validate(string inputName, object? value)
    {
        if (value is not string strValue)
            return ValidationResult.Failure($"{inputName} must be a string");

        if (string.IsNullOrWhiteSpace(strValue))
            return ValidationResult.Failure($"{inputName} cannot be empty");

        if (!Uri.TryCreate(strValue, UriKind.Absolute, out var uri))
            return ValidationResult.Failure($"{inputName} must be a valid URL");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return ValidationResult.Failure($"{inputName} must use HTTP or HTTPS scheme");

        return ValidationResult.Success();
    }
}

internal sealed class EmailValidator : IInputValidator
{
    private static readonly System.Text.RegularExpressions.Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled);

    public ValidationResult Validate(string inputName, object? value)
    {
        if (value is not string strValue)
            return ValidationResult.Failure($"{inputName} must be a string");

        if (string.IsNullOrWhiteSpace(strValue))
            return ValidationResult.Failure($"{inputName} cannot be empty");

        if (!EmailRegex.IsMatch(strValue))
            return ValidationResult.Failure($"{inputName} must be a valid email address");

        return ValidationResult.Success();
    }
}

internal sealed class RegexValidator : IInputValidator
{
    private readonly System.Text.RegularExpressions.Regex _regex;
    private readonly string _pattern;

    public RegexValidator(string pattern)
    {
        _pattern = pattern;
        _regex = new System.Text.RegularExpressions.Regex(pattern, RegexOptions.Compiled);
    }

    public ValidationResult Validate(string inputName, object? value)
    {
        if (value is not string strValue)
            return ValidationResult.Failure($"{inputName} must be a string");

        if (!_regex.IsMatch(strValue))
            return ValidationResult.Failure($"{inputName} must match pattern: {_pattern}");

        return ValidationResult.Success();
    }
}

internal sealed class EnumValidator<TEnum> : IInputValidator where TEnum : struct, Enum
{
    public ValidationResult Validate(string inputName, object? value)
    {
        if (value == null)
            return ValidationResult.Failure($"{inputName} cannot be null");

        // Handle string enum values
        if (value is string strValue)
        {
            if (System.Enum.TryParse<TEnum>(strValue, ignoreCase: true, out _))
                return ValidationResult.Success();

            var validValues = string.Join(", ", System.Enum.GetNames<TEnum>());
            return ValidationResult.Failure(
                $"{inputName} must be one of: {validValues}");
        }

        // Handle numeric enum values
        if (System.Enum.IsDefined(typeof(TEnum), value))
            return ValidationResult.Success();

        var validValuesForNumeric = string.Join(", ", System.Enum.GetNames<TEnum>());
        return ValidationResult.Failure(
            $"{inputName} must be a valid {typeof(TEnum).Name} value. Valid values: {validValuesForNumeric}");
    }
}

internal sealed class StringLengthValidator : IInputValidator
{
    private readonly int _minLength;
    private readonly int _maxLength;

    public StringLengthValidator(int minLength, int maxLength)
    {
        _minLength = minLength;
        _maxLength = maxLength;
    }

    public ValidationResult Validate(string inputName, object? value)
    {
        if (value is not string strValue)
            return ValidationResult.Failure($"{inputName} must be a string");

        if (strValue.Length < _minLength || strValue.Length > _maxLength)
            return ValidationResult.Failure(
                $"{inputName} length must be between {_minLength} and {_maxLength}, got {strValue.Length}");

        return ValidationResult.Success();
    }
}

internal sealed class CollectionCountValidator : IInputValidator
{
    private readonly int _minCount;
    private readonly int _maxCount;

    public CollectionCountValidator(int minCount, int maxCount)
    {
        _minCount = minCount;
        _maxCount = maxCount;
    }

    public ValidationResult Validate(string inputName, object? value)
    {
        if (value is not IEnumerable enumerable)
            return ValidationResult.Failure($"{inputName} must be a collection");

        var count = 0;
        foreach (var _ in enumerable)
        {
            count++;
            // Early exit if we exceed max
            if (count > _maxCount)
                break;
        }

        if (count < _minCount || count > _maxCount)
            return ValidationResult.Failure(
                $"{inputName} count must be between {_minCount} and {_maxCount}, got {count}");

        return ValidationResult.Success();
    }
}
