import type {
  BaseSemanticActivationDefinitionShape,
  BaseSemanticActivationInspectionPage,
  BaseSemanticActivationInspectionRequest
} from '@hpd/base-client';

export interface BaseSemanticInspectionClient {
  inspectSemanticActivations(request: BaseSemanticActivationInspectionRequest, signal?: AbortSignal): Promise<import('@hpd/base-client').BaseResult<BaseSemanticActivationInspectionPage>>;
}

export interface BaseSemanticDefinitionContext {
  readonly storeId: string;
  readonly generatedName: string;
  readonly definitionId: string;
  readonly definitionVersion: number;
  readonly definitionChecksum: string;
}

export interface BaseSemanticDefinitionOption extends BaseSemanticActivationDefinitionShape {
  readonly generatedName: string;
}

export interface BaseSemanticStudioSnapshot {
  readonly phase: 'idle' | 'loading' | 'ready' | 'unavailable' | 'failed';
  readonly context: BaseSemanticDefinitionContext | null;
  readonly page: BaseSemanticActivationInspectionPage | null;
  readonly stale: boolean;
}

export interface BaseSemanticStudioController {
  readonly definitions: readonly BaseSemanticDefinitionOption[];
  snapshot(): BaseSemanticStudioSnapshot;
  subscribe(listener: (snapshot: BaseSemanticStudioSnapshot) => void): () => void;
  inspect(storeId: string, generatedName: string, signal?: AbortSignal): Promise<void>;
  next(signal?: AbortSignal): Promise<void>;
  clear(): void;
  dispose(): void;
}

export function createBaseSemanticStudioController(client: BaseSemanticInspectionClient, installed: Readonly<Record<string, BaseSemanticActivationDefinitionShape>>): BaseSemanticStudioController {
  const definitions = Object.freeze(Object.entries(installed).map(([generatedName, value]) => validateDefinition(generatedName, value)));
  if (definitions.length === 0) throw new TypeError('base.studio.semanticDefinitionsUnavailable');
  let disposed = false;
  let generation = 0;
  let current: BaseSemanticStudioSnapshot = freeze({ phase: 'idle', context: null, page: null, stale: false });
  const listeners = new Set<(snapshot: BaseSemanticStudioSnapshot) => void>();
  const publish = (next: BaseSemanticStudioSnapshot) => {
    current = freeze(next);
    for (const listener of listeners) listener(current);
  };
  const load = async (context: BaseSemanticDefinitionContext, after: BaseSemanticActivationInspectionRequest['after'], signal?: AbortSignal) => {
    if (disposed) return;
    const observed = ++generation;
    publish({ phase: 'loading', context, page: current.page, stale: current.page !== null });
    const result = await client.inspectSemanticActivations({ storeId: context.storeId,
      definitionId: context.definitionId, definitionVersion: context.definitionVersion,
      definitionChecksum: context.definitionChecksum, state: null, after, take: 256 }, signal);
    if (disposed || observed !== generation) return;
    if (result.ok) publish({ phase: 'ready', context, page: result.value, stale: false });
    else if (result.error.code === 'base.semanticActivation.unauthorized' || result.error.code === 'base.semanticActivation.notInstalled')
      publish({ phase: 'unavailable', context, page: null, stale: false });
    else publish({ phase: 'failed', context, page: current.page, stale: current.page !== null });
  };
  return Object.freeze({
    definitions,
    snapshot: () => current,
    subscribe(listener: (snapshot: BaseSemanticStudioSnapshot) => void) { listeners.add(listener); listener(current); return () => listeners.delete(listener); },
    inspect: (storeId: string, generatedName: string, signal?: AbortSignal) => {
      const definition = definitions.find(value => value.generatedName === generatedName);
      if (definition === undefined || !bounded(storeId, new TextEncoder())) throw new TypeError('base.studio.contextInvalid');
      return load(Object.freeze({ storeId, generatedName, definitionId: definition.id,
        definitionVersion: definition.version, definitionChecksum: definition.checksum }), null, signal);
    },
    next: async (signal?: AbortSignal) => {
      if (current.context === null || current.page?.next === null || current.page === null) return;
      await load(current.context, current.page.next, signal);
    },
    clear() { generation++; publish({ phase: 'idle', context: null, page: null, stale: false }); },
    dispose() { disposed = true; generation++; listeners.clear(); }
  });
}

function validateDefinition(generatedName: string, value: BaseSemanticActivationDefinitionShape): BaseSemanticDefinitionOption {
  const encoder = new TextEncoder();
  if (!bounded(generatedName, encoder) || !value || !bounded(value.id, encoder)
    || !Number.isInteger(value.version) || value.version < 1 || value.version > 2_147_483_647
    || !/^(?:[A-Za-z0-9+/]{43}=)$/u.test(value.checksum)
    || typeof value.compactable !== 'boolean' || typeof value.removable !== 'boolean')
    throw new TypeError('base.studio.semanticDefinitionInvalid');
  return Object.freeze({ generatedName, ...value });
}

function bounded(value: string, encoder: TextEncoder): boolean {
  return value.length !== 0 && encoder.encode(value).length <= 256 && !/[\u0000-\u001f\u007f]/u.test(value);
}

function freeze(value: BaseSemanticStudioSnapshot): BaseSemanticStudioSnapshot { return Object.freeze({ ...value }); }
