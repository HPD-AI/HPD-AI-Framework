// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using HPD.Agent.Evaluations.Batch;
using HPD.Serialization;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.RedTeam;

/// <summary>AOT-friendly JSON/YAML loader for red-team run options.</summary>
public static class RedTeamRunConfig
{
    /// <summary>Load red-team options from a JSON or YAML file based on extension.</summary>
    public static RedTeamRunOptions FromFile(string path)
    {
        var text = File.ReadAllText(path);
        var extension = Path.GetExtension(path);
        return extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".yml", StringComparison.OrdinalIgnoreCase)
            ? FromYaml(text)
            : FromJson(text);
    }

    /// <summary>Load red-team options from JSON text.</summary>
    public static RedTeamRunOptions FromJson(string json)
        => FromNode(JsonNode.Parse(json));

    /// <summary>Load red-team options from YAML text.</summary>
    public static RedTeamRunOptions FromYaml(string yaml)
        => FromNode(HpdConfigSerializer.ParseYamlToJsonNode(yaml));

    private static RedTeamRunOptions FromNode(JsonNode? node)
    {
        if (node is not JsonObject root)
            return new RedTeamRunOptions();

        return new RedTeamRunOptions
        {
            CasesPerPlugin = GetInt(root, "cases_per_plugin") ??
                             GetInt(root, "casesPerPlugin") ??
                             GetInt(root, "num_tests") ??
                             GetInt(root, "numTests") ??
                             5,
            DatasetId = GetString(root, "dataset_id") ?? GetString(root, "datasetId") ?? "red-team",
            DatasetVersion = GetString(root, "dataset_version") ?? GetString(root, "datasetVersion"),
            ExperimentName = GetString(root, "experiment_name") ?? GetString(root, "experimentName"),
            Plugins = CreatePlugins(GetArray(root, "plugins")),
            Strategies = CreateStrategies(GetArray(root, "strategies")),
            GlobalEvaluators = CreateEvaluators(GetArray(root, "evaluators")),
            Metadata = CreateMetadata(GetObject(root, "metadata")),
        };
    }

    private static IReadOnlyList<IRedTeamPlugin> CreatePlugins(JsonArray? plugins)
        => plugins?.Select(CreatePlugin).ToList() ?? [];

    private static IRedTeamPlugin CreatePlugin(JsonNode? node)
    {
        var (id, _) = ReadDefinition(node, "plugin");
        return NormalizeName(id) switch
        {
            "promptinjection" => new PromptInjectionPlugin(),
            "indirectpromptinjection" => new IndirectPromptInjectionPlugin(),
            "systempromptextraction" or "promptextraction" => new SystemPromptExtractionPlugin(),
            "tooldiscovery" => new ToolDiscoveryPlugin(),
            "toolabuse" => new ToolAbusePlugin(),
            "dataexfiltration" or "dataexfil" => new DataExfiltrationPlugin(),
            "secretleak" => new SecretLeakPlugin(),
            "piileak" or "pii" or "piidirect" => new PiiLeakPlugin(),
            "unauthorizedaction" => new UnauthorizedActionPlugin(),
            "jailbreak" => new JailbreakPlugin(),
            "shellinjection" => new ShellInjectionPlugin(),
            "sqlinjection" => new SqlInjectionPlugin(),
            "rbacviolation" or "rbac" => new RbacViolationPlugin(),
            "objectaccessviolation" or "bola" => new ObjectAccessViolationPlugin(),
            "policybypass" or "policy" => new PolicyBypassPlugin(),
            "excessiveagency" => new ExcessiveAgencyPlugin(),
            "crosssessionleak" => new CrossSessionLeakPlugin(),
            "ssrf" => new SsrfPlugin(),
            "overreliance" => new OverreliancePlugin(),
            "unverifiableclaims" => new UnverifiableClaimsPlugin(),
            "offtopichijacking" or "offtopic" or "hijacking" => new OffTopicHijackingPlugin(),
            "asciismuggling" => new AsciiSmugglingPlugin(),
            "specialtokeninjection" => new SpecialTokenInjectionPlugin(),
            "debugaccess" => new DebugAccessPlugin(),
            "modelidentification" => new ModelIdentificationPlugin(),
            "reasoningdos" => new ReasoningDosPlugin(),
            "divergentrepetition" => new DivergentRepetitionPlugin(),
            "imitation" => new ImitationPlugin(),
            "competitormention" or "competitors" => new CompetitorMentionPlugin(),
            "goalmisalignment" => new GoalMisalignmentPlugin(),
            "contracts" => new ContractsPlugin(),
            "bfla" => new BflaPlugin(),
            "mcptoolabuse" or "mcp" => new McpToolAbusePlugin(),
            "memorypoisoning" or "agenticmemorypoisoning" => new MemoryPoisoningPlugin(),
            "contextcomplianceattack" or "cca" => new ContextComplianceAttackPlugin(),
            "maliciouscode" => new MaliciousCodePlugin(),
            "harmfulcontent" or "harmful" => new HarmfulContentPlugin(),
            "bias" => new BiasPlugin(),
            _ => throw new InvalidOperationException($"Unknown red-team plugin '{id}'."),
        };
    }

    private static IReadOnlyList<IRedTeamStrategy> CreateStrategies(JsonArray? strategies)
        => strategies?.Select(CreateStrategy).ToList() ?? [];

    private static IRedTeamStrategy CreateStrategy(JsonNode? node)
    {
        var (id, config) = ReadDefinition(node, "strategy");
        return NormalizeName(id) switch
        {
            "basic" => new BasicStrategy(),
            "base64" => new Base64Strategy(),
            "hex" => new HexStrategy(),
            "rot13" => new Rot13Strategy(),
            "leetspeak" => new LeetspeakStrategy(),
            "camelcase" => new CamelCaseStrategy(),
            "morse" => new MorseStrategy(),
            "piglatin" => new PigLatinStrategy(),
            "emoji" => new EmojiStrategy(),
            "homoglyph" => new HomoglyphStrategy(),
            "unicodesmuggling" => new UnicodeSmugglingStrategy(),
            "fakesystemmessage" => new FakeSystemMessageStrategy(),
            "roleplayjailbreak" => new RoleplayJailbreakStrategy(),
            "mathprompt" => new MathPromptStrategy(),
            "citation" => new CitationStrategy(),
            "mischievoususer" => new MischievousUserStrategy(),
            "multiturnescalation" or "multiturn" => new MultiTurnEscalationStrategy(),
            "crescendo" => new CrescendoStrategy(),
            "markdownauthority" => new MarkdownAuthorityStrategy(),
            "authoritativemarkupinjection" => new AuthoritativeMarkupInjectionStrategy(),
            "indirectcontent" or "indirectwebpwn" => new IndirectContentStrategy(),
            "bestofn" => new BestOfNStrategy(GetInt(config, "variant_count") ?? GetInt(config, "n") ?? GetInt(config, "count") ?? 3),
            "retrymutation" or "retry" => new RetryMutationStrategy(GetInt(config, "retry_count") ?? GetInt(config, "count") ?? 2),
            "jailbreaktemplates" or "promptinjection" or "jailbreak" => new JailbreakTemplateStrategy(),
            "compositejailbreak" or "jailbreakcomposite" => new CompositeJailbreakStrategy(),
            "treejailbreak" or "jailbreaktree" => new TreeJailbreakStrategy(),
            "likertjailbreak" or "jailbreaklikert" => new LikertJailbreakStrategy(),
            "layered" or "layer" => new LayeredStrategy(CreateStrategies(GetArray(config, "strategies"))),
            _ => throw new InvalidOperationException($"Unknown red-team strategy '{id}'."),
        };
    }

    private static IReadOnlyList<IEvaluator> CreateEvaluators(JsonArray? evaluators)
        => evaluators is null
            ? []
            : DatasetEvaluatorFactory.CreateMany(evaluators.Select(ToJsonElement));

    private static (string id, JsonObject config) ReadDefinition(JsonNode? node, string kind)
    {
        if (node is JsonValue value)
        {
            var text = GetString(value);
            if (!string.IsNullOrWhiteSpace(text))
                return (text, new JsonObject());
        }

        if (node is not JsonObject obj)
            throw new InvalidOperationException($"Red-team {kind} definitions must be strings or objects.");

        var id = GetString(obj, "id") ?? GetString(obj, "type");
        if (!string.IsNullOrWhiteSpace(id))
            return (id, obj);

        if (obj.Count == 1)
        {
            var property = obj.Single();
            return (property.Key, property.Value as JsonObject ?? new JsonObject());
        }

        throw new InvalidOperationException($"Red-team {kind} object definitions must contain an id.");
    }

    private static IReadOnlyDictionary<string, object>? CreateMetadata(JsonObject? metadata)
    {
        if (metadata is null)
            return null;

        return metadata.ToDictionary(kvp => kvp.Key, kvp => (object)ToJsonElement(kvp.Value));
    }

    private static JsonArray? GetArray(JsonObject? obj, string name)
        => TryGetProperty(obj, name, out var value) ? value as JsonArray : null;

    private static JsonObject? GetObject(JsonObject? obj, string name)
        => TryGetProperty(obj, name, out var value) ? value as JsonObject : null;

    private static int? GetInt(JsonObject? obj, string name)
    {
        if (!TryGetProperty(obj, name, out var value) || value is not JsonValue jsonValue)
            return null;

        if (jsonValue.TryGetValue<int>(out var intValue))
            return intValue;

        return int.TryParse(GetString(jsonValue), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? GetString(JsonObject? obj, string name)
        => TryGetProperty(obj, name, out var value) ? GetString(value) : null;

    private static string? GetString(JsonNode? node)
    {
        if (node is null)
            return null;

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return text;

        return node.ToString();
    }

    private static bool TryGetProperty(JsonObject? obj, string name, out JsonNode? value)
    {
        if (obj is not null)
        {
            var normalizedName = NormalizeName(name);
            foreach (var (key, candidate) in obj)
            {
                if (NormalizeName(key) == normalizedName)
                {
                    value = candidate;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private static JsonElement ToJsonElement(JsonNode? node)
    {
        using var document = JsonDocument.Parse((node ?? JsonValue.Create((string?)null))!.ToJsonString());
        return document.RootElement.Clone();
    }

    private static string NormalizeName(string value)
        => value.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}
