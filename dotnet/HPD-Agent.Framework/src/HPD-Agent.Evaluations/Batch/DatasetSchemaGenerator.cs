// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Batch;

internal static class DatasetSchemaGenerator
{
    internal static string Generate<TInput>(JsonSerializerOptions? serializerOptions = null)
    {
        var inputSchema = CreateInputSchema<TInput>(serializerOptions);
        return Generate(inputSchema);
    }

    internal static string Generate<TInput>(JsonTypeInfo<TInput> inputTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(inputTypeInfo);
        var inputSchema = CreateInputSchema(inputTypeInfo);
        return Generate(inputSchema);
    }

    private static string Generate(JsonNode inputSchema)
    {
        var root = new JsonObject
        {
            ["$schema"] = "http://json-schema.org/draft-07/schema#",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["evaluators"] = EvaluatorsSchema(),
                ["cases"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = CaseSchema(inputSchema),
                },
            },
            ["required"] = new JsonArray("cases"),
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode CreateInputSchema<TInput>(JsonSerializerOptions? serializerOptions)
    {
        try
        {
            var schema = AIJsonUtilities.CreateJsonSchema(
                typeof(TInput),
                description: "Input sent to the agent for this evaluation case.",
                serializerOptions: serializerOptions ?? AIJsonUtilities.DefaultOptions,
                inferenceOptions: new AIJsonSchemaCreateOptions
                {
                    IncludeSchemaKeyword = false,
                });

            return JsonNode.Parse(schema.GetRawText()) ?? FallbackSchema(typeof(TInput));
        }
        catch
        {
            return FallbackSchema(typeof(TInput));
        }
    }

    private static JsonNode CreateInputSchema<TInput>(JsonTypeInfo<TInput> inputTypeInfo)
    {
        try
        {
            var schema = AIJsonUtilities.CreateJsonSchema(
                typeof(TInput),
                description: "Input sent to the agent for this evaluation case.",
                serializerOptions: inputTypeInfo.Options ?? AIJsonUtilities.DefaultOptions,
                inferenceOptions: new AIJsonSchemaCreateOptions
                {
                    IncludeSchemaKeyword = false,
                });

            return JsonNode.Parse(schema.GetRawText()) ?? FallbackSchema(inputTypeInfo);
        }
        catch
        {
            return FallbackSchema(inputTypeInfo);
        }
    }

    private static JsonObject CaseSchema(JsonNode inputSchema) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string" },
            ["input"] = inputSchema,
            ["ground_truth"] = new JsonObject { ["type"] = "string" },
            ["metadata"] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = true,
            },
            ["evaluators"] = EvaluatorsSchema(),
        },
        ["required"] = new JsonArray("input"),
    };

    private static JsonObject EvaluatorsSchema() => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject
        {
            ["oneOf"] = new JsonArray(
                new JsonObject { ["type"] = "string" },
                new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = true,
                    ["minProperties"] = 1,
                    ["maxProperties"] = 1,
                }),
        },
    };

    private static JsonObject FallbackSchema(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string) || type == typeof(char))
            return new JsonObject { ["type"] = "string" };

        if (type == typeof(bool))
            return new JsonObject { ["type"] = "boolean" };

        if (type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) ||
            type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong))
            return new JsonObject { ["type"] = "integer" };

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return new JsonObject { ["type"] = "number" };

        if (type.IsArray)
        {
            var elementType = type.GetElementType() ?? typeof(object);
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = FallbackSchema(elementType),
            };
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = type.GetGenericArguments()[0];
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = FallbackSchema(elementType),
            };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = true,
        };
    }

    private static JsonObject FallbackSchema(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return FallbackSchema(typeInfo.Type);

        var properties = new JsonObject();
        foreach (var property in typeInfo.Properties)
        {
            properties[property.Name] = FallbackSchema(property.PropertyType);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
    }
}
