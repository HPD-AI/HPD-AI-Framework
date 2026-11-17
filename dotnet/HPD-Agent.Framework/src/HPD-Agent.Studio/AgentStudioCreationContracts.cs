using System.Text;
using HPD.AI.Platform.Studio;

namespace HPD.Agent.Studio;

/// <summary>Owns the closed contract for reviewed Agent definition creation.</summary>
internal static class AgentStudioCreationContracts
{
    internal const string CreateAgent = "agent.create";
    internal const string ConfirmationId = "confirm.agent.create";
    internal const string ImpactId = "impact.agent.create";

    internal static readonly StudioNamedTypeContract Input = Node("agent.creation.input",
        Obj(
            P("agentId", "agent.studio.agent-id"),
            P("configJson", "agent.studio.configuration-json")));

    internal static readonly StudioNamedTypeContract Result = Node("agent.creation.result",
        Obj(
            "{\"name\":\"acknowledgements\",\"wireName\":\"acknowledgements\",\"typeId\":\"agent.studio.command-acknowledgements.one\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}",
            "{\"name\":\"agentId\",\"wireName\":\"agentId\",\"typeId\":\"agent.studio.optional-text\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}",
            "{\"name\":\"configChecksum\",\"wireName\":\"configChecksum\",\"typeId\":\"agent.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}",
            "{\"name\":\"created\",\"wireName\":\"created\",\"typeId\":\"agent.studio.boolean\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}",
            "{\"name\":\"expiresAtUtc\",\"wireName\":\"expiresAtUtc\",\"typeId\":\"agent.studio.optional-text\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}",
            "{\"name\":\"mode\",\"wireName\":\"mode\",\"typeId\":\"agent.studio.configuration-command-mode\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}",
            "{\"name\":\"name\",\"wireName\":\"name\",\"typeId\":\"agent.studio.optional-text\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}",
            "{\"name\":\"previewChecksum\",\"wireName\":\"previewChecksum\",\"typeId\":\"agent.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}",
            "{\"name\":\"receiptChecksum\",\"wireName\":\"receiptChecksum\",\"typeId\":\"agent.studio.optional-sha256\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}"));

    private static StudioNamedTypeContract Node(string id, string descriptor) =>
        StudioNamedTypeContract.Create(id, Encoding.UTF8.GetBytes(descriptor));

    private static string P(string name, string typeId) =>
        $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{typeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";

    private static string Obj(params string[] properties) =>
        $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties.Order(StringComparer.Ordinal))}],\"additionalProperties\":false}}";
}
