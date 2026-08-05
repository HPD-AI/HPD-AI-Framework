using System;
using System.Collections.Generic;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.OnnxRuntime;

/// <summary>
/// Serializable ONNX Runtime GenAI-specific chat request options.
/// </summary>
/// <remarks>
/// Generic runtime settings such as max output tokens, temperature, top-p, top-k,
/// seed, stop sequences, presence penalty, and JSON response format belong on
/// <see cref="ChatClientConfig"/> or <see cref="ChatOptions"/>. These options map to
/// ONNX Runtime GenAI search options that do not have generic chat option fields.
/// </remarks>
public sealed class OnnxRuntimeChatRequestOptions : IChatRequestOptions
{
    /// <summary>
    /// Minimum final sequence length. Maps to the ONNX Runtime GenAI min_length search option.
    /// </summary>
    public int? MinLength { get; set; }

    /// <summary>
    /// Batch size of inputs. Maps to the batch_size search option.
    /// </summary>
    public int? BatchSize { get; set; }

    /// <summary>
    /// Enables randomized sampling. Maps to the do_sample search option.
    /// Generic top-p or top-k options also enable sampling automatically.
    /// </summary>
    public bool? DoSample { get; set; }

    /// <summary>
    /// Repetition penalty. Generic presence penalty also maps to repetition_penalty.
    /// </summary>
    public float? RepetitionPenalty { get; set; }

    /// <summary>
    /// Size of n-grams that should not repeat. Maps to no_repeat_ngram_size.
    /// </summary>
    public int? NoRepeatNgramSize { get; set; }

    /// <summary>
    /// Number of beams for beam search. Maps to num_beams.
    /// </summary>
    public int? NumBeams { get; set; }

    /// <summary>
    /// Number of sequences to return. Must be less than or equal to NumBeams when both are set.
    /// </summary>
    public int? NumReturnSequences { get; set; }

    /// <summary>
    /// Whether beam search stops when enough beams are complete. Maps to early_stopping.
    /// </summary>
    public bool? EarlyStopping { get; set; }

    /// <summary>
    /// Beam-search length penalty. Maps to length_penalty.
    /// </summary>
    public float? LengthPenalty { get; set; }

    /// <summary>
    /// Beam-search diversity penalty. Maps to diversity_penalty.
    /// </summary>
    public float? DiversityPenalty { get; set; }

    /// <summary>
    /// Shares past/present key-value buffers when supported by the execution provider.
    /// Maps to past_present_share_buffer.
    /// </summary>
    public bool? PastPresentShareBuffer { get; set; }

    /// <summary>
    /// Prefill chunk size for long-context processing. Maps to chunk_size.
    /// </summary>
    public int? ChunkSize { get; set; }

    /// <summary>
    /// Converts the typed options to additional properties consumed by the ONNX Runtime GenAI chat client.
    /// </summary>
    public Dictionary<string, object> ToAdditionalProperties()
    {
        Validate();

        var properties = new Dictionary<string, object>();

        Add(properties, OnnxRuntimeChatRequestOptionKeys.MinLength, MinLength);
        Add(properties, OnnxRuntimeChatRequestOptionKeys.BatchSize, BatchSize);
        Add(properties, OnnxRuntimeChatRequestOptionKeys.DoSample, DoSample);
        Add(properties, OnnxRuntimeChatRequestOptionKeys.RepetitionPenalty, RepetitionPenalty);
        Add(properties, OnnxRuntimeChatRequestOptionKeys.NoRepeatNgramSize, NoRepeatNgramSize);
        Add(properties, OnnxRuntimeChatRequestOptionKeys.NumBeams, NumBeams);
        Add(properties, OnnxRuntimeChatRequestOptionKeys.NumReturnSequences, NumReturnSequences);
        Add(properties, OnnxRuntimeChatRequestOptionKeys.EarlyStopping, EarlyStopping);
        Add(properties, OnnxRuntimeChatRequestOptionKeys.LengthPenalty, LengthPenalty);
        Add(properties, OnnxRuntimeChatRequestOptionKeys.DiversityPenalty, DiversityPenalty);
        Add(properties, OnnxRuntimeChatRequestOptionKeys.PastPresentShareBuffer, PastPresentShareBuffer);
        Add(properties, OnnxRuntimeChatRequestOptionKeys.ChunkSize, ChunkSize);

        return properties;
    }

    /// <summary>
    /// Applies these options to a serializable HPD chat run configuration.
    /// </summary>
    public void ApplyTo(ChatClientConfig chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        chat.ProviderOptions = this;
    }

    /// <summary>
    /// Applies these options to Microsoft.Extensions.AI chat options.
    /// </summary>
    public void ApplyTo(ChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var properties = ToAdditionalProperties();
        if (properties.Count == 0)
            return;

        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        foreach (var property in properties)
        {
            options.AdditionalProperties[property.Key] = property.Value;
        }
    }

    private void Validate()
    {
        if (MinLength is < 0)
            throw new ArgumentOutOfRangeException(nameof(MinLength), MinLength, "MinLength must be greater than or equal to 0.");

        if (BatchSize is < 1)
            throw new ArgumentOutOfRangeException(nameof(BatchSize), BatchSize, "BatchSize must be greater than 0.");

        if (RepetitionPenalty is <= 0)
            throw new ArgumentOutOfRangeException(nameof(RepetitionPenalty), RepetitionPenalty, "RepetitionPenalty must be greater than 0.");

        if (NumBeams is < 1)
            throw new ArgumentOutOfRangeException(nameof(NumBeams), NumBeams, "NumBeams must be greater than 0.");

        if (NumReturnSequences is < 1)
            throw new ArgumentOutOfRangeException(nameof(NumReturnSequences), NumReturnSequences, "NumReturnSequences must be greater than 0.");

        if (NumReturnSequences.HasValue &&
            NumBeams.HasValue &&
            NumReturnSequences.Value > NumBeams.Value)
        {
            throw new ArgumentException("NumReturnSequences cannot be greater than NumBeams.", nameof(NumReturnSequences));
        }

        if (ChunkSize is < 1)
            throw new ArgumentOutOfRangeException(nameof(ChunkSize), ChunkSize, "ChunkSize must be greater than 0.");
    }

    private static void Add<T>(Dictionary<string, object> properties, string key, T? value)
        where T : struct
    {
        if (value.HasValue)
            properties[key] = value.Value;
    }
}

/// <summary>
/// Extension helpers for applying ONNX Runtime GenAI-specific chat request options.
/// </summary>
public static class OnnxRuntimeChatRequestOptionExtensions
{
    /// <summary>
    /// Applies ONNX Runtime GenAI-specific runtime options to a serializable HPD chat run configuration.
    /// </summary>
    public static ChatClientConfig UseOnnxRuntimeChatRequestOptions(
        this ChatClientConfig chat,
        OnnxRuntimeChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyTo(chat);
        return chat;
    }

    /// <summary>
    /// Applies ONNX Runtime GenAI-specific runtime options to Microsoft.Extensions.AI chat options.
    /// </summary>
    public static ChatOptions UseOnnxRuntimeChatRequestOptions(
        this ChatOptions chat,
        OnnxRuntimeChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyTo(chat);
        return chat;
    }
}

internal static class OnnxRuntimeChatRequestOptionKeys
{
    public const string MinLength = "min_length";
    public const string BatchSize = "batch_size";
    public const string DoSample = "do_sample";
    public const string RepetitionPenalty = "repetition_penalty";
    public const string NoRepeatNgramSize = "no_repeat_ngram_size";
    public const string NumBeams = "num_beams";
    public const string NumReturnSequences = "num_return_sequences";
    public const string EarlyStopping = "early_stopping";
    public const string LengthPenalty = "length_penalty";
    public const string DiversityPenalty = "diversity_penalty";
    public const string PastPresentShareBuffer = "past_present_share_buffer";
    public const string ChunkSize = "chunk_size";
}
