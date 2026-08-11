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
import { readRuntimeConfig, readRuntimeModuleIds } from './studio/config/runtimeConfig';
import { createMemoryBearerAuthentication } from './studio/services/authentication';
import './styles.css';

const target = document.getElementById('app');
if (!target) throw new Error('HPD Studio mount target was not found.');

const configuration = readRuntimeConfig();
const installedModuleIds = readRuntimeModuleIds();
const authentication = createMemoryBearerAuthentication();
const gatewayClient = createGatewayClient({
  baseUrl: globalThis.location.origin,
  apiBasePath: configuration.apiBasePath,
  authentication: { getAccessToken: () => {
    const value = authentication.getAccessToken();
    return value === null ? null : { value };
  } }
});

const modules: StudioModuleRegistration[] = [
  agentStudioModule,
  authStudioModule,
  baseStudioModule,
  graphStudioModule,
  createGatewayStudioModule({ client: gatewayClient }),
  mlStudioModule,
  ragStudioModule
].filter((module) => installedModuleIds === null || installedModuleIds.has(module.id))
  .map((module) => ({ module, requirement: 'optional' }));

const studio = await composeStudio({
  configuration,
  authentication,
  modules
});

export default mount(App, { target, props: { studio } });
