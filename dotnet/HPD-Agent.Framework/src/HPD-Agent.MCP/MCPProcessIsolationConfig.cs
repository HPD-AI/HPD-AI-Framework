using System.Text.Json.Serialization;
using HPD.Environment.Contracts;

namespace HPD.Agent.MCP;

/// <summary>
/// Process isolation configuration for an MCP server process.
/// </summary>
public sealed class MCPProcessIsolationConfig
{
    private bool _allowedDomainsExplicitlySet;
    private string[] _allowedDomains = [];

    [JsonPropertyName("mode")]
    public ProcessIsolationMode Mode { get; set; } = ProcessIsolationMode.Isolated;

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("networkMode")]
    public NetworkEgressMode? NetworkMode { get; set; }

    [JsonPropertyName("allowedDomains")]
    public string[] AllowedDomains
    {
        get => _allowedDomains;
        set
        {
            _allowedDomains = value ?? throw new ArgumentNullException(nameof(value));
            _allowedDomainsExplicitlySet = true;
        }
    }

    [JsonPropertyName("deniedDomains")]
    public string[]? DeniedDomains { get; set; }

    [JsonPropertyName("allowWrite")]
    public string[]? AllowWrite { get; set; }

    [JsonPropertyName("denyRead")]
    public string[]? DenyRead { get; set; }

    [JsonPropertyName("allowRead")]
    public string[]? AllowRead { get; set; }

    [JsonPropertyName("denyWrite")]
    public string[]? DenyWrite { get; set; }

    [JsonPropertyName("allowUnixSockets")]
    public string[]? AllowUnixSockets { get; set; }

    [JsonPropertyName("allowAllUnixSockets")]
    public bool AllowAllUnixSockets { get; set; }

    [JsonPropertyName("allowPty")]
    public bool AllowPty { get; set; }

    [JsonPropertyName("allowLocalBinding")]
    public bool AllowLocalBinding { get; set; }

    [JsonPropertyName("allowedMachLookups")]
    public string[]? AllowedMachLookups { get; set; }

    [JsonPropertyName("allowedEnvironmentVariables")]
    public string[]? AllowedEnvironmentVariables { get; set; }

    [JsonPropertyName("stripUnlistedEnvironmentVariables")]
    public bool StripUnlistedEnvironmentVariables { get; set; } = true;

    [JsonPropertyName("tlsTrustMode")]
    public TlsTrustMode TlsTrustMode { get; set; } = TlsTrustMode.None;

    [JsonPropertyName("injectTlsTrustEnvironmentVariables")]
    public bool InjectTlsTrustEnvironmentVariables { get; set; }

    [JsonPropertyName("ignoreViolations")]
    public string[]? IgnoreViolations { get; set; }

    [JsonPropertyName("violationAction")]
    public ProcessViolationAction ViolationAction { get; set; } = ProcessViolationAction.ObserveAndFailInvocation;

    public ProcessIsolationPolicy ToPolicy()
    {
        var profile = CreateProfile(Profile);
        var networkMode = NetworkMode ?? profile.Network.Mode;
        var allowedDomains = _allowedDomainsExplicitlySet
            ? AllowedDomains
            : profile.Network.AllowedDomains.Select(rule => rule.Pattern).ToArray();

        return profile with
        {
            Mode = Mode,
            Filesystem = new FilesystemAccessPolicy
            {
                Rules = BuildPathRules(profile.Filesystem.Rules),
            },
            Network = new NetworkEgressPolicy
            {
                Mode = networkMode,
                AllowedDomains = ToDomainRules(allowedDomains),
                DeniedDomains = ToDomainRules(DeniedDomains),
            },
            UnixSockets = new UnixSocketAccessPolicy
            {
                AllowAll = AllowAllUnixSockets,
                AllowedSockets = (AllowUnixSockets ?? []).Select(path => new UnixSocketAccessRule
                {
                    Path = new UnixSocketPath(path),
                    AuthorityClass = SensitiveAuthorityClass.ProviderDefined,
                }).ToArray(),
            },
            Environment = new EnvironmentAccessPolicy
            {
                AllowedVariables = AllowedEnvironmentVariables ?? [],
                StripUnlistedVariables = StripUnlistedEnvironmentVariables,
            },
            TlsTrust = new TlsTrustPolicy
            {
                Mode = TlsTrustMode,
                InjectTrustEnvironmentVariables = InjectTlsTrustEnvironmentVariables,
            },
            Interactive = new ProcessInteractivePolicy
            {
                AllowPty = AllowPty,
                AllowLocalBinding = AllowLocalBinding,
                AllowedMachLookups = AllowedMachLookups ?? [],
            },
            Violations = new ProcessViolationPolicy
            {
                Action = ViolationAction,
                IgnorePatterns = IgnoreViolations ?? [],
            },
        };
    }

    private IReadOnlyList<PathAccessRule> BuildPathRules(IReadOnlyList<PathAccessRule> profileRules)
    {
        var rules = new List<PathAccessRule>(profileRules);
        AddRules(rules, PathAccessRuleKind.AllowWrite, AllowWrite);
        AddRules(rules, PathAccessRuleKind.DenyRead, DenyRead);
        AddRules(rules, PathAccessRuleKind.AllowRead, AllowRead);
        AddRules(rules, PathAccessRuleKind.DenyWrite, DenyWrite);
        return rules;
    }

    private static ProcessIsolationPolicy CreateProfile(string? profile)
    {
        return profile?.ToLowerInvariant() switch
        {
            "disabled" => ProcessIsolationPolicy.Default with
            {
                Mode = ProcessIsolationMode.Disabled,
                Network = new NetworkEgressPolicy { Mode = NetworkEgressMode.Unrestricted },
            },
            "permissive" => ProcessIsolationPolicy.Default with
            {
                Mode = ProcessIsolationMode.Isolated,
                Filesystem = new FilesystemAccessPolicy
                {
                    Rules =
                    [
                        Rule(PathAccessRuleKind.AllowWrite, "."),
                        Rule(PathAccessRuleKind.AllowWrite, "/tmp"),
                    ],
                },
                Network = new NetworkEgressPolicy { Mode = NetworkEgressMode.Unrestricted },
            },
            "network-only" => ProcessIsolationPolicy.Default with
            {
                Mode = ProcessIsolationMode.Isolated,
                Filesystem = new FilesystemAccessPolicy
                {
                    Rules =
                    [
                        Rule(PathAccessRuleKind.AllowWrite, "."),
                        Rule(PathAccessRuleKind.AllowWrite, "/tmp"),
                        Rule(PathAccessRuleKind.DenyRead, "~/.ssh"),
                        Rule(PathAccessRuleKind.DenyRead, "~/.aws"),
                        Rule(PathAccessRuleKind.DenyRead, "~/.gnupg"),
                    ],
                },
                Network = new NetworkEgressPolicy { Mode = NetworkEgressMode.Unrestricted },
            },
            "filesystem-only" => ProcessIsolationPolicy.Default with
            {
                Mode = ProcessIsolationMode.Isolated,
                Filesystem = new FilesystemAccessPolicy
                {
                    Rules =
                    [
                        Rule(PathAccessRuleKind.AllowWrite, "."),
                        Rule(PathAccessRuleKind.AllowWrite, "/tmp"),
                    ],
                },
                Network = NetworkEgressPolicy.Blocked,
            },
            _ => ProcessIsolationPolicy.Default with
            {
                Mode = ProcessIsolationMode.Isolated,
                Filesystem = new FilesystemAccessPolicy
                {
                    Rules =
                    [
                        Rule(PathAccessRuleKind.AllowWrite, "."),
                        Rule(PathAccessRuleKind.AllowWrite, "/tmp"),
                        Rule(PathAccessRuleKind.DenyRead, "~/.ssh"),
                        Rule(PathAccessRuleKind.DenyRead, "~/.aws"),
                        Rule(PathAccessRuleKind.DenyRead, "~/.gnupg"),
                    ],
                },
                Network = NetworkEgressPolicy.Blocked,
            },
        };
    }

    private static void AddRules(List<PathAccessRule> rules, PathAccessRuleKind kind, string[]? paths)
    {
        if (paths is null)
            return;

        rules.AddRange(paths.Select(path => Rule(kind, path)));
    }

    private static PathAccessRule Rule(PathAccessRuleKind kind, string path) => new()
    {
        Kind = kind,
        Path = new HostPath(path),
    };

    private static IReadOnlyList<DomainRule> ToDomainRules(string[]? domains) =>
        (domains ?? []).Select(pattern => new DomainRule
        {
            Pattern = pattern,
            Kind = DomainRuleKind.ProviderValidate,
        }).ToArray();
}
