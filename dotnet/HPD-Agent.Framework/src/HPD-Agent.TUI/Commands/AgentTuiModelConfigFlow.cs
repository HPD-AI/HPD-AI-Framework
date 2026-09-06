using HPD.Agent;
using HPD.Agent.TUI.Models;
using HPD.Agent.Providers;
using System.Globalization;

namespace HPD.Agent.TUI.Commands;

public static class AgentTuiModelConfigFlow
{
    public static async ValueTask<AgentTuiSelectedModel?> ConfigureAsync(
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

    private static async ValueTask<AgentTuiSelectedModel?> ConfigureReasoningAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        IReadOnlyList<IAgentTuiModelConfigContributor>? contributors,
        CancellationToken cancellationToken)
    {
        var choice = await context.Dialogs.SelectAsync(
                "Reasoning",
                new[] { ModelConfigChoice.Continue }
                    .Concat(ReasoningChoices(model.Capabilities))
                    .Append(ModelConfigChoice.MoreConfig).ToArray(),
                static choice => choice.Label,
                cancellationToken)
            .ConfigureAwait(false);

        if (!choice.IsSubmitted || choice.Value is null)
        {
            return null;
        }

        var selected = choice.Value;
        if (selected.Kind == "continue")
        {
            return model;
        }

        if (selected.Kind == "more")
        {
            return await ConfigureMoreAsync(context, model, contributors, cancellationToken)
                .ConfigureAwait(false);
        }

        return model with
        {
            Chat = WithReasoningEffort(model.Chat, selected.Effort)
        };
    }

    private static async ValueTask<AgentTuiSelectedModel?> ConfigureNonReasoningAsync(
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

        if (!choice.IsSubmitted || choice.Value is null)
        {
            return null;
        }

        var selected = choice.Value;
        if (selected.Kind == "more")
        {
            return await ConfigureMoreAsync(context, model, contributors, cancellationToken)
                .ConfigureAwait(false);
        }

        return model;
    }

    private static async ValueTask<AgentTuiSelectedModel?> ConfigureMoreAsync(
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

            if (!section.IsSubmitted || section.Value is null)
            {
                return null;
            }

            var selected = section.Value;
            if (selected.Kind == "continue")
            {
                return model;
            }

            var configured = selected.Kind switch
            {
                "reasoning" => await ConfigureReasoningDetailsAsync(context, model, cancellationToken)
                    .ConfigureAwait(false),
                "sampling" => await ConfigureSamplingAsync(context, model, cancellationToken)
                    .ConfigureAwait(false),
                "output" => await ConfigureOutputLengthAsync(context, model, cancellationToken)
                    .ConfigureAwait(false),
                "clear" => model with { Chat = null },
                { } kind when kind.StartsWith("contributor:", StringComparison.Ordinal)
                    => await providerContributors[int.Parse(kind["contributor:".Length..], CultureInfo.InvariantCulture)]
                        .ConfigureAsync(context, model, cancellationToken)
                        .ConfigureAwait(false),
                _ => model
            };

            if (configured is null)
            {
                return null;
            }

            model = configured;
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
                new[] { new ModelConfigChoice("default", "Server default") }
                    .Concat(ReasoningChoices(model.Capabilities)).ToArray(),
                static choice => choice.Label,
                cancellationToken)
            .ConfigureAwait(false);
        if (!effort.IsSubmitted || effort.Value is null)
        {
            return model;
        }

        var chat = CloneChat(model.Chat) ?? new ChatClientConfig();
        chat.Reasoning = new ReasoningOptions
        {
            Effort = effort.Value.Effort
        };
        return model with { Chat = chat };
    }

    private static async ValueTask<AgentTuiSelectedModel> ConfigureSamplingAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        CancellationToken cancellationToken)
    {
        var chat = CloneChat(model.Chat) ?? new ChatClientConfig();
        chat.Temperature = await ReadDoubleAsync(
                context,
                "Temperature",
                chat.Temperature,
                0,
                2,
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
        var chat = CloneChat(model.Chat) ?? new ChatClientConfig();
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

    private static bool HasKnownConfigSurface(AgentTuiSelectedModel model)
        => model.Capabilities is
        {
            SupportsTemperature: true
        }
        || model.Capabilities?.OutputTokenLimit is > 0;

    private static ChatClientConfig WithReasoningEffort(
        ChatClientConfig? source,
        ReasoningEffort? effort)
    {
        var chat = CloneChat(source) ?? new ChatClientConfig();
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
            if (!raw.IsSubmitted || raw.Value is null)
            {
                return current;
            }

            if (string.IsNullOrWhiteSpace(raw.Value))
            {
                return null;
            }

            if (double.TryParse(raw.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
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
            if (!raw.IsSubmitted || raw.Value is null)
            {
                return current;
            }

            if (string.IsNullOrWhiteSpace(raw.Value))
            {
                return null;
            }

            if (int.TryParse(raw.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
                (min is null || value >= min) &&
                (max is null || value <= max))
            {
                return value;
            }
        }
    }

    private static ChatClientConfig? CloneChat(ChatClientConfig? source)
        => source is null
            ? null
            : (ChatClientConfig)ProviderClientConfigSnapshot.Clone(source);

    /// <summary>Offers only implemented efforts permitted by known model metadata.</summary>
    internal static IReadOnlyList<ReasoningEffort> SelectableReasoningEfforts(AgentTuiModelCapabilities? capabilities)
    {
        var levels = new[] { ("none", ReasoningEffort.None), ("low", ReasoningEffort.Low),
            ("medium", ReasoningEffort.Medium), ("high", ReasoningEffort.High), ("xhigh", ReasoningEffort.ExtraHigh) };
        return levels.Where(level => capabilities?.SupportedReasoningEfforts is not { } supported
                || supported.Contains(level.Item1, StringComparer.Ordinal))
            .Select(level => level.Item2).ToArray();
    }

    private static IEnumerable<ModelConfigChoice> ReasoningChoices(AgentTuiModelCapabilities? capabilities) =>
        SelectableReasoningEfforts(capabilities).Select(effort => new ModelConfigChoice(
            effort.ToString(), effort switch
            {
                ReasoningEffort.None => "Off", ReasoningEffort.ExtraHigh => "Extra high", _ => effort.ToString()
            } + (string.Equals(capabilities?.DefaultReasoningEffort,
                effort == ReasoningEffort.ExtraHigh ? "xhigh" : effort.ToString().ToLowerInvariant(),
                StringComparison.Ordinal) ? " (default)" : ""), effort));

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
}

public interface IAgentTuiModelConfigContributor
{
    string Label { get; }

    int Order => 0;

    bool CanConfigure(AgentTuiSelectedModel model);

    ValueTask<AgentTuiSelectedModel?> ConfigureAsync(
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        CancellationToken cancellationToken = default);
}
