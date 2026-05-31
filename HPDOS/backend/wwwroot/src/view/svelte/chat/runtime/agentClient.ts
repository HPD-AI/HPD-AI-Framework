import { AgentClient } from "@hpd/hpd-agent-client";

export const HPD_AGENT_API_BASE = "/api/hpd-agent";

export function createHpdAgentClient(): AgentClient {
  return new AgentClient({
    baseUrl: HPD_AGENT_API_BASE
  });
}
