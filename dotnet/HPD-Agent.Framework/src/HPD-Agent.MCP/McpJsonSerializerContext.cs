using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Authentication;

namespace HPD.Agent.MCP;

/// <summary>Provides reflection-free metadata for final HPD-owned MCP contracts.</summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(McpManifest))]
[JsonSerializable(typeof(McpServerConfig))]
[JsonSerializable(typeof(List<McpServerConfig>))]
[JsonSerializable(typeof(McpProtocolOptions))]
[JsonSerializable(typeof(McpInvocationOptions))]
[JsonSerializable(typeof(McpCatalogOptions))]
[JsonSerializable(typeof(McpSubscriptionOptions))]
[JsonSerializable(typeof(McpOAuthOptions))]
[JsonSerializable(typeof(McpProcessIsolationOptions))]
[JsonSerializable(typeof(McpAuthorizationRecord))]
[JsonSerializable(typeof(TokenContainer))]
[JsonSerializable(typeof(McpInputResolutionContext))]
[JsonSerializable(typeof(McpInputResolution))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string?>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(McpResourceListResult))]
[JsonSerializable(typeof(McpResourceTemplateListResult))]
[JsonSerializable(typeof(McpResourceReadResult))]
[JsonSerializable(typeof(McpResourceSummary))]
[JsonSerializable(typeof(McpResourceTemplateSummary))]
[JsonSerializable(typeof(McpResourceContentSummary))]
[JsonSerializable(typeof(McpPromptListResult))]
[JsonSerializable(typeof(McpPromptGetResult))]
[JsonSerializable(typeof(McpPromptSummary))]
[JsonSerializable(typeof(McpPromptArgumentSummary))]
[JsonSerializable(typeof(McpPromptMessageSummary))]
[JsonSerializable(typeof(McpPromptContentSummary))]
[JsonSerializable(typeof(SubscriptionsListenRequestParams))]
[JsonSerializable(typeof(EmptyResult))]
public partial class McpJsonSerializerContext : JsonSerializerContext;
