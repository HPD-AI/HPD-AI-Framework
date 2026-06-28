using HPD.Agent;
using HPD.Agent.TUI.Models;
using System.Globalization;

namespace HPD.Agent.TUI.Commands;

public static class AgentTuiModelConfigFlow
{
    public static async ValueTask<AgentTuiSelectedModel> ConfigureAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        IReadOnlyList<IAgentTuiModelConfigContributor>? contributors = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);

        if (model.Capabilities?.SupportsReasoning == true)
        {
            return await ConfigureReasoningAsync(context, model, contributors, cancellationToken)
                .ConfigureAwait(false);
        }

        if (HasKnownConfigSurface(model))
        {
            return await ConfigureNonReasoningAsync(context, model, contributors, cancellationToken)
                .ConfigureAwait(false);
        }

        return model;
    }

    private static async ValueTask<AgentTuiSelectedModel> ConfigureReasoningAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        IReadOnlyList<IAgentTuiModelConfigContributor>? contributors,
        CancellationToken cancellationToken)
    {
        var choice = await context.Dialogs.SelectAsync(
                "Reasoning",
                new[]
                {
                    ModelConfigChoice.Continue,
                    new ModelConfigChoice("off", "Off", ReasoningEffort.None),
                    new ModelConfigChoice("low", "Low", ReasoningEffort.Low),
                    new ModelConfigChoice("medium", "Medium", ReasoningEffort.Medium),
                    new ModelConfigChoice("high", "High", ReasoningEffort.High),
                    new ModelConfigChoice("extra-high", "Extra high", ReasoningEffort.ExtraHigh),
                    ModelConfigChoice.MoreConfig
                },
                static choice => choice.Label,
                cancellationToken)
            .ConfigureAwait(false);

        if (choice is null || choice.Kind == "continue")
        {
            return model;
        }

        if (choice.Kind == "more")
        {
            return await ConfigureMoreAsync(context, model, contributors, cancellationToken)
                .ConfigureAwait(false);
        }

        return model with
        {
            Chat = WithReasoningEffort(model.Chat, choice.Effort)
        };
    }

    private static async ValueTask<AgentTuiSelectedModel> ConfigureNonReasoningAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        IReadOnlyList<IAgentTuiModelConfigContributor>? contributors,
        CancellationToken cancellationToken)
    {
        var choice = await context.Dialogs.SelectAsync(
                "Configure model",
                new[]
                {
                    ModelConfigChoice.Continue,
                    ModelConfigChoice.MoreConfig
                },
                static choice => choice.Label,
                cancellationToken)
            .ConfigureAwait(false);

        if (choice?.Kind == "more")
        {
            return await ConfigureMoreAsync(context, model, contributors, cancellationToken)
                .ConfigureAwait(false);
        }

        return model;
    }

    private static async ValueTask<AgentTuiSelectedModel> ConfigureMoreAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        IReadOnlyList<IAgentTuiModelConfigContributor>? contributors,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var providerContributors = GetProviderContributors(model, contributors);
            var sections = GetConfigSections(model, providerContributors);
            var section = await context.Dialogs.SelectAsync(
                    "Model config",
                    sections,
                    static section => section.Label,
                    cancellationToken)
                .ConfigureAwait(false);

            if (section is null || section.Kind == "continue")
            {
                return model;
            }

            model = section.Kind switch
            {
                "reasoning" => await ConfigureReasoningDetailsAsync(context, model, cancellationToken)
                    .ConfigureAwait(false),
                "sampling" => await ConfigureSamplingAsync(context, model, cancellationToken)
                    .ConfigureAwait(false),
                "output" => await ConfigureOutputLengthAsync(context, model, cancellationToken)
                    .ConfigureAwait(false),
                "stop" => await ConfigureStopSequencesAsync(context, model, cancellationToken)
                    .ConfigureAwait(false),
                "seed" => await ConfigureSeedAsync(context, model, cancellationToken)
                    .ConfigureAwait(false),
                "clear" => model with { Chat = null },
                { } kind when kind.StartsWith("contributor:", StringComparison.Ordinal)
                    => await providerContributors[int.Parse(kind["contributor:".Length..], CultureInfo.InvariantCulture)]
                        .ConfigureAsync(context, model, cancellationToken)
                        .ConfigureAwait(false),
                _ => model
            };
        }
    }

    private static IReadOnlyList<IAgentTuiModelConfigContributor> GetProviderContributors(
        AgentTuiSelectedModel model,
        IReadOnlyList<IAgentTuiModelConfigContributor>? contributors)
        => contributors is null
            ? []
            : contributors
                .Where(contributor => contributor.CanConfigure(model))
                .OrderBy(static contributor => contributor.Order)
                .ThenBy(static contributor => contributor.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static IReadOnlyList<ModelConfigSection> GetConfigSections(
        AgentTuiSelectedModel model,
        IReadOnlyList<IAgentTuiModelConfigContributor> providerContributors)
    {
        var sections = new List<ModelConfigSection>
        {
            ModelConfigSection.Continue
        };

        if (model.Capabilities?.SupportsReasoning == true)
        {
            sections.Add(new ModelConfigSection("reasoning", "Reasoning"));
        }

        if (model.Capabilities?.SupportsTemperature == true)
        {
            sections.Add(new ModelConfigSection("sampling", "Sampling"));
        }

        if (model.Capabilities?.OutputTokenLimit is > 0)
        {
            sections.Add(new ModelConfigSection("output", "Output length"));
        }

        sections.Add(new ModelConfigSection("stop", "Stop sequences"));
        sections.Add(new ModelConfigSection("seed", "Seed"));

        for (var i = 0; i < providerContributors.Count; i++)
        {
            sections.Add(new ModelConfigSection($"contributor:{i}", providerContributors[i].Label));
        }

        if (model.Chat is not null)
        {
            sections.Add(new ModelConfigSection("clear", "Clear model config"));
        }

        return sections;
    }

    private static async ValueTask<AgentTuiSelectedModel> ConfigureReasoningDetailsAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        CancellationToken cancellationToken)
    {
        var effort = await context.Dialogs.SelectAsync(
                "Reasoning effort",
                new[]
                {
                    new ModelConfigChoice("off", "Off", ReasoningEffort.None),
                    new ModelConfigChoice("low", "Low", ReasoningEffort.Low),
                    new ModelConfigChoice("medium", "Medium", ReasoningEffort.Medium),
                    new ModelConfigChoice("high", "High", ReasoningEffort.High),
                    new ModelConfigChoice("extra-high", "Extra high", ReasoningEffort.ExtraHigh)
                },
                static choice => choice.Label,
                cancellationToken)
            .ConfigureAwait(false);
        if (effort is null)
        {
            return model;
        }

        var output = await context.Dialogs.SelectAsync(
                "Reasoning output",
                new[]
                {
                    new ReasoningOutputChoice(null, "Unchanged"),
                    new ReasoningOutputChoice(ReasoningOutput.None, "Hidden"),
                    new ReasoningOutputChoice(ReasoningOutput.Summary, "Summary"),
                    new ReasoningOutputChoice(ReasoningOutput.Full, "Full")
                },
                static choice => choice.Label,
                cancellationToken)
            .ConfigureAwait(false);

        var chat = CloneChat(model.Chat) ?? new ChatRunConfig();
        chat.Reasoning = new ReasoningOptions
        {
            Effort = effort.Effort,
            Output = output?.Output ?? model.Chat?.Reasoning?.Output
        };
        return model with { Chat = chat };
    }

    private static async ValueTask<AgentTuiSelectedModel> ConfigureSamplingAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        CancellationToken cancellationToken)
    {
        var chat = CloneChat(model.Chat) ?? new ChatRunConfig();
        chat.Temperature = await ReadDoubleAsync(
                context,
                "Temperature",
                chat.Temperature,
                0,
                2,
                cancellationToken)
            .ConfigureAwait(false);
        chat.TopP = await ReadDoubleAsync(
                context,
                "Top-p",
                chat.TopP,
                0,
                1,
                cancellationToken)
            .ConfigureAwait(false);
        chat.TopK = await ReadIntAsync(
                context,
                "Top-k",
                chat.TopK,
                min: 1,
                max: null,
                cancellationToken)
            .ConfigureAwait(false);
        return model with { Chat = chat };
    }

    private static async ValueTask<AgentTuiSelectedModel> ConfigureOutputLengthAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        CancellationToken cancellationToken)
    {
        var max = model.Capabilities?.OutputTokenLimit;
        var chat = CloneChat(model.Chat) ?? new ChatRunConfig();
        chat.MaxOutputTokens = await ReadIntAsync(
                context,
                max is > 0 ? $"Max output tokens (max {max.Value})" : "Max output tokens",
                chat.MaxOutputTokens,
                min: 1,
                max,
                cancellationToken)
            .ConfigureAwait(false);
        return model with { Chat = chat };
    }

    private static async ValueTask<AgentTuiSelectedModel> ConfigureStopSequencesAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        CancellationToken cancellationToken)
    {
        var chat = CloneChat(model.Chat) ?? new ChatRunConfig();
        var current = chat.StopSequences is null ? null : string.Join(", ", chat.StopSequences);
        var value = await context.Dialogs.InputAsync(
                "Stop sequences",
                current,
                allowEmpty: true,
                cancellationToken)
            .ConfigureAwait(false);
        if (value is null)
        {
            return model;
        }

        chat.StopSequences = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return model with { Chat = chat };
    }

    private static async ValueTask<AgentTuiSelectedModel> ConfigureSeedAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        CancellationToken cancellationToken)
    {
        var chat = CloneChat(model.Chat) ?? new ChatRunConfig();
        chat.Seed = await ReadLongAsync(
                context,
                "Seed",
                chat.Seed,
                min: 0,
                cancellationToken)
            .ConfigureAwait(false);
        return model with { Chat = chat };
    }

    private static bool HasKnownConfigSurface(AgentTuiSelectedModel model)
        => model.Capabilities is
        {
            SupportsTemperature: true
        }
        || model.Capabilities?.OutputTokenLimit is > 0;

    private static ChatRunConfig WithReasoningEffort(
        ChatRunConfig? source,
        ReasoningEffort? effort)
    {
        var chat = CloneChat(source) ?? new ChatRunConfig();
        chat.Reasoning = new ReasoningOptions
        {
            Effort = effort
        };
        return chat;
    }

    private static async ValueTask<double?> ReadDoubleAsync(
        AgentTuiCommandContext context,
        string title,
        double? current,
        double? min,
        double? max,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var raw = await context.Dialogs.InputAsync(
                    title,
                    current?.ToString(CultureInfo.InvariantCulture),
                    allowEmpty: true,
                    cancellationToken)
                .ConfigureAwait(false);
            if (raw is null)
            {
                return current;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                (min is null || value >= min) &&
                (max is null || value <= max))
            {
                return value;
            }
        }
    }

    private static async ValueTask<int?> ReadIntAsync(
        AgentTuiCommandContext context,
        string title,
        int? current,
        int? min,
        int? max,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var raw = await context.Dialogs.InputAsync(
                    title,
                    current?.ToString(CultureInfo.InvariantCulture),
                    allowEmpty: true,
                    cancellationToken)
                .ConfigureAwait(false);
            if (raw is null)
            {
                return current;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
                (min is null || value >= min) &&
                (max is null || value <= max))
            {
                return value;
            }
        }
    }

    private static async ValueTask<long?> ReadLongAsync(
        AgentTuiCommandContext context,
        string title,
        long? current,
        long? min,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var raw = await context.Dialogs.InputAsync(
                    title,
                    current?.ToString(CultureInfo.InvariantCulture),
                    allowEmpty: true,
                    cancellationToken)
                .ConfigureAwait(false);
            if (raw is null)
            {
                return current;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
                (min is null || value >= min))
            {
                return value;
            }
        }
    }

    private static ChatRunConfig? CloneChat(ChatRunConfig? source)
        => source is null
            ? null
            : new ChatRunConfig
            {
                Temperature = source.Temperature,
                TopP = source.TopP,
                TopK = source.TopK,
                MaxOutputTokens = source.MaxOutputTokens,
                FrequencyPenalty = source.FrequencyPenalty,
                PresencePenalty = source.PresencePenalty,
                Seed = source.Seed,
                ModelId = source.ModelId,
                StopSequences = source.StopSequences?.ToArray(),
                AdditionalProperties = source.AdditionalProperties is null
                    ? null
                    : new Dictionary<string, object>(source.AdditionalProperties),
                Reasoning = source.Reasoning?.Clone(),
                ResponseFormat = source.ResponseFormat
            };

    private sealed record ModelConfigChoice(
        string Kind,
        string Label,
        ReasoningEffort? Effort = null)
    {
        public static ModelConfigChoice Continue { get; } = new("continue", "Continue");

        public static ModelConfigChoice MoreConfig { get; } = new("more", "More config");
    }

    private sealed record ModelConfigSection(string Kind, string Label)
    {
        public static ModelConfigSection Continue { get; } = new("continue", "Continue");
    }

    private sealed record ReasoningOutputChoice(ReasoningOutput? Output, string Label);
}

public interface IAgentTuiModelConfigContributor
{
    string Label { get; }

    int Order => 0;

    bool CanConfigure(AgentTuiSelectedModel model);

    ValueTask<AgentTuiSelectedModel> ConfigureAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        CancellationToken cancellationToken = default);
}
