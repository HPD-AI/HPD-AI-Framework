# Archived TypeScript SDK

## `hpd-agent-client-manual`

This is the former hand-written `@hpd-research/hpd-agent-client`.

It was archived after the generated TypeScript target became the primary
package at:

`typescript/hpd-agent-client`

The manual implementation remains important for three reasons:

- it is the behavioral reference for chat, SSE, tools, errors, and transcript
  projection;
- its 131 tests define compatibility work that generated TypeScript must
  recover;
- it documents the ergonomics that generated facades should preserve.

Do not add new endpoint or schema definitions here. Restore behavior by moving
it into the generated package's hand-written runtime layer or by generating the
corresponding IR node.
