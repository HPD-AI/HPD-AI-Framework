# HPD Agent SDK Contract

This library owns the language-neutral input boundary for HPD agent SDK
generators.

It currently provides:

- OpenAPI 3.1 loading and contract digesting;
- inline object schema promotion;
- normalized operation, parameter, request-body, input, and event-union records;
- HPD-specific validation for SSE, submitted inputs, interaction responses, and
  WebSocket operations;
- versioned HPD Agent SDK IR v1 with deterministic JSON snapshots;
- explicit type, operation, union, capability, and diagnostic nodes;
- IR graph and reference validation.

This is intentionally an HPD agent contract rather than a generic HPD SDK
contract. The current validation rules depend on agent operation IDs and agent
event/input semantics.

Language generators consume `OpenApiContract` and must not parse the original
OpenAPI document independently.
