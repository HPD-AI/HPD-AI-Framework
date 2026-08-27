using System.Runtime.CompilerServices;

// Make internals visible to the FFI layer
[assembly: InternalsVisibleTo("HPD-Agent.FFI")]

// Make internals visible to the Hosting layer (needed to create Session/Thread without agent)
[assembly: InternalsVisibleTo("HPD-Agent.Hosting")]
[assembly: InternalsVisibleTo("HPD-Agent.TUI")]

// Make internals visible to the MCP layer (needed for AddParentToolMetadata in flat mode)
[assembly: InternalsVisibleTo("HPD-Agent.MCP")]
[assembly: InternalsVisibleTo("HPD-Agent.MCP.Tasks")]

// Make internals visible to the OpenAPI layer (needed for IOpenApiLoader, OpenApiSourceRegistration, OpenApiLoadResult)
[assembly: InternalsVisibleTo("HPD-Agent.OpenApi")]

// Make internals visible to the Audio layer (needed for realtime function execution core integration)
[assembly: InternalsVisibleTo("HPD-Agent.Audio")]
[assembly: InternalsVisibleTo("HPD-Agent.Evaluations")]
[assembly: InternalsVisibleTo("HPD-Agent.MultiAgent")]

// Make internals visible to the OpenAPI test project (needed for AgentContext helpers)
[assembly: InternalsVisibleTo("HPD-Agent.OpenApi.Tests")]

// Make internals visible to the main test project (needed for Skill internals, session construction, and state assertions)
[assembly: InternalsVisibleTo("HPD-Agent.Tests")]

// Make internals visible to the audio test project (needed for Session/Thread construction in audio middleware tests)
[assembly: InternalsVisibleTo("HPD-Agent.Audio.Tests")]

// Make internals visible to the audio V2 test project (needed for AgentContext construction in attachment integration tests)
[assembly: InternalsVisibleTo("HPD.Agent.Audio.V2.Tests")]
