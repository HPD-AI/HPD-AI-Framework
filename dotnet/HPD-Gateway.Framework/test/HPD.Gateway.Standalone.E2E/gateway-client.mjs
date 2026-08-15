import { createGatewayClient } from "../../../../typescript/hpd-gateway-client/dist/index.js";

const baseUrl = process.env.HPD_GATEWAY_CLIENT_E2E_URL;
const token = process.env.HPD_GATEWAY_CLIENT_E2E_TOKEN;
if (!baseUrl || !token) throw new Error("Gateway client E2E environment is incomplete.");

const client = createGatewayClient({
  baseUrl,
  authentication: { getAccessToken: () => ({ value: token }) },
});
const result = await client.capabilities({ path: {}, headers: { correlationId: "typescript-native-e2e" } });
if (!result.ok || result.status !== 200 || result.value.apiVersion !== "v1")
  throw new Error(`Gateway client native E2E failed: ${JSON.stringify(result)}`);
process.stdout.write("HPD Gateway TypeScript client native Admin execution passed\n");
