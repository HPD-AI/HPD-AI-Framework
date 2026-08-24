import { mount } from 'svelte';
import App from './App.svelte';
import { readStudioHostContract } from './studio/host-contract.ts';
import { StudioShellRuntime } from './studio/shell-runtime.ts';
import './styles.css';

const target = document.getElementById('app');
if (!target) throw new Error('base.studio.mountTargetMissing');

const host = await readStudioHostContract();
const runtime = new StudioShellRuntime(host);
const isFreshAuthenticationCallback = runtime.authentication.consumeFreshAuthenticationCallback();
const mounted = isFreshAuthenticationCallback ? null : mount(App, { target, props: { runtime } });
if (isFreshAuthenticationCallback) {
  target.textContent = 'Authentication complete. This window can be closed.';
  globalThis.close();
} else await runtime.start();

export default mounted;
