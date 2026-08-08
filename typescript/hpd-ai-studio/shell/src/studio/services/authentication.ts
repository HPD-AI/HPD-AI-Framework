import type { StudioAuthenticationService, StudioAuthenticationSnapshot } from '@hpd-research/hpd-studio-core';

export function createAnonymousAuthentication(): StudioAuthenticationService {
  const snapshot: StudioAuthenticationSnapshot = Object.freeze({ isAuthenticated: false });
  return Object.freeze({
    snapshot: () => snapshot,
    subscribe(listener: (value: StudioAuthenticationSnapshot) => void) {
      listener(snapshot);
      return () => {};
    }
  });
}
