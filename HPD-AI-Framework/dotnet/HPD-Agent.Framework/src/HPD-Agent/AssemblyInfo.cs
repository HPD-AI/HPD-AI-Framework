using System.Runtime.CompilerServices;

// Make internals visible to the FFI layer
[assembly: InternalsVisibleTo("HPD-Agent.FFI")]

// Make internals visible to the Hosting layer (needed to create Session/Branch without agent)
[assembly: InternalsVisibleTo("HPD-Agent.Hosting")]

// Make internals visible to the MCP layer (needed for AddParentToolMetadata in flat mode)
[assembly: InternalsVisibleTo("HPD-Agent.MCP")]

// Make internals visible to the OpenAPI layer (needed for IOpenApiLoader, OpenApiSourceRegistration, OpenApiLoadResult)
[assembly: InternalsVisibleTo("HPD-Agent.OpenApi")]

// Make internals visible to the OpenAPI test project (needed for AgentContext helpers)
[assembly: InternalsVisibleTo("HPD-Agent.OpenApi.Tests")]

// Make internals visible to the main test project (needed for Skill internals: ResolvedFunctionReferences, DocumentReferences, etc.)
[assembly: InternalsVisibleTo("HPD-Agent.Tests")]

// Make internals visible to the audio test project (needed for Session/Branch construction in audio middleware tests)
[assembly: InternalsVisibleTo("HPD-Agent.Audio.Tests")]
