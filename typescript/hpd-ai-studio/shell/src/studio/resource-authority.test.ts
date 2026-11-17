import { describe, expect, it } from 'vitest';
import { studioOutwardResourceChecksum, validateStudioOutwardResource } from '@hpd-research/hpd-studio-core';

describe('Studio resource authority vectors', () => {
  it.each([
    [{ kind: 'agent', applicationId: 'hpd.studio.demo', agentId: 'support-agent' },
      '862f5a99db8ec2d7a35ef379147a9eedea5141c08e38c7efd7d69bb248a69a94'],
    [{ kind: 'agentSession', applicationId: 'hpd.studio.demo', sessionId: 'session-ops-1042' },
      '48bbfdc0fecbafb1c6faf69a4e8a821944d6bcb1a6df8168069308f383f00ec8'],
    [{ kind: 'toolHarness', applicationId: 'hpd.studio.demo', harnessId: 'support-harness' },
      '5c9c6e0d8246b3021fb0e123490e515ca791d72df969186c15123c9c3263baf0']
  ] as const)('matches the server-issued %s authority', (resource, checksum) => {
    expect(studioOutwardResourceChecksum(resource as never)).toBe(checksum);
    expect(validateStudioOutwardResource({ ...resource, authorityChecksum: checksum } as never).kind).toBe(resource.kind);
  });
});
