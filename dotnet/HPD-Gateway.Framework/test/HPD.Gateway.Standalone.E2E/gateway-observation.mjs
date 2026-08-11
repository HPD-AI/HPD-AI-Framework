import { createGatewayClient } from '../../../../typescript/hpd-gateway-client/dist/index.js';

const baseUrl = process.env.HPD_GATEWAY_CLIENT_E2E_URL;
const token = process.env.HPD_GATEWAY_CLIENT_E2E_TOKEN;
if (!baseUrl || !token) throw new Error('Gateway observation E2E environment is incomplete.');
const client = createGatewayClient({
  baseUrl,
  authentication: { getAccessToken: () => ({ value: token }) }
});
const path = { ns: 'namespace-a', target: 'node-a' };
const results = {
  capabilities: await client.capabilities({ path: {} }),
  host: await client['host-capabilities']({ path: {} }),
  status: await client.status({ path }),
  desired: await client.desired({ path }),
  effective: await client.effective({ path })
};
for (const [name, result] of Object.entries(results)) {
  if (!result.ok) throw new Error(`${name} failed: ${JSON.stringify(result)}`);
}
console.log('HPD Gateway complete generated observation execution passed');
