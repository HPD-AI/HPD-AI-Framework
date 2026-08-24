import { describe, expect, it } from 'vitest';
import { decodeStudioHostContract } from './host-contract.ts';

const zero = '0'.repeat(64);
const valid = () => ({ shellContractChecksum: zero, editionAssetGraphChecksum: zero, runtimeClientChecksum: zero,
  bootstrapRoute: '/control/bootstrap', sessionRoute: '/auth/session', loginRoute: '/auth/login', logoutRoute: '/auth/logout',
  authentication: { kind: 'cookieBff', authorizationRoute: '/auth/authorize', descriptorChecksum: zero },
  modules: [{ moduleId: 'base', moduleVersion: 1, entryModulePath: '/modules/base/1/assets/base-studio.js', assetGraphChecksum: zero }] });

describe('Studio host contract', () => {
  it('deeply owns one authorization-neutral edition contract', () => {
    const input = valid(); const result = decodeStudioHostContract(input); input.modules[0]!.moduleId = 'changed';
    expect(result.modules[0]?.moduleId).toBe('base'); expect(Object.isFrozen(result.modules)).toBe(true);
  });
  it('rejects additional members and traversal', () => {
    expect(() => decodeStudioHostContract({ ...valid(), productTitle: 'legacy' })).toThrow('base.studio.hostContractInvalid');
    const input = valid(); input.modules[0]!.entryModulePath = '/modules/../secret.js';
    expect(() => decodeStudioHostContract(input)).toThrow('base.studio.hostContractInvalid');
  });
  it('rejects duplicate and noncanonical module identities', () => {
    const input = valid(); input.modules.push({ ...input.modules[0]! });
    expect(() => decodeStudioHostContract(input)).toThrow('base.studio.hostContractInvalid');
  });
});
