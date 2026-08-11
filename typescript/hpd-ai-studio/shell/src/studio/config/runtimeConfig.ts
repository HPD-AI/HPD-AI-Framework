import type { StudioMode, StudioShellConfiguration } from '@hpd-research/hpd-studio-core';

declare const __HPD_STUDIO_SHELL_MARKER__: string;

const shellMarkerPrefix = 'hpd-shell-contract-v1:';
const shellMarker = __HPD_STUDIO_SHELL_MARKER__;
if (!shellMarker.startsWith(shellMarkerPrefix) || !/^[0-9a-f]{64}$/.test(shellMarker.slice(shellMarkerPrefix.length))) {
  throw new Error('The HPD Studio compiled shell identity is invalid.');
}
export const compiledShellContractIdentity = shellMarker.slice(shellMarkerPrefix.length);

type RuntimeModule = {
  readonly id: unknown;
  readonly label: unknown;
  readonly title: unknown;
  readonly status: unknown;
};

type RuntimeConfiguration = {
  readonly apiBasePath: unknown;
  readonly routePrefix: unknown;
  readonly productTitle: unknown;
  readonly mode: unknown;
  readonly assetContractVersion: unknown;
  readonly assetIdentity: unknown;
  readonly shellContractIdentity: unknown;
  readonly capabilities: unknown;
  readonly studioModules: unknown;
};

declare global {
  var HPD_STUDIO_CONFIG: RuntimeConfiguration | undefined;
}

const exactConfigurationMembers = new Set([
  'apiBasePath', 'routePrefix', 'productTitle', 'mode', 'assetContractVersion',
  'assetIdentity', 'shellContractIdentity', 'capabilities', 'studioModules'
]);
const exactModuleMembers = new Set(['id', 'label', 'title', 'status']);

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value) &&
    Object.getPrototypeOf(value) === Object.prototype;
}

function requireExactMembers(value: Record<string, unknown>, members: ReadonlySet<string>, kind: string): void {
  const keys = Object.keys(value);
  if (keys.length !== members.size || keys.some(key => !members.has(key))) {
    throw new Error(`The HPD Studio ${kind} contains unknown or missing members.`);
  }
}

function validText(value: unknown, maximumBytes: number): value is string {
  return typeof value === 'string' && value.normalize('NFC') === value &&
    new TextEncoder().encode(value).length >= 1 && new TextEncoder().encode(value).length <= maximumBytes &&
    !/\p{Cc}/u.test(value);
}

function validAbsolutePath(value: unknown, maximumBytes: number): value is string {
  return typeof value === 'string' && value.startsWith('/') && value !== '/' &&
    new TextEncoder().encode(value).length <= maximumBytes && /^[\x21-\x7e]+$/.test(value) &&
    !value.includes('\\') && !value.includes('//') && !value.includes('..') && !value.endsWith('/');
}

function requireConfiguration(): RuntimeConfiguration {
  const supplied: unknown = globalThis.HPD_STUDIO_CONFIG;
  if (!isPlainRecord(supplied)) throw new Error('The HPD Studio runtime configuration is missing or invalid.');
  requireExactMembers(supplied, exactConfigurationMembers, 'runtime configuration');
  return supplied as RuntimeConfiguration;
}

export function readRuntimeModuleIds(): ReadonlySet<string> | null {
  const modules = requireConfiguration().studioModules;
  if (!Array.isArray(modules) || modules.length > 64) throw new Error('The HPD Studio module catalog is invalid.');
  const ids = new Set<string>();
  for (const module of modules) {
    if (!isPlainRecord(module)) throw new Error('The HPD Studio module catalog is invalid.');
    requireExactMembers(module, exactModuleMembers, 'module registration');
    const typed = module as RuntimeModule;
    if (typeof typed.id !== 'string' || !/^[a-z][a-z0-9-]{0,63}$/.test(typed.id) || ids.has(typed.id) ||
        !validText(typed.label, 128) || !validText(typed.title, 256) || typed.status !== 'active') {
      throw new Error('The HPD Studio module catalog is invalid.');
    }
    ids.add(typed.id);
  }
  return ids;
}

export function readRuntimeConfig(): StudioShellConfiguration {
  const supplied = requireConfiguration();
  if (supplied.assetContractVersion !== '1') {
    throw new Error('The HPD Studio asset contract version is unsupported.');
  }
  if (typeof supplied.assetIdentity !== 'string' || !/^[0-9a-f]{64}$/.test(supplied.assetIdentity)) {
    throw new Error('The HPD Studio asset identity is invalid.');
  }
  if (supplied.shellContractIdentity !== compiledShellContractIdentity) {
    throw new Error('The HPD Studio shell and runtime configuration do not match.');
  }
  if (!validText(supplied.productTitle, 256) || !validAbsolutePath(supplied.apiBasePath, 256) ||
      !validAbsolutePath(supplied.routePrefix, 128) ||
      (supplied.mode !== 'development' && supplied.mode !== 'read-only')) {
    throw new Error('The HPD Studio runtime configuration is invalid.');
  }
  if (!Array.isArray(supplied.capabilities) || supplied.capabilities.length > 64 ||
      supplied.capabilities.some(value => typeof value !== 'string' || !/^[a-z][a-z0-9.-]{0,127}$/.test(value)) ||
      new Set(supplied.capabilities).size !== supplied.capabilities.length) {
    throw new Error('The HPD Studio capability catalog is invalid.');
  }
  const mode: StudioMode = supplied.mode;
  return Object.freeze({
    productTitle: supplied.productTitle,
    apiBasePath: supplied.apiBasePath,
    routePrefix: supplied.routePrefix,
    assetContractVersion: '1',
    assetIdentity: supplied.assetIdentity,
    mode
  });
}
