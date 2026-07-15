export interface BridgeConfig {
  /** Base URL of the HPD server, e.g. http://localhost:5000 */
  serverUrl: string;
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

  return {
    serverUrl: serverUrl.replace(/\/$/, ''),
    agentName: process.env['HPD_AGENT_NAME'],
    apiKey:    process.env['HPD_API_KEY'],
  };
}
