import { studioSha256, type StudioSha256 } from '@hpd-research/hpd-studio-core';

export interface StudioEditionModuleAsset {
  readonly moduleId: string;
  readonly moduleVersion: number;
  readonly entryModulePath: string;
  readonly assetGraphChecksum: StudioSha256;
}

/** Public, authorization-neutral shell authority emitted by the platform host. */
export interface StudioHostContract {
  readonly shellContractChecksum: StudioSha256;
  readonly editionAssetGraphChecksum: StudioSha256;
  readonly runtimeClientChecksum: StudioSha256;
  readonly bootstrapRoute: string;
  readonly sessionRoute: string;
  readonly loginRoute: string;
  readonly logoutRoute: string;
  readonly authentication: StudioHostAuthenticationContract;
  readonly modules: readonly StudioEditionModuleAsset[];
}

export type StudioHostAuthenticationContract = Readonly<
  | { readonly kind: 'cookieBff'; readonly authorizationRoute: string; readonly descriptorChecksum: StudioSha256 }
  | { readonly kind: 'bearer'; readonly authorizationRoute: string; readonly refreshSupported: boolean; readonly descriptorChecksum: StudioSha256 }
>;

const ID = /^[a-z][a-zA-Z0-9]*(?:[.-][a-zA-Z0-9]+)*$/u;
const ROUTE = /^\/[A-Za-z0-9._~!$&'()*+,;=:@%/-]{1,255}$/u;

/** Decodes the exact host-owned edition contract without accepting legacy runtime configuration. */
export function decodeStudioHostContract(value: unknown): StudioHostContract {
  exact(value, ['shellContractChecksum', 'editionAssetGraphChecksum', 'runtimeClientChecksum', 'bootstrapRoute',
    'sessionRoute', 'loginRoute', 'logoutRoute', 'authentication', 'modules']);
  const input = value as Record<string, unknown>;
  const routes = ['bootstrapRoute', 'sessionRoute', 'loginRoute', 'logoutRoute'] as const;
  for (const route of routes) if (typeof input[route] !== 'string' || !ROUTE.test(input[route]) || input[route].includes('..') || input[route].includes('//'))
    throw new TypeError('base.studio.hostContractInvalid');
  if (!Array.isArray(input.modules) || input.modules.length < 1 || input.modules.length > 64)
    throw new TypeError('base.studio.hostContractInvalid');
  exact(input.authentication, (input.authentication as { kind?: unknown }).kind === 'cookieBff'
    ? ['kind', 'authorizationRoute', 'descriptorChecksum'] : ['kind', 'authorizationRoute', 'refreshSupported', 'descriptorChecksum']);
  const authentication = input.authentication as Record<string, unknown>;
  let auth: StudioHostAuthenticationContract;
  if (authentication.kind === 'cookieBff' && typeof authentication.authorizationRoute === 'string' && ROUTE.test(authentication.authorizationRoute))
    auth = Object.freeze({ kind: 'cookieBff', authorizationRoute: authentication.authorizationRoute,
      descriptorChecksum: studioSha256(authentication.descriptorChecksum as string) });
  else if (authentication.kind === 'bearer' && typeof authentication.authorizationRoute === 'string' && ROUTE.test(authentication.authorizationRoute) &&
      typeof authentication.refreshSupported === 'boolean')
    auth = Object.freeze({ kind: 'bearer', authorizationRoute: authentication.authorizationRoute, refreshSupported: authentication.refreshSupported,
      descriptorChecksum: studioSha256(authentication.descriptorChecksum as string) });
  else throw new TypeError('base.studio.hostContractInvalid');
  const modules = input.modules.map(item => {
    exact(item, ['moduleId', 'moduleVersion', 'entryModulePath', 'assetGraphChecksum']);
    const module = item as Record<string, unknown>;
    if (typeof module.moduleId !== 'string' || !ID.test(module.moduleId) || !Number.isSafeInteger(module.moduleVersion) ||
        (module.moduleVersion as number) < 1 || typeof module.entryModulePath !== 'string' || !ROUTE.test(module.entryModulePath) ||
        module.entryModulePath.includes('..') || module.entryModulePath.includes('//')) throw new TypeError('base.studio.hostContractInvalid');
    return Object.freeze({ moduleId: module.moduleId, moduleVersion: module.moduleVersion as number,
      entryModulePath: module.entryModulePath, assetGraphChecksum: studioSha256(module.assetGraphChecksum as string) });
  });
  const keys = modules.map(module => `${module.moduleId}\0${module.moduleVersion}`);
  if (!canonical(keys)) throw new TypeError('base.studio.hostContractInvalid');
  return Object.freeze({
    shellContractChecksum: studioSha256(input.shellContractChecksum as string),
    editionAssetGraphChecksum: studioSha256(input.editionAssetGraphChecksum as string),
    runtimeClientChecksum: studioSha256(input.runtimeClientChecksum as string),
    bootstrapRoute: input.bootstrapRoute as string, sessionRoute: input.sessionRoute as string,
    loginRoute: input.loginRoute as string, logoutRoute: input.logoutRoute as string, authentication: auth,
    modules: Object.freeze(modules)
  });
}

export async function readStudioHostContract(signal?: AbortSignal): Promise<StudioHostContract> {
  const response = await fetch(new URL('control/shell', document.baseURI), {
    credentials: 'same-origin', headers: { Accept: 'application/json' }, cache: 'no-store', redirect: 'error', signal
  });
  if (!response.ok) throw new Error('base.studio.hostUnavailable');
  return decodeStudioHostContract(await response.json());
}

function exact(value: unknown, keys: readonly string[]): void {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new TypeError('base.studio.hostContractInvalid');
  const actual = Object.keys(value).sort(); const expected = [...keys].sort();
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index])) throw new TypeError('base.studio.hostContractInvalid');
}

function canonical(values: readonly string[]): boolean {
  if (new Set(values).size !== values.length) return false;
  for (let index = 1; index < values.length; index++) if (values[index - 1]! >= values[index]!) return false;
  return true;
}
