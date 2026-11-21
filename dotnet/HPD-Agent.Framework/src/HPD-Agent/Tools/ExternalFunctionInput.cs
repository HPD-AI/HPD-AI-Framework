using System.Text;
using System.Text.Json;

namespace HPD.Agent;

/// <summary>Prepares external function arguments without interpreting their source schema.</summary>
internal static class ExternalFunctionInput
{
    internal const int MaximumBytes = 262_144;
    internal const int MaximumDepth = 32;
    internal const int MaximumNodes = 2_048;

    /// <summary>Checks structural limits and returns a detached argument object.</summary>
    internal static AIFunctionBindingResult Prepare(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return Reject("external_arguments_not_object", "External function arguments must be a JSON object.");
        if (Encoding.UTF8.GetByteCount(arguments.GetRawText()) > MaximumBytes)
            return Reject("external_arguments_too_large", $"External function arguments exceed {MaximumBytes} UTF-8 bytes.");

        var nodes = 0;
        var error = Check(arguments, 0, ref nodes);
        return error is null
            ? AIFunctionBindingResult.Success(null, arguments)
            : AIFunctionBindingResult.Failure(error);
    }

    private static ValidationError? Check(JsonElement value, int depth, ref int nodes)
    {
        if (++nodes > MaximumNodes)
            return Error("external_arguments_too_complex", $"External function arguments exceed {MaximumNodes} values.");
        if (depth > MaximumDepth)
            return Error("external_arguments_too_deep", $"External function arguments exceed depth {MaximumDepth}.");

        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    return Error("external_arguments_duplicate_property", "External function arguments contain a duplicate property.");
                if (Check(property.Value, depth + 1, ref nodes) is { } error)
                    return error;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                if (Check(item, depth + 1, ref nodes) is { } error)
                    return error;
        }
        return null;
    }

    private static AIFunctionBindingResult Reject(string code, string message) =>
        AIFunctionBindingResult.Failure(Error(code, message));

    private static ValidationError Error(string code, string message) => new()
    {
        ErrorCode = code,
        ErrorMessage = message
    };
}
