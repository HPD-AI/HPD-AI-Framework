import assert from 'node:assert/strict';
import test from 'node:test';
import type { Component } from 'svelte';
import {
  defineStudioModuleDescriptor,
  computeStudioFrontendAbiChecksum,
  studioClientId,
  studioPageId,
  studioSha256,
  validateStudioModuleActivation,
  type StudioModuleActivationContext,
  type StudioPageProps
} from '../src/module-abi.ts';

const checksum = studioSha256('a'.repeat(64));
const component = (() => {}) as unknown as Component<StudioPageProps>;
const componentBinding = { componentExportId: 'base.overview.component', componentAbiChecksum: checksum, component };
const slot = (id = 'base.control-plane') => ({ clientId: studioClientId(id), version: 1, staticRuntimeAbiChecksum: checksum,
  protocol: 'baseL41DynamicMap' as const, generatedContractChecksum: checksum, operationInventoryChecksum: checksum,
  endpointSurfaceId: 'base.runtime', transportClass: 'sameOriginShellAuthenticated' as const,
  owningPageIds: [studioPageId('base.overview')], limitsChecksum: checksum });

function descriptor() {
  const clients = [slot()];
  const pages = { [studioPageId('base.overview')]: componentBinding };
  return defineStudioModuleDescriptor({
    moduleId: 'base',
    moduleVersion: 1,
    frontendAbiChecksum: computeStudioFrontendAbiChecksum('base', 1, clients, pages),
    clientSlots: clients,
    pageComponents: pages
  });
}

test('static descriptor is canonical and deeply owned', () => {
  const source = [slot()];
  const value = defineStudioModuleDescriptor({
    moduleId: 'base', moduleVersion: 1, frontendAbiChecksum: computeStudioFrontendAbiChecksum('base', 1, source,
      { [studioPageId('base.overview')]: componentBinding }), clientSlots: source,
    pageComponents: { [studioPageId('base.overview')]: componentBinding }
  });
  source[0] = { ...source[0]!, version: 2 };
  assert.equal(value.clientSlots[0]!.version, 1);
  assert.ok(Object.isFrozen(value));
  assert.ok(Object.isFrozen(value.clientSlots));
  assert.ok(Object.isFrozen(value.pageComponents));
});

test('activation requires exact static pages and client slots', () => {
  const value = descriptor();
  const frontendAbiChecksum = value.frontendAbiChecksum;
  const context: StudioModuleActivationContext = {
    moduleId: 'base', moduleVersion: 1, frontendAbiChecksum,
    disclosedPageIds: [studioPageId('base.overview')],
    clients: new Map([[studioClientId('base.control-plane'), Object.freeze({
      ...slot(), client: Object.freeze({})
    })]]),
    navigation: { async navigate() {} },
    lifecycle: { signal: new AbortController().signal, defer() {} }
  };
  assert.doesNotThrow(() => validateStudioModuleActivation(value, context));
  assert.throws(() => validateStudioModuleActivation(value, {
    ...context, disclosedPageIds: [studioPageId('base.unknown')]
  }));
  assert.throws(() => validateStudioModuleActivation(value, { ...context, clients: new Map() }));
});

test('activation accepts client ownership filtered to disclosed pages', () => {
  const overview = studioPageId('base.overview');
  const security = studioPageId('base.security');
  const clients = [{ ...slot(), owningPageIds: [overview, security] }];
  const pages = {
    [overview]: componentBinding,
    [security]: { ...componentBinding, componentExportId: 'base.security.component' }
  };
  const value = defineStudioModuleDescriptor({ moduleId: 'base', moduleVersion: 1,
    frontendAbiChecksum: computeStudioFrontendAbiChecksum('base', 1, clients, pages), clientSlots: clients, pageComponents: pages });
  const context: StudioModuleActivationContext = { moduleId: 'base', moduleVersion: 1, frontendAbiChecksum: value.frontendAbiChecksum,
    disclosedPageIds: [overview], clients: new Map([[studioClientId('base.control-plane'), Object.freeze({
      ...clients[0]!, owningPageIds: [overview], client: Object.freeze({})
    })]]), navigation: { async navigate() {} }, lifecycle: { signal: new AbortController().signal, defer() {} } };
  assert.doesNotThrow(() => validateStudioModuleActivation(value, context));
  assert.throws(() => validateStudioModuleActivation(value, { ...context, clients: new Map([[studioClientId('base.control-plane'),
    Object.freeze({ ...clients[0]!, client: Object.freeze({}) })]]) }));
});

test('checksums and identities reject noncanonical values', () => {
  assert.throws(() => studioSha256('A'.repeat(64)));
  assert.throws(() => studioPageId('Base Overview'));
  assert.throws(() => defineStudioModuleDescriptor({
    moduleId: 'base', moduleVersion: 1, frontendAbiChecksum: checksum,
    clientSlots: [
      slot('z.client'), slot('a.client')
    ],
    pageComponents: { [studioPageId('base.overview')]: componentBinding }
  }));
  assert.throws(() => defineStudioModuleDescriptor({ ...descriptor(), frontendAbiChecksum: checksum }));
  assert.throws(() => defineStudioModuleDescriptor({ ...descriptor(), unexpected: true } as never));
});

test('fixed Studio camelCase identities remain distinct from strict L41 type identities', () => {
  assert.equal(studioPageId('base.fileBucket.detail'), 'base.fileBucket.detail');
  assert.equal(studioPageId('base.lifecycleConsumer.detail'), 'base.lifecycleConsumer.detail');
});
