import { describe, expect, it } from 'vitest';
import { createMemoryBearerAuthentication } from './authentication';

describe('memory bearer authentication', () => {
  it('retains one bounded token only in the service and clears it on sign-out', async () => {
    const token = jwt({ sub: 'operator-a' });
    const authentication = createMemoryBearerAuthentication(() => token);

    await authentication.beginSignIn?.();

    expect(authentication.getAccessToken()).toBe(token);
    expect(authentication.snapshot()).toEqual({ isAuthenticated: true, subjectHint: 'operator-a' });
    await authentication.beginSignOut?.();
    expect(authentication.getAccessToken()).toBeNull();
    expect(authentication.snapshot()).toEqual({ isAuthenticated: false });
  });

  it.each(['', 'short', 'contains space', 'x'.repeat(16_385)])('rejects unsafe provider input', async (value) => {
    const authentication = createMemoryBearerAuthentication(() => value);
    await authentication.beginSignIn?.();
    expect(authentication.snapshot().isAuthenticated).toBe(false);
    expect(authentication.getAccessToken()).toBeNull();
  });

  it('treats a token without a stable subject conservatively', async () => {
    const token = jwt({ role: 'operator' });
    const authentication = createMemoryBearerAuthentication(() => token);
    await authentication.beginSignIn?.();
    expect(authentication.snapshot()).toEqual({ isAuthenticated: true });
  });

  it('awaits the host token request before publishing authentication', async () => {
    const token = jwt({ sub: 'operator-dialog' });
    let release!: (value: string) => void;
    const authentication = createMemoryBearerAuthentication(() => new Promise((resolve) => { release = resolve; }));
    const signingIn = authentication.beginSignIn?.();
    expect(authentication.snapshot()).toEqual({ isAuthenticated: false });
    release(token);
    await signingIn;
    expect(authentication.snapshot()).toEqual({ isAuthenticated: true, subjectHint: 'operator-dialog' });
  });
});

function jwt(payload: Record<string, string>): string {
  const encode = (value: object) => btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
  return `${encode({ alg: 'none' })}.${encode(payload)}.${'x'.repeat(32)}`;
}
