// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Batch;

/// <summary>
/// Options for DatasetGenerator.GenerateAsync.
/// </summary>
public sealed class DatasetGenerationOptions
{
    /// <summary>Number of cases to generate. Default: 10.</summary>
    public int Count { get; init; } = 10;

    /// <summary>
    /// The judge LLM client used for generation. Required — no default provider.
    /// </summary>
    public required IChatClient ChatClient { get; init; }

    /// <summary>
    /// Additional instructions passed to the generator LLM to guide case creation.
    /// e.g. "Focus on edge cases involving ambiguous queries."
    /// </summary>
    public string? ExtraInstructions { get; init; }

    /// <summary>
    /// If set, the generated dataset is saved to this path as JSON.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Maximum number of retries when generated output fails schema validation.
    /// On each retry the validation error is fed back to the LLM.
    /// Default: 3.
    /// </summary>
    public int MaxRetries { get; init; } = 3;
}

/// <summary>
/// Options for DatasetGenerator.AugmentAsync.
/// </summary>
public sealed class DatasetAugmentOptions
{
    /// <summary>Number of new cases to generate. Default: 5.</summary>
    public int Count { get; init; } = 5;

    /// <summary>
    /// The judge LLM client used for augmentation. Required.
    /// </summary>
    public required IChatClient ChatClient { get; init; }

    /// <summary>
    /// Instructions for how to diversify the new cases relative to existing ones.
    /// e.g. "Avoid inputs similar to those already in the dataset."
    /// </summary>
    public string? DiversityInstructions { get; init; }

    /// <summary>Maximum retries on schema validation failure. Default: 3.</summary>
    public int MaxRetries { get; init; } = 3;
}

/// <summary>
/// Thrown when dataset generation fails after all retries are exhausted.
/// </summary>
public sealed class DatasetGenerationException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// LLM-based dataset generator. Creates <see cref="EvalCase{TInput}"/> instances
/// from type schemas using a generator LLM. Validates generated output against the
/// schema and retries with error feedback on failure — improving over PydanticAI's
/// single-retry approach which does not feed the validation error back.
///
/// Usage:
/// <code>
/// var dataset = await DatasetGenerator.GenerateAsync&lt;MyInput, MyOutput&gt;(
///     new DatasetGenerationOptions
///     {
///         Count = 20,
///         ChatClient = myClient,
///         ExtraInstructions = "Focus on edge cases",
///     });
/// </code>
/// </summary>
public static class DatasetGenerator
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Generate a <see cref="Dataset{TInput}"/> by asking the LLM to produce
    /// <paramref name="options"/>.Count test cases matching the <typeparamref name="TInput"/>
    /// JSON schema. Failed schema validation retries up to <see cref="DatasetGenerationOptions.MaxRetries"/>
    /// times, feeding the error message back to the LLM each time.
    /// </summary>
    public static async Task<Dataset<TInput>> GenerateAsync<TInput, TOutput>(
        DatasetGenerationOptions options,
        CancellationToken ct = default)
        where TInput : notnull
        where TOutput : notnull
    {
        var inputSchema = GetJsonSchema<TInput>();
        var outputSchema = GetJsonSchema<TOutput>();

        var systemPrompt =
            "You are a test-case generator for an AI agent evaluation system. " +
            "Generate test cases as a JSON array. Each case must be a JSON object with " +
            "an 'input' field matching the input schema and an optional 'expected_output' " +
            "field matching the output schema. Return ONLY valid JSON — no markdown, no explanation.";

        var userPrompt = BuildGenerationPrompt(options.Count, inputSchema, outputSchema, options.ExtraInstructions);

        var cases = await GenerateWithRetryAsync<TInput>(
            options.ChatClient, systemPrompt, userPrompt, options.MaxRetries, ct)
            .ConfigureAwait(false);

        var dataset = new Dataset<TInput> { Cases = cases };

        if (options.OutputPath is not null)
            dataset.ToFile(options.OutputPath);

        return dataset;
    }

    /// <summary>
    /// Augment an existing dataset with <paramref name="options"/>.Count new cases
    /// that are semantically diverse from the existing ones. Feeds the existing inputs
    /// to the LLM so it can avoid generating similar cases.
    /// </summary>
    public static async Task<Dataset<TInput>> AugmentAsync<TInput>(
        Dataset<TInput> existing,
        DatasetAugmentOptions options,
        CancellationToken ct = default)
        where TInput : notnull
    {
        var inputSchema = GetJsonSchema<TInput>();

        // Summarise existing inputs so the LLM knows what already exists
        var existingSummary = JsonSerializer.Serialize(
            existing.Cases.Select(c => c.Input),
            _jsonOptions);

        var systemPrompt =
            "You are a test-case augmentation assistant for an AI agent evaluation system. " +
            "Generate new test cases as a JSON array. Each case must be a JSON object with " +
            "an 'input' field. Return ONLY valid JSON — no markdown, no explanation.";

        var diversityNote = options.DiversityInstructions is not null
            ? $"\n\nDiversity requirement: {options.DiversityInstructions}"
            : "\n\nAvoid generating inputs that are semantically similar to the existing cases.";

        var userPrompt =
            $"Generate {options.Count} new test cases that are diverse relative to the existing ones.\n\n" +
            $"Input schema:\n{inputSchema}\n\n" +
            $"Existing inputs (do NOT duplicate these):\n{existingSummary}" +
            diversityNote;

        var newCases = await GenerateWithRetryAsync<TInput>(
            options.ChatClient, systemPrompt, userPrompt, options.MaxRetries, ct)
            .ConfigureAwait(false);

        // Return a new dataset combining original + augmented cases
        return new Dataset<TInput>
        {
            Cases = [.. existing.Cases, .. newCases],
            Evaluators = existing.Evaluators,
            ReportEvaluators = existing.ReportEvaluators,
        };
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static async Task<List<EvalCase<TInput>>> GenerateWithRetryAsync<TInput>(
        IChatClient client,
        string systemPrompt,
        string userPrompt,
        int maxRetries,
        CancellationToken ct)
        where TInput : notnull
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt),
        };

        string? lastError = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0 && lastError is not null)
            {
                // Feed the validation error back to the LLM on retry
                messages.Add(new ChatMessage(ChatRole.Assistant,
                    "[previous attempt that failed validation]"));
                messages.Add(new ChatMessage(ChatRole.User,
                    $"Your previous output failed validation with this error:\n\n{lastError}\n\n" +
                    "Please fix the issue and return only valid JSON."));
            }

            var response = await client.GetResponseAsync(
                messages,
                new ChatOptions { Temperature = 0.7f, ResponseFormat = ChatResponseFormat.Text },
                ct).ConfigureAwait(false);

            var rawJson = StripMarkdownFences(response.Text ?? string.Empty);

            try
            {
                var cases = ParseCases<TInput>(rawJson);
                return cases;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                if (attempt == maxRetries)
                    throw new DatasetGenerationException(
                        $"Dataset generation failed after {maxRetries + 1} attempts. " +
                        $"Last error: {ex.Message}", ex);
            }
        }

        // Unreachable — throw in loop above
        throw new DatasetGenerationException("Dataset generation failed unexpectedly.");
    }

    private static List<EvalCase<TInput>> ParseCases<TInput>(string json)
        where TInput : notnull
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected a JSON array at the root level.");

        var cases = new List<EvalCase<TInput>>();
        int index = 0;

        foreach (var element in root.EnumerateArray())
        {
            if (!element.TryGetProperty("input", out var inputEl))
                throw new JsonException($"Case at index {index} is missing the 'input' field.");

            var inputJson = inputEl.GetRawText();
            TInput input;
            try
            {
                input = JsonSerializer.Deserialize<TInput>(inputJson, _jsonOptions)
                    ?? throw new JsonException($"Case {index}: 'input' deserialized to null.");
            }
            catch (JsonException ex)
            {
                throw new JsonException(
                    $"Case at index {index}: 'input' does not match the expected schema. {ex.Message}", ex);
            }

            string? groundTruth = null;
            if (element.TryGetProperty("expected_output", out var outputEl) &&
                outputEl.ValueKind != JsonValueKind.Null)
            {
                groundTruth = outputEl.ValueKind == JsonValueKind.String
                    ? outputEl.GetString()
                    : outputEl.GetRawText();
            }

            cases.Add(new EvalCase<TInput>
            {
                Name = $"generated-{index + 1}",
                Input = input,
                GroundTruth = groundTruth,
            });
            index++;
        }

        if (cases.Count == 0)
            throw new JsonException("Generator returned an empty array — expected at least one case.");

        return cases;
    }

    private static string BuildGenerationPrompt(
        int count,
        string inputSchema,
        string outputSchema,
        string? extraInstructions)
    {
        var prompt =
            $"Generate {count} test cases for an AI agent.\n\n" +
            $"Input schema (each case's 'input' field must conform to this):\n{inputSchema}\n\n" +
            $"Output schema (each case's optional 'expected_output' must conform to this):\n{outputSchema}";

        if (extraInstructions is not null)
            prompt += $"\n\nAdditional instructions: {extraInstructions}";

        prompt += "\n\nReturn a JSON array where each element has an 'input' field and optionally an 'expected_output' field.";
        return prompt;
    }

    /// <summary>
    /// Strips leading/trailing whitespace and markdown code fences (```json ... ```)
    /// from a model response before attempting JSON parsing.
    /// </summary>
    private static string StripMarkdownFences(string text)
    {
        var trimmed = text.Trim();

        // Strip ```json ... ``` or ``` ... ```
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
                trimmed = trimmed[(firstNewline + 1)..];

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
                trimmed = trimmed[..^3].TrimEnd();
        }

        return trimmed.Trim();
    }

    /// <summary>
    /// Returns the JSON schema for <typeparamref name="T"/> as a formatted string.
    /// Uses <see cref="AIJsonUtilities.CreateJsonSchema"/> when available; falls back
    /// to a minimal schema derived from property names via reflection.
    /// </summary>
    private static string GetJsonSchema<T>()
    {
        try
        {
            var schema = AIJsonUtilities.CreateJsonSchema(typeof(T));
            return schema.ToString();
        }
        catch
        {
            // Fallback: describe T by its property names using reflection
            var props = typeof(T).GetProperties()
                .Select(p => $"\"{p.Name.ToLowerInvariant()}\": {{ \"type\": \"{MapTypeName(p.PropertyType)}\" }}");
            return $"{{ \"type\": \"object\", \"properties\": {{ {string.Join(", ", props)} }} }}";
        }
    }

    private static string MapTypeName(Type t)
    {
        if (t == typeof(string)) return "string";
        if (t == typeof(bool) || t == typeof(bool?)) return "boolean";
        if (t == typeof(int) || t == typeof(long) || t == typeof(int?) || t == typeof(long?)) return "integer";
        if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)) return "number";
        if (t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))) return "array";
        return "object";
    }
}
