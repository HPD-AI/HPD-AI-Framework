# HPD Agent Client Tools TypeScript

TypeScript provider SDK for app-scoped HPD client tools.

```ts
import { createClientToolProvider } from '@hpd-research/hpd-agent-client-tools-typescript';

const provider = createClientToolProvider({
  baseUrl: '/api/hpd',
  appProvider: { name: 'code-server', displayName: 'Code Server' },
  identity: {
    providerName: 'code-server-extension',
    appKind: 'code-server',
    instanceId: 'workspace-1',
  },
});

provider.harness('editor', { description: 'Active editor tools.' })
  .tool('get_selected_text', {
    description: 'Gets selected text from the active editor.',
    parametersSchema: { type: 'object', properties: {} },
    handler: async () => 'selected text',
  });

await provider.connect();
```

## Live provider context

Fresh-context tools compare the invocation context selected by HPD with the
application's live context. Applications with mutable context should expose
their native change source through `subscribeContextChanges`; the SDK
deduplicates and debounces manifest updates and disposes the subscription when
the provider disconnects.

```ts
const provider = createClientToolProvider({
  // identity and appProvider omitted
  context: () => workspace.currentContext(),
  contextSnapshot: () => workspace.currentContext(),
  subscribeContextChanges: listener =>
    workspace.subscribeContextChanges(listener),
  contextUpdateDebounceMs: 50,
});
```

Calling `updateManifest()` remains available for explicit changes to tools,
readiness, metadata, or context.
