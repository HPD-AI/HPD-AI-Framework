import type { StudioAuthenticationService, StudioAuthenticationSnapshot } from '@hpd-research/hpd-studio-core';

export interface StudioBearerAuthentication extends StudioAuthenticationService {
  getAccessToken(): string | null;
}

type BearerTokenRequest = () => string | null | Promise<string | null>;

export function createMemoryBearerAuthentication(
  requestToken: BearerTokenRequest = requestBearerToken
): StudioBearerAuthentication {
  const listeners = new Set<(snapshot: StudioAuthenticationSnapshot) => void>();
  let token: string | null = null;
  let current: StudioAuthenticationSnapshot = Object.freeze({ isAuthenticated: false });

  const publish = (next: StudioAuthenticationSnapshot) => {
    current = Object.freeze(next);
    for (const listener of listeners) listener(current);
  };

  return Object.freeze({
    snapshot: () => current,
    getAccessToken: () => token,
    subscribe(listener: (value: StudioAuthenticationSnapshot) => void) {
      listeners.add(listener);
      listener(current);
      return () => listeners.delete(listener);
    },
    async beginSignIn() {
      const supplied = await requestToken();
      if (supplied === null || !isBearerToken(supplied)) return;
      token = supplied;
      const subjectHint = jwtSubjectHint(supplied);
      publish({ isAuthenticated: true, ...(subjectHint === undefined ? {} : { subjectHint }) });
    },
    beginSignOut() {
      token = null;
      publish({ isAuthenticated: false });
    }
  });
}

function requestBearerToken(): Promise<string | null> {
  if (typeof document === 'undefined') return Promise.resolve(null);

  return new Promise((resolve) => {
    const dialog = document.createElement('dialog');
    dialog.className = 'studio-auth-dialog';
    dialog.setAttribute('aria-labelledby', 'studio-auth-title');

    const form = document.createElement('form');
    form.className = 'studio-auth-form';
    form.method = 'dialog';

    const title = document.createElement('h2');
    title.id = 'studio-auth-title';
    title.textContent = 'Sign in to Gateway Studio';

    const explanation = document.createElement('p');
    explanation.textContent = 'Paste a short-lived HPD Cloud access token. It remains in memory and is cleared on sign out or reload.';

    const label = document.createElement('label');
    label.htmlFor = 'studio-auth-token';
    label.textContent = 'Access token';

    const input = document.createElement('textarea');
    input.id = 'studio-auth-token';
    input.name = 'token';
    input.rows = 4;
    input.autocomplete = 'off';
    input.autocapitalize = 'none';
    input.spellcheck = false;

    const actions = document.createElement('div');
    actions.className = 'studio-auth-actions';
    const cancel = document.createElement('button');
    cancel.type = 'button';
    cancel.textContent = 'Cancel';
    const submit = document.createElement('button');
    submit.type = 'submit';
    submit.textContent = 'Continue';
    submit.className = 'studio-auth-submit';
    actions.append(cancel, submit);
    form.append(title, explanation, label, input, actions);
    dialog.append(form);
    document.body.append(dialog);

    let completed = false;
    const complete = (value: string | null) => {
      if (completed) return;
      completed = true;
      dialog.remove();
      resolve(value);
    };
    form.addEventListener('submit', (event) => {
      event.preventDefault();
      complete(input.value);
    });
    cancel.addEventListener('click', () => complete(null));
    dialog.addEventListener('cancel', (event) => {
      event.preventDefault();
      complete(null);
    });

    if (typeof dialog.showModal === 'function') dialog.showModal();
    else dialog.setAttribute('open', '');
    input.focus();
  });
}

function isBearerToken(value: string): boolean {
  return value.length >= 16 && value.length <= 16_384 && /^[!-~]+$/.test(value) && !/\s/.test(value);
}

function jwtSubjectHint(value: string): string | undefined {
  try {
    const payload = value.split('.')[1];
    if (payload === undefined) return undefined;
    const decoded = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(payload.length / 4) * 4, '='))) as unknown;
    if (decoded === null || typeof decoded !== 'object') return undefined;
    const subject = (decoded as Record<string, unknown>).sub;
    return typeof subject === 'string' && subject.length > 0 && subject.length <= 256 ? subject : undefined;
  } catch {
    return undefined;
  }
}
