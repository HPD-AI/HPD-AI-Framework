import { mount } from 'svelte';
import { composeStudio, type StudioModuleRegistration } from '@hpd-research/hpd-studio-core';
import { agentStudioModule } from '@hpd-research/hpd-agent-studio';
import { authStudioModule } from '@hpd-research/hpd-auth-studio';
import { baseStudioModule } from '@hpd-research/hpd-base-studio';
import { graphStudioModule } from '@hpd-research/hpd-graph-studio';
import { createGatewayStudioModule } from '@hpd-research/hpd-gateway-studio';
import { createGatewayClient } from '@hpd/gateway-client';
import { mlStudioModule } from '@hpd-research/hpd-ml-studio';
import { ragStudioModule } from '@hpd-research/hpd-rag-studio';
import App from './App.svelte';
import { readRuntimeConfig } from './studio/config/runtimeConfig';
import { createAnonymousAuthentication } from './studio/services/authentication';
import './styles.css';

const target = document.getElementById('app');
if (!target) throw new Error('HPD Studio mount target was not found.');

const configuration = readRuntimeConfig();
const gatewayClient = createGatewayClient({
  baseUrl: globalThis.location.origin,
  authentication: { getAccessToken: () => null }
});

const modules: StudioModuleRegistration[] = [
  agentStudioModule,
  authStudioModule,
  baseStudioModule,
  graphStudioModule,
  createGatewayStudioModule({ client: gatewayClient }),
  mlStudioModule,
  ragStudioModule
].map((module) => ({ module, requirement: 'optional' }));

const studio = await composeStudio({
  configuration,
  authentication: createAnonymousAuthentication(),
  modules
});

export default mount(App, { target, props: { studio } });
