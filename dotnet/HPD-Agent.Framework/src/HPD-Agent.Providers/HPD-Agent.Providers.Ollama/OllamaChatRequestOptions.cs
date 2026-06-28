using System;
using System.Collections.Generic;
using System.Text.Json;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Ollama;

/// <summary>
/// Serializable Ollama-specific chat request options.
/// </summary>
/// <remarks>
/// Generic runtime settings such as model, temperature, top-p, top-k, max output tokens,
/// seed, stop sequences, penalties, response format, and reasoning belong on
/// <see cref="ChatRunConfig"/> or <see cref="ChatOptions"/>.
/// </remarks>
public sealed class OllamaChatRequestOptions
{
    /// <summary>
    /// How long Ollama should keep the model loaded after the request, such as <c>10m</c>, <c>1h</c>, or <c>-1</c>.
    /// </summary>
    public string? KeepAlive { get; set; }

    /// <summary>
    /// Prompt template override for the request. This overrides the template defined in the model file.
    /// </summary>
    public string? Template { get; set; }

    /// <summary>
    /// Enables Mirostat sampling. Use 0 to disable, 1 for Mirostat, or 2 for Mirostat 2.0.
    /// </summary>
    public int? MiroStat { get; set; }

    /// <summary>
    /// Mirostat learning rate. Higher values make the sampler react more quickly to feedback.
    /// </summary>
    public float? MiroStatEta { get; set; }

    /// <summary>
    /// Mirostat target entropy. Lower values produce more focused output; higher values produce more variety.
    /// </summary>
    public float? MiroStatTau { get; set; }

    /// <summary>
    /// Context window size used by the model.
    /// </summary>
    public int? NumCtx { get; set; }

    /// <summary>
    /// Number of grouped-query-attention groups. Required by some model architectures.
    /// </summary>
    public int? NumGqa { get; set; }

    /// <summary>
    /// Number of layers to offload to GPU. Ollama/model defaults apply when unset.
    /// </summary>
    public int? NumGpu { get; set; }

    /// <summary>
    /// Main GPU index used for small tensors when multiple GPUs are available.
    /// </summary>
    public int? MainGpu { get; set; }

    /// <summary>
    /// Maximum batch size for prompt processing.
    /// </summary>
    public int? NumBatch { get; set; }

    /// <summary>
    /// Number of CPU threads used for generation. Ollama auto-detects when unset.
    /// </summary>
    public int? NumThread { get; set; }

    /// <summary>
    /// Number of tokens to keep from the initial prompt. Ollama supports -1 to keep all.
    /// </summary>
    public int? NumKeep { get; set; }

    /// <summary>
    /// Number of previous tokens considered for repetition penalties. Ollama supports 0 to disable and -1 for the full context.
    /// </summary>
    public int? RepeatLastN { get; set; }

    /// <summary>
    /// Repetition penalty strength. Higher values penalize repetition more strongly.
    /// </summary>
    public float? RepeatPenalty { get; set; }

    /// <summary>
    /// Minimum probability sampling threshold relative to the most likely token.
    /// </summary>
    public float? MinP { get; set; }

    /// <summary>
    /// Locally typical sampling value. Lower values make output more conservative.
    /// </summary>
    public float? TypicalP { get; set; }

    /// <summary>
    /// Tail-free sampling value. A value near 1 disables the setting.
    /// </summary>
    public float? TfsZ { get; set; }

    /// <summary>
    /// Whether newline tokens are penalized during repetition control.
    /// </summary>
    public bool? PenalizeNewline { get; set; }

    /// <summary>
    /// Whether model weights are memory-mapped.
    /// </summary>
    public bool? UseMmap { get; set; }

    /// <summary>
    /// Whether to lock model memory to avoid swapping.
    /// </summary>
    public bool? UseMlock { get; set; }

    /// <summary>
    /// Enables low-VRAM mode.
    /// </summary>
    public bool? LowVram { get; set; }

    /// <summary>
    /// Enables f16 key/value cache.
    /// </summary>
    public bool? F16kv { get; set; }

    /// <summary>
    /// Returns logits for all tokens, not only the last token.
    /// </summary>
    public bool? LogitsAll { get; set; }

    /// <summary>
    /// Loads only the vocabulary, not model weights.
    /// </summary>
    public bool? VocabOnly { get; set; }

    /// <summary>
    /// Enables NUMA support.
    /// </summary>
    public bool? Numa { get; set; }

    /// <summary>
    /// Converts the typed options to the Ollama keys consumed from <see cref="ChatOptions.AdditionalProperties"/>.
    /// </summary>
    public Dictionary<string, object> ToAdditionalProperties()
    {
        var properties = new Dictionary<string, object>();

        Add(properties, OllamaChatRequestOptionKeys.KeepAlive, KeepAlive);
        Add(properties, OllamaChatRequestOptionKeys.Template, Template);
        Add(properties, OllamaChatRequestOptionKeys.MiroStat, MiroStat);
        Add(properties, OllamaChatRequestOptionKeys.MiroStatEta, MiroStatEta);
        Add(properties, OllamaChatRequestOptionKeys.MiroStatTau, MiroStatTau);
        Add(properties, OllamaChatRequestOptionKeys.NumCtx, NumCtx);
        Add(properties, OllamaChatRequestOptionKeys.NumGqa, NumGqa);
        Add(properties, OllamaChatRequestOptionKeys.NumGpu, NumGpu);
        Add(properties, OllamaChatRequestOptionKeys.MainGpu, MainGpu);
        Add(properties, OllamaChatRequestOptionKeys.NumBatch, NumBatch);
        Add(properties, OllamaChatRequestOptionKeys.NumThread, NumThread);
        Add(properties, OllamaChatRequestOptionKeys.NumKeep, NumKeep);
        Add(properties, OllamaChatRequestOptionKeys.RepeatLastN, RepeatLastN);
        Add(properties, OllamaChatRequestOptionKeys.RepeatPenalty, RepeatPenalty);
        Add(properties, OllamaChatRequestOptionKeys.MinP, MinP);
        Add(properties, OllamaChatRequestOptionKeys.TypicalP, TypicalP);
        Add(properties, OllamaChatRequestOptionKeys.TfsZ, TfsZ);
        Add(properties, OllamaChatRequestOptionKeys.PenalizeNewline, PenalizeNewline);
        Add(properties, OllamaChatRequestOptionKeys.UseMmap, UseMmap);
        Add(properties, OllamaChatRequestOptionKeys.UseMlock, UseMlock);
        Add(properties, OllamaChatRequestOptionKeys.LowVram, LowVram);
        Add(properties, OllamaChatRequestOptionKeys.F16kv, F16kv);
        Add(properties, OllamaChatRequestOptionKeys.LogitsAll, LogitsAll);
        Add(properties, OllamaChatRequestOptionKeys.VocabOnly, VocabOnly);
        Add(properties, OllamaChatRequestOptionKeys.Numa, Numa);

        return properties;
    }

    /// <summary>
    /// Applies these options to a serializable HPD chat run configuration.
    /// </summary>
    public void ApplyTo(ChatRunConfig chat)
    {
        ArgumentNullException.ThrowIfNull(chat);

        var properties = ToAdditionalProperties();
        if (properties.Count == 0)
            return;

        chat.AdditionalProperties ??= new Dictionary<string, object>();
        foreach (var property in properties)
        {
            chat.AdditionalProperties[property.Key] = property.Value;
        }
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

    private static void Add<T>(Dictionary<string, object> properties, string key, T? value)
        where T : struct
    {
        if (value.HasValue)
            properties[key] = value.Value;
    }

    private static void Add(Dictionary<string, object> properties, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            properties[key] = value;
    }
}

public static class OllamaChatRequestOptionExtensions
{
    public static ChatRunConfig UseOllamaChatRequestOptions(
        this ChatRunConfig chat,
        OllamaChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(options);

        options.ApplyTo(chat);
        return chat;
    }

    public static ChatOptions UseOllamaChatRequestOptions(
        this ChatOptions chat,
        OllamaChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(options);

        options.ApplyTo(chat);
        return chat;
    }
}

internal static class OllamaChatRequestOptionKeys
{
    public const string KeepAlive = "keep_alive";
    public const string Template = "template";
    public const string MiroStat = "mirostat";
    public const string MiroStatEta = "mirostat_eta";
    public const string MiroStatTau = "mirostat_tau";
    public const string NumCtx = "num_ctx";
    public const string NumGqa = "num_gqa";
    public const string NumGpu = "num_gpu";
    public const string MainGpu = "main_gpu";
    public const string NumBatch = "num_batch";
    public const string NumThread = "num_thread";
    public const string NumKeep = "num_keep";
    public const string RepeatLastN = "repeat_last_n";
    public const string RepeatPenalty = "repeat_penalty";
    public const string MinP = "min_p";
    public const string TypicalP = "typical_p";
    public const string TfsZ = "tfs_z";
    public const string PenalizeNewline = "penalize_newline";
    public const string UseMmap = "use_mmap";
    public const string UseMlock = "use_mlock";
    public const string LowVram = "low_vram";
    public const string F16kv = "f16_kv";
    public const string LogitsAll = "logits_all";
    public const string VocabOnly = "vocab_only";
    public const string Numa = "numa";

    public static bool IsKnown(string key)
        => key is KeepAlive or Template or MiroStat or MiroStatEta or MiroStatTau or
            NumCtx or NumGqa or NumGpu or MainGpu or NumBatch or NumThread or NumKeep or
            RepeatLastN or RepeatPenalty or MinP or TypicalP or TfsZ or PenalizeNewline or
            UseMmap or UseMlock or LowVram or F16kv or LogitsAll or VocabOnly or Numa;

    public static object? Normalize(string key, object? value)
    {
        if (value is JsonElement json)
            return NormalizeJsonElement(key, json);

        return key switch
        {
            KeepAlive or Template => value?.ToString(),
            MiroStat or NumCtx or NumGqa or NumGpu or MainGpu or NumBatch or NumThread or
                NumKeep or RepeatLastN => Convert.ToInt32(value),
            MiroStatEta or MiroStatTau or RepeatPenalty or MinP or TypicalP or TfsZ => Convert.ToSingle(value),
            PenalizeNewline or UseMmap or UseMlock or LowVram or F16kv or LogitsAll or VocabOnly or Numa => Convert.ToBoolean(value),
            _ => value
        };
    }

    private static object? NormalizeJsonElement(string key, JsonElement json)
        => key switch
        {
            KeepAlive or Template => json.ValueKind == JsonValueKind.Null ? null : json.GetString(),
            MiroStat or NumCtx or NumGqa or NumGpu or MainGpu or NumBatch or NumThread or
                NumKeep or RepeatLastN => json.GetInt32(),
            MiroStatEta or MiroStatTau or RepeatPenalty or MinP or TypicalP or TfsZ => json.GetSingle(),
            PenalizeNewline or UseMmap or UseMlock or LowVram or F16kv or LogitsAll or VocabOnly or Numa => json.GetBoolean(),
            _ => json
        };
}
