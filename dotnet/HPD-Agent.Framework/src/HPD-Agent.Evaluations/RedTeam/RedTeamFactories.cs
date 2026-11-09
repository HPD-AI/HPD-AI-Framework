// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent.Evaluations.RedTeam;

/// <summary>Stable metadata for a built-in red-team plugin.</summary>
public sealed record RedTeamPluginDescriptor(
    string Id,
    string DisplayName,
    RedTeamCategory Category);

/// <summary>Stable metadata for a built-in red-team strategy.</summary>
public sealed record RedTeamStrategyDescriptor(
    string Id,
    string DisplayName);

/// <summary>Creates built-in red-team plugins by stable identifier.</summary>
public static class RedTeamPlugins
{
    private static readonly IReadOnlyList<IRedTeamPlugin> _all =
    [
        new PromptInjectionPlugin(),
        new IndirectPromptInjectionPlugin(),
        new SystemPromptExtractionPlugin(),
        new JailbreakPlugin(),
        new ToolDiscoveryPlugin(),
        new ToolAbusePlugin(),
        new UnauthorizedActionPlugin(),
        new DataExfiltrationPlugin(),
        new CrossSessionLeakPlugin(),
        new PiiLeakPlugin(),
        new SecretLeakPlugin(),
        new ShellInjectionPlugin(),
        new SqlInjectionPlugin(),
        new SsrfPlugin(),
        new RbacViolationPlugin(),
        new ObjectAccessViolationPlugin(),
        new ExcessiveAgencyPlugin(),
        new OverreliancePlugin(),
        new UnverifiableClaimsPlugin(),
        new PolicyBypassPlugin(),
        new OffTopicHijackingPlugin(),
        new AsciiSmugglingPlugin(),
        new SpecialTokenInjectionPlugin(),
        new DebugAccessPlugin(),
        new ModelIdentificationPlugin(),
        new ReasoningDosPlugin(),
        new DivergentRepetitionPlugin(),
        new ImitationPlugin(),
        new CompetitorMentionPlugin(),
        new GoalMisalignmentPlugin(),
        new ContractsPlugin(),
        new BflaPlugin(),
        new McpToolAbusePlugin(),
        new MemoryPoisoningPlugin(),
        new ContextComplianceAttackPlugin(),
        new MaliciousCodePlugin(),
        new HarmfulContentPlugin(),
        new BiasPlugin(),
    ];

    /// <summary>Gets metadata for every built-in plugin.</summary>
    public static IReadOnlyList<RedTeamPluginDescriptor> Catalog { get; } =
        _all.Select(plugin => new RedTeamPluginDescriptor(
                plugin.Id,
                plugin.DisplayName,
                plugin.Category))
            .ToArray();

    /// <summary>Creates every built-in plugin using its default configuration.</summary>
    public static IReadOnlyList<IRedTeamPlugin> CreateAll() => _all;

    /// <summary>Creates one built-in plugin by stable identifier.</summary>
    public static IRedTeamPlugin Create(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
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

    internal static string NormalizeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

/// <summary>Creates built-in red-team strategies by stable identifier.</summary>
public static class RedTeamStrategies
{
    private static readonly IReadOnlyList<IRedTeamStrategy> _all =
    [
        new BasicStrategy(),
        new Base64Strategy(),
        new HexStrategy(),
        new Rot13Strategy(),
        new LeetspeakStrategy(),
        new CamelCaseStrategy(),
        new MorseStrategy(),
        new PigLatinStrategy(),
        new EmojiStrategy(),
        new HomoglyphStrategy(),
        new UnicodeSmugglingStrategy(),
        new MarkdownAuthorityStrategy(),
        new AuthoritativeMarkupInjectionStrategy(),
        new FakeSystemMessageStrategy(),
        new RoleplayJailbreakStrategy(),
        new JailbreakTemplateStrategy(),
        new CompositeJailbreakStrategy(),
        new TreeJailbreakStrategy(),
        new LikertJailbreakStrategy(),
        new MathPromptStrategy(),
        new CitationStrategy(),
        new MischievousUserStrategy(),
        new MultiTurnEscalationStrategy(),
        new CrescendoStrategy(),
        new BestOfNStrategy(),
        new RetryMutationStrategy(),
        new LayeredStrategy([new BasicStrategy()]),
        new IndirectContentStrategy(),
    ];

    /// <summary>Gets metadata for every built-in strategy.</summary>
    public static IReadOnlyList<RedTeamStrategyDescriptor> Catalog { get; } =
        _all.Select(strategy => new RedTeamStrategyDescriptor(
                strategy.Id,
                strategy.DisplayName))
            .ToArray();

    /// <summary>Creates every built-in strategy using its default configuration.</summary>
    public static IReadOnlyList<IRedTeamStrategy> CreateAll() => _all;

    /// <summary>Creates one built-in strategy by stable identifier.</summary>
    public static IRedTeamStrategy Create(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return RedTeamPlugins.NormalizeName(id) switch
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
            "bestofn" => new BestOfNStrategy(),
            "retrymutation" or "retry" => new RetryMutationStrategy(),
            "jailbreaktemplates" or "promptinjection" or "jailbreak" => new JailbreakTemplateStrategy(),
            "compositejailbreak" or "jailbreakcomposite" => new CompositeJailbreakStrategy(),
            "treejailbreak" or "jailbreaktree" => new TreeJailbreakStrategy(),
            "likertjailbreak" or "jailbreaklikert" => new LikertJailbreakStrategy(),
            "layered" or "layer" => new LayeredStrategy([new BasicStrategy()]),
            _ => throw new InvalidOperationException($"Unknown red-team strategy '{id}'."),
        };
    }

    public static IRedTeamStrategy BestOfN(int variantCount = 3) =>
        new BestOfNStrategy(variantCount);

    public static IRedTeamStrategy RetryMutation(int retryCount = 2) =>
        new RetryMutationStrategy(retryCount);

    public static IRedTeamStrategy Layered(params IRedTeamStrategy[] strategies) =>
        new LayeredStrategy(strategies);

    public static IRedTeamStrategy Layered(IReadOnlyList<IRedTeamStrategy> strategies) =>
        new LayeredStrategy(strategies);

    public static IRedTeamStrategy Custom(
        string id,
        string displayName,
        Func<RedTeamCase, RedTeamStrategyContext, RedTeamCase> transform) =>
        new CustomDelegateStrategy(id, displayName, transform);
}
