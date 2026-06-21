export type HpdTransport = 'websocket' | 'sse';

export interface BridgeConfig {
  /** Base URL of the HPD server, e.g. http://localhost:5000 */
  serverUrl: string;
  /** Transport to use when connecting to the HPD server */
  transport: HpdTransport;
  /** Optional agent name — used when the HPD server hosts multiple named agents */
  agentName?: string;
  /** Optional API key forwarded as Authorization header */
  apiKey?: string;
}

export function loadConfig(): BridgeConfig {
  const serverUrl = process.env['HPD_SERVER_URL'];
  if (!serverUrl) {
    process.stderr.write('HPD_SERVER_URL is required\n');
    process.exit(1);
  }

  const transport = (process.env['HPD_TRANSPORT'] ?? 'websocket') as HpdTransport;
  if (transport !== 'websocket' && transport !== 'sse') {
    process.stderr.write(`HPD_TRANSPORT must be "websocket" or "sse", got "${transport}"\n`);
    process.exit(1);
  }

  return {
    serverUrl: serverUrl.replace(/\/$/, ''),
    transport,
    agentName: process.env['HPD_AGENT_NAME'],
    apiKey:    process.env['HPD_API_KEY'],
  };
}
