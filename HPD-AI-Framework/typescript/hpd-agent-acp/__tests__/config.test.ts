/**
 * Unit tests for config.ts — environment variable loading.
 *
 * What these tests cover:
 *   loadConfig() reads HPD_SERVER_URL, HPD_TRANSPORT, HPD_AGENT_NAME, HPD_API_KEY
 *   from process.env and returns a validated BridgeConfig. Invalid/missing values
 *   write to stderr and call process.exit(1).
 *
 * Test type: unit — all process.env access is replaced by vi.stubEnv,
 * process.exit is replaced by vi.spyOn to prevent actual exit.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// We import loadConfig fresh per test by resetting modules so env stubs take effect.
// vitest's vi.stubEnv handles cleanup automatically between tests.

const BASE_ENV = {
  HPD_SERVER_URL: 'http://localhost:5000',
  HPD_TRANSPORT: 'websocket',
};

describe('loadConfig', () => {
  let exitSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    vi.resetModules();
    // Prevent process.exit from actually killing the test process
    exitSpy = vi.spyOn(process, 'exit').mockImplementation((() => {
      throw new Error('process.exit called');
    }) as never);
    vi.spyOn(process.stderr, 'write').mockImplementation(() => true);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllEnvs();
  });

  async function load() {
    const { loadConfig } = await import('../src/config.js');
    return loadConfig();
  }

  it('returns a valid config with all env vars set', async () => {
    vi.stubEnv('HPD_SERVER_URL', 'http://localhost:5000');
    vi.stubEnv('HPD_TRANSPORT', 'websocket');
    vi.stubEnv('HPD_AGENT_NAME', 'my-agent');
    vi.stubEnv('HPD_API_KEY', 'secret');

    const config = await load();

    expect(config.serverUrl).toBe('http://localhost:5000');
    expect(config.transport).toBe('websocket');
    expect(config.agentName).toBe('my-agent');
    expect(config.apiKey).toBe('secret');
  });

  it('strips trailing slash from serverUrl', async () => {
    vi.stubEnv('HPD_SERVER_URL', 'http://localhost:5000/');
    vi.stubEnv('HPD_TRANSPORT', 'websocket');

    const config = await load();

    expect(config.serverUrl).toBe('http://localhost:5000');
  });

  it('throws (via process.exit) when HPD_SERVER_URL is missing', async () => {
    vi.stubEnv('HPD_SERVER_URL', '');
    vi.stubEnv('HPD_TRANSPORT', 'websocket');

    await expect(load()).rejects.toThrow('process.exit called');
    expect(exitSpy).toHaveBeenCalledWith(1);
  });

  it('defaults HPD_TRANSPORT to websocket when not set', async () => {
    vi.stubEnv('HPD_SERVER_URL', 'http://localhost:5000');
    vi.stubEnv('HPD_TRANSPORT', '');

    // vitest stubEnv sets to empty string; simulate unset by deleting
    delete process.env['HPD_TRANSPORT'];

    const config = await load();

    expect(config.transport).toBe('websocket');
  });

  it('accepts sse as a valid transport', async () => {
    vi.stubEnv('HPD_SERVER_URL', 'http://localhost:5000');
    vi.stubEnv('HPD_TRANSPORT', 'sse');

    const config = await load();

    expect(config.transport).toBe('sse');
  });

  it('throws (via process.exit) when HPD_TRANSPORT is invalid', async () => {
    vi.stubEnv('HPD_SERVER_URL', 'http://localhost:5000');
    vi.stubEnv('HPD_TRANSPORT', 'grpc');

    await expect(load()).rejects.toThrow('process.exit called');
    expect(exitSpy).toHaveBeenCalledWith(1);
  });

  it('returns undefined apiKey when HPD_API_KEY is not set', async () => {
    vi.stubEnv('HPD_SERVER_URL', 'http://localhost:5000');
    vi.stubEnv('HPD_TRANSPORT', 'websocket');
    delete process.env['HPD_API_KEY'];

    const config = await load();

    expect(config.apiKey).toBeUndefined();
  });

  it('returns undefined agentName when HPD_AGENT_NAME is not set', async () => {
    vi.stubEnv('HPD_SERVER_URL', 'http://localhost:5000');
    vi.stubEnv('HPD_TRANSPORT', 'websocket');
    delete process.env['HPD_AGENT_NAME'];

    const config = await load();

    expect(config.agentName).toBeUndefined();
  });
});
