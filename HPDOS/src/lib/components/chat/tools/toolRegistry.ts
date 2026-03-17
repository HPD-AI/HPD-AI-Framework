import type { Component } from 'svelte';
import type { ToolCall } from '@hpd/hpd-agent-headless-ui';

export interface ToolRendererProps {
	toolCall: ToolCall;
}

/**
 * Registry of specialized tool renderers.
 *
 * Keys are tool names (exact match). The registry is checked before
 * falling back to the generic DefaultToolCall renderer.
 *
 * Registration is done at app init time (or lazily by feature modules).
 * Example:
 *   import { toolRegistry } from './toolRegistry';
 *   import BashTool from './renderers/BashTool.svelte';
 *   toolRegistry.register('bash', BashTool);
 */
class ToolRegistry {
	readonly #map = new Map<string, Component<ToolRendererProps>>();

	register(toolName: string, component: Component<ToolRendererProps>) {
		this.#map.set(toolName, component);
	}

	resolve(toolName: string): Component<ToolRendererProps> | undefined {
		// Exact match first
		if (this.#map.has(toolName)) return this.#map.get(toolName);

		// Prefix match — e.g. "mcp__filesystem__read_file" matches "mcp__filesystem__*"
		// and also a generic "mcp__*" catch-all if registered
		for (const [key, component] of this.#map) {
			if (key.endsWith('*') && toolName.startsWith(key.slice(0, -1))) {
				return component;
			}
		}

		return undefined;
	}
}

export const toolRegistry = new ToolRegistry();
