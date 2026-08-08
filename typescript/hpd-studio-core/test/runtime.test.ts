import assert from 'node:assert/strict';
import test from 'node:test';
import type { Component } from 'svelte';
import {
  composeStudio,
  StudioCompositionError,
  type StudioAuthenticationService,
  type StudioModule,
  type StudioModuleContextWriter,
  type StudioModuleRegistration
} from '../src/index.ts';

const component = (() => {}) as unknown as Component;
const authentication: StudioAuthenticationService = Object.freeze({
  snapshot: () => Object.freeze({ isAuthenticated: false }),
  subscribe(listener: (snapshot: { readonly isAuthenticated: boolean }) => void) {
    listener(Object.freeze({ isAuthenticated: false }));
    return () => {};
  }
});

function module(id: string, initialize?: StudioModule['initialize']): StudioModule {
  return {
    id,
    label: id.toUpperCase(),
    title: `${id} Studio`,
    routes: [{ path: `/${id}`, component, title: id, summary: `${id} module` }],
    navItems: [{ path: `/${id}`, label: id }],
    initialize
  };
}

function registration(id: string, requirement: 'required' | 'optional' = 'optional', initialize?: StudioModule['initialize']): StudioModuleRegistration {
  return { module: module(id, initialize), requirement };
}

async function compose(modules: Iterable<StudioModuleRegistration>) {
  return composeStudio({
    configuration: { productTitle: 'Test Studio', mode: 'development' },
    authentication,
    modules
  });
}

test('empty, individual, omitted, and arbitrary subsets compose deterministically', async () => {
  const all = ['agent', 'auth', 'base', 'ml'];
  for (let mask = 0; mask < 1 << all.length; mask++) {
    const selected = all.filter((_, index) => (mask & (1 << index)) !== 0);
    const runtime = await compose(selected.reverse().map((id) => registration(id)));
    assert.deepEqual(runtime.modules.map((item) => item.id), [...selected].sort());
    assert.deepEqual(runtime.routes.map((item) => item.path), [...selected].sort().map((id) => `/${id}`));
    await runtime.dispose();
  }
});

test('initialization and disposal order are independent of registration order', async () => {
  const events: string[] = [];
  const modules = ['zeta', 'alpha', 'middle'].map((id) => registration(id, 'optional', ({ lifecycle }) => {
    events.push(`init:${id}`);
    lifecycle.defer(() => { events.push(`dispose:${id}`); });
  }));
  const runtime = await compose(modules);
  await runtime.dispose();
  await runtime.dispose();
  assert.deepEqual(events, [
    'init:alpha', 'init:middle', 'init:zeta',
    'dispose:zeta', 'dispose:middle', 'dispose:alpha'
  ]);
});

test('duplicate module and normalized route ownership fail before initialization', async () => {
  let initialized = 0;
  const duplicateId = [registration('same'), registration('same', 'optional', () => { initialized++; })];
  await assert.rejects(() => compose(duplicateId), (error: unknown) =>
    error instanceof StudioCompositionError && error.code === 'studio.module.idDuplicate');
  assert.equal(initialized, 0);

  const first = module('first');
  const second = { ...module('second'), routes: [{ path: '//first/', component, title: 'second', summary: 'second' }] };
  await assert.rejects(() => compose([
    { module: first, requirement: 'optional' },
    { module: second, requirement: 'optional' }
  ]), (error: unknown) => error instanceof StudioCompositionError && error.code === 'studio.route.ownershipConflict');
  assert.equal(initialized, 0);

  for (const order of [[first, second], [second, first]]) {
    await assert.rejects(() => compose(order.map((module) => ({ module, requirement: 'optional' }))),
      (error: unknown) => error instanceof StudioCompositionError &&
        error.code === 'studio.route.ownershipConflict' && error.moduleId === '/first');
  }
});

test('exact parent and child routes compose because v1 has no prefix ownership', async () => {
  const parent = module('parent');
  const child = {
    ...module('child'),
    routes: [{ path: '/parent/child', component, title: 'child', summary: 'child' }],
    navItems: [{ path: '/parent/child', label: 'child' }]
  };
  const runtime = await compose([
    { module: child, requirement: 'optional' },
    { module: parent, requirement: 'optional' }
  ]);
  assert.deepEqual(runtime.routes.map((route) => route.path), ['/parent', '/parent/child']);
  await runtime.dispose();
});

test('optional failure is quarantined and cannot leak contexts or resources', async () => {
  let failedContext: StudioModuleContextWriter | undefined;
  let aborted = false;
  const runtime = await compose([
    registration('healthy'),
    registration('broken', 'optional', ({ contexts, lifecycle }) => {
      failedContext = contexts;
      contexts.set('private.value', 'secret');
      const controller = lifecycle.trackAbortController();
      controller.signal.addEventListener('abort', () => aborted = true);
      throw new Error('sensitive failure');
    })
  ]);
  assert.deepEqual(runtime.modules.map((item) => item.id), ['healthy']);
  assert.deepEqual(runtime.quarantinedModules, [{ id: 'broken', code: 'studio.module.initializationFailed' }]);
  assert.equal(aborted, true);
  assert.equal(failedContext?.get('private.value'), undefined);
  assert.throws(() => failedContext?.set('private.value', 'again'));
  await runtime.dispose();
});

test('required failure cleans partial and prior modules then fails closed', async () => {
  const events: string[] = [];
  await assert.rejects(() => compose([
    registration('alpha', 'optional', ({ lifecycle }) => lifecycle.defer(() => { events.push('alpha-disposed'); })),
    registration('bravo', 'required', ({ lifecycle }) => {
      lifecycle.defer(() => { events.push('bravo-disposed'); });
      throw new Error('do not expose');
    }),
    registration('charlie', 'optional', () => { events.push('charlie-initialized'); })
  ]), (error: unknown) => error instanceof StudioCompositionError &&
    error.code === 'studio.module.requiredInitializationFailed' && error.moduleId === 'bravo');
  assert.deepEqual(events, ['bravo-disposed', 'alpha-disposed']);
});

test('malformed activation and duplicate navigation follow module failure policy', async () => {
  const malformed = module('malformed', () => 'invalid' as never);
  const runtime = await compose([{ module: malformed, requirement: 'optional' }]);
  assert.deepEqual(runtime.quarantinedModules, [{ id: 'malformed', code: 'studio.module.initializationFailed' }]);
  await runtime.dispose();

  const duplicateNavigation = {
    ...module('navigation'),
    navItems: [
      { path: '/navigation', label: 'one' },
      { path: '//navigation/', label: 'two' }
    ]
  };
  await assert.rejects(() => compose([{ module: duplicateNavigation, requirement: 'optional' }]),
    (error: unknown) => error instanceof StudioCompositionError && error.code === 'studio.navigation.pathDuplicate');
});

test('module contexts are private, non-enumerable, bounded, and cleared on disposal', async () => {
  let alpha: StudioModuleContextWriter | undefined;
  let beta: StudioModuleContextWriter | undefined;
  const runtime = await compose([
    registration('alpha', 'optional', ({ contexts }) => { alpha = contexts; contexts.set('shared.name', 'alpha'); }),
    registration('beta', 'optional', ({ contexts }) => { beta = contexts; contexts.set('shared.name', 'beta'); })
  ]);
  assert.equal(alpha?.get('shared.name'), 'alpha');
  assert.equal(beta?.get('shared.name'), 'beta');
  assert.equal('keys' in (alpha as object), false);
  await runtime.dispose();
  assert.equal(alpha?.get('shared.name'), undefined);
  assert.equal(beta?.get('shared.name'), undefined);
});

test('tracked resources dispose LIFO, idempotently, and failure-isolated', async () => {
  const events: string[] = [];
  const target = new EventTarget();
  let observed = 0;
  let controller: AbortController | undefined;
  let lifetimeSignal: AbortSignal | undefined;
  let intervalTicks = 0;
  const listener = () => observed++;
  const runtime = await compose([registration('resources', 'optional', ({ lifecycle }) => {
    lifetimeSignal = lifecycle.signal;
    lifecycle.defer(() => { events.push('first'); });
    lifecycle.defer(() => { events.push('throwing'); throw new Error('cleanup'); });
    lifecycle.defer(() => { events.push('last'); });
    lifecycle.listen(target, 'update', listener);
    controller = lifecycle.trackAbortController();
    lifecycle.setInterval(() => intervalTicks++, 100);
    return { dispose: () => { events.push('activation'); } };
  })]);
  target.dispatchEvent(new Event('update'));
  assert.equal(observed, 1);
  await runtime.dispose();
  await runtime.dispose();
  target.dispatchEvent(new Event('update'));
  assert.equal(observed, 1);
  assert.equal(controller?.signal.aborted, true);
  assert.equal(lifetimeSignal?.aborted, true);
  await new Promise((resolve) => setTimeout(resolve, 125));
  assert.equal(intervalTicks, 0);
  assert.deepEqual(events, ['activation', 'last', 'throwing', 'first']);
});

test('capacity failure does not leak timers, listeners, or activation resources', async () => {
  let intervalTicks = 0;
  const timerRuntime = await compose([registration('timer-capacity', 'optional', ({ lifecycle }) => {
    for (let index = 0; index < 128; index++) lifecycle.defer(() => {});
    lifecycle.setInterval(() => intervalTicks++, 100);
  })]);
  assert.deepEqual(timerRuntime.quarantinedModules, [{ id: 'timer-capacity', code: 'studio.module.initializationFailed' }]);
  await new Promise((resolve) => setTimeout(resolve, 125));
  assert.equal(intervalTicks, 0);
  await timerRuntime.dispose();

  let installedListeners = 0;
  const target = {
    addEventListener() { installedListeners++; },
    removeEventListener() { installedListeners--; }
  };
  const listenerRuntime = await compose([registration('listener-capacity', 'optional', ({ lifecycle }) => {
    for (let index = 0; index < 128; index++) lifecycle.defer(() => {});
    lifecycle.listen(target, 'update', () => {});
  })]);
  assert.deepEqual(listenerRuntime.quarantinedModules, [{ id: 'listener-capacity', code: 'studio.module.initializationFailed' }]);
  assert.equal(installedListeners, 0);
  await listenerRuntime.dispose();

  let activationDisposals = 0;
  const activationRuntime = await compose([registration('activation-capacity', 'optional', ({ lifecycle }) => {
    for (let index = 0; index < 128; index++) lifecycle.defer(() => {});
    return { dispose: () => { activationDisposals++; } };
  })]);
  assert.deepEqual(activationRuntime.quarantinedModules, [{ id: 'activation-capacity', code: 'studio.module.initializationFailed' }]);
  assert.equal(activationDisposals, 1);
  await activationRuntime.dispose();
  assert.equal(activationDisposals, 1);
});

test('concurrent disposal callers wait for the same active cleanup', async () => {
  let release!: () => void;
  const cleanupGate = new Promise<void>((resolve) => { release = resolve; });
  let cleanupCompleted = false;
  const runtime = await compose([registration('async-cleanup', 'optional', ({ lifecycle }) => {
    lifecycle.defer(async () => {
      await cleanupGate;
      cleanupCompleted = true;
    });
  })]);

  let firstCompleted = false;
  let secondCompleted = false;
  const first = runtime.dispose().then(() => { firstCompleted = true; });
  const second = runtime.dispose().then(() => { secondCompleted = true; });
  await Promise.resolve();
  assert.equal(firstCompleted, false);
  assert.equal(secondCompleted, false);
  assert.equal(cleanupCompleted, false);
  release();
  await Promise.all([first, second]);
  assert.equal(firstCompleted, true);
  assert.equal(secondCompleted, true);
  assert.equal(cleanupCompleted, true);
});

test('closed identifiers use ordinal ordering independent of locale collation', async () => {
  const runtime = await compose([
    registration('aa'),
    registration('a0'),
    registration('a-1')
  ]);
  assert.deepEqual(runtime.modules.map((item) => item.id), ['a-1', 'a0', 'aa']);
  assert.deepEqual(runtime.routes.map((item) => item.path), ['/a-1', '/a0', '/aa']);
  await runtime.dispose();
});

test('unknown, malformed, omitted, quarantined, and disposed navigation uses safe fallback', async () => {
  const runtime = await compose([
    registration('active'),
    registration('broken', 'optional', () => { throw new Error('failure'); })
  ]);
  assert.equal(runtime.navigate('/active').isFallback, false);
  for (const path of ['/missing', '/broken', '/../active', '/active?query=yes']) {
    const result = runtime.navigate(path);
    assert.equal(result.isFallback, true);
    assert.equal(result.route, null);
  }
  await runtime.dispose();
  assert.equal(runtime.navigate('/active').isFallback, true);
});

test('module iterable is hard bounded before initialization', async () => {
  let initialized = 0;
  function* registrations() {
    for (let index = 0; index < 10_000; index++) {
      yield registration(`m-${index}`, 'optional', () => { initialized++; });
    }
  }
  await assert.rejects(() => compose(registrations()), (error: unknown) =>
    error instanceof StudioCompositionError && error.code === 'studio.modules.capacityExceeded');
  assert.equal(initialized, 0);
});

test('authentication projection is bounded, immutable, and product-neutral', async () => {
  const runtime = await composeStudio({
    configuration: { productTitle: 'Auth Studio', mode: 'read-only' },
    authentication: {
      snapshot: () => ({ isAuthenticated: true, displayName: 'Operator', subjectHint: 'subject' }),
      subscribe: (listener) => { listener({ isAuthenticated: true }); return () => {}; }
    },
    modules: []
  });
  const snapshot = runtime.authentication.snapshot();
  assert.deepEqual(snapshot, { isAuthenticated: true, displayName: 'Operator', subjectHint: 'subject' });
  assert.equal(Object.isFrozen(snapshot), true);
  assert.equal('token' in snapshot, false);
  await runtime.dispose();

  await assert.rejects(() => composeStudio({
    configuration: { productTitle: 'Auth Studio', mode: 'development' },
    authentication: {
      snapshot: () => ({ isAuthenticated: true, displayName: 'x'.repeat(129) }),
      subscribe: () => () => {}
    },
    modules: []
  }), (error: unknown) => error instanceof StudioCompositionError && error.code === 'studio.authentication.displayNameInvalid');
});
