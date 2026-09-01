using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Serializes function-call arguments through their declared dictionary contract.</summary>
internal static class FunctionCallArgumentSerializer
{
    /// <summary>Serializes one function call's argument object.</summary>
    /// <param name="functionCall">The function call whose arguments should be serialized.</param>
    /// <returns>The attached canonical JSON when available; otherwise, the serialized argument map.</returns>
    internal static string Serialize(FunctionCallContent functionCall)
    {
        ArgumentNullException.ThrowIfNull(functionCall);
        if (functionCall.Arguments is AIFunctionArguments arguments)
        {
            var json = arguments.GetJson();
            if (json.ValueKind != JsonValueKind.Undefined)
                return json.GetRawText();
        }

        return functionCall.Arguments is { Count: > 0 } values
            ? JsonSerializer.Serialize(values, HPDJsonContext.Default.IDictionaryStringObject)
            : "{}";
    }
}
