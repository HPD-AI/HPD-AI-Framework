namespace HPD.Agent.AspNetCore;

/// <summary>Selects administration routes mapped by the administration module.</summary>
public sealed class HPDAgentAdministrationEndpointOptions
{
    public bool MapAgentDefinitions { get; set; } = true;
    public bool MapRuntimeExtensionManagement { get; set; } = true;
}
