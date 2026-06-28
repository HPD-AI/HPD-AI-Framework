using System.Text.RegularExpressions;

namespace HPD.Agent.ModelsDev;

public readonly partial record struct ModelsDevModelId(string Provider, string Model)
{
    public bool IsZero => string.IsNullOrEmpty(Provider) && string.IsNullOrEmpty(Model);

    public bool IsValid => !string.IsNullOrWhiteSpace(Provider) && !string.IsNullOrWhiteSpace(Model);

    public override string ToString()
        => IsZero ? string.Empty : $"{Provider}/{Model}";

    public static ModelsDevModelId Parse(string value)
    {
        if (!TryParse(value, out var id))
        {
            throw new FormatException($"Invalid models.dev model reference '{value}'. Expected 'provider/model'.");
        }

        return id;
    }

    public static bool TryParse(string? value, out ModelsDevModelId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separator = value.IndexOf('/');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        var provider = value[..separator].Trim();
        var model = value[(separator + 1)..].Trim();
        if (provider.Length == 0 || model.Length == 0)
        {
            return false;
        }

        id = new ModelsDevModelId(provider, model);
        return true;
    }

    public static bool HasDateSuffix(string modelId)
        => !string.IsNullOrWhiteSpace(modelId)
            && DateSuffixRegex().IsMatch(modelId);

    public static string StripDateSuffix(string modelId)
        => HasDateSuffix(modelId)
            ? DateSuffixRegex().Replace(modelId, string.Empty)
            : modelId;

    [GeneratedRegex(@"-\d{4}-?\d{2}-?\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DateSuffixRegex();
}
