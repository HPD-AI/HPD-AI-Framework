#!/usr/bin/env node
import { AgentClient } from '@hpd/hpd-agent-client';
import { AcpReader } from './acp/reader.js';
import { AcpWriter } from './acp/writer.js';
import { SessionRegistry } from './bridge/session.js';
import { loadConfig } from './config.js';
import { createBridge } from './bridge.js';

const config   = loadConfig();
const reader   = new AcpReader();
const writer   = new AcpWriter();
const sessions = new SessionRegistry();

const client = new AgentClient({
  baseUrl:   config.serverUrl,
  transport: config.transport,
  ...(config.apiKey ? { headers: { Authorization: `Bearer ${config.apiKey}` } } : {}),
});

createBridge(client, reader, writer, sessions, config);
